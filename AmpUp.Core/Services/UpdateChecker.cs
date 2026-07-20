using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AmpUp.Core.Services;

public sealed record UpdateInfo(
    string Tag,
    string Version,
    string AssetName,
    string DownloadUrl,
    long AssetSize,
    string Sha256);

public static class UpdateChecker
{
    public static readonly string CurrentVersion =
        (Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";
    private const string GitHubRepo = "audioslayer/ampup";
    private static readonly HttpClient _http = new();
    private static readonly SemaphoreSlim _installLock = new(1, 1);

    /// <summary>
    /// Set by the platform host to handle clean shutdown when an update is ready to install.
    /// On WPF: App sets this to call App.ShutdownForUpdate().
    /// </summary>
    public static Action? OnShutdownRequested { get; set; }

    static UpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AmpUp/" + CurrentVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>
    /// Checks GitHub for a newer release and its matching AmpUp installer asset.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"https://api.github.com/repos/{GitHubRepo}/releases?per_page=20",
                cancellationToken);
            var releases = JArray.Parse(json);
            bool includePrereleases = IsPrerelease(CurrentVersion);

            foreach (var release in releases)
            {
                if (release["draft"]?.Value<bool>() == true) continue;
                if (release["prerelease"]?.Value<bool>() == true && !includePrereleases) continue;

                var tag = release["tag_name"]?.ToString() ?? "";
                var remoteVersion = tag.StartsWith('v') ? tag[1..] : tag;
                if (!IsNewer(remoteVersion, CurrentVersion))
                    continue;

                var assets = release["assets"] as JArray;
                if (assets == null) continue;

                // The release script always produces this exact name. Requiring it avoids
                // ever executing an unrelated .exe that happens to be attached to a release.
                var expectedName = $"AmpUp-Setup-{remoteVersion}.exe";
                var asset = assets
                    .OfType<JObject>()
                    .FirstOrDefault(candidate => string.Equals(
                        candidate["name"]?.ToString(), expectedName,
                        StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                {
                    Logger.Log($"Update {tag} is newer, but release asset {expectedName} is missing");
                    continue;
                }

                var url = asset["browser_download_url"]?.ToString() ?? "";
                if (!IsTrustedReleaseUrl(url))
                {
                    Logger.Log($"Ignored untrusted update URL for {tag}");
                    continue;
                }

                var digest = asset["digest"]?.ToString();
                var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                    ? digest[7..]
                    : null;
                if (!IsValidSha256(sha256))
                {
                    Logger.Log($"Update {tag} is missing a valid GitHub SHA-256 digest");
                    continue;
                }

                var assetSize = asset["size"]?.Value<long>() ?? 0;
                if (assetSize <= 0)
                {
                    Logger.Log($"Update {tag} has an invalid installer size");
                    continue;
                }

                return new UpdateInfo(
                    tag,
                    remoteVersion,
                    expectedName,
                    url,
                    assetSize,
                    sha256!);
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"Update check failed: {ex.Message}");
            throw;
        }
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
        return PrereleaseRank(remoteParts) > PrereleaseRank(localParts);
    }

    private static bool IsPrerelease(string version)
        => version.Contains('-', StringComparison.Ordinal);

    private static int PrereleaseRank(string[] versionParts)
    {
        if (versionParts.Length == 1) return 100;
        var label = versionParts[1].ToLowerInvariant();
        if (label.StartsWith("alpha")) return 10;
        if (label.StartsWith("beta")) return 20;
        if (label.StartsWith("preview")) return 20;
        if (label.StartsWith("rc")) return 30;
        return 1;
    }

    private static bool IsTrustedReleaseUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(
                $"/{GitHubRepo}/releases/download/",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    /// <summary>
    /// Downloads, verifies, and hands the installer off to a helper process. The helper
    /// waits for AmpUp to exit, installs silently after the normal UAC prompt, and then
    /// relaunches the updated executable.
    /// </summary>
    public static async Task DownloadAndInstallAsync(
        UpdateInfo update,
        Action<int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsTrustedReleaseUrl(update.DownloadUrl))
            throw new InvalidOperationException("The update download URL is not trusted.");
        if (!string.Equals(update.AssetName, $"AmpUp-Setup-{update.Version}.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The release does not contain the expected AmpUp installer.");
        if (!IsValidSha256(update.Sha256))
            throw new InvalidOperationException("The release does not contain a valid SHA-256 digest.");
        if (update.AssetSize <= 0)
            throw new InvalidOperationException("The release does not contain a valid installer size.");
        if (OnShutdownRequested == null)
            throw new InvalidOperationException("The application did not configure update shutdown handling.");
        if (!await _installLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("An update is already being installed.");

        bool handedOff = false;
        string? partialPath = null;
        try
        {
            var safeVersion = string.Concat(update.Version.Select(ch =>
                char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));
            var updateDirectory = Path.Combine(Path.GetTempPath(), "AmpUp", "Updates", safeVersion);
            Directory.CreateDirectory(updateDirectory);
            var installerPath = Path.Combine(updateDirectory, update.AssetName);
            partialPath = installerPath + ".download";

            if (File.Exists(partialPath))
                File.Delete(partialPath);

            using var response = await _http.GetAsync(
                update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;
            long downloaded = 0;
            bool hasExecutableHeader = false;
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(
                partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (downloaded == 0 && read >= 2)
                        hasExecutableHeader = buffer[0] == (byte)'M' && buffer[1] == (byte)'Z';

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer.AsSpan(0, read));
                    downloaded += read;
                    if (totalBytes > 0)
                        onProgress?.Invoke((int)Math.Min(100, downloaded * 100 / totalBytes));
                }

                await file.FlushAsync(cancellationToken);
            }

            if (!hasExecutableHeader)
                throw new InvalidDataException("The downloaded update is not a Windows installer.");
            if (update.AssetSize > 0 && downloaded != update.AssetSize)
                throw new InvalidDataException(
                    $"The update download was incomplete ({downloaded} of {update.AssetSize} bytes).");

            var actualSha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            if (!actualSha256.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update failed its SHA-256 integrity check.");
            }

            File.Move(partialPath, installerPath, true);
            onProgress?.Invoke(100);
            Logger.Log(
                $"Update {update.Tag} downloaded and verified: {installerPath} (SHA-256 {actualSha256})");

            var helperProcess = LaunchInstallerHelper(installerPath);
            try
            {
                OnShutdownRequested();
                handedOff = true;
            }
            catch
            {
                try { helperProcess.Kill(true); } catch { }
                throw;
            }
        }
        finally
        {
            if (!handedOff)
            {
                if (partialPath != null)
                {
                    try { File.Delete(partialPath); } catch { }
                }
                _installLock.Release();
            }
        }
    }

    private static Process LaunchInstallerHelper(string installerPath)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
            throw new InvalidOperationException("AmpUp could not determine its executable path for restart.");

        var helperDirectory = Path.GetDirectoryName(installerPath)!;
        var helperPath = Path.Combine(helperDirectory, $"install-{Guid.NewGuid():N}.ps1");
        var helperLogPath = Path.Combine(helperDirectory, "update-helper.log");
        var script = BuildInstallerHelperScript(
            Environment.ProcessId,
            installerPath,
            currentExe,
            helperLogPath);
        File.WriteAllText(helperPath, script, new UTF8Encoding(false));

        var powerShellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powerShellPath))
            powerShellPath = "powershell.exe";

        var startInfo = new ProcessStartInfo
        {
            FileName = powerShellPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("The update installer helper could not be started.");
    }

    private static string BuildInstallerHelperScript(
        int processId,
        string installerPath,
        string currentExe,
        string logPath)
    {
        static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

        return $$"""
            $ErrorActionPreference = 'Stop'
            $installer = {{PsQuote(installerPath)}}
            $app = {{PsQuote(currentExe)}}
            $log = {{PsQuote(logPath)}}
            try {
                Wait-Process -Id {{processId}} -ErrorAction SilentlyContinue
                Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) Starting installer $installer"
                $setup = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CLOSEAPPLICATIONS') -Verb RunAs -Wait -PassThru
                if ($setup.ExitCode -ne 0) {
                    throw "Installer exited with code $($setup.ExitCode)."
                }
                Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) Update installed successfully"
                Start-Process -FilePath $app
            }
            catch {
                $message = "Amp Up could not install the update. Your existing installation was left in place.`n`n$($_.Exception.Message)"
                try { Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) $message" } catch {}
                try { if (Test-Path -LiteralPath $app) { Start-Process -FilePath $app } } catch {}
                try {
                    Add-Type -AssemblyName PresentationFramework
                    [System.Windows.MessageBox]::Show($message, 'Amp Up Update') | Out-Null
                } catch {}
            }
            finally {
                Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
            }
            """;
    }
}
