using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AmpUp.Controls;
using AmpUp.Core.Models;

namespace AmpUp.Services;

/// <summary>
/// Renders EffectPreviewControl snapshots to PNG and wires the FX Space
/// DisplayKeys to those paths. One-shot utility invoked by the Settings
/// "Export Effect Previews" button.
/// </summary>
public static class EffectPreviewExporter
{
    private const int PixelSize = 256;
    private const int SnapshotFrame = 45; // ~1.5 s into the animation
    private const string FxSpaceName = "FX";

    /// <summary>Export directory: %AppData%\AmpUp\IconCache\effects\</summary>
    public static string ExportDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "AmpUp", "IconCache", "effects");
        }
    }

    public class ExportResult
    {
        public int Exported;
        public int Failed;
        public List<string> Errors = new();
    }

    /// <summary>
    /// Render every room_effect button in the FX Space to a 256×256 PNG and
    /// point the matching DisplayKey at the saved file. Caller should then
    /// persist the mutated config.
    /// </summary>
    public static ExportResult ExportFxSpaceIcons(AppConfig config)
    {
        var result = new ExportResult();
        if (config?.N3?.Folders == null) return result;

        var fx = config.N3.Folders.Find(f => string.Equals(f.Name, FxSpaceName, StringComparison.OrdinalIgnoreCase));
        if (fx == null)
        {
            result.Errors.Add($"No '{FxSpaceName}' Space found in config.");
            return result;
        }

        Directory.CreateDirectory(ExportDirectory);

        foreach (var btn in fx.Buttons)
        {
            if (btn.Action != "room_effect" || string.IsNullOrWhiteSpace(btn.Path)) continue;
            if (!Enum.TryParse<LightEffect>(btn.Path, out var effect))
            {
                result.Failed++;
                result.Errors.Add($"Unknown effect '{btn.Path}' on button idx {btn.Idx}");
                continue;
            }

            try
            {
                var tileColor = EffectPickerControl.EffectColors.GetValueOrDefault(effect, Colors.White);
                var accent = EffectPickerControl.GetCompanionColor(effect, tileColor);

                var preview = new EffectPreviewControl
                {
                    EffectKind = effect,
                    TileColor = tileColor,
                    AccentColor = accent,
                    Width = PixelSize,
                    Height = PixelSize,
                };
                preview.Measure(new System.Windows.Size(PixelSize, PixelSize));
                preview.Arrange(new System.Windows.Rect(0, 0, PixelSize, PixelSize));

                var bmp = preview.RenderToBitmap(PixelSize, PixelSize, SnapshotFrame);

                var outPath = Path.Combine(ExportDirectory, $"{effect}.png");
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    encoder.Save(fs);
                }

                // Wire the matching DisplayKey's ImagePath. Button idx is
                // global (100 + page*6 + slot); DisplayKey idx is local (page*6 + slot).
                int localIdx = btn.Idx - 100;
                var dk = fx.DisplayKeys.Find(d => d.Idx == localIdx);
                if (dk != null)
                {
                    dk.ImagePath = outPath;
                    dk.PresetIconKind = ""; // clear any preset so image wins
                }
                result.Exported++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{btn.Path}: {ex.Message}");
            }
        }

        return result;
    }
}
