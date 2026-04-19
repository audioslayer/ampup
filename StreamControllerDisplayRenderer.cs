using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AmpUp.Core.Models;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrush = System.Drawing.SolidBrush;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingImage = System.Drawing.Image;
using DrawingLinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using DrawingPen = System.Drawing.Pen;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.Size;
using DrawingStringAlignment = System.Drawing.StringAlignment;
using DrawingStringFormat = System.Drawing.StringFormat;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AmpUp;

internal static class StreamControllerDisplayRenderer
{
    private const int RenderCanvasSize = 240;
    private const int DeviceCanvasSize = 60;
    private const int SceneTileSize = 160;
    private const int SceneStripWidth = SceneTileSize * N3Controller.DisplayKeyCount;

    internal sealed class StreamControllerEffectFrame : IDisposable
    {
        public int Tick { get; }
        public float[] AudioBands { get; }
        public DrawingBitmap? EffectStrip { get; }

        public StreamControllerEffectFrame(int tick, float[]? audioBands, DrawingBitmap? effectStrip)
        {
            Tick = tick;
            AudioBands = audioBands ?? Array.Empty<float>();
            EffectStrip = effectStrip;
        }

        public void Dispose() => EffectStrip?.Dispose();
    }

    public static StreamControllerEffectFrame CreateFrame(N3Config? n3, int tick, float[]? audioBands, int monitorIndex = 0)
    {
        DrawingBitmap? effectStrip = null;
        if (n3?.ScreensaverEnabled == true)
        {
            effectStrip = n3.ScreensaverEffect == StreamControllerScreensaverEffect.ScreenSync
                ? CreateScreenSceneStrip(opacity: Math.Clamp(n3.ScreensaverOpacity, 0, 100), monitorIndex)
                : CreateAnimatedSceneStrip(n3, tick, audioBands);
        }

        return new StreamControllerEffectFrame(tick, audioBands, effectStrip);
    }

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
        using var bitmap = RenderKeyBitmap(key, n3, frame);
        return EncodeForDevice(bitmap);
    }

    public static byte[] CreateDeviceJpegFromPath(string imagePath)
    {
        using var source = DrawingImage.FromFile(imagePath);
        using var canvas = RenderSourceToCanvasCover(source, RenderCanvasSize);
        return EncodeForDevice(canvas);
    }

    private static DrawingBitmap RenderKeyBitmap(StreamControllerDisplayKeyConfig key, N3Config? n3, StreamControllerEffectFrame? frame)
    {
        if (n3?.ScreensaverEnabled == true && frame?.EffectStrip != null)
            return ExtractKeyScene(frame.EffectStrip, key.Idx);

        return ComposeImage(key, n3, frame);
    }

    private static DrawingBitmap ComposeImage(StreamControllerDisplayKeyConfig key, N3Config? n3, StreamControllerEffectFrame? frame)
    {
        string title = key.Title?.Trim() ?? "";
        string subtitle = key.Subtitle?.Trim() ?? "";
        bool hasText = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(subtitle);

        if (!string.IsNullOrWhiteSpace(key.ImagePath) && File.Exists(key.ImagePath))
        {
            using var source = DrawingImage.FromFile(key.ImagePath);
            var loaded = RenderSourceToCanvasCover(source, RenderCanvasSize);
            ApplyEffectOverlay(loaded, key.Idx, n3, frame);
            return loaded;
        }

        var bitmap = new DrawingBitmap(RenderCanvasSize, RenderCanvasSize);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(hasText
            ? ParseColor(key.BackgroundColor, DrawingColor.FromArgb(0x1C, 0x1C, 0x1C))
            : DrawingColor.Black);

        if (hasText)
            DrawKeyCard(graphics, key, RenderCanvasSize, title, subtitle);
        ApplyEffectOverlay(bitmap, key.Idx, n3, frame);

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

    private static void ApplyEffectOverlay(DrawingBitmap bitmap, int keyIdx, N3Config? n3, StreamControllerEffectFrame? frame)
    {
        if (n3 == null || !n3.ScreensaverEnabled)
            return;

        int opacity = Math.Clamp(n3.ScreensaverOpacity, 0, 100);
        if (opacity <= 0)
            return;

        int tick = frame?.Tick ?? Environment.TickCount;
        var audioBands = frame?.AudioBands ?? Array.Empty<float>();

        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);

        switch (n3.ScreensaverEffect)
        {
            case StreamControllerScreensaverEffect.Aurora:
                DrawAuroraOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Fire:
                DrawLavaOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Prism:
                DrawPrismOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.MusicBounce:
                DrawMusicOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed, audioBands);
                break;
            case StreamControllerScreensaverEffect.ScreenSync:
                DrawScreenSyncOverlay(graphics, bitmap.Width, bitmap.Height, opacity, frame?.EffectStrip);
                break;
            default:
                DrawRainbowOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
        }
    }

    private static DrawingBitmap CreateAnimatedSceneStrip(N3Config n3, int tick, float[]? audioBands)
    {
        var bitmap = new DrawingBitmap(SceneStripWidth, SceneTileSize);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(DrawingColor.Black);

        int opacity = Math.Clamp(n3.ScreensaverOpacity, 0, 100);
        var bands = audioBands ?? Array.Empty<float>();

        switch (n3.ScreensaverEffect)
        {
            case StreamControllerScreensaverEffect.Aurora:
                DrawAuroraOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Fire:
                DrawLavaOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Prism:
                DrawPrismOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.MusicBounce:
                DrawMusicOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed, bands);
                break;
            default:
                DrawRainbowOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
        }

        return bitmap;
    }

    private static DrawingBitmap? CreateScreenSceneStrip(int opacity, int monitorIndex)
    {
        using var capture = ScreenCapture.CapturePreviewFrame(monitorIndex, SceneStripWidth, SceneTileSize);
        if (capture == null)
            return null;

        var bitmap = new DrawingBitmap(SceneStripWidth, SceneTileSize);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(CreateSaturationMatrix(1.35f, Math.Clamp(opacity / 100f, 0f, 1f)));
        graphics.DrawImage(
            capture,
            new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height),
            0,
            0,
            capture.Width,
            capture.Height,
            System.Drawing.GraphicsUnit.Pixel,
            attributes);

        using var gloss = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, bitmap.Height),
            DrawingColor.FromArgb((int)(opacity * 1.1f), 255, 255, 255),
            DrawingColor.FromArgb(0, 255, 255, 255));
        graphics.FillRectangle(gloss, 0, 0, bitmap.Width, bitmap.Height * 0.2f);

        return bitmap;
    }

    private static DrawingBitmap ExtractKeyScene(DrawingBitmap sceneStrip, int keyIdx)
    {
        int idx = Math.Clamp(keyIdx, 0, N3Controller.DisplayKeyCount - 1);
        int sliceWidth = Math.Max(1, sceneStrip.Width / N3Controller.DisplayKeyCount);
        int srcX = Math.Clamp(idx * sliceWidth, 0, Math.Max(0, sceneStrip.Width - sliceWidth));

        var bitmap = new DrawingBitmap(RenderCanvasSize, RenderCanvasSize);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        graphics.Clear(DrawingColor.Black);
        graphics.DrawImage(
            sceneStrip,
            new DrawingRectangle(0, 0, RenderCanvasSize, RenderCanvasSize),
            srcX,
            0,
            sliceWidth,
            sceneStrip.Height,
            System.Drawing.GraphicsUnit.Pixel);
        return bitmap;
    }

    private static void DrawRainbowOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.35f + speed / 70f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(width, height),
            DrawingColor.FromArgb((int)(70 * alpha), 6, 8, 22),
            DrawingColor.FromArgb((int)(35 * alpha), 18, 6, 28));
        graphics.FillRectangle(bg, 0, 0, width, height);

        for (int i = 0; i < 6; i++)
        {
            float hue = (time * 72f + i * 48f) % 360f;
            float cx = width * (0.16f + i * 0.15f + 0.06f * (float)Math.Sin(time * 1.1f + i * 0.85f));
            float cy = height * (0.28f + 0.16f * (float)Math.Sin(time * 1.4f + i * 0.6f));
            FillGlow(graphics, cx - width * 0.30f, cy - height * 0.34f, width * 0.60f, height * 0.68f, FromHsv((int)hue, 0.82, 1.0, alpha * 0.28f));
        }

        for (int i = -1; i < 4; i++)
        {
            float start = i * width * 0.26f + (time * width * 0.18f % (width * 0.26f));
            var c1 = FromHsv((int)((time * 88f + i * 54f) % 360), 0.85, 1.0, alpha * 0.32f);
            var c2 = FromHsv((int)((time * 88f + i * 54f + 55f) % 360), 0.7, 1.0, 0f);
            using var streak = new DrawingLinearGradientBrush(
                new DrawingPointF(start, 0),
                new DrawingPointF(start + width * 0.18f, height),
                c1,
                c2);
            graphics.FillRectangle(streak, start, 0, width * 0.18f, height);
        }
    }

    private static void DrawAuroraOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.20f + speed / 110f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, height),
            DrawingColor.FromArgb((int)(70 * alpha), 3, 10, 18),
            DrawingColor.FromArgb((int)(90 * alpha), 2, 22, 18));
        graphics.FillRectangle(bg, 0, 0, width, height);

        DrawingColor[] colors =
        {
            DrawingColor.FromArgb((int)(alpha * 120), 70, 255, 185),
            DrawingColor.FromArgb((int)(alpha * 110), 48, 220, 255),
            DrawingColor.FromArgb((int)(alpha * 90), 154, 102, 255),
        };

        for (int i = 0; i < colors.Length; i++)
        {
            float cx = width * (0.12f + i * 0.26f + 0.10f * (float)Math.Sin(time * 1.2f + i));
            float cy = height * (0.46f + 0.08f * (float)Math.Sin(time * 1.6f + i * 1.4f));
            FillGlow(graphics, cx - width * 0.22f, cy - height * 0.42f, width * 0.44f, height * 0.88f, colors[i]);
        }

        for (int line = 0; line < 7; line++)
        {
            float x = width * (0.05f + line * 0.15f);
            float sway = Math.Max(16f, width * 0.018f) * (float)Math.Sin(time * 2.0f + line * 0.9f);
            var color = line % 2 == 0
                ? DrawingColor.FromArgb((int)(alpha * 58), 160, 255, 214)
                : DrawingColor.FromArgb((int)(alpha * 46), 92, 225, 255);
            using var pen = new DrawingPen(color, Math.Max(4f, width * 0.03f));
            graphics.DrawBezier(
                pen,
                new DrawingPointF(x, height * 0.08f),
                new DrawingPointF(x + sway, height * 0.28f),
                new DrawingPointF(x - sway, height * 0.72f),
                new DrawingPointF(x + sway * 0.4f, height * 0.96f));
        }
    }

    private static void DrawLavaOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.35f + speed / 75f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, height),
            new DrawingPointF(0, 0),
            DrawingColor.FromArgb((int)(110 * alpha), 255, 115, 10),
            DrawingColor.FromArgb((int)(70 * alpha), 40, 6, 3));
        graphics.FillRectangle(bg, 0, 0, width, height);

        for (int i = 0; i < 5; i++)
        {
            float cx = width * (0.10f + i * 0.18f + 0.06f * (float)Math.Sin(time * 0.9f + i));
            float cy = height * (0.85f - 0.22f * (float)Math.Sin(time * 1.3f + i * 0.9f));
            float size = width * (0.22f + 0.05f * (float)Math.Sin(time * 1.9f + i));
            FillGlow(graphics, cx - size * 0.7f, cy - size * 0.9f, size * 1.4f, size * 1.8f, DrawingColor.FromArgb((int)(alpha * 140), 255, 180, 38));
            FillGlow(graphics, cx - size * 0.45f, cy - size * 0.5f, size * 0.9f, size, DrawingColor.FromArgb((int)(alpha * 100), 255, 92, 0));
        }

        for (int ember = 0; ember < 12; ember++)
        {
            float px = width * (float)Rand(ember * 17 + 31);
            float phase = ((tick / 850f) + ember * 0.19f) % 1f;
            float py = height * (1f - phase);
            float radius = Math.Max(2f, width * 0.012f * (1f + 0.8f * (float)Rand(ember * 41)));
            using var brush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 180 * (1f - phase)), 255, 220, 120));
            graphics.FillEllipse(brush, px - radius, py - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawPrismOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.28f + speed / 100f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(width, height),
            DrawingColor.FromArgb((int)(60 * alpha), 12, 9, 26),
            DrawingColor.FromArgb((int)(40 * alpha), 8, 4, 20));
        graphics.FillRectangle(bg, 0, 0, width, height);

        for (int i = -1; i < 6; i++)
        {
            float x = i * width * 0.18f + (time * width * 0.22f % (width * 0.18f));
            int hue = (int)((i * 52f + time * 92f) % 360f);
            var c1 = FromHsv(hue, 0.85, 1.0, alpha * 0.42f);
            var c2 = FromHsv((hue + 45) % 360, 0.65, 1.0, 0f);
            using var brush = new DrawingLinearGradientBrush(
                new DrawingPointF(x, 0),
                new DrawingPointF(x + width * 0.20f, height),
                c1,
                c2);
            DrawingPointF[] points =
            {
                new(x, 0),
                new(x + width * 0.17f, 0),
                new(x + width * 0.32f, height),
                new(x + width * 0.10f, height),
            };
            graphics.FillPolygon(brush, points);
        }

        FillGlow(graphics, width * 0.18f, height * 0.14f, width * 0.64f, height * 0.5f, DrawingColor.FromArgb((int)(alpha * 55), 255, 255, 255));
    }

    private static void DrawMusicOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed, float[] audioBands)
    {
        float alpha = opacity / 100f;
        float[] bands = audioBands.Length >= 5 ? audioBands : new float[] { 0.1f, 0.18f, 0.13f, 0.08f, 0.05f };
        float time = tick / 1000f * (0.35f + speed / 100f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, height),
            DrawingColor.FromArgb((int)(45 * alpha), 4, 8, 18),
            DrawingColor.FromArgb((int)(85 * alpha), 6, 2, 20));
        graphics.FillRectangle(bg, 0, 0, width, height);

        int barCount = 18;
        float spacing = width / (barCount + 1f);
        float barWidth = Math.Max(12f, width * 0.028f);

        for (int i = 0; i < barCount; i++)
        {
            float band = Math.Clamp(bands[i % bands.Length], 0f, 1f);
            float shimmer = 0.14f * (float)Math.Sin(time * 5f + i * 0.8f);
            float heightPct = Math.Clamp(0.16f + band * 0.78f + shimmer, 0.08f, 0.92f);
            float barHeight = height * heightPct;
            float x = spacing * (i + 0.55f) - barWidth * 0.5f;
            float y = height - (barHeight + height * 0.08f);
            var top = FromHsv((int)((145 + i * 22 + tick / 20) % 360), 0.62, 1.0, alpha * 0.95f);
            var bottom = FromHsv((int)((185 + i * 18 + tick / 28) % 360), 0.92, 0.95, alpha * 0.72f);

            FillGlow(graphics, x - barWidth, y - barWidth * 1.4f, barWidth * 3f, barHeight + barWidth * 2.2f, DrawingColor.FromArgb((int)(alpha * 80), top.R, top.G, top.B));

            using var path = CreateRoundedRectPath(x, y, barWidth, barHeight, barWidth * 0.45f);
            using var brush = new DrawingLinearGradientBrush(
                new DrawingPointF(x, y),
                new DrawingPointF(x, y + barHeight),
                top,
                bottom);
            graphics.FillPath(brush, path);

            using var capBrush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 160), 255, 255, 255));
            graphics.FillEllipse(capBrush, x + barWidth * 0.18f, y + barWidth * 0.12f, barWidth * 0.64f, barWidth * 0.18f);
        }
    }

    private static void DrawScreenSyncOverlay(DrawingGraphics graphics, int width, int height, int opacity, DrawingBitmap? screenStrip)
    {
        if (screenStrip == null)
        {
            DrawPrismOverlay(graphics, width, height, Environment.TickCount, opacity, 55);
            return;
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(CreateSaturationMatrix(1.30f, opacity / 100f));
        graphics.DrawImage(
            screenStrip,
            new DrawingRectangle(0, 0, width, height),
            0,
            0,
            screenStrip.Width,
            screenStrip.Height,
            System.Drawing.GraphicsUnit.Pixel,
            attributes);

        using var gloss = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, height),
            DrawingColor.FromArgb((int)(opacity * 1.1), 255, 255, 255),
            DrawingColor.FromArgb(0, 255, 255, 255));
        graphics.FillRectangle(gloss, 0, 0, width, height * 0.22f);

        using var borderPen = new DrawingPen(DrawingColor.FromArgb((int)(opacity * 1.5), 255, 255, 255), Math.Max(2f, width * 0.012f));
        graphics.DrawRectangle(borderPen, width * 0.02f, height * 0.02f, width * 0.96f, height * 0.96f);
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

    private static void FillGlow(DrawingGraphics graphics, float x, float y, float width, float height, DrawingColor centerColor)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(x, y, width, height);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = centerColor,
            SurroundColors = new[] { DrawingColor.FromArgb(0, centerColor.R, centerColor.G, centerColor.B) }
        };
        graphics.FillPath(brush, path);
    }

    private static ColorMatrix CreateSaturationMatrix(float saturation, float alpha)
    {
        const float rw = 0.3086f;
        const float gw = 0.6094f;
        const float bw = 0.0820f;
        float inv = 1f - saturation;

        return new ColorMatrix(new[]
        {
            new[] { inv * rw + saturation, inv * rw, inv * rw, 0f, 0f },
            new[] { inv * gw, inv * gw + saturation, inv * gw, 0f, 0f },
            new[] { inv * bw, inv * bw, inv * bw + saturation, 0f, 0f },
            new[] { 0f, 0f, 0f, Math.Clamp(alpha, 0f, 1f), 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        });
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

    private static double Rand(int seed)
    {
        uint x = (uint)seed * 2654435761u;
        x ^= x >> 13;
        x *= 1597334677u;
        x ^= x >> 16;
        return (x & 0xFFFFFF) / (double)0x1000000;
    }
}
