using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AmpUp.Core.Models;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingBrush = System.Drawing.SolidBrush;
using DrawingPen = System.Drawing.Pen;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingStringAlignment = System.Drawing.StringAlignment;
using DrawingStringFormat = System.Drawing.StringFormat;
using DrawingLinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AmpUp;

internal static class StreamControllerDisplayRenderer
{
    internal readonly record struct StreamControllerEffectFrame(int Tick, float[]? AudioBands);

    public static BitmapSource CreatePreview(StreamControllerDisplayKeyConfig key, N3Config? n3 = null, StreamControllerEffectFrame? frame = null)
    {
        using var bitmap = ComposeImage(key, n3, frame);
        return ToBitmapSource(bitmap);
    }

    public static BitmapSource CreateHardwarePreview(StreamControllerDisplayKeyConfig key, N3Config? n3 = null, StreamControllerEffectFrame? frame = null)
    {
        var jpeg = CreateDeviceJpeg(key, n3, frame);
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

    public static byte[] CreateDeviceJpeg(StreamControllerDisplayKeyConfig key, N3Config? n3 = null, StreamControllerEffectFrame? frame = null)
    {
        using var bitmap = ComposeImage(key, n3, frame);
        return EncodeForDevice(bitmap);
    }

    public static byte[] CreateDeviceJpegFromPath(string imagePath)
    {
        using var source = DrawingImage.FromFile(imagePath);
        using var canvas = new DrawingBitmap(60, 60);
        using var graphics = DrawingGraphics.FromImage(canvas);
        graphics.Clear(DrawingColor.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = Math.Min(60f / source.Width, 60f / source.Height);
        float drawWidth = source.Width * scale;
        float drawHeight = source.Height * scale;
        float x = (60f - drawWidth) / 2f;
        float y = (60f - drawHeight) / 2f;
        graphics.DrawImage(source, x, y, drawWidth, drawHeight);

        return EncodeForDevice(canvas);
    }

    private static DrawingBitmap ComposeImage(StreamControllerDisplayKeyConfig key, N3Config? n3, StreamControllerEffectFrame? frame)
    {
        if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
        {
            using var source = DrawingImage.FromFile(key.ImagePath);
            var loaded = new DrawingBitmap(60, 60);
            using var graphics = DrawingGraphics.FromImage(loaded);
            graphics.Clear(DrawingColor.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float scale = Math.Min(60f / source.Width, 60f / source.Height);
            float drawWidth = source.Width * scale;
            float drawHeight = source.Height * scale;
            float x = (60f - drawWidth) / 2f;
            float y = (60f - drawHeight) / 2f;
            graphics.DrawImage(source, x, y, drawWidth, drawHeight);
            ApplyEffectOverlay(loaded, n3, frame);
            return loaded;
        }

        var bitmap = new DrawingBitmap(60, 60);
        using var graphicsCard = DrawingGraphics.FromImage(bitmap);
        graphicsCard.SmoothingMode = SmoothingMode.AntiAlias;
        graphicsCard.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphicsCard.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphicsCard.Clear(ParseColor(key.BackgroundColor, DrawingColor.FromArgb(0x1C, 0x1C, 0x1C)));

        var accent = ParseColor(key.AccentColor, DrawingColor.FromArgb(0x00, 0xE6, 0x76));
        using var accentBrush = new DrawingBrush(accent);
        using var shadowBrush = new DrawingBrush(DrawingColor.FromArgb(120, 0, 0, 0));
        using var whiteBrush = new DrawingBrush(DrawingColor.White);
        using var borderPen = new DrawingPen(DrawingColor.FromArgb(180, accent.R, accent.G, accent.B), 2f);

        graphicsCard.FillRectangle(shadowBrush, 0, 38, 60, 22);
        graphicsCard.FillRectangle(accentBrush, 0, 0, 60, 10);
        graphicsCard.DrawRectangle(borderPen, 1, 1, 57, 57);

        using var titleFont = new DrawingFont("Segoe UI", 10f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
        using var subFont = new DrawingFont("Segoe UI", 8f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
        using var badgeFont = new DrawingFont("Segoe UI", 18f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);

        string title = key.Title?.Trim() ?? "";
        string subtitle = key.Subtitle?.Trim() ?? "";
        bool hasText = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(subtitle);

        var center = new DrawingStringFormat
        {
            Alignment = DrawingStringAlignment.Center,
            LineAlignment = DrawingStringAlignment.Center
        };

        if (hasText)
        {
            graphicsCard.DrawString((key.Idx + 1).ToString(), badgeFont, whiteBrush, new DrawingRectangleF(0, 8, 60, 22), center);
            if (!string.IsNullOrWhiteSpace(title))
                graphicsCard.DrawString(title, titleFont, whiteBrush, new DrawingRectangleF(4, 30, 52, 14), center);
            if (!string.IsNullOrWhiteSpace(subtitle))
                graphicsCard.DrawString(subtitle, subFont, whiteBrush, new DrawingRectangleF(4, 44, 52, 10), center);
        }

        ApplyEffectOverlay(bitmap, n3, frame);

        return bitmap;
    }

    private static void ApplyEffectOverlay(DrawingBitmap bitmap, N3Config? n3, StreamControllerEffectFrame? frame)
    {
        if (n3 == null || !n3.ScreensaverEnabled)
            return;

        int opacity = Math.Clamp(n3.ScreensaverOpacity, 0, 100);
        if (opacity <= 0)
            return;

        int tick = frame?.Tick ?? Environment.TickCount;
        var audioBands = frame?.AudioBands ?? Array.Empty<float>();

        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        switch (n3.ScreensaverEffect)
        {
            case StreamControllerScreensaverEffect.Fire:
                DrawFireOverlay(graphics, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.MusicBounce:
                DrawMusicBounceOverlay(graphics, tick, opacity, n3.ScreensaverSpeed, audioBands);
                break;
            default:
                DrawRainbowOverlay(graphics, tick, opacity, n3.ScreensaverSpeed);
                break;
        }
    }

    private static void DrawRainbowOverlay(DrawingGraphics graphics, int tick, int opacity, int speed)
    {
        float shift = (tick / 1000f) * (0.7f + (speed / 70f));
        for (int i = -1; i < 5; i++)
        {
            float start = i * 18f + ((shift * 18f) % 18f);
            var c1 = FromHsv((i * 68 + tick / 18) % 360, 0.82, 1.0, opacity / 100f * 0.75f);
            var c2 = FromHsv((i * 68 + 48 + tick / 18) % 360, 0.9, 1.0, opacity / 100f * 0.15f);
            using var brush = new DrawingLinearGradientBrush(
                new System.Drawing.PointF(start, 0),
                new System.Drawing.PointF(start + 18, 60),
                c1,
                c2);
            graphics.FillRectangle(brush, start, 0, 18, 60);
        }
    }

    private static void DrawFireOverlay(DrawingGraphics graphics, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        for (int x = 0; x < 60; x += 6)
        {
            double wave = (Math.Sin((tick / (120.0 - speed)) + x * 0.35) + 1.0) * 0.5;
            double wave2 = (Math.Sin((tick / (85.0 - speed * 0.5)) + x * 0.18 + 1.5) + 1.0) * 0.5;
            float height = 16f + (float)((wave * 18f) + (wave2 * 10f));
            int y = (int)(60 - height);

            using var glow = new DrawingLinearGradientBrush(
                new System.Drawing.PointF(x, 60),
                new System.Drawing.PointF(x, y),
                DrawingColor.FromArgb((int)(160 * alpha), 255, 210, 70),
                DrawingColor.FromArgb(0, 255, 80, 20));
            graphics.FillEllipse(glow, x - 4, y - 8, 14, height + 12);

            using var coreBrush = new DrawingBrush(DrawingColor.FromArgb((int)(110 * alpha), 255, 120, 20));
            graphics.FillRectangle(coreBrush, x, y + 8, 6, height - 6);
        }
    }

    private static void DrawMusicBounceOverlay(DrawingGraphics graphics, int tick, int opacity, int speed, float[] audioBands)
    {
        float alpha = opacity / 100f;
        float[] bands = audioBands.Length >= 5 ? audioBands : new float[] { 0.1f, 0.2f, 0.15f, 0.1f, 0.08f };
        int[] bandMap = { 0, 1, 2, 3, 4, 3 };
        for (int i = 0; i < 6; i++)
        {
            float band = bands[bandMap[i]];
            float pulse = 0.4f + 0.6f * band;
            float bob = (float)Math.Sin((tick / (140.0 - speed)) + i * 0.9) * 4f;
            float height = 12f + band * 32f;
            float x = 4 + i * 9;
            float y = 44 - height - bob;
            var color = FromHsv((120 + i * 22 + tick / 25) % 360, 0.7, 1.0, alpha * (0.35f + pulse * 0.45f));
            using var brush = new DrawingBrush(color);
            using var path = CreateRoundedRectPath(x, y, 6, height, 3);
            graphics.FillPath(brush, path);
        }
    }

    private static byte[] EncodeForDevice(DrawingImage image)
    {
        using var rotated = new DrawingBitmap(image);
        rotated.RotateFlip(System.Drawing.RotateFlipType.Rotate90FlipNone);

        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
        rotated.Save(stream, codec, encoderParams);
        return stream.ToArray();
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

    private static DrawingColor FromHsv(int hue, double saturation, double value, float alpha = 1f)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60d % 2) - 1));
        double m = value - c;
        double r, g, b;
        if (hue < 60) (r, g, b) = (c, x, 0);
        else if (hue < 120) (r, g, b) = (x, c, 0);
        else if (hue < 180) (r, g, b) = (0, c, x);
        else if (hue < 240) (r, g, b) = (0, x, c);
        else if (hue < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return DrawingColor.FromArgb(
            (int)(Math.Clamp(alpha, 0f, 1f) * 255),
            (int)((r + m) * 255),
            (int)((g + m) * 255),
            (int)((b + m) * 255));
    }

    private static GraphicsPath CreateRoundedRectPath(float x, float y, float width, float height, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
