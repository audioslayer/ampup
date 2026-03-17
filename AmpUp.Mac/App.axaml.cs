using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AmpUp.Mac;

public partial class App : Application
{
    /// <summary>
    /// Global tray icon manager — accessible from MainWindow to push connection/profile state.
    /// </summary>
    public static TrayIconManager? Tray { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Tray = new TrayIconManager();

            var window = new MainWindow();
            desktop.MainWindow = window;

            // Attach window to tray manager (enables show/hide + close-to-tray)
            Tray.AttachWindow(window);

            // On shutdown, dispose tray cleanly
            desktop.Exit += (_, _) => Tray?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
