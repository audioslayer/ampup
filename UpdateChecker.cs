using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace AmpUp;

public static class UpdateChecker
{
    public const string CurrentVersion = "0.3.1-alpha";
    private const string GitHubRepo = "audioslayer/ampup";
    private static readonly HttpClient _http = new();

    static UpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AmpUp/" + CurrentVersion);
    }

    /// <summary>
    /// Checks GitHub for a newer release. Returns (tagName, downloadUrl) or null if up to date.
    /// </summary>
    public static async Task<(string Tag, string DownloadUrl)?> CheckForUpdateAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            var release = JObject.Parse(json);

            var tag = release["tag_name"]?.ToString() ?? "";
            // Strip leading 'v' for comparison
            var remoteVersion = tag.TrimStart('v');

            if (remoteVersion == CurrentVersion)
                return null;

            // Find the .exe asset
            var assets = release["assets"] as JArray;
            if (assets == null) return null;

            foreach (var asset in assets)
            {
                var name = asset["name"]?.ToString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset["browser_download_url"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(url))
                        return (tag, url);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Downloads the installer to a temp file and launches it.
    /// </summary>
    public static async Task DownloadAndInstallAsync(string downloadUrl, Action<int>? onProgress = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AmpUp-Update.exe");

        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloaded = 0;

        using (var stream = await response.Content.ReadAsStreamAsync())
        using (var file = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await file.WriteAsync(buffer, 0, read);
                downloaded += read;
                if (totalBytes > 0)
                    onProgress?.Invoke((int)(downloaded * 100 / totalBytes));
            }
        }

        Logger.Log($"Update downloaded to {tempPath}, launching installer...");

        // Launch a helper cmd that waits for us to exit, then runs the installer
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c timeout /t 2 /nobreak >nul & \"{tempPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        // Shut down the app so the installer can replace files
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }
}
