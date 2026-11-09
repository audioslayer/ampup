using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;
using AmpUp.Services;

namespace AmpUp.Controls;

/// <summary>
/// "Press a key" dialog: records the next key combo and returns it in the same
/// string format <see cref="KeyboardInjector"/> parses (e.g. "ctrl+shift+f13").
/// </summary>
public class KeyCaptureDialog : Window
{
    private readonly TextBlock _preview;
    private readonly TextBlock _hint;
    private string _captured = "";

    public string? Result { get; private set; }

    public KeyCaptureDialog(string current)
    {
        Title = "Record keystroke";
        Width = 360;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Res("BgDarkBrush");

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "PRESS THE KEY OR COMBO",
            Foreground = Res("TextSecBrush"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        });

        _preview = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(current) ? "Waiting for a key…" : current,
            Foreground = Res("TextPrimaryBrush"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 6),
        };
        root.Children.Add(new Border
        {
            Background = Res("InputBgBrush"),
            BorderBrush = Res("InputBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = _preview,
        });

        _hint = new TextBlock
        {
            Text = "F13–F24, numpad and media keys work too. Hold modifiers (Ctrl/Shift/Alt) first.",
            Foreground = Res("TextDimBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 14),
        };
        root.Children.Add(_hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeButton("Cancel");
        cancel.Click += (_, _) => { Result = null; DialogResult = false; };
        var save = MakeButton("Use this");
        save.Margin = new Thickness(8, 0, 0, 0);
        save.Click += (_, _) => { Result = _captured; DialogResult = !string.IsNullOrEmpty(_captured); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
        Focusable = true;
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => Focus();
    }

    private static Brush Res(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static Button MakeButton(string text) => new()
    {
        Content = text,
        MinWidth = 84,
        Padding = new Thickness(12, 6, 12, 6),
        Focusable = false, // keep keyboard focus on the window so Space/Enter are recordable
        Background = Res("CardBorderBrush"),
        Foreground = Res("TextPrimaryBrush"),
        BorderThickness = new Thickness(0),
        Cursor = Cursors.Hand,
    };

    private void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || KeyboardInjector.IsModifierVk(vk))
        {
            // Show the modifiers being held while waiting for the main key.
            var held = HeldModifiers();
            if (held.Count > 0) _preview.Text = string.Join("+", held) + "+…";
            return;
        }

        var name = KeyboardInjector.NameForVk(vk);
        if (name == null)
        {
            _hint.Text = $"That key ({key}) isn't supported — try another.";
            return;
        }

        var parts = HeldModifiers();
        parts.Add(name);
        _captured = string.Join("+", parts);
        _preview.Text = _captured;
        _hint.Text = "Press again to re-record, or click Use this.";
    }

    private static List<string> HeldModifiers()
    {
        var parts = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("win");
        return parts;
    }

    /// <summary>Opens the dialog; returns the recorded combo or null if cancelled.</summary>
    public static string? Capture(DependencyObject anchor, string current)
    {
        var dlg = new KeyCaptureDialog(current) { Owner = Window.GetWindow(anchor) };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    /// <summary>Small keyboard-icon button that records a combo into <paramref name="box"/>.</summary>
    public static Button MakeRecordButton(TextBox box)
    {
        var btn = new Button
        {
            Content = new MaterialIcon { Kind = MaterialIconKind.KeyboardOutline, Width = 16, Height = 16, Foreground = Res("TextSecBrush") },
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Res("CardBorderBrush"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Record keystroke",
        };
        btn.Click += (_, _) =>
        {
            var result = Capture(box, IsPlaceholder(box) ? "" : box.Text);
            if (!string.IsNullOrEmpty(result))
            {
                box.Foreground = Res("TextPrimaryBrush");
                box.Text = result;
            }
        };
        return btn;
    }

    private static bool IsPlaceholder(TextBox box) =>
        box.Tag is string ph && box.Text == ph;
}
