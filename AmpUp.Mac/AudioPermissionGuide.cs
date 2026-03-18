using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AmpUp.Core;

namespace AmpUp.Mac;

/// <summary>
/// Setup wizard window that guides users through granting the macOS
/// "Screen & System Audio Recording" permission required for per-app
/// audio control via Core Audio Process Taps.
/// Pure code-behind (no XAML) — matches the app's dark glass theme.
/// </summary>
public class AudioPermissionGuide : Window
{
    // ── Theme colors ─────────────────────────────────────────────────
    private static readonly Color BgBase = Color.Parse("#0F0F0F");
    private static readonly Color CardBg = Color.Parse("#1C1C1C");
    private static readonly Color CardBorder = Color.Parse("#2A2A2A");
    private static readonly Color Accent = Color.Parse("#00E676");
    private static readonly Color AccentDim = Color.Parse("#00A854");
    private static readonly Color TextPrimary = Color.Parse("#E8E8E8");
    private static readonly Color TextSec = Color.Parse("#9A9A9A");
    private static readonly Color TextDim = Color.Parse("#555555");
    private static readonly Color SuccessGreen = Color.Parse("#00DD77");
    private static readonly Color DangerRed = Color.Parse("#FF4444");

    private static readonly IBrush BgBaseBrush = new SolidColorBrush(BgBase);
    private static readonly IBrush CardBgBrush = new SolidColorBrush(CardBg);
    private static readonly IBrush CardBorderBrush = new SolidColorBrush(CardBorder);
    private static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
    private static readonly IBrush AccentDimBrush = new SolidColorBrush(AccentDim);
    private static readonly IBrush TextPrimaryBrush = new SolidColorBrush(TextPrimary);
    private static readonly IBrush TextSecBrush = new SolidColorBrush(TextSec);
    private static readonly IBrush TextDimBrush = new SolidColorBrush(TextDim);

    // ── P/Invoke for permission test ─────────────────────────────────
    [DllImport("libAmpUpAudio")]
    private static extern bool ampup_create_tap(int pid);

    private TextBlock? _statusText;
    private Border? _statusDot;

    public AudioPermissionGuide()
    {
        Title = "Audio Control Setup";
        Width = 520;
        Height = 540;
        CanResize = false;
        Background = BgBaseBrush;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SystemDecorations = SystemDecorations.Full;

        Content = BuildContent();
    }

    private Control BuildContent()
    {
        var root = new StackPanel
        {
            Margin = new Thickness(32, 24, 32, 24),
        };

        // ── Title ────────────────────────────────────────────────────
        root.Children.Add(new TextBlock
        {
            Text = "Audio Control Setup",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });

        // ── Subtitle ─────────────────────────────────────────────────
        root.Children.Add(new TextBlock
        {
            Text = "AmpUp needs Screen & System Audio Recording permission to control per-app volume (Chrome, Spotify, etc.). Master volume works without this.",
            FontSize = 13,
            Foreground = TextSecBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20),
            LineHeight = 20,
        });

        // ── Steps card ───────────────────────────────────────────────
        var stepsCard = new Border
        {
            Background = CardBgBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16),
            Margin = new Thickness(0, 0, 0, 20),
        };

        var stepsPanel = new StackPanel { Spacing = 16 };

        // Step 1 — with button
        stepsPanel.Children.Add(BuildStep(1, "Open System Settings", "Opens Privacy & Security directly to the right section."));
        var openBtn = new Button
        {
            Content = "Open System Settings",
            Background = AccentDimBrush,
            Foreground = TextPrimaryBrush,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(40, -4, 0, 0),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        openBtn.Click += (_, _) => OpenSystemSettings();
        stepsPanel.Children.Add(openBtn);

        // Step 2
        stepsPanel.Children.Add(BuildStep(2, "Find 'Screen & System Audio Recording'", "Look in the sidebar under Privacy & Security."));

        // Step 3 — with Reveal in Finder button
        stepsPanel.Children.Add(BuildStep(3, "Click + and add AmpUp",
            "If AmpUp isn't listed, click + then navigate to the app. Use the button below to reveal it in Finder first."));

        var revealBtn = new Button
        {
            Content = "Reveal AmpUp in Finder",
            Background = new SolidColorBrush(Color.FromArgb(0x20, Accent.R, Accent.G, Accent.B)),
            Foreground = AccentBrush,
            Padding = new Thickness(14, 6),
            Margin = new Thickness(44, 0, 0, 12),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        revealBtn.Click += (_, _) =>
        {
            try
            {
                // Get the running app's bundle path
                var appPath = System.IO.Path.GetDirectoryName(
                    System.IO.Path.GetDirectoryName(
                        System.IO.Path.GetDirectoryName(
                            Environment.ProcessPath)));
                if (appPath != null && appPath.EndsWith(".app"))
                {
                    Process.Start("open", $"-R \"{appPath}\"");
                }
                else
                {
                    // Fallback: reveal the dist folder
                    Process.Start("open", "-R \"/Users/audio/Projects/ampup-core/AmpUp.Mac/dist/AmpUp.app\"");
                }
            }
            catch { }
        };
        stepsPanel.Children.Add(revealBtn);

        // Step 4
        stepsPanel.Children.Add(BuildStep(4, "Restart AmpUp", "The permission takes effect after restarting the app."));

        stepsCard.Child = stepsPanel;
        root.Children.Add(stepsCard);

        // ── Permission check row ─────────────────────────────────────
        var checkCard = new Border
        {
            Background = CardBgBrush,
            BorderBrush = CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 14),
            Margin = new Thickness(0, 0, 0, 20),
        };

        var checkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        var checkBtn = new Button
        {
            Content = "Check Permission",
            Background = AccentDimBrush,
            Foreground = TextPrimaryBrush,
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        checkBtn.Click += (_, _) => OnCheckPermission();
        checkRow.Children.Add(checkBtn);

        _statusDot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(TextDim),
            VerticalAlignment = VerticalAlignment.Center,
        };
        checkRow.Children.Add(_statusDot);

        _statusText = new TextBlock
        {
            Text = "Not checked yet",
            FontSize = 12,
            Foreground = TextSecBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        checkRow.Children.Add(_statusText);

        checkCard.Child = checkRow;
        root.Children.Add(checkCard);

        // ── Bottom buttons ───────────────────────────────────────────
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
        };

        var skipLink = new TextBlock
        {
            Text = "Skip for now",
            FontSize = 12,
            Foreground = TextSecBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Margin = new Thickness(0, 0, 8, 0),
        };
        skipLink.PointerPressed += (_, _) => Close();
        // Underline on hover
        skipLink.PointerEntered += (_, _) => skipLink.TextDecorations = TextDecorations.Underline;
        skipLink.PointerExited += (_, _) => skipLink.TextDecorations = null;
        buttonRow.Children.Add(skipLink);

        var doneBtn = new Button
        {
            Content = "Done",
            Background = AccentBrush,
            Foreground = new SolidColorBrush(Color.Parse("#0F0F0F")),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(24, 8),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        doneBtn.Click += (_, _) => Close();
        buttonRow.Children.Add(doneBtn);

        root.Children.Add(buttonRow);

        return root;
    }

    private static Control BuildStep(int number, string title, string description)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        // Number badge
        var badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = AccentBrush,
            Child = new TextBlock
            {
                Text = number.ToString(),
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(BgBase),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        row.Children.Add(badge);

        // Text column
        var textCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textCol.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = TextPrimaryBrush,
        });
        textCol.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = TextSecBrush,
            Margin = new Thickness(0, 2, 0, 0),
        });
        row.Children.Add(textCol);

        return row;
    }

    private static void OpenSystemSettings()
    {
        try
        {
            // macOS 14+ URL scheme for Privacy & Security > Screen & System Audio Recording
            var psi = new ProcessStartInfo("open",
                "\"x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioPermissionGuide: Failed to open System Settings: {ex.Message}");
        }
    }

    private void OnCheckPermission()
    {
        bool granted = CheckAudioPermission();

        if (granted)
        {
            _statusDot!.Background = new SolidColorBrush(SuccessGreen);
            _statusText!.Text = "Permission granted — per-app audio is available";
            _statusText.Foreground = new SolidColorBrush(SuccessGreen);
        }
        else
        {
            _statusDot!.Background = new SolidColorBrush(DangerRed);
            _statusText!.Text = "Permission not granted — follow the steps above";
            _statusText.Foreground = new SolidColorBrush(DangerRed);
        }
    }

    /// <summary>
    /// Tests whether the Screen & System Audio Recording permission is granted
    /// by attempting to create an audio tap on the current process.
    /// Returns true if ampup_create_tap succeeds (permission granted).
    /// </summary>
    public static bool CheckAudioPermission()
    {
        try
        {
            int pid = Environment.ProcessId;
            return ampup_create_tap(pid);
        }
        catch (DllNotFoundException)
        {
            // Native library not available (e.g. running on Windows dev machine)
            Logger.Log("AudioPermissionGuide: libAmpUpAudio not available, assuming permission not testable");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioPermissionGuide: Permission check failed: {ex.Message}");
            return false;
        }
    }
}
