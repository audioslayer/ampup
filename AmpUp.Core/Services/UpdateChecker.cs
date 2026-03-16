using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AmpUp.Core.Services;

public static class UpdateChecker
{
    public static readonly string CurrentVersion =
        (Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";
    private const string GitHubRepo = "audioslayer/ampup";
    private static readonly HttpClient _http = new();

    /// <summary>
    /// Set by the platform host to handle clean shutdown when an update is ready to install.
    /// On WPF: App sets this to call App.ShutdownForUpdate().
    /// </summary>
    public static Action? OnShutdownRequested { get; set; }

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

            if (!IsNewer(remoteVersion, CurrentVersion))
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
    /// Returns true if remoteVersion is strictly newer than localVersion.
    /// Supports formats like "0.3.2-alpha", "1.0.0", "0.3.2".
    /// </summary>
    private static bool IsNewer(string remoteVersion, string localVersion)
    {
        // Split off pre-release suffix (e.g., "0.3.2-alpha" → "0.3.2" + "alpha")
        var remoteParts = remoteVersion.Split('-', 2);
        var localParts = localVersion.Split('-', 2);

        var remoteNums = remoteParts[0].Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
        var localNums = localParts[0].Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();

        // Compare numeric parts (major.minor.patch)
        int len = Math.Max(remoteNums.Length, localNums.Length);
        for (int i = 0; i < len; i++)
        {
            int r = i < remoteNums.Length ? remoteNums[i] : 0;
            int l = i < localNums.Length ? localNums[i] : 0;
            if (r > l) return true;
            if (r < l) return false;
        }

        // Same numeric version — release (no suffix) is newer than pre-release (has suffix)
        bool remoteIsPreRelease = remoteParts.Length > 1;
        bool localIsPreRelease = localParts.Length > 1;
        if (!remoteIsPreRelease && localIsPreRelease) return true;

        return false;
    }

    /// <summary>
    /// Downloads the installer to a temp file and launches it.
    /// The bat-file launch logic is Windows-specific; on other platforms a different
    /// update mechanism will be needed.
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

        // Launch a helper script that waits for us to exit, then runs the installer
        var batPath = Path.Combine(Path.GetTempPath(), "AmpUp-Update.bat");
        File.WriteAllText(batPath, $"@echo off\ntimeout /t 3 /nobreak >nul\nstart \"\" \"{tempPath}\"\ndel \"%~f0\"\n");
        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        // Shut down the app cleanly so the installer can replace files
        OnShutdownRequested?.Invoke();
    }
}
