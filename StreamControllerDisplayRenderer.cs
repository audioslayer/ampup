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
    private const int RenderCanvasSize = 126;
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

        if (TryParseMaterialIconKind(key.PresetIconKind, out var presetKind))
        {
            var iconBitmap = RenderPresetIconCanvas(key, presetKind, RenderCanvasSize, title, subtitle);
            ApplyEffectOverlay(iconBitmap, key.Idx, n3, frame);
            return iconBitmap;
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
            case StreamControllerScreensaverEffect.Ocean:
                DrawOceanOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Matrix:
                DrawMatrixOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Plasma:
                DrawPlasmaOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Nebula:
                DrawNebulaOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Starfield:
                DrawStarfieldOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Lightning:
                DrawLightningOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Cyber:
                DrawCyberOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.GradientFlow:
                DrawGradientFlowOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
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
            case StreamControllerScreensaverEffect.Ocean:
                DrawOceanOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Matrix:
                DrawMatrixOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Plasma:
                DrawPlasmaOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Nebula:
                DrawNebulaOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Starfield:
                DrawStarfieldOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Lightning:
                DrawLightningOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.Cyber:
                DrawCyberOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
                break;
            case StreamControllerScreensaverEffect.GradientFlow:
                DrawGradientFlowOverlay(graphics, bitmap.Width, bitmap.Height, tick, opacity, n3.ScreensaverSpeed);
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

    private static void DrawOceanOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.25f + speed / 80f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, height),
            DrawingColor.FromArgb((int)(90 * alpha), 2, 12, 28),
            DrawingColor.FromArgb((int)(110 * alpha), 4, 38, 58));
        graphics.FillRectangle(bg, 0, 0, width, height);

        for (int wave = 0; wave < 5; wave++)
        {
            float waveY = height * (0.30f + wave * 0.14f);
            float amp = height * (0.06f + wave * 0.018f);
            float freq = 0.008f + wave * 0.003f;
            float phase = time * (1.2f + wave * 0.4f);
            int hue = 185 + wave * 12;
            var waveColor = FromHsv(hue, 0.72, 0.95, alpha * (0.35f - wave * 0.04f));
            using var pen = new DrawingPen(waveColor, Math.Max(6f, height * 0.04f));

            var points = new DrawingPointF[(int)(width / 4f) + 2];
            for (int p = 0; p < points.Length; p++)
            {
                float px = p * 4f;
                float py = waveY + amp * (float)(Math.Sin(px * freq + phase) + 0.5 * Math.Sin(px * freq * 2.1 + phase * 1.3));
                points[p] = new DrawingPointF(px, py);
            }
            if (points.Length > 1)
                graphics.DrawCurve(pen, points, 0.5f);
        }

        for (int foam = 0; foam < 8; foam++)
        {
            float fx = width * (float)Rand(foam * 29 + 7);
            float baseY = height * (0.32f + 0.12f * (float)Rand(foam * 13 + 3));
            float fy = baseY + height * 0.04f * (float)Math.Sin(time * 2.2f + foam * 1.1f);
            float radius = Math.Max(3f, width * 0.015f * (1f + (float)Rand(foam * 47)));
            float foamPhase = (time * 1.5f + foam * 0.3f) % 3f;
            float foamAlpha = foamPhase < 1f ? foamPhase : foamPhase < 2f ? 1f : 3f - foamPhase;
            using var brush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 180 * foamAlpha), 220, 245, 255));
            graphics.FillEllipse(brush, fx - radius, fy - radius, radius * 2, radius * 2);
        }

        FillGlow(graphics, width * 0.1f, height * 0.55f, width * 0.8f, height * 0.5f, DrawingColor.FromArgb((int)(alpha * 50), 20, 140, 200));
    }

    private static void DrawMatrixOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.4f + speed / 65f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, height),
            DrawingColor.FromArgb((int)(95 * alpha), 0, 4, 0),
            DrawingColor.FromArgb((int)(60 * alpha), 0, 8, 2));
        graphics.FillRectangle(bg, 0, 0, width, height);

        int columns = Math.Max(8, width / 28);
        float colWidth = width / (float)columns;

        for (int col = 0; col < columns; col++)
        {
            float colSpeed = 0.6f + 1.4f * (float)Rand(col * 37 + 11);
            float dropLen = 4f + 8f * (float)Rand(col * 53 + 19);
            float phase = (time * colSpeed + (float)Rand(col * 71) * 20f) % (height * 0.08f + dropLen);
            float headY = phase / (height * 0.08f + dropLen) * (height + dropLen * height * 0.05f);
            float x = col * colWidth + colWidth * 0.5f;

            for (int cell = 0; cell < (int)dropLen; cell++)
            {
                float cellY = headY - cell * height * 0.05f;
                if (cellY < -height * 0.05f || cellY > height * 1.05f) continue;

                float fade = cell == 0 ? 1f : Math.Max(0f, 1f - cell / dropLen);
                int g = cell == 0 ? 255 : (int)(180 * fade + 40);
                int r = cell == 0 ? 200 : 0;
                int b = cell == 0 ? 200 : (int)(30 * fade);
                using var brush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 230 * fade), r, g, b));
                float charSize = Math.Max(4f, colWidth * 0.65f);
                graphics.FillRectangle(brush, x - charSize * 0.4f, cellY - charSize * 0.4f, charSize * 0.8f, charSize * 0.6f);
            }

            if (headY >= 0 && headY <= height)
                FillGlow(graphics, x - colWidth * 1.2f, headY - colWidth * 1.5f, colWidth * 2.4f, colWidth * 3f, DrawingColor.FromArgb((int)(alpha * 60), 100, 255, 100));
        }
    }

    private static void DrawPlasmaOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.22f + speed / 90f);

        int step = Math.Max(6, width / 40);
        for (int py = 0; py < height; py += step)
        {
            for (int px = 0; px < width; px += step)
            {
                float nx = px / (float)width;
                float ny = py / (float)height;
                float v1 = (float)Math.Sin(nx * 6f + time * 1.3f);
                float v2 = (float)Math.Sin(ny * 5f + time * 1.1f);
                float v3 = (float)Math.Sin((nx + ny) * 4f + time * 0.9f);
                float v4 = (float)Math.Sin(Math.Sqrt(nx * nx + ny * ny) * 8f - time * 1.5f);
                float v = (v1 + v2 + v3 + v4) * 0.25f;

                int hue = (int)((v + 1f) * 180f + time * 40f) % 360;
                var color = FromHsv(hue, 0.82, 0.95, alpha * 0.88f);
                using var brush = new DrawingBrush(color);
                graphics.FillRectangle(brush, px, py, step, step);
            }
        }

        FillGlow(graphics, width * 0.2f, height * 0.2f, width * 0.6f, height * 0.6f, DrawingColor.FromArgb((int)(alpha * 30), 255, 255, 255));
    }

    private static void DrawNebulaOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.18f + speed / 100f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(width, height),
            DrawingColor.FromArgb((int)(80 * alpha), 4, 2, 14),
            DrawingColor.FromArgb((int)(60 * alpha), 2, 4, 18));
        graphics.FillRectangle(bg, 0, 0, width, height);

        DrawingColor[] nebColors =
        {
            DrawingColor.FromArgb((int)(alpha * 80), 120, 40, 220),
            DrawingColor.FromArgb((int)(alpha * 70), 40, 100, 255),
            DrawingColor.FromArgb((int)(alpha * 75), 220, 50, 180),
            DrawingColor.FromArgb((int)(alpha * 65), 60, 200, 220),
            DrawingColor.FromArgb((int)(alpha * 55), 255, 120, 60),
            DrawingColor.FromArgb((int)(alpha * 60), 100, 255, 140),
        };

        for (int i = 0; i < nebColors.Length; i++)
        {
            float cx = width * (0.15f + 0.14f * i + 0.12f * (float)Math.Sin(time * 0.7f + i * 1.2f));
            float cy = height * (0.35f + 0.18f * (float)Math.Sin(time * 0.5f + i * 0.9f));
            float sx = width * (0.35f + 0.08f * (float)Math.Sin(time * 0.4f + i));
            float sy = height * (0.45f + 0.10f * (float)Math.Sin(time * 0.6f + i * 0.7f));
            FillGlow(graphics, cx - sx * 0.5f, cy - sy * 0.5f, sx, sy, nebColors[i]);
        }

        for (int s = 0; s < 20; s++)
        {
            float sx = width * (float)Rand(s * 23 + 5);
            float sy = height * (float)Rand(s * 37 + 11);
            float twinkle = 0.4f + 0.6f * (float)(0.5 + 0.5 * Math.Sin(time * 3f + s * 2.1f));
            float radius = Math.Max(1.5f, width * 0.005f * (1f + (float)Rand(s * 59)));
            using var brush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 200 * twinkle), 255, 255, 255));
            graphics.FillEllipse(brush, sx - radius, sy - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawStarfieldOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.3f + speed / 85f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(width, height),
            DrawingColor.FromArgb((int)(90 * alpha), 3, 3, 12),
            DrawingColor.FromArgb((int)(75 * alpha), 8, 4, 18));
        graphics.FillRectangle(bg, 0, 0, width, height);

        FillGlow(graphics, width * -0.1f, height * 0.5f, width * 0.5f, height * 0.6f, DrawingColor.FromArgb((int)(alpha * 18), 80, 60, 160));
        FillGlow(graphics, width * 0.6f, height * -0.1f, width * 0.5f, height * 0.5f, DrawingColor.FromArgb((int)(alpha * 14), 40, 80, 140));

        int starCount = 35;
        for (int s = 0; s < starCount; s++)
        {
            float sx = width * (float)Rand(s * 31 + 7);
            float sy = height * (float)Rand(s * 47 + 13);
            float twinkleSpeed = 1.5f + 3f * (float)Rand(s * 61 + 19);
            float twinkle = 0.2f + 0.8f * (float)(0.5 + 0.5 * Math.Sin(time * twinkleSpeed + s * 1.7f));
            float size = Math.Max(1f, width * (0.003f + 0.008f * (float)Rand(s * 73)));

            bool warm = Rand(s * 83) > 0.5;
            int r = warm ? 255 : (int)(200 + 55 * Rand(s * 91));
            int g = warm ? (int)(220 + 35 * Rand(s * 97)) : (int)(220 + 35 * Rand(s * 97));
            int b = warm ? (int)(180 + 40 * Rand(s * 101)) : 255;

            using var brush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 240 * twinkle), r, g, b));
            graphics.FillEllipse(brush, sx - size, sy - size, size * 2, size * 2);

            if (size > width * 0.006f)
            {
                using var glowBrush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 50 * twinkle), r, g, b));
                float gs = size * 3f;
                graphics.FillEllipse(glowBrush, sx - gs, sy - gs, gs * 2, gs * 2);
            }
        }
    }

    private static void DrawLightningOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.5f + speed / 60f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(0, height),
            DrawingColor.FromArgb((int)(85 * alpha), 6, 4, 18),
            DrawingColor.FromArgb((int)(70 * alpha), 12, 8, 28));
        graphics.FillRectangle(bg, 0, 0, width, height);

        for (int cloud = 0; cloud < 4; cloud++)
        {
            float cx = width * (0.1f + cloud * 0.22f + 0.05f * (float)Math.Sin(time * 0.3f + cloud));
            float cy = height * (0.08f + 0.06f * (float)Math.Sin(time * 0.4f + cloud * 1.3f));
            FillGlow(graphics, cx - width * 0.18f, cy - height * 0.12f, width * 0.36f, height * 0.24f,
                DrawingColor.FromArgb((int)(alpha * 35), 100, 100, 140));
        }

        for (int bolt = 0; bolt < 3; bolt++)
        {
            float boltCycle = (time * (0.8f + bolt * 0.3f) + bolt * 3.7f) % 4f;
            if (boltCycle > 0.6f) continue;

            float flashAlpha = boltCycle < 0.1f ? boltCycle * 10f : Math.Max(0f, 1f - (boltCycle - 0.1f) / 0.5f);
            float startX = width * (0.2f + 0.6f * (float)Rand(bolt * 113 + (int)(time * 0.8f) * 7));

            float bx = startX;
            float by = 0f;
            int segments = 6 + (int)(4 * Rand(bolt * 67));

            for (int seg = 0; seg < segments; seg++)
            {
                float nextX = bx + width * 0.04f * (float)(Rand((int)(bolt * 200 + seg * 31 + time * 2)) - 0.5) * 2f;
                float nextY = by + height / (float)segments;
                nextX = Math.Clamp(nextX, width * 0.05f, width * 0.95f);

                int boltR = 180 + (int)(75 * flashAlpha);
                int boltG = 180 + (int)(75 * flashAlpha);
                int boltB = 255;
                using var pen = new DrawingPen(DrawingColor.FromArgb((int)(alpha * 230 * flashAlpha), boltR, boltG, boltB), Math.Max(2f, width * 0.012f));
                graphics.DrawLine(pen, bx, by, nextX, nextY);

                using var corePen = new DrawingPen(DrawingColor.FromArgb((int)(alpha * 180 * flashAlpha), 255, 255, 255), Math.Max(1f, width * 0.005f));
                graphics.DrawLine(corePen, bx, by, nextX, nextY);

                bx = nextX;
                by = nextY;
            }

            FillGlow(graphics, startX - width * 0.2f, -height * 0.1f, width * 0.4f, height * 0.5f,
                DrawingColor.FromArgb((int)(alpha * 70 * flashAlpha), 160, 170, 255));
        }
    }

    private static void DrawCyberOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.30f + speed / 80f);

        using var bg = new DrawingLinearGradientBrush(
            new DrawingPointF(0, 0),
            new DrawingPointF(width, height),
            DrawingColor.FromArgb((int)(90 * alpha), 8, 2, 16),
            DrawingColor.FromArgb((int)(80 * alpha), 4, 4, 22));
        graphics.FillRectangle(bg, 0, 0, width, height);

        DrawingColor[] neonColors =
        {
            DrawingColor.FromArgb((int)(alpha * 100), 255, 40, 200),
            DrawingColor.FromArgb((int)(alpha * 90), 0, 220, 255),
            DrawingColor.FromArgb((int)(alpha * 80), 180, 60, 255),
            DrawingColor.FromArgb((int)(alpha * 70), 255, 100, 60),
        };

        for (int i = 0; i < neonColors.Length; i++)
        {
            float cx = width * (0.15f + i * 0.22f + 0.08f * (float)Math.Sin(time * 1.0f + i * 1.5f));
            float cy = height * (0.4f + 0.15f * (float)Math.Sin(time * 0.8f + i * 1.1f));
            FillGlow(graphics, cx - width * 0.22f, cy - height * 0.35f, width * 0.44f, height * 0.7f, neonColors[i]);
        }

        int lineCount = 6;
        for (int line = 0; line < lineCount; line++)
        {
            float ly = height * (0.15f + line * 0.14f);
            float scanX = (time * width * (0.3f + 0.15f * (float)Rand(line * 41))) % (width * 1.4f) - width * 0.2f;
            int hue = (int)(300 + line * 28 + time * 50) % 360;
            var lineColor = FromHsv(hue, 0.9, 1.0, alpha * 0.45f);
            using var pen = new DrawingPen(lineColor, Math.Max(1.5f, height * 0.008f));
            graphics.DrawLine(pen, 0, ly, width, ly);

            FillGlow(graphics, scanX - width * 0.08f, ly - height * 0.06f, width * 0.16f, height * 0.12f,
                DrawingColor.FromArgb((int)(alpha * 140), 255, 255, 255));
        }

        for (int hex = 0; hex < 5; hex++)
        {
            float hx = width * (float)Rand(hex * 43 + 9);
            float hy = height * (float)Rand(hex * 59 + 17);
            float phase = (time * 2f + hex * 1.3f) % 3f;
            float hexAlpha = phase < 0.5f ? phase * 2f : phase < 1.5f ? 1f : Math.Max(0f, (3f - phase) / 1.5f);
            float size = Math.Max(4f, width * 0.025f);
            using var brush = new DrawingBrush(DrawingColor.FromArgb((int)(alpha * 120 * hexAlpha), 0, 255, 220));
            graphics.FillEllipse(brush, hx - size, hy - size, size * 2, size * 2);
        }
    }

    private static void DrawGradientFlowOverlay(DrawingGraphics graphics, int width, int height, int tick, int opacity, int speed)
    {
        float alpha = opacity / 100f;
        float time = tick / 1000f * (0.20f + speed / 75f);

        int step = Math.Max(4, width / 60);
        for (int px = 0; px < width; px += step)
        {
            float nx = px / (float)width;
            float wave1 = (float)Math.Sin(nx * 3f + time * 0.8f) * 0.5f + 0.5f;
            float wave2 = (float)Math.Sin(nx * 5f - time * 1.2f) * 0.5f + 0.5f;
            float blend = (wave1 + wave2) * 0.5f;

            int hue1 = (int)(time * 30f) % 360;
            int hue2 = (hue1 + 120) % 360;
            int hue3 = (hue1 + 240) % 360;
            int hue = blend < 0.5f
                ? (int)(hue1 + (hue2 - hue1 + 360) % 360 * (blend * 2f)) % 360
                : (int)(hue2 + (hue3 - hue2 + 360) % 360 * ((blend - 0.5f) * 2f)) % 360;

            var topColor = FromHsv(hue, 0.75, 1.0, alpha * 0.9f);
            var bottomColor = FromHsv((hue + 40) % 360, 0.85, 0.7, alpha * 0.9f);

            using var brush = new DrawingLinearGradientBrush(
                new DrawingPointF(px, 0),
                new DrawingPointF(px, height),
                topColor,
                bottomColor);
            graphics.FillRectangle(brush, px, 0, step, height);
        }

        float glossY = height * 0.15f * (float)(0.5 + 0.5 * Math.Sin(time * 0.6f));
        using var gloss = new DrawingLinearGradientBrush(
            new DrawingPointF(0, glossY),
            new DrawingPointF(0, glossY + height * 0.3f),
            DrawingColor.FromArgb((int)(alpha * 40), 255, 255, 255),
            DrawingColor.FromArgb(0, 255, 255, 255));
        graphics.FillRectangle(gloss, 0, glossY, width, height * 0.3f);
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
