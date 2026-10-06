using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AmpUp;

/// <summary>
/// GPU screen capture via DXGI Desktop Duplication. Each acquired frame is copied into a
/// mip-chained texture, GenerateMips box-filters it down on the GPU, and only the small
/// mip level (1/2^MipLevel of source) is read back through a staging texture into the
/// caller's 32bpp Bitmap. CPU cost per frame is a ~430x180 row copy instead of a full
/// GDI StretchBlt of the desktop.
///
/// Not thread-safe; the owning ScreenCapture serializes access.
/// </summary>
internal sealed class DxgiDesktopDuplicator : IDisposable
{
    /// <summary>Mip level read back. 3 = 1/8 resolution (3440x1440 -> 430x180).</summary>
    public const int MipLevel = 3;

    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    private const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);

    private readonly string _deviceName;

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _mipTexture;
    private ID3D11ShaderResourceView? _mipSrv;
    private ID3D11Texture2D? _staging;
    private uint _srcW, _srcH;

    // Texture format of the duplicated surface. FP16 = HDR desktop (linear scRGB) that must be
    // tone-mapped to SDR sRGB on the CPU after the GPU downsample.
    private Format _texFormat = Format.B8G8R8A8_UNorm;
    private bool _isHdr;
    private float _hdrScale = 80f / 200f; // scRGB 1.0 = 80 nits; divide by SDR white level
    private static byte[]? _linToSrgb;    // linear -> sRGB 8-bit LUT
    private const int LutSize = 4096;
    private static readonly HashSet<string> _loggedFormats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when capturing an HDR (FP16 scRGB) desktop.</summary>
    public bool IsHdr => _isHdr;

    /// <summary>Width/height of the downsampled read-back image.</summary>
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>True once at least one frame has been copied into the caller's bitmap.</summary>
    public bool HasFrame { get; private set; }

    public string DeviceName => _deviceName;

    /// <summary>Force the next TryCapture to copy a frame even if only the cursor moved.</summary>
    public void ResetFrameState() => HasFrame = false;

    private DxgiDesktopDuplicator(string deviceName) => _deviceName = deviceName;

    /// <summary>
    /// Create a duplicator for the output whose GDI device name (e.g. \\.\DISPLAY1) matches.
    /// Searches every adapter, so monitors on an iGPU / second GPU are handled.
    /// Throws on failure; caller falls back to GDI.
    /// </summary>
    public static DxgiDesktopDuplicator Create(string gdiDeviceName)
    {
        var dup = new DxgiDesktopDuplicator(gdiDeviceName);
        try
        {
            dup.Initialize();
            return dup;
        }
        catch
        {
            dup.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

        for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1? adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter!.EnumOutputs(o, out IDXGIOutput? output).Success; o++)
                {
                    using (output)
                    {
                        var desc = output!.Description;
                        if (!string.Equals(desc.DeviceName, _deviceName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Rotated outputs deliver an unrotated desktop image; the samplers assume
                        // landscape screen orientation, so let GDI handle portrait/rotated monitors.
                        if (desc.Rotation != ModeRotation.Identity && desc.Rotation != ModeRotation.Unspecified)
                            throw new NotSupportedException($"output {_deviceName} is rotated ({desc.Rotation})");

                        // Device must be created on the adapter that owns the output.
                        var fl = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
                        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                            fl, out _device, out _context).CheckError();

                        // NOTE: do NOT use IDXGIOutput5.DuplicateOutput1 — Vortice 3.6.2's binding
                        // AccessViolates (uncatchable, kills the process). Plain DuplicateOutput already
                        // returns FP16 scRGB on HDR desktops, which the tone-map path below handles.
                        string api = "DuplicateOutput";
                        using (var output1 = output.QueryInterface<IDXGIOutput1>())
                            _duplication = output1.DuplicateOutput(_device);

                        var dd = _duplication.Description;
                        var fmt = dd.ModeDescription.Format;
                        if (fmt == Format.R16G16B16A16_Float)
                        {
                            _texFormat = Format.R16G16B16A16_Float;
                            _isHdr = true;
                            float nits = QuerySdrWhiteNits(_deviceName) ?? 200f;
                            _hdrScale = 80f / nits;
                            EnsureLut();
                            LogOnce($"DXGI capture {_deviceName}: HDR FP16 scRGB via {api}, SDR white {nits:0} nits");
                        }
                        else if (fmt == Format.B8G8R8A8_UNorm || fmt == Format.B8G8R8A8_UNorm_SRgb
                                 || fmt == Format.B8G8R8A8_Typeless)
                        {
                            _texFormat = Format.B8G8R8A8_UNorm;
                            _isHdr = false;
                            LogOnce($"DXGI capture {_deviceName}: SDR BGRA8 via {api}");
                        }
                        else
                        {
                            throw new NotSupportedException($"desktop duplication format {fmt} not supported");
                        }

                        CreateTextures(dd.ModeDescription.Width, dd.ModeDescription.Height);
                        return;
                    }
                }
            }
        }

        throw new InvalidOperationException($"no DXGI output matches {_deviceName}");
    }

    private void CreateTextures(uint w, uint h)
    {
        DisposeTextures();
        _srcW = w;
        _srcH = h;

        _mipTexture = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = w,
            Height = h,
            MipLevels = MipLevel + 1,
            ArraySize = 1,
            Format = _texFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        _mipSrv = _device.CreateShaderResourceView(_mipTexture);

        uint mw = Math.Max(1u, w >> MipLevel);
        uint mh = Math.Max(1u, h >> MipLevel);
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = mw,
            Height = mh,
            MipLevels = 1,
            ArraySize = 1,
            Format = _texFormat,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });

        Width = (int)mw;
        Height = (int)mh;
        HasFrame = false;
    }

    public enum CaptureResult { NewFrame, NoChange, Lost }

    /// <summary>
    /// Try to grab a new desktop frame (non-blocking) and write the downsampled image into
    /// <paramref name="target"/> (must be Width x Height, Format32bppRgb).
    /// NoChange = nothing new since last call; target keeps the previous frame.
    /// Lost = duplication invalidated (mode change, fullscreen switch, secure desktop);
    /// caller should dispose and recreate.
    /// </summary>
    public string LastLostReason { get; private set; } = "";

    public CaptureResult TryCapture(Bitmap target)
    {
        if (_duplication == null || _context == null) return CaptureResult.Lost;

        var hr = _duplication.AcquireNextFrame(0, out OutduplFrameInfo info, out IDXGIResource? resource);
        if (hr.Code == DXGI_ERROR_WAIT_TIMEOUT)
            return CaptureResult.NoChange;
        if (hr.Code == DXGI_ERROR_ACCESS_LOST || hr.Failure)
        {
            LastLostReason = $"AcquireNextFrame hr=0x{hr.Code:X8}";
            resource?.Dispose();
            return CaptureResult.Lost;
        }

        bool copied = false;
        try
        {
            // LastPresentTime == 0 means only the mouse moved — desktop image unchanged.
            if (info.LastPresentTime != 0 || !HasFrame)
            {
                using var srcTex = resource!.QueryInterface<ID3D11Texture2D>();
                var sd = srcTex.Description;
                if (sd.Width != _srcW || sd.Height != _srcH)
                {
                    LastLostReason = $"surface {sd.Width}x{sd.Height} != output {_srcW}x{_srcH}";
                    return CaptureResult.Lost; // resolution changed without ACCESS_LOST — rebuild
                }

                // CopySubresourceRegion silently does nothing (debug-layer error only) when the
                // formats are in different typeless groups, which would leave the readback black
                // forever. IDXGIOutput1 duplication normally hands out BGRA8 even on HDR desktops,
                // but guard anyway so an FP16 / 10-bit surface falls back to GDI instead.
                // The duplication's ModeDescription can say FP16 on an HDR desktop while the
                // acquired surfaces are actually BGRA8 (driver-converted) — or vice versa after a
                // game switches modes. Follow the real surface format instead of rebuilding the
                // whole duplication every frame (that loop made the lights flash / go black).
                bool surfHdr = sd.Format == Format.R16G16B16A16_Float || sd.Format == Format.R16G16B16A16_Typeless;
                bool surfSdr = sd.Format == Format.B8G8R8A8_UNorm || sd.Format == Format.B8G8R8A8_UNorm_SRgb
                    || sd.Format == Format.B8G8R8A8_Typeless;
                if (!surfHdr && !surfSdr)
                    throw new NotSupportedException($"desktop surface format {sd.Format} not supported");
                if (surfHdr != _isHdr)
                {
                    _isHdr = surfHdr;
                    _texFormat = surfHdr ? Format.R16G16B16A16_Float : Format.B8G8R8A8_UNorm;
                    if (surfHdr)
                    {
                        float nits = QuerySdrWhiteNits(_deviceName) ?? 200f;
                        _hdrScale = 80f / nits;
                        EnsureLut();
                    }
                    CreateTextures(_srcW, _srcH);
                    LogOnce($"DXGI capture {_deviceName}: surfaces are {sd.Format}, using {(surfHdr ? "HDR tone-map" : "SDR")} path");
                }

                _context.CopySubresourceRegion(_mipTexture!, 0, 0, 0, 0, srcTex, 0, null);
                copied = true;
            }
        }
        finally
        {
            resource?.Dispose();
            _duplication.ReleaseFrame();
        }

        if (!copied) return CaptureResult.NoChange;

        _context.GenerateMips(_mipSrv!);
        _context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, _mipTexture!, MipLevel, null);

        var mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var bd = target.LockBits(new Rectangle(0, 0, Width, Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                unsafe
                {
                    byte* src = (byte*)mapped.DataPointer;
                    byte* dst = (byte*)bd.Scan0;
                    long srcPitch = (long)mapped.RowPitch;
                    if (_isHdr)
                    {
                        ToneMapRows(src, srcPitch, dst, bd.Stride);
                    }
                    else
                    {
                        long rowBytes = (long)Width * 4;
                        for (int y = 0; y < Height; y++)
                            Buffer.MemoryCopy(src + y * srcPitch, dst + (long)y * bd.Stride, rowBytes, rowBytes);
                    }
                }
            }
            finally
            {
                target.UnlockBits(bd);
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }

        HasFrame = true;
        return CaptureResult.NewFrame;
    }

    /// <summary>
    /// FP16 scRGB (linear, Rec.709 primaries, 1.0 = 80 nits) -> SDR sRGB BGRA8.
    /// Scales so the user's SDR white maps to 1.0, applies a soft-knee Reinhard on luminance
    /// above 0.8 so HDR highlights roll off instead of hard-clipping, then LUT linear->sRGB.
    /// </summary>
    private unsafe void ToneMapRows(byte* src, long srcPitch, byte* dst, int dstStride)
    {
        const float Knee = 0.8f;
        const float Span = 1f - Knee;
        var lut = _linToSrgb!;
        float scale = _hdrScale;
        int w = Width, h = Height;
        for (int y = 0; y < h; y++)
        {
            ushort* s = (ushort*)(src + y * srcPitch);
            byte* d = dst + (long)y * dstStride;
            for (int x = 0; x < w; x++, s += 4, d += 4)
            {
                float r = (float)BitConverter.UInt16BitsToHalf(s[0]) * scale;
                float g = (float)BitConverter.UInt16BitsToHalf(s[1]) * scale;
                float b = (float)BitConverter.UInt16BitsToHalf(s[2]) * scale;
                // Out-of-sRGB-gamut scRGB values can be negative; clip them (also rejects NaN).
                if (!(r > 0f)) r = 0f;
                if (!(g > 0f)) g = 0f;
                if (!(b > 0f)) b = 0f;

                float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                if (lum > Knee)
                {
                    float t = (lum - Knee) / Span;
                    float mapped = Knee + Span * (t / (1f + t));
                    float k = mapped / lum;
                    r *= k; g *= k; b *= k;
                }

                d[0] = lut[ToLutIndex(b)];
                d[1] = lut[ToLutIndex(g)];
                d[2] = lut[ToLutIndex(r)];
                d[3] = 255;
            }
        }
    }

    private static int ToLutIndex(float v)
    {
        int i = (int)(v * (LutSize - 1) + 0.5f);
        return i >= LutSize ? LutSize - 1 : i;
    }

    private static void EnsureLut()
    {
        if (_linToSrgb != null) return;
        var lut = new byte[LutSize];
        for (int i = 0; i < LutSize; i++)
        {
            double l = i / (double)(LutSize - 1);
            double sv = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055;
            lut[i] = (byte)Math.Clamp((int)Math.Round(sv * 255.0), 0, 255);
        }
        _linToSrgb = lut;
    }

    private static void LogOnce(string msg)
    {
        lock (_loggedFormats)
            if (!_loggedFormats.Add(msg)) return;
        Logger.Log(msg);
    }

    // SDR white level = Windows "SDR content brightness" slider for this monitor.
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SDR_WHITE_LEVEL
    {
        public NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel; // nits = SDRWhiteLevel * 80 / 1000
    }

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL requestPacket);

    private static float? QuerySdrWhiteNits(string gdiDeviceName)
    {
        try
        {
            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pc, out uint mc) != 0)
                return null;
            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pc];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[mc];
            if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pc, paths, ref mc, modes, IntPtr.Zero) != 0)
                return null;

            for (int i = 0; i < pc; i++)
            {
                var srcName = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                srcName.header.type = 1; // GET_SOURCE_NAME
                srcName.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                srcName.header.adapterId = paths[i].sourceInfo.adapterId;
                srcName.header.id = paths[i].sourceInfo.id;
                if (NativeMethods.DisplayConfigGetDeviceInfo(ref srcName) != 0) continue;
                if (!string.Equals(srcName.viewGdiDeviceName?.Trim('\0'), gdiDeviceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var req = new DISPLAYCONFIG_SDR_WHITE_LEVEL();
                req.header.type = 11; // DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL
                req.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>();
                req.header.adapterId = paths[i].targetInfo.adapterId;
                req.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref req) != 0 || req.SDRWhiteLevel == 0) return null;
                float nits = req.SDRWhiteLevel * 80f / 1000f;
                return nits is >= 40f and <= 1000f ? nits : null;
            }
        }
        catch { }
        return null;
    }

    private void DisposeTextures()
    {
        _mipSrv?.Dispose(); _mipSrv = null;
        _mipTexture?.Dispose(); _mipTexture = null;
        _staging?.Dispose(); _staging = null;
    }

    public void Dispose()
    {
        DisposeTextures();
        _duplication?.Dispose(); _duplication = null;
        _context?.ClearState();
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
    }
}
