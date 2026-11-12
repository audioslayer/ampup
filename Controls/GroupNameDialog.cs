using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Material.Icons;

namespace AmpUp.Controls;

/// <summary>
/// Modal for naming a group (new app group, rename app / device group), same chrome as
/// <see cref="TextEditDialog"/>: glowing accent icon tile + title / subtitle, close button, drag to move.
/// Large text field with icon and placeholder, inline validation (empty / duplicate name disables the
/// primary button and shows a red hint), optional suggestion chips. Enter confirms, Esc cancels.
/// </summary>
public sealed class GroupNameDialog : Window
{
    public static readonly string[] AppGroupSuggestions = { "Games", "Browsers", "Music", "Chat", "Streaming" };

    private readonly TextBox _box;
    private readonly TextBlock _placeholder;
    private readonly TextBlock _hint;
    private readonly Border _fieldBorder;
    private readonly Border _primary;
    private readonly IEnumerable<string> _existing;
    private readonly string _initial;
    private bool _valid;
    private string? _result;

    /// <summary>
    /// Show the dialog. Returns the trimmed name on confirm, or null when cancelled.
    /// <paramref name="existingNames"/> are names already taken (case-insensitive); the initial name is always allowed.
    /// <paramref name="suggestions"/> (optional) are shown as chips that fill the field.
    /// </summary>
    public static string? Prompt(Window? owner, string title, string subtitle, string initial,
        IEnumerable<string> existingNames, string primaryText = "Save",
        IEnumerable<string>? suggestions = null, MaterialIconKind icon = MaterialIconKind.PencilOutline,
        int maxLength = 40)
    {
        var dlg = new GroupNameDialog(title, subtitle, initial, existingNames, primaryText, suggestions, icon, maxLength) { Owner = owner };
        dlg.ShowDialog();
        return dlg._result;
    }

    private GroupNameDialog(string title, string subtitle, string initial, IEnumerable<string> existingNames,
        string primaryText, IEnumerable<string>? suggestions, MaterialIconKind icon, int maxLength)
    {
        _existing = existingNames.ToList();
        _initial = initial ?? "";
        var a = ThemeManager.Accent;

        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; // no AllowsTransparency: layered windows break hit-testing on Win11
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgDarkBrush");

        var root = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12) };
        root.SetResourceReference(Border.BackgroundProperty, "BgDarkBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        root.Child = stack;

        // Header: glowing icon tile + title / subtitle, close on the right (drag to move)
        var header = new Grid { Margin = new Thickness(0, 0, 0, 18), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = UiKit.IconTile(a, icon, 38);
        tile.Margin = new Thickness(0, 0, 12, 0);
        tile.Effect = GlassStyle.GlowEffect(a, 18, 0.35);
        header.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var st = new TextBlock { Text = subtitle, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        st.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        titles.Children.Add(t);
        titles.Children.Add(st);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var close = UiKit.IconButton(MaterialIconKind.Close, "Cancel (Esc)", _ => Close(), size: 26);
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Grid or TextBlock or StackPanel)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        stack.Children.Add(header);

        // Large text field: icon + text box with placeholder
        _box = new TextBox
        {
            Text = _initial,
            MaxLength = maxLength,
            FontSize = 15,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 2, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _box.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _box.CaretBrush = new SolidColorBrush(a);
        _placeholder = new TextBlock
        {
            Text = "Group name",
            FontSize = 15,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

        var fieldGrid = new Grid();
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fieldIcon = UiKit.Icon(MaterialIconKind.TagOutline, 18);
        fieldIcon.Margin = new Thickness(0, 0, 10, 0);
        fieldGrid.Children.Add(fieldIcon);
        var textHost = new Grid();
        textHost.Children.Add(_box);
        textHost.Children.Add(_placeholder);
        Grid.SetColumn(textHost, 1);
        fieldGrid.Children.Add(textHost);
        _fieldBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            MinHeight = 46,
            Child = fieldGrid,
        };
        _fieldBorder.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        stack.Children.Add(_fieldBorder);

        // Validation hint (left) + character count (right)
        var under = new Grid { Margin = new Thickness(2, 6, 2, 0) };
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        under.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "DangerRedBrush");
        under.Children.Add(_hint);
        var count = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 1, 0, 0) };
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        Grid.SetColumn(count, 1);
        under.Children.Add(count);
        stack.Children.Add(under);

        // Suggestion chips
        var chips = suggestions?.ToList();
        if (chips is { Count: > 0 })
        {
            var cap = new TextBlock { Text = "SUGGESTIONS", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) };
            cap.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            stack.Children.Add(cap);
            var wrap = new WrapPanel();
            foreach (var s in chips) wrap.Children.Add(MakeChip(s));
            stack.Children.Add(wrap);
        }

        // Cancel / primary
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(MakeButton("Cancel", primary: false, Close, out _));
        _primary = MakeButton(primaryText, primary: true, Confirm, out _);
        buttons.Children.Add(_primary);
        stack.Children.Add(buttons);

        void Refresh()
        {
            count.Text = $"{_box.Text.Length} / {maxLength}";
            _placeholder.Visibility = _box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Validate();
        }
        _box.TextChanged += (_, _) => Refresh();
        _box.GotKeyboardFocus += (_, _) => PaintField();
        _box.LostKeyboardFocus += (_, _) => PaintField();
        Refresh();

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Confirm(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    private void Validate()
    {
        var name = _box.Text.Trim();
        string hint = "";
        if (name.Length == 0)
            hint = _box.Text.Length > 0 ? "Name can't be blank." : "";
        else if (!name.Equals(_initial.Trim(), StringComparison.OrdinalIgnoreCase)
                 && _existing.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            hint = $"A group named \"{name}\" already exists.";
        _valid = name.Length > 0 && hint.Length == 0;
        _hint.Text = hint;
        _hint.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _primary.Opacity = _valid ? 1.0 : 0.4;
        _primary.Cursor = _valid ? Cursors.Hand : Cursors.Arrow;
        PaintField();
    }

    private void PaintField()
    {
        var a = ThemeManager.Accent;
        if (_hint.Visibility == Visibility.Visible)
            _fieldBorder.SetResourceReference(Border.BorderBrushProperty, "DangerRedBrush");
        else if (_box.IsKeyboardFocused)
            _fieldBorder.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x88));
        else
            _fieldBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
    }

    private void Confirm()
    {
        if (!_valid) return;
        _result = _box.Text.Trim();
        Close();
    }

    private Border MakeChip(string text)
    {
        var a = ThemeManager.Accent;
        var label = new TextBlock { Text = text, FontSize = 11.5 };
        var chip = new Border
        {
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = label,
        };
        void Paint(bool hover)
        {
            chip.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, hover ? (byte)0x30 : (byte)0x14));
            chip.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, hover ? (byte)0x77 : (byte)0x3A));
            if (hover) label.Foreground = new SolidColorBrush(a);
            else label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        }
        chip.MouseEnter += (_, _) => Paint(true);
        chip.MouseLeave += (_, _) => Paint(false);
        chip.MouseLeftButtonUp += (_, _) =>
        {
            _box.Text = text;
            _box.Focus();
            _box.CaretIndex = _box.Text.Length;
        };
        Paint(false);
        return chip;
    }

    private Border MakeButton(string text, bool primary, Action onClick, out TextBlock label)
    {
        var a = ThemeManager.Accent;
        var lbl = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        label = lbl;
        var b = new Border
        {
            MinWidth = 88,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = lbl,
        };
        void Paint(bool hover)
        {
            if (primary)
            {
                // Hover: 15% toward white (only while the name is valid)
                bool lit = hover && _valid;
                var bg = lit
                    ? Color.FromRgb((byte)(a.R + (255 - a.R) * 0.15), (byte)(a.G + (255 - a.G) * 0.15), (byte)(a.B + (255 - a.B) * 0.15))
                    : a;
                b.Background = new SolidColorBrush(bg);
                b.BorderBrush = new SolidColorBrush(bg);
                lbl.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
            }
            else
            {
                b.Background = hover ? (Brush)Application.Current.FindResource("InputBgBrush") : Brushes.Transparent;
                b.BorderBrush = (Brush)Application.Current.FindResource("CardBorderBrush");
                lbl.Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush");
            }
        }
        b.MouseEnter += (_, _) => Paint(true);
        b.MouseLeave += (_, _) => Paint(false);
        b.MouseLeftButtonUp += (_, _) => onClick();
        Paint(false);
        return b;
    }
}
