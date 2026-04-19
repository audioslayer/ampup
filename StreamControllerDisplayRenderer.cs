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

    public static BitmapSource CreatePreview(StreamControllerDisplayKeyConfig key)
    {
        using var bitmap = ComposeImage(key);
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
        using var bitmap = ComposeImage(key);
        return EncodeForDevice(bitmap);
    }

    public static byte[] CreateDeviceJpegFromPath(string imagePath)
    {
        using var source = DrawingImage.FromFile(imagePath);
        using var canvas = RenderSourceToCanvasCover(source, RenderCanvasSize);
        return EncodeForDevice(canvas);
    }

    private static DrawingBitmap ComposeImage(StreamControllerDisplayKeyConfig key)
    {
        string title = key.Title?.Trim() ?? "";
        string subtitle = key.Subtitle?.Trim() ?? "";
        bool hasText = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(subtitle);

        if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
        {
            using var source = DrawingImage.FromFile(key.ImagePath);
            return RenderSourceToCanvasCover(source, RenderCanvasSize);
        }

        if (TryParseMaterialIconKind(key.PresetIconKind, out var presetKind))
        {
            return RenderPresetIconCanvas(key, presetKind, RenderCanvasSize, title, subtitle);
        }

        var bitmap = new DrawingBitmap(RenderCanvasSize, RenderCanvasSize);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(hasText
            ? ParseColor(key.BackgroundColor, DrawingColor.FromArgb(0x1C, 0x1C, 0x1C))
            : DrawingColor.Black);

        if (hasText)
            DrawKeyCard(graphics, key, RenderCanvasSize, title, subtitle);

        return bitmap;
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
        string title,
        string subtitle)
    {
        bool hasText = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(subtitle);
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

        if (hasText)
        {
            root.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = size * 0.34,
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromArgb(0x00, 0, 0, 0),
                    System.Windows.Media.Color.FromArgb(0xAA, 0, 0, 0),
                    90)
            });
        }

        var icon = new MaterialIcon
        {
            Kind = presetKind,
            Width = size * (hasText ? 0.56 : 0.68),
            Height = size * (hasText ? 0.56 : 0.68),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF7, 0xF7, 0xF7)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, size * (hasText ? -0.12 : 0), 0, 0)
        };
        root.Children.Add(icon);

        if (hasText)
        {
            var textStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(size * 0.08, 0, size * 0.08, size * 0.07)
            };

            if (!string.IsNullOrWhiteSpace(title))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5)),
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = size * 0.085,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                textStack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0xE8, 0xE8, 0xE8)),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = size * 0.06,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            root.Children.Add(textStack);
        }

        return RenderElementToDrawingBitmap(root, size, size);
    }

    private static void DrawKeyCard(DrawingGraphics graphics, StreamControllerDisplayKeyConfig key, int size, string title, string subtitle)
    {
        float scale = size / 60f;
        var accent = ParseColor(key.AccentColor, DrawingColor.FromArgb(0x00, 0xE6, 0x76));

        using var accentBrush = new DrawingBrush(accent);
        using var shadowBrush = new DrawingBrush(DrawingColor.FromArgb(120, 0, 0, 0));
        using var whiteBrush = new DrawingBrush(DrawingColor.White);
        using var borderPen = new DrawingPen(DrawingColor.FromArgb(180, accent.R, accent.G, accent.B), 2f * scale);

        graphics.FillRectangle(shadowBrush, 0, 38f * scale, size, 22f * scale);
        graphics.FillRectangle(accentBrush, 0, 0, size, 10f * scale);
        graphics.DrawRectangle(borderPen, 1f * scale, 1f * scale, size - (3f * scale), size - (3f * scale));

        using var titleFont = new DrawingFont("Segoe UI", 10f * scale, System.Drawing.FontStyle.Bold, DrawingGraphicsUnit.Pixel);
        using var subFont = new DrawingFont("Segoe UI", 8f * scale, System.Drawing.FontStyle.Regular, DrawingGraphicsUnit.Pixel);
        using var badgeFont = new DrawingFont("Segoe UI", 18f * scale, System.Drawing.FontStyle.Bold, DrawingGraphicsUnit.Pixel);

        var center = new DrawingStringFormat
        {
            Alignment = DrawingStringAlignment.Center,
            LineAlignment = DrawingStringAlignment.Center
        };

        graphics.DrawString((key.Idx + 1).ToString(), badgeFont, whiteBrush, new DrawingRectangleF(0, 8f * scale, size, 22f * scale), center);
        if (!string.IsNullOrWhiteSpace(title))
            graphics.DrawString(title, titleFont, whiteBrush, new DrawingRectangleF(4f * scale, 30f * scale, 52f * scale, 14f * scale), center);
        if (!string.IsNullOrWhiteSpace(subtitle))
            graphics.DrawString(subtitle, subFont, whiteBrush, new DrawingRectangleF(4f * scale, 44f * scale, 52f * scale, 10f * scale), center);
    }

    private static byte[] EncodeForDevice(DrawingImage image)
    {
        using var deviceSized = new DrawingBitmap(DeviceCanvasSize, DeviceCanvasSize);
        using (var graphics = DrawingGraphics.FromImage(deviceSized))
        {
            ConfigureGraphics(graphics);
            graphics.Clear(DrawingColor.Black);
            graphics.DrawImage(image, 0, 0, DeviceCanvasSize, DeviceCanvasSize);
        }

        deviceSized.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);

        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
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
