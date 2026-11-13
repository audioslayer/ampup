using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AmpUp.Services;

/// <summary>
/// Developer-only screenshot endpoint so Claude (or any local tool) can see the running UI.
/// Started ONLY when AmpUp is launched with <c>--dev-screenshots</c> (deploy.bat does this);
/// a normal install / Start-with-Windows launch never opens the port.
///
/// Listens on http://127.0.0.1:47823 (loopback only):
///   GET /health                          → "ok"
///   GET /pages                           → JSON list of page names
///   GET /screenshot?page=mixer&amp;scroll=0  → PNG of the main window (page / scroll optional)
///   GET /screenshot?...&amp;w=1440&amp;h=900      → resize the window first (DIPs)
/// </summary>
public sealed class DevScreenshotServer : IDisposable
{
    public const int Port = 47823;
    public const string Flag = "--dev-screenshots";

    private readonly MainWindow _window;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    private static readonly Dictionary<string, string> PageButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mixer"] = "NavMixer",
        ["buttons"] = "NavButtons",
        ["lights"] = "NavLights",
        ["room"] = "NavAmbience",
        ["groups"] = "NavGroups",
        ["osd"] = "NavOsd",
        ["overview"] = "NavBindings",
        ["settings"] = "NavSettings",
    };

    private DevScreenshotServer(MainWindow window)
    {
        _window = window;
        _listener = new TcpListener(IPAddress.Loopback, Port);
    }

    /// <summary>Start the server if the process was launched with <see cref="Flag"/>; else null.</summary>
    public static DevScreenshotServer? StartIfEnabled(MainWindow window, string[] args)
    {
        if (!args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            var server = new DevScreenshotServer(window);
            server._listener.Start();
            _ = Task.Run(server.AcceptLoopAsync);
            Logger.Log($"Dev screenshot server listening on http://127.0.0.1:{Port}");
            return server;
        }
        catch (Exception ex)
        {
            Logger.Log($"Dev screenshot server failed to start: {ex.Message}");
            return null;
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync() ?? "";
                // Drain headers.
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync())) { }

                var parts = requestLine.Split(' ');
                if (parts.Length < 2 || parts[0] != "GET") { await WriteAsync(stream, 405, "text/plain", "GET only"); return; }

                var target = parts[1];
                var path = target.Split('?')[0].TrimEnd('/').ToLowerInvariant();
                var query = ParseQuery(target);

                switch (path)
                {
                    case "/health":
                        await WriteAsync(stream, 200, "text/plain", "ok");
                        break;
                    case "/pages":
                        await WriteAsync(stream, 200, "application/json",
                            "[" + string.Join(",", PageButtons.Keys.Select(k => $"\"{k}\"")) + "]");
                        break;
                    case "/screenshot":
                        var png = await _window.Dispatcher.InvokeAsync(() => CaptureAsync(query)).Task.Unwrap();
                        await WriteAsync(stream, 200, "image/png", png);
                        break;
                    default:
                        await WriteAsync(stream, 404, "text/plain", "Try /health, /pages or /screenshot?page=mixer");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Dev screenshot request failed: {ex.Message}");
                try { await WriteAsync(client.GetStream(), 500, "text/plain", ex.Message); } catch { }
            }
        }
    }

    // ── Capture (UI thread) ─────────────────────────────────────────

    private async Task<byte[]> CaptureAsync(Dictionary<string, string> q)
    {
        // Make sure there is something to render (AmpUp may be in the tray).
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;

        if (q.TryGetValue("w", out var ws) && double.TryParse(ws, out var w) && w >= 400) _window.Width = w;
        if (q.TryGetValue("h", out var hs) && double.TryParse(hs, out var h) && h >= 300) _window.Height = h;

        if (q.TryGetValue("page", out var page) && !string.IsNullOrWhiteSpace(page))
        {
            if (!PageButtons.TryGetValue(page, out var btnName) || _window.FindName(btnName) is not ButtonBase btn)
                throw new ArgumentException($"Unknown page '{page}'. Pages: {string.Join(", ", PageButtons.Keys)}");
            // Same path as a user click, so each page's own LoadConfig/refresh logic runs.
            btn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        // Let layout, data loading and the nav / fade animations settle.
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await Task.Delay(q.TryGetValue("delay", out var ds) && int.TryParse(ds, out var d) ? Math.Clamp(d, 0, 5000) : 450);

        if (q.TryGetValue("scroll", out var ss) && double.TryParse(ss, out var scroll))
        {
            if (FindScrollViewer(_window.ContentArea) is { } sv)
            {
                sv.ScrollToVerticalOffset(scroll);
                await Dispatcher.Yield(DispatcherPriority.ContextIdle);
                await Task.Delay(120);
            }
        }

        return Render();
    }

    private byte[] Render()
    {
        if (_window.Content is not FrameworkElement root || root.ActualWidth < 1 || root.ActualHeight < 1)
            throw new InvalidOperationException("Main window has no rendered content yet");

        var dpi = VisualTreeHelper.GetDpi(root);
        int pw = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX);
        int ph = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);

        // Mica / transparent window backgrounds render as black: paint the theme base colour first.
        var bg = Application.Current.TryFindResource("BgBaseBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x0F));
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var rect = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            dc.DrawRectangle(bg, null, rect);
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, rect);
        }

        var rtb = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? node)
    {
        if (node == null) return null;
        if (node is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
        return null;
    }

    // ── HTTP helpers ───────────────────────────────────────────────

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int qi = target.IndexOf('?');
        if (qi < 0) return dict;
        foreach (var pair in target[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            dict[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return dict;
    }

    private static Task WriteAsync(Stream s, int status, string type, string body) =>
        WriteAsync(s, status, type, Encoding.UTF8.GetBytes(body));

    private static async Task WriteAsync(Stream s, int status, string type, byte[] body)
    {
        var reason = status switch { 200 => "OK", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        var hb = Encoding.ASCII.GetBytes(header);
        await s.WriteAsync(hb);
        await s.WriteAsync(body);
        await s.FlushAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
