using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;
using AmpUp.Services;
using MacroStep = AmpUp.Services.KeyboardInjector.MacroStep;

namespace AmpUp.Controls;

/// <summary>
/// Macro sequence builder shared by <see cref="KeyCaptureDialog"/> and the inline recorder card:
/// a wrap row of step chips (keycap combos and amber pause chips; click to select) plus a toolbar
/// with Add pause (ms field + presets), move left / right, delete and Clear all.
/// Recording a combo is left to the host (<see cref="PutCombo"/> appends or replaces the selection).
/// </summary>
public sealed class MacroSequenceBuilder
{
    public static readonly Color PauseColor = Color.FromRgb(0xFF, 0xB3, 0x2E);
    private static readonly int[] PausePresets = { 100, 250, 500, 1000 };

    private readonly List<MacroStep> _steps = new();
    private readonly ContentControl _chipsHost;
    private readonly double _capSize;
    private readonly Border _moveLeft, _moveRight, _delete, _clearAll;
    private readonly TextBox _msBox;

    /// <summary>Index of the selected step, or -1.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The chip row.</summary>
    public FrameworkElement Chips => _chipsHost;

    /// <summary>Add pause + step tools (wraps on narrow panels).</summary>
    public FrameworkElement Toolbar { get; }

    /// <summary>Raised when the steps change (not on selection-only changes).</summary>
    public event Action? Changed;

    /// <summary>Raised when the selection changes.</summary>
    public event Action? SelectionChanged;

    public int Count => _steps.Count;

    public string Sequence => KeyboardInjector.FormatSequence(_steps);

    public MacroSequenceBuilder(double capSize)
    {
        _capSize = capSize;
        _chipsHost = new ContentControl { Focusable = false, HorizontalAlignment = HorizontalAlignment.Center };

        var bar = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };

        // Add pause: [⏱ Add pause] [500] ms  100 250 500 1000
        var pauseGroup = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 4) };
        pauseGroup.Children.Add(MakePauseButton());
        _msBox = new TextBox
        {
            Text = "500",
            Width = 50,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(6, 0, 3, 0),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Pause length in milliseconds (10 – 60000). Mouse wheel steps by 50.",
            CaretBrush = new SolidColorBrush(PauseColor),
        };
        _msBox.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        _msBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        _msBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorderBrush");
        _msBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        _msBox.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            SetMs(PauseMs + (e.Delta > 0 ? 50 : -50));
        };
        _msBox.LostFocus += (_, _) => SetMs(PauseMs);
        pauseGroup.Children.Add(_msBox);
        var msLabel = new TextBlock { Text = "ms", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        msLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        pauseGroup.Children.Add(msLabel);
        foreach (var ms in PausePresets)
            pauseGroup.Children.Add(MakePresetPill(ms));
        bar.Children.Add(pauseGroup);

        // Step tools
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        _moveLeft = UiKit.IconButton(MaterialIconKind.ChevronLeft, "Move step left", _ => Move(-1), size: 24);
        _moveRight = UiKit.IconButton(MaterialIconKind.ChevronRight, "Move step right", _ => Move(1), size: 24);
        _delete = UiKit.IconButton(MaterialIconKind.DeleteOutline, "Delete step (Del)", _ => DeleteSelected(), danger: true, size: 24);
        _clearAll = UiKit.IconButton(MaterialIconKind.DeleteSweepOutline, "Clear all steps", _ => ClearAll(), danger: true, size: 24);
        tools.Children.Add(_moveLeft);
        tools.Children.Add(_moveRight);
        tools.Children.Add(_delete);
        var sep = new Border { Width = 1, Height = 16, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        sep.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        tools.Children.Add(sep);
        tools.Children.Add(_clearAll);
        bar.Children.Add(tools);
        Toolbar = bar;

        Render();
    }

    // ── Editing ────────────────────────────────────────────────────

    /// <summary>Replace all steps without raising <see cref="Changed"/>.</summary>
    public void Load(string? sequence)
    {
        _steps.Clear();
        _steps.AddRange(KeyboardInjector.ParseSequence(sequence));
        Selected = -1;
        Render();
    }

    /// <summary>Replace the selected step with a combo, or append it when nothing is selected.</summary>
    public void PutCombo(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return;
        var step = new MacroStep(combo.Trim().ToLowerInvariant(), 0);
        if (Selected >= 0 && Selected < _steps.Count) _steps[Selected] = step;
        else _steps.Add(step);
        Selected = -1;
        Commit();
    }

    /// <summary>Insert a pause after the selected step (and select it), or append one.</summary>
    public void AddPause(int ms)
    {
        var step = new MacroStep(null, KeyboardInjector.ClampDelay(ms));
        if (Selected >= 0 && Selected < _steps.Count)
        {
            _steps.Insert(Selected + 1, step);
            Selected++;
        }
        else _steps.Add(step);
        Commit();
    }

    public void Move(int dir)
    {
        int to = Selected + dir;
        if (Selected < 0 || Selected >= _steps.Count || to < 0 || to >= _steps.Count) return;
        (_steps[Selected], _steps[to]) = (_steps[to], _steps[Selected]);
        Selected = to;
        Commit();
    }

    public void DeleteSelected()
    {
        if (Selected < 0 || Selected >= _steps.Count) return;
        _steps.RemoveAt(Selected);
        Selected = Math.Min(Selected, _steps.Count - 1);
        Commit();
    }

    public void ClearAll()
    {
        if (_steps.Count == 0) return;
        _steps.Clear();
        Selected = -1;
        Commit();
    }

    public void Select(int index)
    {
        Selected = index >= 0 && index < _steps.Count ? index : -1;
        Render();
        SelectionChanged?.Invoke();
    }

    private void Commit()
    {
        Render();
        Changed?.Invoke();
        SelectionChanged?.Invoke();
    }

    private int PauseMs => int.TryParse(_msBox.Text, out int ms) ? KeyboardInjector.ClampDelay(ms) : 500;

    private void SetMs(int ms) => _msBox.Text = KeyboardInjector.ClampDelay(ms).ToString();

    private void Render()
    {
        // Pause/step tools only appear once there is a step to work with.
        if (Toolbar != null) Toolbar.Visibility = _steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _chipsHost.Content = _steps.Count == 0
            ? KeyCaptureDialog.MakeHintText("No keys set", 13, "TextDimBrush")
            : BuildChips(_steps, _capSize, Selected, i => Select(i == Selected ? -1 : i));

        bool sel = Selected >= 0;
        SetEnabled(_moveLeft, sel && Selected > 0);
        SetEnabled(_moveRight, sel && Selected < _steps.Count - 1);
        SetEnabled(_delete, sel);
        SetEnabled(_clearAll, _steps.Count > 0);
    }

    private static void SetEnabled(UIElement e, bool on)
    {
        e.IsEnabled = on;
        e.Opacity = on ? 1 : 0.35;
    }

    // ── Chips ──────────────────────────────────────────────────────

    /// <summary>"500ms" / "1.5s".</summary>
    public static string FormatDelay(int ms) => ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:0.##}s";

    /// <summary>
    /// Wrap row of step chips joined by small chevrons. <paramref name="onClick"/> (optional) gets the
    /// clicked step index; <paramref name="selected"/> gets an accent outline.
    /// </summary>
    public static FrameworkElement BuildChips(IReadOnlyList<MacroStep> steps, double capSize, int selected = -1, Action<int>? onClick = null)
    {
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < steps.Count; i++)
        {
            var chip = MakeChip(steps[i], capSize, i == selected);
            if (onClick != null)
            {
                int idx = i;
                chip.Cursor = Cursors.Hand;
                chip.ToolTip = "Click to select";
                chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(idx); };
            }
            if (i > 0)
            {
                var arrow = UiKit.Icon(MaterialIconKind.ChevronRight, 14);
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.Margin = new Thickness(1, 0, 1, 4);
                row.Children.Add(arrow);
            }
            row.Children.Add(chip);
        }
        return row;
    }

    private static Border MakeChip(MacroStep step, double capSize, bool selected)
    {
        var a = ThemeManager.Accent;
        FrameworkElement content;
        Brush bg, border;
        if (step.IsDelay)
        {
            var p = PauseColor;
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new MaterialIcon
            {
                Kind = MaterialIconKind.TimerOutline,
                Width = 13, Height = 13,
                Margin = new Thickness(0, 0, 5, 0),
                Foreground = new SolidColorBrush(p),
                VerticalAlignment = VerticalAlignment.Center,
            });
            sp.Children.Add(new TextBlock
            {
                Text = "wait " + FormatDelay(step.DelayMs),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(p),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content = sp;
            bg = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x1F));
            border = new SolidColorBrush(selected ? a : ThemeManager.WithAlpha(p, 0x77));
        }
        else
        {
            content = KeyCaptureDialog.BuildKeycaps(KeyCaptureDialog.SplitCombo(step.Combo!), capSize);
            bg = selected ? new SolidColorBrush(ThemeManager.WithAlpha(a, 0x14)) : Brushes.Transparent;
            border = selected ? new SolidColorBrush(a) : Brushes.Transparent;
        }
        return new Border
        {
            Child = content,
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = step.IsDelay ? new Thickness(9, 5, 9, 5) : new Thickness(4),
            Margin = new Thickness(0, 0, 0, 4),
            MinHeight = step.IsDelay ? 0 : capSize + 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // ── Pause controls ─────────────────────────────────────────────

    private Border MakePauseButton()
    {
        var p = PauseColor;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new MaterialIcon { Kind = MaterialIconKind.TimerOutline, Width = 13, Height = 13, Margin = new Thickness(0, 0, 5, 0), Foreground = new SolidColorBrush(p), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = "Add pause", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(p), VerticalAlignment = VerticalAlignment.Center });
        var b = new Border
        {
            Child = sp,
            Padding = new Thickness(9, 4, 9, 4),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x77)),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x14)),
            Cursor = Cursors.Hand,
            ToolTip = "Insert a pause after the selected step (or at the end)",
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x2E));
        b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x14));
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; AddPause(PauseMs); };
        return b;
    }

    private Border MakePresetPill(int ms)
    {
        var text = new TextBlock { Text = ms >= 1000 ? FormatDelay(ms) : ms.ToString(), FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        var b = new Border
        {
            Child = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = $"Set pause to {FormatDelay(ms)}",
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; SetMs(ms); };
        return b;
    }
}
