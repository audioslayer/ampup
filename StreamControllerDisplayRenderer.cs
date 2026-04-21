using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Controls;
using AmpUp.Core.Models;
using Material.Icons;
using Material.Icons.WPF;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.SolidBrush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingImage = System.Drawing.Image;
using DrawingPen = System.Drawing.Pen;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingStringAlignment = System.Drawing.StringAlignment;
using DrawingStringFormat = System.Drawing.StringFormat;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AmpUp;

internal static class StreamControllerDisplayRenderer
{
    private const int RenderCanvasSize = 126;
    private const int DeviceCanvasSize = 60;

    /// <summary>
    /// Optional state resolver used by <see cref="DisplayKeyType.DynamicState"/> keys.
    /// Wired up at app startup so the renderer can ask "is this source active?" without
    /// taking a hard dependency on OBS / AudioMixer.
    /// </summary>
    public static Func<string, bool>? DynamicStateResolver { get; set; }

    public static BitmapSource CreatePreview(StreamControllerDisplayKeyConfig key)
    {
        using var bitmap = ComposeImage(ResolveEffectiveKey(key));
        return ToBitmapSource(bitmap);
    }

    /// <summary>
    /// Render a high-resolution preview for in-app UI at the requested
    /// <paramref name="size"/>. Skips the 60x60 JPEG round-trip that
    /// <see cref="CreateHardwarePreview"/> uses, so vector icons render
    /// crisply at any size and bitmap sources aren't downscaled first.
    /// </summary>
    public static BitmapSource CreateEditorPreview(StreamControllerDisplayKeyConfig key, int size = 256)
    {
        using var bitmap = ComposeImage(ResolveEffectiveKey(key), size);
        return ToBitmapSource(bitmap);
    }

    public static BitmapSource CreateHardwarePreview(StreamControllerDisplayKeyConfig key)
    {
        var jpeg = CreateDeviceJpeg(key);
        using var stream = new MemoryStream(jpeg);
        using var bitmap = new DrawingBitmap(stream);
        bitmap.RotateFlip(System.Drawing.RotateFlipType.Rotate270FlipNone);
        return ToBitmapSource(bitmap);
    }

    private static BitmapSource ToBitmapSource(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static byte[] CreateDeviceJpeg(StreamControllerDisplayKeyConfig key)
    {
        using var bitmap = ComposeImage(ResolveEffectiveKey(key));
        return EncodeForDevice(bitmap);
    }

    /// <summary>
    /// UI-thread-only step: compose the RAW device-resolution bitmap for a
    /// key. The caller is responsible for disposing the returned bitmap.
    /// Split out from <see cref="CreateDeviceJpeg"/> so the slow JPEG
    /// encode + HID send can run on a background task — the WPF render
    /// must stay on the UI thread, but the encode/send doesn't.
    /// </summary>
    public static DrawingBitmap ComposeDeviceBitmap(StreamControllerDisplayKeyConfig key)
    {
        return ComposeImage(ResolveEffectiveKey(key));
    }

    /// <summary>Thread-safe: encode a composed device bitmap to the wire JPEG.</summary>
    public static byte[] EncodeDeviceBitmap(DrawingBitmap bitmap) => EncodeForDevice(bitmap);

    /// <summary>
    /// Returns a key config with DisplayType-specific overrides baked in:
    ///   Clock        — title becomes the current time, images/icons are cleared.
    ///   DynamicState — when active, swap icon + title to the configured active values.
    ///   Normal       — returned as-is.
    /// </summary>
    private static StreamControllerDisplayKeyConfig ResolveEffectiveKey(StreamControllerDisplayKeyConfig key)
    {
        if (key.DisplayType == DisplayKeyType.Normal)
            return key;

        // Clone so we don't mutate the user's config.
        var clone = new StreamControllerDisplayKeyConfig
        {
            Idx = key.Idx,
            ImagePath = key.ImagePath,
            PresetIconKind = key.PresetIconKind,
            Title = key.Title,
            Subtitle = key.Subtitle,
            BackgroundColor = key.BackgroundColor,
            AccentColor = key.AccentColor,
            TextPosition = key.TextPosition,
            TextSize = key.TextSize,
            TextColor = key.TextColor,
            IconColor = key.IconColor,
            FontFamily = key.FontFamily,
            Brightness = key.Brightness,
            DisplayType = key.DisplayType,
            ClockFormat = key.ClockFormat,
            DynamicStateSource = key.DynamicStateSource,
            DynamicStateActiveIcon = key.DynamicStateActiveIcon,
            DynamicStateActiveTitle = key.DynamicStateActiveTitle,
        };

        if (key.DisplayType == DisplayKeyType.Solid)
        {
            // Flat fill — strip every icon/image source so ComposeImage
            // falls through to the solid-BackgroundColor branch. Title
            // still overlays if the user left one set.
            clone.ImagePath = "";
            clone.PresetIconKind = "";
            return clone;
        }

        if (key.DisplayType == DisplayKeyType.Clock)
        {
            string fmt = string.IsNullOrWhiteSpace(key.ClockFormat) ? "HH:mm" : key.ClockFormat;
            string rendered;
            try { rendered = DateTime.Now.ToString(fmt); }
            catch { rendered = DateTime.Now.ToString("HH:mm"); }

            clone.Title = rendered;
            clone.ImagePath = "";
            clone.PresetIconKind = "";
            // Center the time, default large font if the user hasn't customised it.
            clone.TextPosition = DisplayTextPosition.Middle;
            if (clone.TextSize < 18) clone.TextSize = 22;
            return clone;
        }

        if (key.DisplayType == DisplayKeyType.DynamicState)
        {
            bool active = false;
            try
            {
                active = DynamicStateResolver?.Invoke(key.DynamicStateSource) ?? false;
            }
            catch
            {
                active = false;
            }

            if (active)
            {
                if (!string.IsNullOrWhiteSpace(key.DynamicStateActiveIcon))
                {
                    clone.PresetIconKind = key.DynamicStateActiveIcon;
                    clone.ImagePath = "";
                }
                if (!string.IsNullOrWhiteSpace(key.DynamicStateActiveTitle))
                    clone.Title = key.DynamicStateActiveTitle;
            }
            return clone;
        }

        return clone;
    }

    public static byte[] CreateDeviceJpegFromPath(string imagePath)
    {
        using var source = DrawingImage.FromFile(imagePath);
        using var canvas = RenderSourceToCanvasCover(source, RenderCanvasSize);
        return EncodeForDevice(canvas);
    }

    private static DrawingBitmap ComposeImage(StreamControllerDisplayKeyConfig key, int? size = null)
    {
        int canvas = size ?? RenderCanvasSize;
        string title = key.Title?.Trim() ?? "";
        bool hasText = !string.IsNullOrWhiteSpace(title);

        DrawingBitmap bitmap;

        if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
        {
            using var source = DrawingImage.FromFile(key.ImagePath);
            bitmap = RenderSourceToCanvasCover(source, canvas);
        }
        else if (TryResolveCustomPackImagePath(key.PresetIconKind, out var customPackImagePath))
        {
            using var source = DrawingImage.FromFile(customPackImagePath);
            bitmap = RenderSourceToCanvasCover(source, canvas);
        }
        else if (TryParseMaterialIconKind(key.PresetIconKind, out var presetKind))
        {
            bitmap = RenderPresetIconCanvas(key, presetKind, canvas, title);
        }
        else
        {
            bitmap = new DrawingBitmap(canvas, canvas);
            using var graphics = DrawingGraphics.FromImage(bitmap);
            ConfigureGraphics(graphics);
            graphics.Clear(ParseColor(key.BackgroundColor, DrawingColor.FromArgb(0x1C, 0x1C, 0x1C)));
        }

        // Draw text overlay on all key types
        if (hasText && key.TextPosition != DisplayTextPosition.Hidden)
            DrawTextOverlay(bitmap, key, canvas, title);

        // Per-key brightness — final multiply pass. 100 = unchanged, 0 = black.
        int brightness = Math.Clamp(key.Brightness, 0, 100);
        if (brightness < 100)
            ApplyBrightness(bitmap, brightness);

        return bitmap;
    }

    private static bool TryResolveCustomPackImagePath(string? kind, out string imagePath)
    {
        imagePath = string.Empty;
        if (string.IsNullOrWhiteSpace(kind))
            return false;

        if (!(kind.StartsWith("neon_", StringComparison.OrdinalIgnoreCase)
            || kind.StartsWith("material_", StringComparison.OrdinalIgnoreCase)
            || kind.StartsWith("retro_", StringComparison.OrdinalIgnoreCase)
            || kind.StartsWith("synthwave_", StringComparison.OrdinalIgnoreCase)
            || kind.StartsWith("cyber_", StringComparison.OrdinalIgnoreCase)))
            return false;

        var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Icons", kind + ".jpg");
        if (File.Exists(candidate))
        {
            imagePath = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Multiply every pixel's RGB by <paramref name="brightnessPct"/>/100.
    /// Iterates a locked byte buffer for speed — avoids per-pixel GetPixel
    /// which is ~100x slower on 126x126 images.
    /// </summary>
    private static void ApplyBrightness(DrawingBitmap bitmap, int brightnessPct)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * data.Height;
            var buffer = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);

            // Format32bppArgb is B, G, R, A in memory (little-endian).
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                buffer[i]     = (byte)((buffer[i]     * brightnessPct) / 100);
                buffer[i + 1] = (byte)((buffer[i + 1] * brightnessPct) / 100);
                buffer[i + 2] = (byte)((buffer[i + 2] * brightnessPct) / 100);
                // leave alpha alone
            }

            System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, bytes);
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static DrawingBitmap RenderSourceToCanvas(DrawingImage source, int size)
    {
        var canvas = new DrawingBitmap(size, size);
        using var graphics = DrawingGraphics.FromImage(canvas);
        ConfigureGraphics(graphics);
        graphics.Clear(DrawingColor.Black);

        float scale = Math.Min(size / (float)source.Width, size / (float)source.Height);
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        float x = (size - drawWidth) * 0.5f;
        float y = (size - drawHeight) * 0.5f;
        graphics.DrawImage(source, x, y, drawWidth, drawHeight);
        return canvas;
    }

    private static DrawingBitmap RenderSourceToCanvasCover(DrawingImage source, int size)
    {
        var canvas = new DrawingBitmap(size, size);
        using var graphics = DrawingGraphics.FromImage(canvas);
        ConfigureGraphics(graphics);
        graphics.Clear(DrawingColor.Black);

        float scale = Math.Max(size / (float)source.Width, size / (float)source.Height);
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        float x = (size - drawWidth) * 0.5f;
        float y = (size - drawHeight) * 0.5f;
        graphics.DrawImage(source, x, y, drawWidth, drawHeight);
        return canvas;
    }

    private static DrawingBitmap RenderPresetIconCanvas(
        StreamControllerDisplayKeyConfig key,
        MaterialIconKind presetKind,
        int size,
        string title)
    {
        bool showText = !string.IsNullOrWhiteSpace(title) && key.TextPosition != DisplayTextPosition.Hidden;
        var accent = TryParseMediaColor(key.AccentColor, System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));
        var bg = TryParseMediaColor(key.BackgroundColor, System.Windows.Media.Color.FromRgb(0x12, 0x12, 0x12));
        var bg2 = System.Windows.Media.Color.FromRgb(
            (byte)Math.Max(0, bg.R * 0.52),
            (byte)Math.Max(0, bg.G * 0.52),
            (byte)Math.Max(0, bg.B * 0.52));

        var root = new Grid
        {
            Width = size,
            Height = size,
            Background = new System.Windows.Media.LinearGradientBrush(bg, bg2, 55),
            ClipToBounds = true
        };

        var accentGlow = new Border
        {
            Width = size * 1.16,
            Height = size * 1.16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(size * 0.58),
            Background = new RadialGradientBrush(
                System.Windows.Media.Color.FromArgb(0x86, accent.R, accent.G, accent.B),
                System.Windows.Media.Color.FromArgb(0x00, accent.R, accent.G, accent.B))
        };
        root.Children.Add(accentGlow);

        var edgeGlow = new Border
        {
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromArgb(0x28, 255, 255, 255),
                System.Windows.Media.Color.FromArgb(0x00, 255, 255, 255),
                90)
        };
        edgeGlow.OpacityMask = new RadialGradientBrush(
            System.Windows.Media.Color.FromArgb(0x00, 255, 255, 255),
            System.Windows.Media.Color.FromArgb(0xFF, 255, 255, 255))
        {
            Center = new System.Windows.Point(0.5, 0.5),
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.88,
            RadiusY = 0.88
        };
        root.Children.Add(edgeGlow);

        var iconTint = TryParseMediaColor(key.IconColor, System.Windows.Media.Color.FromRgb(0xF7, 0xF7, 0xF7));
        var icon = new MaterialIcon
        {
            Kind = presetKind,
            Width = size * (showText ? 0.52 : 0.68),
            Height = size * (showText ? 0.52 : 0.68),
            Foreground = new SolidColorBrush(iconTint),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        // Shift icon up if text at bottom, down if text at top
        if (showText && key.TextPosition == DisplayTextPosition.Bottom)
            icon.Margin = new Thickness(0, size * -0.12, 0, 0);
        else if (showText && key.TextPosition == DisplayTextPosition.Top)
            icon.Margin = new Thickness(0, size * 0.12, 0, 0);
        root.Children.Add(icon);

        return RenderElementToDrawingBitmap(root, size, size);
    }

    private static void DrawTextOverlay(DrawingBitmap bitmap, StreamControllerDisplayKeyConfig key, int size, string title)
    {
        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);

        float scale = size / 60f;
        var textColor = ParseColor(key.TextColor, DrawingColor.White);
        float baseFontSize = Math.Clamp(key.TextSize, 6, 28);

        var fontName = string.IsNullOrWhiteSpace(key.FontFamily) ? "Segoe UI" : key.FontFamily;
        DrawingFont titleFont;
        try
        {
            titleFont = new DrawingFont(fontName, baseFontSize * scale, System.Drawing.FontStyle.Bold, DrawingGraphicsUnit.Pixel);
        }
        catch
        {
            titleFont = new DrawingFont("Segoe UI", baseFontSize * scale, System.Drawing.FontStyle.Bold, DrawingGraphicsUnit.Pixel);
        }
        using var _ = titleFont;
        using var textBrush = new DrawingBrush(textColor);

        // Split on both Windows and Unix newlines; cap at 3 lines so long
        // paragraphs don't run off the tile. Extra lines past 3 are joined
        // onto the third line with an ellipsis suffix.
        var lines = title.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 3)
        {
            var truncated = new string[3];
            Array.Copy(lines, 0, truncated, 0, 3);
            lines = truncated;
        }

        var format = new DrawingStringFormat
        {
            Alignment = DrawingStringAlignment.Center,
            LineAlignment = DrawingStringAlignment.Center,
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.NoWrap
        };

        float lineH = graphics.MeasureString("Ag", titleFont).Height; // ascender+descender sample
        float lineSpacing = 2f * scale;
        float totalH = (lineH * lines.Length) + lineSpacing * Math.Max(0, lines.Length - 1);
        float pad = 4f * scale;

        float textY;
        switch (key.TextPosition)
        {
            case DisplayTextPosition.Top:
                textY = pad;
                // Dark gradient at top for readability
                using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.PointF(0, 0),
                    new System.Drawing.PointF(0, totalH + pad * 3),
                    DrawingColor.FromArgb(180, 0, 0, 0),
                    DrawingColor.FromArgb(0, 0, 0, 0)))
                    graphics.FillRectangle(grad, 0, 0, size, totalH + pad * 3);
                break;
            case DisplayTextPosition.Middle:
                textY = (size - totalH) * 0.5f;
                // Subtle center darkening
                using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.PointF(0, textY - pad * 2),
                    new System.Drawing.PointF(0, textY + totalH + pad * 2),
                    DrawingColor.FromArgb(0, 0, 0, 0),
                    DrawingColor.FromArgb(120, 0, 0, 0)))
                    graphics.FillRectangle(grad, 0, textY - pad * 2, size, pad * 2);
                using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.PointF(0, textY),
                    new System.Drawing.PointF(0, textY + totalH + pad * 2),
                    DrawingColor.FromArgb(120, 0, 0, 0),
                    DrawingColor.FromArgb(0, 0, 0, 0)))
                    graphics.FillRectangle(grad, 0, textY, size, totalH + pad * 2);
                break;
            default: // Bottom
                textY = size - totalH - pad;
                // Dark gradient at bottom for readability
                using (var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.PointF(0, textY - pad * 3),
                    new System.Drawing.PointF(0, size),
                    DrawingColor.FromArgb(0, 0, 0, 0),
                    DrawingColor.FromArgb(180, 0, 0, 0)))
                    graphics.FillRectangle(grad, 0, textY - pad * 3, size, size - textY + pad * 3);
                break;
        }

        // Draw each line centered horizontally, stacked vertically.
        for (int li = 0; li < lines.Length; li++)
        {
            float lineY = textY + li * (lineH + lineSpacing);
            graphics.DrawString(lines[li], titleFont, textBrush,
                new DrawingRectangleF(2 * scale, lineY, size - 4 * scale, lineH),
                format);
        }
    }

    private static byte[] EncodeForDevice(DrawingImage image)
    {
        using var deviceSized = new DrawingBitmap(DeviceCanvasSize, DeviceCanvasSize);
        using (var graphics = DrawingGraphics.FromImage(deviceSized))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(DrawingColor.Black);
            graphics.DrawImage(image, 0, 0, DeviceCanvasSize, DeviceCanvasSize);

            // Zero out the extreme corners before encoding. Small sparkle /
            // glow detail in icon art produces visible JPEG blocking on the
            // dark corners once the tile is shown with a rounded-corner
            // clip — the artifacts appeared as little white squares at the
            // edges. Clearing the pixels the UI mask will hide anyway gives
            // the encoder clean uniform black in those 8x8 blocks.
            using var cornerBrush = new DrawingBrush(DrawingColor.Black);
            graphics.FillRectangle(cornerBrush, 0, 0, 4, 4);
            graphics.FillRectangle(cornerBrush, DeviceCanvasSize - 4, 0, 4, 4);
            graphics.FillRectangle(cornerBrush, 0, DeviceCanvasSize - 4, 4, 4);
            graphics.FillRectangle(cornerBrush, DeviceCanvasSize - 4, DeviceCanvasSize - 4, 4, 4);
        }

        deviceSized.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);

        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        // Quality 98 gives cleaner sharp edges on icon art at the 60x60
        // device resolution — output is still only ~4-6 KB vs ~3-4 KB at
        // q92, well within HID bandwidth.
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 98L);
        deviceSized.Save(stream, codec, encoderParams);
        return stream.ToArray();
    }

    private static void ConfigureGraphics(DrawingGraphics graphics)
    {
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    private static DrawingColor ParseColor(string hex, DrawingColor fallback)
    {
        try
        {
            return System.Drawing.ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private static DrawingBitmap RenderElementToDrawingBitmap(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new DrawingBitmap(stream);
    }

    private static bool TryParseMaterialIconKind(string? kind, out MaterialIconKind presetKind)
    {
        if (!string.IsNullOrWhiteSpace(kind) && Enum.TryParse(kind, out presetKind))
            return true;

        presetKind = default;
        return false;
    }

    private static System.Windows.Media.Color TryParseMediaColor(string hex, System.Windows.Media.Color fallback)
    {
        try
        {
            var parsed = ColorConverter.ConvertFromString(hex);
            if (parsed is System.Windows.Media.Color color)
                return color;
        }
        catch
        {
        }

        return fallback;
    }
}
