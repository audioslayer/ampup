using System.Runtime.InteropServices;
using AmpUp.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace N3Diag;

internal sealed class NativeHidProbe : IDisposable
{
    private readonly string _devicePath;
    private readonly int _inputReportLength;
    private readonly int _outputReportLength;
    private readonly Action<string> _log;
    private SafeFileHandle? _handle;
    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private bool _disposed;

    public NativeHidProbe(string devicePath, int inputReportLength, int outputReportLength, Action<string> log)
    {
        _devicePath = devicePath;
        _inputReportLength = Math.Max(inputReportLength, 8);
        _outputReportLength = Math.Max(outputReportLength, 8);
        _log = log;
    }

    public bool Start()
    {
        if (_disposed) return false;
        if (string.IsNullOrWhiteSpace(_devicePath))
        {
            _log("N3 native: probe skipped because device path is empty");
            return false;
        }

        Stop();

        _handle = NativeMethods.CreateFile(
            _devicePath,
            NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (_handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            _log($"N3 native: open failed ({error}) path={_devicePath}");
            _handle.Dispose();
            _handle = null;
            return false;
        }

        _log($"N3 native: probe opened path={_devicePath} in={_inputReportLength} out={_outputReportLength}");
        _cts = new CancellationTokenSource();
        _readTask = Task.Run(() => ReadLoop(_cts.Token));
        return true;
    }

    public void SendReport(string label, params byte[] payload)
    {
        SafeFileHandle? handle = _handle;
        if (handle == null || handle.IsInvalid)
        {
            _log($"N3 native: send skipped ({label}) because probe is not open");
            return;
        }

        try
        {
            byte[] report = new byte[_outputReportLength];
            Array.Copy(payload, report, Math.Min(payload.Length, report.Length));
            _log($"N3 native tx [{label}]: {ToHex(report.AsSpan(0, Math.Min(payload.Length, report.Length)).ToArray())}");

            if (!NativeMethods.WriteFile(handle, report, (uint)report.Length, out uint bytesWritten, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                _log($"N3 native: write failed ({error}) label={label}");
                return;
            }

            _log($"N3 native: write ok ({label}) bytes={bytesWritten}");
        }
        catch (Exception ex)
        {
            _log($"N3 native: write failed ({label}) - {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _handle?.Dispose();
        }
        catch
        {
        }

        try
        {
            _readTask?.Wait(250);
        }
        catch
        {
        }

        try
        {
            _cts?.Dispose();
        }
        catch
        {
        }

        _cts = null;
        _readTask = null;
        _handle = null;
    }

    private void ReadLoop(CancellationToken ct)
    {
        byte[] buffer = new byte[_inputReportLength];

        while (!ct.IsCancellationRequested && !_disposed)
        {
            SafeFileHandle? handle = _handle;
            if (handle == null || handle.IsInvalid) break;

            try
            {
                if (!NativeMethods.ReadFile(handle, buffer, (uint)buffer.Length, out uint bytesRead, IntPtr.Zero))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == NativeMethods.ERROR_OPERATION_ABORTED || error == NativeMethods.ERROR_INVALID_HANDLE)
                    {
                        break;
                    }

                    _log($"N3 native: read failed ({error})");
                    break;
                }

                if (bytesRead == 0) continue;

                byte[] report = buffer.AsSpan(0, (int)bytesRead).ToArray();
                if (IsAllZero(report)) continue;

                if (N3Controller.TryParseInputReport(report, out var parsed))
                {
                    _log($"N3 input [native]: {parsed.Describe()} raw={ToHex(report)}");
                }
                else
                {
                    _log($"N3 raw [native]: {ToHex(report)}");
                }
            }
            catch (Exception ex)
            {
                _log($"N3 native: read loop stopped - {ex.Message}");
                break;
            }
        }

        _log("N3 native: read loop stopped");
    }

    private static bool IsAllZero(byte[] data)
    {
        foreach (byte b in data)
        {
            if (b != 0) return false;
        }

        return true;
    }

    private static string ToHex(byte[] data)
    {
        return BitConverter.ToString(data).Replace('-', ' ');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
