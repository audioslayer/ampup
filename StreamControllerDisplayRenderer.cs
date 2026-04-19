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
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AmpUp;

internal static class StreamControllerDisplayRenderer
{
    public static BitmapSource CreatePreview(StreamControllerDisplayKeyConfig key)
    {
        using var bitmap = ComposeImage(key);
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

    private static DrawingBitmap ComposeImage(StreamControllerDisplayKeyConfig key)
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

        string title = string.IsNullOrWhiteSpace(key.Title) ? $"K{key.Idx + 1}" : key.Title.Trim();
        string subtitle = key.Subtitle?.Trim() ?? "";

        var center = new DrawingStringFormat
        {
            Alignment = DrawingStringAlignment.Center,
            LineAlignment = DrawingStringAlignment.Center
        };

        graphicsCard.DrawString((key.Idx + 1).ToString(), badgeFont, whiteBrush, new DrawingRectangleF(0, 8, 60, 22), center);
        graphicsCard.DrawString(title, titleFont, whiteBrush, new DrawingRectangleF(4, 30, 52, 14), center);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            graphicsCard.DrawString(subtitle, subFont, whiteBrush, new DrawingRectangleF(4, 44, 52, 10), center);
        }

        return bitmap;
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
}
