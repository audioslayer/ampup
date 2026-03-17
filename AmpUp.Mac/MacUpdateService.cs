using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AmpUp.Core.Services;

namespace AmpUp.Mac;

/// <summary>
/// Mac-specific update flow: download .dmg from GitHub, mount it via hdiutil, open in Finder.
/// User drags the new .app to /Applications to complete the update.
/// </summary>
public static class MacUpdateService
{
    /// <summary>
    /// Fired when an update is available. Tag = version string (e.g. "v0.2.0-alpha").
    /// </summary>
    public static event Action<string>? UpdateAvailable;

    /// <summary>Whether the periodic background check is enabled. Set from config on load.</summary>
    public static bool AutoCheckEnabled { get; set; } = true;

    private static string? _pendingTag;
    private static string? _pendingDownloadUrl;

    /// <summary>
    /// Check GitHub for a newer .dmg release. Fires UpdateAvailable if found.
    /// Safe to call multiple times — no-ops if already found a pending update.
    /// </summary>
    public static async Task CheckAsync()
    {
        try
        {
            var result = await UpdateChecker.CheckForUpdateAsync(".dmg");
            if (result == null) return;

            var (tag, url) = result.Value;
            _pendingTag = tag;
            _pendingDownloadUrl = url;
            UpdateAvailable?.Invoke(tag);
        }
        catch
        {
            // Swallow — update check is best-effort
        }
    }

    public static string? PendingTag => _pendingTag;
    public static bool HasPendingUpdate => _pendingDownloadUrl != null;

    /// <summary>
    /// Download the .dmg and open it in Finder so the user can drag to Applications.
    /// Reports 0-100 progress via onProgress.
    /// </summary>
    public static async Task DownloadAndOpenAsync(Action<int>? onProgress = null)
    {
        if (_pendingDownloadUrl == null)
            throw new InvalidOperationException("No pending update to download.");

        var fileName = $"AmpUp-{_pendingTag ?? "update"}.dmg";
        var localPath = await UpdateChecker.DownloadUpdateAsync(_pendingDownloadUrl, fileName, onProgress);

        // Open the .dmg — macOS will mount it and Finder shows the window with the .app
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{localPath}\"",
            UseShellExecute = false,
        });
    }
}
