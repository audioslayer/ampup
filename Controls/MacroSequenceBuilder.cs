using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Material.Icons;
using Material.Icons.WPF;
using AmpUp.Services;
using MacroStep = AmpUp.Services.KeyboardInjector.MacroStep;

namespace AmpUp.Controls;

/// <summary>
/// Macro timeline editor shared by <see cref="KeyCaptureDialog"/> and the inline recorder card:
/// a wrapping row of numbered step chips (keycap combos and amber pause chips) joined by a connector,
/// with a hover "+" insertion slot between every pair of steps and at both ends. A slot opens a glass
/// flyout with "Record keys here" and "Pause here" (presets + custom ms). Pause chips edit in place
/// (click to type, wheel ±50 / Shift ±10 / Ctrl ±250); chips show an × on hover and drag to reorder;
/// clicking a keycap chip selects it so Record replaces it. Footer: step count, duration, Clear all.
/// Recording a combo is left to the host: <see cref="RecordRequested"/> asks for it and
/// <see cref="PutCombo"/> inserts it at the pending slot, replaces the selection, or appends.
/// </summary>
public sealed class MacroSequenceBuilder
{
    public static readonly Color PauseColor = Color.FromRgb(0xFF, 0xB3, 0x2E);
    private static readonly int[] PausePresets = { 100, 250, 500, 1000, 2000 };
    private const string DragFormat = "AmpUp.MacroStepIndex";
    private const double NumberHeight = 13;
    private const int KeyStepMs = 16; // rough per-key time for the duration estimate

    private readonly List<MacroStep> _steps = new();
    private readonly StackPanel _root;
    private readonly ContentControl _timelineHost;
    private readonly Grid _footer;
    private readonly TextBlock _footerText;
    private readonly double _capSize;

    private int _editing = -1;          // pause chip being edited inline
    private TextBox? _editBox;
    private int _dragIndex = -1;        // chip pressed (drag candidate)
    private Point _dragStart;
    private bool _suppressClick;

    /// <summary>Index of the selected step, or -1.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>Slot index the next recorded combo is inserted at, or -1.</summary>
    public int PendingInsert { get; private set; } = -1;

    /// <summary>Show a Record button in the empty state (off when the host has its own right below).</summary>
    public bool ShowEmptyRecordButton { get; set; } = true;

    /// <summary>The timeline (steps + footer).</summary>
    public FrameworkElement Chips => _root;

    /// <summary>Raised when the steps change (not on selection-only changes).</summary>
    public event Action? Changed;

    /// <summary>Raised when the selection or pending insert slot changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// The user asked to record keys: the slot index to insert at (also in <see cref="PendingInsert"/>),
    /// or -1 to append / replace the selection.
    /// </summary>
    public event Action<int>? RecordRequested;

    public int Count => _steps.Count;

    public string Sequence => KeyboardInjector.FormatSequence(_steps);

    public MacroSequenceBuilder(double capSize)
    {
        _capSize = capSize;
        _timelineHost = new ContentControl { Focusable = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };

        // Footer: "4 steps · ≈ 1.3s" ........ [Clear all]
        _footer = new Grid { Margin = new Thickness(2, 6, 2, 0) };
        _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footerText = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _footerText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        _footer.Children.Add(_footerText);
        var clear = MakeClearLink();
        Grid.SetColumn(clear, 1);
        _footer.Children.Add(clear);

        _root = new StackPanel();
        _root.Children.Add(_timelineHost);
        _root.Children.Add(_footer);

        Render();
    }

    // ── Editing ────────────────────────────────────────────────────

    /// <summary>Replace all steps without raising <see cref="Changed"/>.</summary>
    public void Load(string? sequence)
    {
        _steps.Clear();
        _steps.AddRange(KeyboardInjector.ParseSequence(sequence));
        Selected = -1;
        PendingInsert = -1;
        _editing = -1;
        Render();
    }

    /// <summary>
    /// Insert a combo at the pending slot, else replace the selected step, else append it.
    /// </summary>
    public void PutCombo(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return;
        var step = new MacroStep(combo.Trim().ToLowerInvariant(), 0);
        if (PendingInsert >= 0) _steps.Insert(Math.Min(PendingInsert, _steps.Count), step);
        else if (Selected >= 0 && Selected < _steps.Count) _steps[Selected] = step;
        else _steps.Add(step);
        PendingInsert = -1;
        Selected = -1;
        Commit();
    }

    /// <summary>Append a pause.</summary>
    public void AddPause(int ms) => InsertPause(_steps.Count, ms);

    /// <summary>Insert a pause at slot <paramref name="index"/> (0 = before the first step).</summary>
    public void InsertPause(int index, int ms)
    {
        index = Math.Clamp(index, 0, _steps.Count);
        _steps.Insert(index, new MacroStep(null, KeyboardInjector.ClampDelay(ms)));
        Selected = -1;
        PendingInsert = -1;
        Commit();
    }

    /// <summary>Change the length of the pause at <paramref name="index"/>.</summary>
    public void SetPause(int index, int ms)
    {
        if (index < 0 || index >= _steps.Count || !_steps[index].IsDelay) return;
        _steps[index] = new MacroStep(null, KeyboardInjector.ClampDelay(ms));
        Commit();
    }

    /// <summary>Move a step to slot <paramref name="slot"/> (0..Count, measured before the move).</summary>
    public void MoveStep(int from, int slot)
    {
        if (from < 0 || from >= _steps.Count) return;
        slot = Math.Clamp(slot, 0, _steps.Count);
        if (slot == from || slot == from + 1) { Render(); return; }
        bool wasSelected = Selected == from;
        var step = _steps[from];
        _steps.RemoveAt(from);
        if (slot > from) slot--;
        _steps.Insert(slot, step);
        Selected = wasSelected ? slot : -1;
        PendingInsert = -1;
        Commit();
    }

    public void Move(int dir)
    {
        if (Selected < 0) return;
        MoveStep(Selected, dir < 0 ? Selected - 1 : Selected + 2);
    }

    public void DeleteAt(int index)
    {
        if (index < 0 || index >= _steps.Count) return;
        _steps.RemoveAt(index);
        if (Selected == index) Selected = -1;
        else if (Selected > index) Selected--;
        PendingInsert = -1;
        Commit();
    }

    public void DeleteSelected()
    {
        if (Selected < 0 || Selected >= _steps.Count) return;
        DeleteAt(Selected);
    }

    public void ClearAll()
    {
        if (_steps.Count == 0) return;
        _steps.Clear();
        Selected = -1;
        PendingInsert = -1;
        Commit();
    }

    public void Select(int index)
    {
        Selected = index >= 0 && index < _steps.Count ? index : -1;
        PendingInsert = -1;
        Render();
        SelectionChanged?.Invoke();
    }

    /// <summary>Mark slot <paramref name="index"/> as the insert point and raise <see cref="RecordRequested"/>.</summary>
    public void RecordAt(int index)
    {
        BeginInsert(index);
        SelectionChanged?.Invoke();
        RecordRequested?.Invoke(PendingInsert);
    }

    /// <summary>Mark slot <paramref name="index"/> as the insert point for the next <see cref="PutCombo"/> (no events).</summary>
    public void BeginInsert(int index)
    {
        if (index < 0) return;
        PendingInsert = Math.Clamp(index, 0, _steps.Count);
        Selected = -1;
        _editing = -1;
        Render();
    }

    /// <summary>Drop the pending insert slot (recording stopped without a combo).</summary>
    public void CancelInsert()
    {
        if (PendingInsert < 0) return;
        PendingInsert = -1;
        Render();
        SelectionChanged?.Invoke();
    }

    private void Commit()
    {
        _editing = -1;
        Render();
        Changed?.Invoke();
        SelectionChanged?.Invoke();
    }

    // ── Rendering ──────────────────────────────────────────────────

    /// <summary>
    /// Rounded icon tile with an accent gradient face, bright hairline border and soft glow
    /// (header / empty-state badge). Icon is ~55% of <paramref name="size"/>.
    /// </summary>
    public static Border GlowTile(Color color, MaterialIconKind kind, double size)
    {
        var face = new LinearGradientBrush(ThemeManager.WithAlpha(color, 0x40), ThemeManager.WithAlpha(color, 0x12), new Point(0, 0), new Point(1, 1));
        face.Freeze();
        return new Border
        {
            Width = size, Height = size,
            CornerRadius = new CornerRadius(size * 0.28),
            Background = face,
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(color, 0x88)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Effect = GlassStyle.GlowEffect(color, size * 0.6, 0.45), // single header/empty-state badge; null when Reduce effects
            Child = new MaterialIcon
            {
                Kind = kind,
                Width = size * 0.55, Height = size * 0.55,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>"500ms" / "1.5s".</summary>
    public static string FormatDelay(int ms) => ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:0.##}s";

    private void Render()
    {
        _editBox = null; // the old editor (if any) is discarded; its LostFocus must not commit
        if (_steps.Count == 0 && PendingInsert < 0)
        {
            _timelineHost.Content = BuildEmptyState();
            _footer.Visibility = Visibility.Collapsed;
            return;
        }

        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < _steps.Count; i++)
        {
            row.Children.Add(MakeSlot(i));
            row.Children.Add(MakeStepItem(i));
        }
        row.Children.Add(MakeSlot(_steps.Count));
        _timelineHost.Content = row;

        int keys = _steps.Count(s => !s.IsDelay);
        int pauses = _steps.Count - keys;
        long total = _steps.Where(s => s.IsDelay).Sum(s => (long)s.DelayMs) + (long)keys * KeyStepMs;
        _footerText.Text = $"{_steps.Count} step{(_steps.Count == 1 ? "" : "s")}"
            + (pauses > 0 ? $" · {pauses} pause{(pauses == 1 ? "" : "s")}" : "")
            + $" · ≈ {FormatDelay((int)Math.Min(total, int.MaxValue))}";
        _footer.Visibility = _steps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildEmptyState()
    {
        var a = ThemeManager.Accent;
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        var tile = GlowTile(a, MaterialIconKind.KeyboardOutline, 52);
        tile.HorizontalAlignment = HorizontalAlignment.Center;
        tile.Margin = new Thickness(0, 4, 0, 12);
        sp.Children.Add(tile);
        var title = KeyCaptureDialog.MakeHintText("Press Record and tap your keys", 14, "TextPrimaryBrush");
        title.TextWrapping = TextWrapping.Wrap;
        title.TextAlignment = TextAlignment.Center;
        sp.Children.Add(title);
        var sub = KeyCaptureDialog.MakeHintText("Then hover between steps to drop in pauses", 11, "TextDimBrush");
        sub.FontWeight = FontWeights.Normal;
        sub.TextWrapping = TextWrapping.Wrap;
        sub.TextAlignment = TextAlignment.Center;
        sub.Margin = new Thickness(0, 3, 0, 0);
        sp.Children.Add(sub);

        if (ShowEmptyRecordButton)
        {
            var icon = new MaterialIcon { Kind = MaterialIconKind.RecordCircleOutline, Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = "Record", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(label);
            var btn = new Border
            {
                Child = content,
                Padding = new Thickness(18, 7, 20, 7),
                Margin = new Thickness(0, 12, 0, 0),
                CornerRadius = new CornerRadius(18),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Effect = GlassStyle.GlowEffect(a, 14, 0.3),
            };
            void Paint(bool hover)
            {
                var bg = hover
                    ? Color.FromRgb((byte)(a.R + (255 - a.R) * 0.15), (byte)(a.G + (255 - a.G) * 0.15), (byte)(a.B + (255 - a.B) * 0.15))
                    : a;
                btn.Background = new SolidColorBrush(bg);
                var fg = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
                label.Foreground = fg;
                icon.Foreground = fg;
            }
            btn.MouseEnter += (_, _) => Paint(true);
            btn.MouseLeave += (_, _) => Paint(false);
            btn.MouseLeftButtonUp += (_, e) => { e.Handled = true; RecordRequested?.Invoke(-1); };
            Paint(false);
            sp.Children.Add(btn);
        }
        return sp;
    }

    // ── Insertion slots ────────────────────────────────────────────

    /// <summary>Slim slot before step <paramref name="index"/>: connector line + hover "+" (also a drop target).</summary>
    private FrameworkElement MakeSlot(int index)
    {
        var a = ThemeManager.Accent;
        bool pending = index == PendingInsert;
        bool edge = index == 0 || index == _steps.Count;

        var line = new Border
        {
            Height = 1.5,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x45)),
            Visibility = edge ? Visibility.Hidden : Visibility.Visible,
        };
        var plusIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.Plus,
            Width = 14, Height = 14,
            Foreground = new SolidColorBrush(a),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var plus = new Border
        {
            Width = 22, Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0xCC)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = plusIcon,
            Opacity = pending ? 1 : 0,
        };
        plus.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        // Repeated insert slots: glow only on the single pending slot
        plus.Effect = pending ? GlassStyle.GlowEffect(a, 12, 0.75) : null;

        var slot = new Grid
        {
            Width = _steps.Count == 0 ? 40 : 24,
            MinHeight = _capSize + 8,
            Margin = new Thickness(0, 0, 0, NumberHeight),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            AllowDrop = true,
            ToolTip = pending ? "Recording inserts here" : "Insert a key or pause here",
        };
        slot.Children.Add(line);
        slot.Children.Add(plus);

        void Show(bool on) => plus.Opacity = on || pending ? 1 : 0;
        slot.MouseEnter += (_, _) => Show(true);
        slot.MouseLeave += (_, _) => Show(false);
        slot.MouseLeftButtonUp += (_, e) => { e.Handled = true; ShowInsertFlyout(plus, index); };
        slot.DragEnter += (_, e) => { e.Effects = DragSource(e) >= 0 ? DragDropEffects.Move : DragDropEffects.None; Show(true); e.Handled = true; };
        slot.DragOver += (_, e) => { e.Effects = DragSource(e) >= 0 ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        slot.DragLeave += (_, _) => Show(false);
        slot.Drop += (_, e) =>
        {
            e.Handled = true;
            int from = DragSource(e);
            if (from >= 0) MoveStep(from, index);
        };
        return slot;
    }

    private static int DragSource(DragEventArgs e) =>
        e.Data.GetDataPresent(DragFormat) && e.Data.GetData(DragFormat) is int i ? i : -1;

    /// <summary>Glass flyout under a slot: Record keys here / Pause here (presets + custom ms).</summary>
    private void ShowInsertFlyout(FrameworkElement anchor, int index)
    {
        var a = ThemeManager.Accent;
        var p = PauseColor;
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -24,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };

        var root = new Border
        {
            Width = 236,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Margin = new Thickness(10), // room for the drop shadow
            Effect = ThemeManager.ReduceEffects ? null : new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 4, Opacity = 0.55 }, // single popup
        };
        root.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        var stack = new StackPanel();
        root.Child = stack;

        // Accent hairline, same as GlassContextMenuHost
        stack.Children.Add(new Border
        {
            Height = 2,
            Margin = new Thickness(6, 0, 6, 6),
            CornerRadius = new CornerRadius(1),
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(ThemeManager.WithAlpha(a, 0x00), 0.0),
                    new(ThemeManager.WithAlpha(a, 0xA0), 0.5),
                    new(ThemeManager.WithAlpha(a, 0x00), 1.0),
                }, new Point(0, 0), new Point(1, 0)),
        });

        var where = new TextBlock
        {
            Text = _steps.Count == 0 ? "INSERT" : $"INSERT AT STEP {index + 1}",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(8, 0, 8, 4),
        };
        where.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        stack.Children.Add(where);

        // Record keys here
        var recRow = new Border
        {
            Padding = new Thickness(8, 7, 10, 7),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        var recContent = new StackPanel { Orientation = Orientation.Horizontal };
        recContent.Children.Add(new MaterialIcon { Kind = MaterialIconKind.RecordCircleOutline, Width = 18, Height = 18, Margin = new Thickness(0, 0, 9, 0), Foreground = new SolidColorBrush(a), VerticalAlignment = VerticalAlignment.Center });
        var recText = new TextBlock { Text = "Record keys here", FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        recText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        recContent.Children.Add(recText);
        recRow.Child = recContent;
        recRow.MouseEnter += (_, _) => recRow.Background = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x2A));
        recRow.MouseLeave += (_, _) => recRow.Background = Brushes.Transparent;
        recRow.MouseLeftButtonUp += (_, e) => { e.Handled = true; popup.IsOpen = false; RecordAt(index); };
        stack.Children.Add(recRow);

        stack.Children.Add(new Border { Height = 1, Margin = new Thickness(10, 4, 10, 6), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)) });

        // Pause here: presets
        var pauseHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 6) };
        pauseHead.Children.Add(new MaterialIcon { Kind = MaterialIconKind.TimerOutline, Width = 18, Height = 18, Margin = new Thickness(0, 0, 9, 0), Foreground = new SolidColorBrush(p), VerticalAlignment = VerticalAlignment.Center });
        var pauseText = new TextBlock { Text = "Pause here", FontSize = 12, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        pauseText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        pauseHead.Children.Add(pauseText);
        stack.Children.Add(pauseHead);

        var presets = new WrapPanel { Margin = new Thickness(6, 0, 6, 6) };
        foreach (var ms in PausePresets)
        {
            int value = ms;
            presets.Children.Add(MakePausePill(FormatDelay(value), () => { popup.IsOpen = false; InsertPause(index, value); }));
        }
        stack.Children.Add(presets);

        // Custom: [ 750 ] ms [Add]
        var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 4) };
        var msBox = new TextBox
        {
            Text = "750",
            Width = 58,
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            ToolTip = "Custom pause in milliseconds (10 – 60000). Mouse wheel steps by 50.",
            CaretBrush = new SolidColorBrush(p),
        };
        msBox.SetResourceReference(TextBox.BackgroundProperty, "InputBgBrush");
        msBox.SetResourceReference(TextBox.ForegroundProperty, "TextPrimaryBrush");
        msBox.SetResourceReference(TextBox.BorderBrushProperty, "InputBorderBrush");
        int BoxMs() => int.TryParse(msBox.Text, out int v) ? KeyboardInjector.ClampDelay(v) : 500;
        void AddCustom() { popup.IsOpen = false; InsertPause(index, BoxMs()); }
        msBox.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        msBox.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            msBox.Text = KeyboardInjector.ClampDelay(BoxMs() + WheelStep(e.Delta)).ToString();
        };
        msBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; AddCustom(); }
            else if (e.Key == Key.Escape) { e.Handled = true; popup.IsOpen = false; }
        };
        custom.Children.Add(msBox);
        var msLabel = new TextBlock { Text = "ms", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
        msLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        custom.Children.Add(msLabel);
        custom.Children.Add(MakePausePill("Add", AddCustom));
        stack.Children.Add(custom);

        popup.Child = root;
        popup.IsOpen = true;
    }

    private static Border MakePausePill(string text, Action onClick)
    {
        var p = PauseColor;
        var b = new Border
        {
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(p), VerticalAlignment = VerticalAlignment.Center },
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(2, 0, 2, 4),
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x66)),
            Background = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x14)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x33));
        b.MouseLeave += (_, _) => b.Background = new SolidColorBrush(ThemeManager.WithAlpha(p, 0x14));
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    /// <summary>Wheel step for pause lengths: 50 ms, Shift = 10, Ctrl = 250.</summary>
    private static int WheelStep(int delta)
    {
        var m = Keyboard.Modifiers;
        int step = m.HasFlag(ModifierKeys.Control) ? 250 : m.HasFlag(ModifierKeys.Shift) ? 10 : 50;
        return delta > 0 ? step : -step;
    }

    // ── Step chips ─────────────────────────────────────────────────

    /// <summary>Chip + hover delete "×" + position number underneath.</summary>
    private FrameworkElement MakeStepItem(int i)
    {
        var step = _steps[i];
        var chip = step.IsDelay ? MakePauseChip(i) : MakeKeyChip(i);

        var del = new Border
        {
            Width = 19, Height = 19,
            CornerRadius = new CornerRadius(9.5),
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -7, -7, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Remove step",
            Visibility = Visibility.Hidden,
            Child = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 13, Height = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        del.MouseLeftButtonUp += (_, e) => { e.Handled = true; DeleteAt(i); };

        var chipGrid = new Grid();
        chipGrid.Children.Add(chip);
        chipGrid.Children.Add(del);

        var number = new TextBlock
        {
            Text = (i + 1).ToString(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Height = NumberHeight,
            Margin = new Thickness(0, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        number.SetResourceReference(TextBlock.ForegroundProperty, i == Selected ? "AccentBrush" : "TextDimBrush");

        var item = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 2) };
        item.Children.Add(chipGrid);
        item.Children.Add(number);
        item.MouseEnter += (_, _) => del.Visibility = _editing == i ? Visibility.Hidden : Visibility.Visible;
        item.MouseLeave += (_, _) => del.Visibility = Visibility.Hidden;

        // Drag to reorder (past the system drag threshold), drop on a chip = before / after by half
        chip.AllowDrop = true;
        chip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _suppressClick = false;
            _dragIndex = _editing == i ? -1 : i;
            _dragStart = e.GetPosition(_root);
        };
        chip.PreviewMouseMove += (_, e) =>
        {
            if (_dragIndex != i || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(_root);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragIndex = -1;
            _suppressClick = true;
            item.Opacity = 0.4;
            del.Visibility = Visibility.Hidden;
            DragDrop.DoDragDrop(chip, new DataObject(DragFormat, i), DragDropEffects.Move);
            item.Opacity = 1; // no-op when a drop already re-rendered
        };
        chip.PreviewMouseLeftButtonUp += (_, _) => _dragIndex = -1;
        chip.DragOver += (_, e) => { e.Effects = DragSource(e) >= 0 ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        chip.Drop += (_, e) =>
        {
            e.Handled = true;
            int from = DragSource(e);
            if (from < 0) return;
            bool before = e.GetPosition(chip).X < chip.ActualWidth / 2;
            MoveStep(from, before ? i : i + 1);
        };
        return item;
    }

    private Border MakeKeyChip(int i)
    {
        var a = ThemeManager.Accent;
        bool selected = i == Selected;
        var chip = new Border
        {
            Child = KeyCaptureDialog.BuildKeycaps(KeyCaptureDialog.SplitCombo(_steps[i].Combo!), _capSize),
            Background = selected ? new SolidColorBrush(ThemeManager.WithAlpha(a, 0x1A)) : new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)),
            BorderBrush = selected ? new SolidColorBrush(a) : new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(4, 4, 4, 2),
            MinHeight = _capSize + 8,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = selected ? "Selected: Record replaces this step (click to deselect)" : "Click to select, drag to move",
        };
        if (selected) chip.Effect = GlassStyle.GlowEffect(a, 12, 0.45); // single selected chip only
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_suppressClick) { _suppressClick = false; return; }
            Select(i == Selected ? -1 : i);
        };
        return chip;
    }

    private Border MakePauseChip(int i)
    {
        var p = PauseColor;
        int ms = _steps[i].DelayMs;
        bool editing = i == _editing;

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new MaterialIcon
        {
            Kind = MaterialIconKind.TimerOutline,
            Width = 17, Height = 17,
            Margin = new Thickness(0, 0, 5, 0),
            Foreground = new SolidColorBrush(p),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var chip = new Border
        {
            Child = sp,
            Background = new SolidColorBrush(ThemeManager.WithAlpha(p, (byte)(editing ? 0x2E : 0x1A))),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(p, (byte)(editing ? 0xDD : 0x77))),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius((_capSize + 8) / 2),
            Padding = new Thickness(10, 0, 11, 0),
            MinHeight = _capSize + 8,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = editing ? Cursors.IBeam : Cursors.Hand,
            ToolTip = "Click to edit · wheel ±50 ms (Shift ±10, Ctrl ±250) · drag to move",
        };

        if (editing)
        {
            var tb = new TextBox
            {
                Text = ms.ToString(),
                MinWidth = 44,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(p),
                CaretBrush = new SolidColorBrush(p),
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            var unit = new TextBlock { Text = "ms", FontSize = 10.5, Foreground = new SolidColorBrush(ThemeManager.WithAlpha(p, 0xAA)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
            sp.Children.Add(tb);
            sp.Children.Add(unit);
            _editBox = tb;

            void Finish(bool save)
            {
                if (_editBox != tb) return; // already finished or discarded by a re-render
                _editBox = null;
                _editing = -1;
                if (save && int.TryParse(tb.Text, out int v) && KeyboardInjector.ClampDelay(v) != ms) SetPause(i, v);
                else Render();
            }
            tb.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
            tb.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Finish(true); }
                else if (e.Key == Key.Escape) { e.Handled = true; Finish(false); }
            };
            tb.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                int cur = int.TryParse(tb.Text, out int v) ? v : ms;
                tb.Text = KeyboardInjector.ClampDelay(cur + WheelStep(e.Delta)).ToString();
                tb.CaretIndex = tb.Text.Length;
            };
            tb.LostKeyboardFocus += (_, _) => Finish(true);
            tb.Loaded += (_, _) => { tb.Focus(); Keyboard.Focus(tb); tb.SelectAll(); };
        }
        else
        {
            sp.Children.Add(new TextBlock
            {
                Text = FormatDelay(ms),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(p),
                VerticalAlignment = VerticalAlignment.Center,
            });
            chip.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_suppressClick) { _suppressClick = false; return; }
                _editing = i;
                Render();
            };
            chip.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                SetPause(i, ms + WheelStep(e.Delta));
            };
        }
        return chip;
    }

    // ── Footer ─────────────────────────────────────────────────────

    private Border MakeClearLink()
    {
        var icon = UiKit.Icon(MaterialIconKind.DeleteSweepOutline, 13);
        icon.Margin = new Thickness(0, 0, 4, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var text = new TextBlock { Text = "Clear all", FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(icon);
        sp.Children.Add(text);
        var link = new Border { Child = sp, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Remove every step", Margin = new Thickness(8, 0, 0, 0) };
        link.MouseEnter += (_, _) =>
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "DangerRedBrush");
            icon.SetResourceReference(MaterialIcon.ForegroundProperty, "DangerRedBrush");
        };
        link.MouseLeave += (_, _) =>
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
            icon.SetResourceReference(MaterialIcon.ForegroundProperty, "TextDimBrush");
        };
        link.MouseLeftButtonUp += (_, e) => { e.Handled = true; ClearAll(); };
        return link;
    }
}
