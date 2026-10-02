using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using AmpUp.Core;

namespace AmpUp.Services;

/// <summary>
/// Resolves the real app icon for Settings → Connected / Lighting Apps cards
/// by locating the installed executable (or a running process) and extracting
/// its icon. Returns null when the app isn't installed so callers keep the
/// generic Phosphor glyph as a fallback.
/// </summary>
public static class IntegrationAppIcons
{
    private static readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static BitmapSource? Get(string app, string? voiceMeeterDir = null)
    {
        string key = app == "voicemeeter" ? $"{app}|{voiceMeeterDir}" : app;
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }

        BitmapSource? icon = null;
        try
        {
            var exe = FindExecutable(app, voiceMeeterDir);
            if (exe != null) icon = ExtractIcon(exe);
        }
        catch (Exception ex)
        {
            Logger.Log($"IntegrationAppIcons: failed to resolve '{app}': {ex.Message}");
        }

        lock (_lock) _cache[key] = icon;
        return icon;
    }

    private static string? FindExecutable(string app, string? voiceMeeterDir)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        IEnumerable<string> candidates = app switch
        {
            "discord" => LatestVersioned(Path.Combine(local, "Discord"), "Discord.exe")
                .Append(Path.Combine(local, "Discord", "app.ico")),
            "spotify" => new[] { Path.Combine(roaming, "Spotify", "Spotify.exe") },
            "obs" => new[]
            {
                Path.Combine(pf, "obs-studio", "bin", "64bit", "obs64.exe"),
                Path.Combine(pf86, "Steam", "steamapps", "common", "OBS Studio", "bin", "64bit", "obs64.exe"),
            },
            "voicemeeter" => VoiceMeeterCandidates(voiceMeeterDir, pf, pf86),
            "corsair" => new[]
            {
                Path.Combine(pf, "Corsair", "Corsair iCUE5 Software", "iCUE.exe"),
                Path.Combine(pf, "Corsair", "CORSAIR iCUE 4 Software", "iCUE.exe"),
                Path.Combine(pf86, "Corsair", "CORSAIR iCUE 4 Software", "iCUE.exe"),
                Path.Combine(pf86, "Corsair", "CORSAIR iCUE Software", "iCUE.exe"),
            },
            "signalrgb" => LatestVersioned(Path.Combine(local, "VortxEngine"), Path.Combine("Signal-x64", "SignalRgb.exe")),
            "govee" => new[]
            {
                Path.Combine(pf, "Govee", "Govee Desktop", "GoveeDesktop.exe"),
                Path.Combine(pf86, "Govee", "Govee Desktop", "GoveeDesktop.exe"),
            },
            _ => Array.Empty<string>(),
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;

        // Store / custom installs: fall back to a running process if there is one.
        string[] processNames = app switch
        {
            "discord" => new[] { "Discord" },
            "spotify" => new[] { "Spotify" },
            "obs" => new[] { "obs64", "obs" },
            "voicemeeter" => new[] { "voicemeeter8x64", "voicemeeter8", "voicemeeterpro_x64", "voicemeeterpro", "voicemeeter_x64", "voicemeeter" },
            "corsair" => new[] { "iCUE" },
            "signalrgb" => new[] { "SignalRgb" },
            "govee" => new[] { "GoveeDesktop" },
            _ => Array.Empty<string>(),
        };
        foreach (var name in processNames)
        {
            var procs = Process.GetProcessesByName(name);
            try
            {
                foreach (var proc in procs)
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                    }
                    catch { }
                }
            }
            finally
            {
                foreach (var proc in procs) proc.Dispose();
            }
        }
        return null;
    }

    private static IEnumerable<string> LatestVersioned(string root, string relativeExe)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.GetDirectories(root, "app-*")
            .Select(dir => new DirectoryInfo(dir))
            .OrderByDescending(dir => dir.LastWriteTimeUtc)
            .Select(dir => Path.Combine(dir.FullName, relativeExe))
            .ToList();
    }

    private static IEnumerable<string> VoiceMeeterCandidates(string? configuredDir, string pf, string pf86)
    {
        string[] exes = { "voicemeeter8x64.exe", "voicemeeter8.exe", "voicemeeterpro_x64.exe", "voicemeeterpro.exe", "voicemeeter_x64.exe", "voicemeeter.exe" };
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredDir)) dirs.Add(configuredDir.Trim());
        dirs.Add(Path.Combine(pf86, "VB", "Voicemeeter"));
        dirs.Add(Path.Combine(pf, "VB", "Voicemeeter"));
        return dirs.SelectMany(dir => exes.Select(exe => Path.Combine(dir, exe)));
    }

    private static BitmapSource? ExtractIcon(string path)
    {
        System.Drawing.Icon? sysIcon = null;
        try
        {
            if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                sysIcon = new System.Drawing.Icon(path, 64, 64);
            else
                sysIcon = System.Drawing.Icon.ExtractIcon(path, 0, 64)
                          ?? System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (sysIcon == null) return null;

            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                sysIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        finally
        {
            sysIcon?.Dispose();
        }
    }
}
