using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;

namespace AmpUp.Mac;

public partial class App : Application
{
    private DispatcherTimer? _updateTimer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();

        // Check for updates on startup (slight delay so the app is fully visible first)
        var startupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        startupTimer.Tick += (_, _) =>
        {
            startupTimer.Stop();
            _ = MacUpdateService.CheckAsync();
        };
        startupTimer.Start();

        // Periodic check every 4 hours (respects AutoCheckEnabled flag set from config)
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updateTimer.Tick += (_, _) =>
        {
            if (MacUpdateService.AutoCheckEnabled)
                _ = MacUpdateService.CheckAsync();
        };
        _updateTimer.Start();
    }
}
