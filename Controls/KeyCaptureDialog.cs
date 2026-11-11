using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Material.Icons;
using Material.Icons.WPF;
using AmpUp.Services;

namespace AmpUp.Controls;

/// <summary>
/// Macro recorder modal (same chrome as <see cref="TextEditDialog"/>): a glowing stage that
/// shows the sequence as a step timeline (<see cref="MacroSequenceBuilder"/>: hover between steps to
/// insert keys or pauses), Record (appends a step, inserts at the chosen slot, or replaces the selected
/// one), Cancel / Save. The result uses the sequence format
/// <see cref="KeyboardInjector.ParseSequence"/> reads (e.g. "ctrl+1 wait:500 2"; one combo = "ctrl+shift+f13").
/// While recording, Esc stops recording; otherwise Esc cancels and Enter saves.
/// </summary>
public sealed class KeyCaptureDialog : Window
{
    private readonly Border _stage;
    private readonly Border _glow;
    private readonly DropShadowEffect _glowFx;
    private readonly ContentControl _capsHost;
    private readonly TextBlock _status;
    private readonly System.Windows.Shapes.Ellipse _statusDot;
    private readonly TextBlock _recordLabel;
    private readonly MaterialIcon _recordIcon;
    private readonly MacroSequenceBuilder _builder;
    private readonly TextBlock _rawText;

    private bool _recording;
    private bool _pulsing;
    private List<string> _peakMods = new();
    private readonly List<string> _chordKeys = new();   // non-modifier keys pressed this recording, in press order
    private readonly HashSet<string> _heldKeyNames = new(); // non-modifier keys currently down
    private string? _result;

    /// <summary>
    /// Show the recorder. Returns the sequence on Save ("" when the user cleared it), or null when cancelled.
    /// <paramref name="selected"/> pre-selects a step so Record replaces it; <paramref name="insertAt"/>
    /// (slot index) starts recording right away and inserts the combo there.
    /// </summary>
    public static string? Capture(DependencyObject anchor, string current, int selected = -1, int insertAt = -1)
    {
        var dlg = new KeyCaptureDialog(current, selected, insertAt) { Owner = Window.GetWindow(anchor) };
        dlg.ShowDialog();
        return dlg._result;
    }

    private KeyCaptureDialog(string current, int selected, int insertAt)
    {
        _builder = new MacroSequenceBuilder(30);
        _builder.Load(current);

        Title = "Record macro";
        Width = 520;
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

        // Header: icon tile + title / subtitle, help + close on the right (drag to move)
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16), Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = MacroSequenceBuilder.GlowTile(ThemeManager.Accent, MaterialIconKind.KeyboardOutline, 42);
        tile.Margin = new Thickness(0, 0, 12, 0);
        header.Children.Add(tile);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = "Record macro", FontSize = 14, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var st = new TextBlock { Text = "Record keys, then hover between steps to add more or a pause", FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        st.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        titles.Children.Add(t);
        titles.Children.Add(st);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var help = UiKit.HelpTip(
            "Each Record adds one step: hold Ctrl / Shift / Alt / Win, then press the key. Hold several keys together (like T + Y) for a chord. A modifier on its own works too.\n" +
            "Hover between two steps (or at either end) and click + to record keys or drop in a pause (10–60000 ms) right there, so you can build WoW-style macros like Ctrl+1, wait 500ms, 2.\n" +
            "Click a pause to type its length, or use the mouse wheel (Shift = fine, Ctrl = coarse). Drag steps to reorder; the × removes one.\n" +
            "Click a key step to select it: Record replaces it, Del removes it.\nEsc stops recording, Enter saves.",
            "Building a macro");
        help.Margin = new Thickness(0, 0, 4, 0);
        help.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(help, 2);
        header.Children.Add(help);
        var close = UiKit.IconButton(MaterialIconKind.Close, "Cancel (Esc)", _ => Close(), size: 26);
        close.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Grid or TextBlock or StackPanel)) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        stack.Children.Add(header);

        // Stage: glow layer behind (keeps the keycap text crisp) + keycaps + status line
        var a = ThemeManager.Accent;
        _glowFx = new DropShadowEffect { Color = a, BlurRadius = 26, ShadowDepth = 0, Opacity = 0 };
        _glow = new Border { CornerRadius = new CornerRadius(14), Effect = _glowFx };
        _glow.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        _stage = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5),
            MinHeight = 150,
            Padding = new Thickness(16, 18, 16, 14),
            Cursor = Cursors.Hand,
            ToolTip = "Click to record",
        };
        _stage.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        _stage.MouseLeftButtonUp += (_, e) => { e.Handled = true; if (!_recording) StartRecording(); };
        var stageBody = new Grid();
        stageBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stageBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _capsHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        stageBody.Children.Add(_capsHost);
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        _statusDot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        _status = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        statusRow.Children.Add(_statusDot);
        statusRow.Children.Add(_status);
        Grid.SetRow(statusRow, 1);
        stageBody.Children.Add(statusRow);
        _stage.Child = stageBody;
        var stageGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        stageGrid.Children.Add(_glow);
        stageGrid.Children.Add(_stage);
        stack.Children.Add(stageGrid);

        // Raw sequence string (what "Edit as text" shows)
        _rawText = new TextBlock
        {
            FontSize = 10.5,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _rawText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        stack.Children.Add(_rawText);

        // Buttons: [Record] ........ [Cancel] [Save]
        var buttons = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        _recordIcon = new MaterialIcon { Kind = MaterialIconKind.RecordCircleOutline, Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        _recordLabel = new TextBlock { Text = "Record", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var recordBtn = MakeButton(_recordLabel, _recordIcon, primary: false, ToggleRecording);
        recordBtn.Margin = new Thickness(0);
        left.Children.Add(recordBtn);
        buttons.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(MakeButton(new TextBlock { Text = "Cancel", FontSize = 12, FontWeight = FontWeights.SemiBold }, null, primary: false, Close));
        right.Children.Add(MakeButton(new TextBlock { Text = "Save", FontSize = 12, FontWeight = FontWeights.SemiBold }, null, primary: true, Save));
        Grid.SetColumn(right, 2);
        buttons.Children.Add(right);
        stack.Children.Add(buttons);

        Content = root;
        Focusable = true;
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Closed += (_, _) => StopPulse();
        _builder.Changed += () => Render();
        _builder.SelectionChanged += () => Render();
        _builder.RecordRequested += _ => StartRecording();
        _builder.Select(selected);
        _builder.BeginInsert(insertAt);
        Loaded += (_, _) =>
        {
            Focus();
            // Nothing set yet or inserting at a slot: start listening right away. Otherwise show the sequence first.
            if (_builder.Count == 0 || _builder.PendingInsert >= 0) StartRecording();
            else Render();
        };
    }

    // ── States ─────────────────────────────────────────────────────

    private void StartRecording()
    {
        _peakMods = new List<string>();
        _chordKeys.Clear();
        _heldKeyNames.Clear();
        _recording = true;
        Focus();
        Render();
    }

    private void StopRecording()
    {
        _recording = false;
        _builder.CancelInsert();
        Render();
    }

    /// <summary>Recording finished: append the combo as a step (or replace the selected step).</summary>
    private void CommitCombo(string combo)
    {
        _recording = false;
        _builder.PutCombo(combo);
        Render();
    }

    private void ToggleRecording()
    {
        if (_recording) StopRecording();
        else StartRecording();
    }

    private void Save()
    {
        // A chord still being recorded is dropped; committed steps are kept.
        _result = _builder.Sequence;
        Close();
    }

    private void Render(List<string>? liveMods = null)
    {
        var a = ThemeManager.Accent;
        _rawText.Text = _builder.Sequence;
        if (_recording)
        {
            _capsHost.Content = liveMods != null && liveMods.Count > 0
                ? BuildKeycaps(liveMods, 52, pending: true)
                : MakeHintText("Press your keys…", 18, "TextPrimaryBrush");
            _status.Text = _builder.PendingInsert >= 0
                ? $"RECORDING INTO STEP {_builder.PendingInsert + 1} · Esc to stop"
                : _builder.Selected >= 0
                ? $"RECORDING STEP {_builder.Selected + 1} · Esc to stop"
                : "RECORDING · Esc to stop";
            _status.Foreground = new SolidColorBrush(a);
            _statusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
            _stage.BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0xCC));
            _recordLabel.Text = "Stop";
            _recordIcon.Kind = MaterialIconKind.StopCircleOutline;
            StartPulse();
        }
        else
        {
            StopPulse();
            bool empty = _builder.Count == 0;
            int sel = _builder.Selected;
            _capsHost.Content = _builder.Chips;
            _status.Text = empty ? "Click Record to capture keys"
                : sel >= 0 ? $"STEP {sel + 1} SELECTED · Record replaces it"
                : "Hover between steps to insert · Enter to save";
            _status.SetResourceReference(TextBlock.ForegroundProperty, empty ? "TextDimBrush" : "TextSecBrush");
            _statusDot.Fill = empty
                ? (Brush)(Application.Current.TryFindResource("TextDimBrush") ?? Brushes.Gray)
                : new SolidColorBrush(a);
            _stage.BorderBrush = empty
                ? (Brush)(Application.Current.TryFindResource("InputBorderBrush") ?? Brushes.Gray)
                : new SolidColorBrush(ThemeManager.WithAlpha(a, 0x55));
            _recordLabel.Text = sel >= 0 ? "Replace step" : "Record";
            _recordIcon.Kind = MaterialIconKind.RecordCircleOutline;
        }
    }

    private void StartPulse()
    {
        if (_pulsing) return;
        _pulsing = true;
        var glow = new DoubleAnimation(0.25, 0.75, TimeSpan.FromMilliseconds(850))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _glowFx.BeginAnimation(DropShadowEffect.OpacityProperty, glow);
        var dot = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(650))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _statusDot.BeginAnimation(OpacityProperty, dot);
    }

    private void StopPulse()
    {
        _pulsing = false;
        _glowFx.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        _glowFx.Opacity = 0;
        _statusDot.BeginAnimation(OpacityProperty, null);
        _statusDot.Opacity = 1;
    }

    // ── Keyboard ───────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;

        if (!_recording)
        {
            if (e.OriginalSource is TextBox) return; // inline pause editor handles its own Enter / Esc
            if (key == Key.Enter) { e.Handled = true; Save(); }
            else if (key == Key.Delete && e.OriginalSource is not TextBox && _builder.Selected >= 0) { e.Handled = true; _builder.DeleteSelected(); }
            else if (key == Key.Escape) { e.Handled = true; Close(); }
            else if (key == Key.System || e.Key == Key.System) e.Handled = true; // keep Alt from opening the system menu
            return;
        }

        e.Handled = true;
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            StopRecording();
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || KeyboardInjector.IsModifierVk(vk))
        {
            // Show the modifiers being held while waiting for the main key.
            var held = HeldModifiers();
            if (held.Count >= _peakMods.Count) _peakMods = held;
            Render(held);
            return;
        }

        var name = KeyboardInjector.NameForVk(vk);
        if (name == null)
        {
            _status.Text = $"{key} isn't supported, try another key";
            return;
        }

        // Collect every key held together (T + Y); the combo is committed once all are released.
        var heldMods = HeldModifiers();
        if (heldMods.Count >= _peakMods.Count) _peakMods = heldMods;
        if (!_chordKeys.Contains(name)) _chordKeys.Add(name);
        _heldKeyNames.Add(name);
        Render(LiveParts(heldMods));
    }

    private List<string> LiveParts(List<string> mods)
    {
        var parts = new List<string>(mods);
        parts.AddRange(_chordKeys);
        return parts;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        var upKey = e.Key == Key.System ? e.SystemKey : e.Key;
        if (upKey == Key.ImeProcessed) upKey = e.ImeProcessedKey;
        int upVk = KeyInterop.VirtualKeyFromKey(upKey);
        if (upVk != 0 && !KeyboardInjector.IsModifierVk(upVk))
        {
            var upName = KeyboardInjector.NameForVk(upVk);
            if (upName != null) _heldKeyNames.Remove(upName);
        }

        var held = HeldModifiers();
        if (_heldKeyNames.Count > 0 || held.Count > 0)
        {
            Render(LiveParts(held));
            return;
        }

        // Everything released: commit the whole chord, or just the modifiers if no main key was pressed.
        if (_chordKeys.Count > 0)
        {
            var parts = new List<string>(_peakMods);
            parts.AddRange(_chordKeys);
            CommitCombo(string.Join("+", parts));
        }
        else if (_peakMods.Count > 0)
        {
            CommitCombo(string.Join("+", _peakMods));
        }
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

    // ── Keycaps ────────────────────────────────────────────────────

    /// <summary>Split a stored combo into its key names ("ctrl++" → ctrl, plus).</summary>
    public static List<string> SplitCombo(string combo)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(combo)) return list;
        var s = combo.Trim();
        bool trailingPlus = s.EndsWith("++");
        if (trailingPlus) s = s[..^2];
        foreach (var part in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            list.Add(part);
        if (trailingPlus) list.Add("plus");
        return list;
    }

    private static bool IsModifierName(string part) => part.ToLowerInvariant() is
        "ctrl" or "control" or "lctrl" or "rctrl" or "shift" or "lshift" or "rshift" or
        "alt" or "lalt" or "ralt" or "altgr" or "win" or "windows" or "super" or "lwin" or "rwin";

    /// <summary>Friendly keycap label for a key name ("ctrl" → "Ctrl", "f13" → "F13", "pageup" → "PgUp").</summary>
    public static string DisplayName(string part)
    {
        var p = part.Trim().ToLowerInvariant();
        return p switch
        {
            "ctrl" or "control" or "lctrl" => "Ctrl",
            "rctrl" => "R Ctrl",
            "shift" or "lshift" => "Shift",
            "rshift" => "R Shift",
            "alt" or "lalt" => "Alt",
            "ralt" or "altgr" => "AltGr",
            "win" or "windows" or "super" or "lwin" => "Win",
            "rwin" => "R Win",
            "enter" or "return" => "Enter",
            "escape" or "esc" => "Esc",
            "space" or "spacebar" => "Space",
            "backspace" => "⌫",
            "delete" or "del" => "Del",
            "insert" or "ins" => "Ins",
            "pageup" or "pgup" => "PgUp",
            "pagedown" or "pgdn" => "PgDn",
            "printscreen" or "prtsc" => "PrtSc",
            "capslock" => "Caps",
            "up" => "↑",
            "down" => "↓",
            "left" => "←",
            "right" => "→",
            "semicolon" => ";",
            "equals" => "=",
            "plus" => "+",
            "comma" => ",",
            "minus" => "-",
            "period" => ".",
            "slash" => "/",
            "backtick" => "`",
            "lbracket" => "[",
            "rbracket" => "]",
            "backslash" => "\\",
            "quote" => "'",
            "nummultiply" => "Num *",
            "numadd" => "Num +",
            "numsubtract" => "Num -",
            "numdecimal" => "Num .",
            "numdivide" => "Num /",
            "volumeup" => "Vol +",
            "volumedown" => "Vol -",
            "volumemute" => "Mute",
            "medianext" => "Next",
            "mediaprev" => "Prev",
            "mediastop" => "Stop",
            "mediaplay" => "Play",
            _ when p.Length <= 3 => p.ToUpperInvariant(),
            _ when p.StartsWith("num") && p.Length == 4 => "Num " + p[3..],
            _ => char.ToUpperInvariant(p[0]) + p[1..],
        };
    }

    /// <summary>
    /// Row of 3D keycaps joined by "+". Modifiers get an accent tint; <paramref name="pending"/>
    /// adds a dashed "…" cap for the key still to come.
    /// </summary>
    public static FrameworkElement BuildKeycaps(IReadOnlyList<string> parts, double size, bool pending = false)
    {
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) row.Children.Add(MakePlus(size));
            row.Children.Add(MakeKeycap(DisplayName(parts[i]), IsModifierName(parts[i]), size));
        }
        if (pending)
        {
            if (parts.Count > 0) row.Children.Add(MakePlus(size));
            var dash = new Border
            {
                MinWidth = size, Height = size,
                CornerRadius = new CornerRadius(size * 0.18),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0x66)),
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 3),
                Child = new TextBlock
                {
                    Text = "…", FontSize = size * 0.34, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(ThemeManager.WithAlpha(ThemeManager.Accent, 0xAA)),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            row.Children.Add(dash);
        }
        return row;
    }

    private static FrameworkElement MakeKeycap(string label, bool modifier, double size)
    {
        var a = ThemeManager.Accent;
        var top = modifier ? Color.FromRgb(0x2A, 0x2E, 0x2C) : Color.FromRgb(0x30, 0x30, 0x30);
        var bottom = modifier ? Color.FromRgb(0x1A, 0x1D, 0x1B) : Color.FromRgb(0x1C, 0x1C, 0x1C);
        var face = new LinearGradientBrush(top, bottom, new System.Windows.Point(0, 0), new System.Windows.Point(0, 1));
        face.Freeze();
        var text = new TextBlock
        {
            Text = label,
            FontSize = label.Length > 4 ? size * 0.27 : size * 0.32,
            FontWeight = FontWeights.SemiBold,
            Foreground = modifier ? new SolidColorBrush(a) : new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            MinWidth = size,
            Height = size,
            Padding = new Thickness(size * 0.24, 0, size * 0.24, 0),
            CornerRadius = new CornerRadius(size * 0.18),
            // Thicker bottom edge reads as the side of a physical key
            BorderThickness = new Thickness(1, 1, 1, 4),
            BorderBrush = modifier
                ? new SolidColorBrush(ThemeManager.WithAlpha(a, 0x88))
                : new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C)),
            Background = face,
            Effect = new DropShadowEffect
            {
                Color = modifier ? a : Colors.Black,
                BlurRadius = modifier ? 14 : 8,
                ShadowDepth = modifier ? 0 : 2,
                Opacity = modifier ? 0.35 : 0.5,
            },
            Child = text,
        };
    }

    private static TextBlock MakePlus(double size)
    {
        var plus = new TextBlock
        {
            Text = "+",
            FontSize = size * 0.34,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(size * 0.16, 0, size * 0.16, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        return plus;
    }

    internal static TextBlock MakeHintText(string text, double fontSize, string brushKey)
    {
        var tb = new TextBlock { Text = text, FontSize = fontSize, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        tb.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        return tb;
    }

    private static Border MakeButton(TextBlock label, MaterialIcon? icon, bool primary, Action onClick)
    {
        var a = ThemeManager.Accent;
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon != null) content.Children.Add(icon);
        content.Children.Add(label);
        var b = new Border
        {
            MinWidth = 84,
            Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = content,
        };
        void Paint(bool hover)
        {
            if (primary)
            {
                // Hover: 15% toward white
                var bg = hover
                    ? Color.FromRgb((byte)(a.R + (255 - a.R) * 0.15), (byte)(a.G + (255 - a.G) * 0.15), (byte)(a.B + (255 - a.B) * 0.15))
                    : a;
                b.Background = new SolidColorBrush(bg);
                b.BorderBrush = new SolidColorBrush(bg);
                label.Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
                if (icon != null) icon.Foreground = label.Foreground;
            }
            else
            {
                b.Background = hover ? (Brush)Application.Current.FindResource("InputBgBrush") : Brushes.Transparent;
                b.BorderBrush = (Brush)Application.Current.FindResource("CardBorderBrush");
                label.Foreground = (Brush)Application.Current.FindResource("TextPrimaryBrush");
                if (icon != null) icon.Foreground = label.Foreground;
            }
        }
        b.MouseEnter += (_, _) => Paint(true);
        b.MouseLeave += (_, _) => Paint(false);
        b.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
        Paint(false);
        return b;
    }

    // ── Inline recorder for the macro editors ──────────────────────

    /// <summary>
    /// Front-and-centre sequence builder for the "Send keystroke" / "Hold key" editors: the step
    /// timeline (<see cref="MacroSequenceBuilder"/>, hover between steps to insert keys or pauses), a big Record button that opens the
    /// recorder dialog, and <paramref name="box"/> (the stored sequence string) demoted to an
    /// "Edit as text" fallback underneath. <paramref name="box"/> must not have a parent yet.
    /// Placeholder text (box.Tag shown in TextDimBrush) counts as empty.
    /// </summary>
    public static FrameworkElement MakeRecorder(TextBox box)
    {
        var a = ThemeManager.Accent;
        var outer = new StackPanel();

        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ThemeManager.WithAlpha(a, 0x33)),
            Padding = new Thickness(12, 14, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
        };
        card.SetResourceReference(Border.BackgroundProperty, "InputBgBrush");
        var body = new StackPanel();
        card.Child = body;

        // Header: glowing macro tile + label
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var headTile = MacroSequenceBuilder.GlowTile(a, MaterialIconKind.KeyboardOutline, 28);
        headTile.Margin = new Thickness(0, 0, 9, 0);
        head.Children.Add(headTile);
        var headText = new TextBlock { Text = "MACRO STEPS", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        headText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecBrush");
        head.Children.Add(headText);
        body.Children.Add(head);

        // Step timeline (click to select, hover slots to insert, footer with Clear all)
        var builder = new MacroSequenceBuilder(24) { ShowEmptyRecordButton = false };
        builder.Chips.MinHeight = 34;
        builder.Chips.Margin = new Thickness(0, 0, 0, 10);
        body.Children.Add(builder.Chips);

        // Big accent Record button
        var recIcon = new MaterialIcon { Kind = MaterialIconKind.RecordCircleOutline, Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var recLabel = new TextBlock { Text = "Record", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var recContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        recContent.Children.Add(recIcon);
        recContent.Children.Add(recLabel);
        var rec = new Border
        {
            Height = 42,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = recContent,
            Effect = new DropShadowEffect { Color = a, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.3 },
        };
        void PaintRec(bool hover)
        {
            var bg = hover
                ? Color.FromRgb((byte)(a.R + (255 - a.R) * 0.15), (byte)(a.G + (255 - a.G) * 0.15), (byte)(a.B + (255 - a.B) * 0.15))
                : a;
            rec.Background = new SolidColorBrush(bg);
            rec.BorderBrush = new SolidColorBrush(bg);
            var fg = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
            recLabel.Foreground = fg;
            recIcon.Foreground = fg;
        }
        rec.MouseEnter += (_, _) => PaintRec(true);
        rec.MouseLeave += (_, _) => PaintRec(false);
        PaintRec(false);
        body.Children.Add(rec);

        // Footer: "Edit as text" link
        var editIcon = UiKit.Icon(MaterialIconKind.FormTextbox, 14);
        editIcon.Margin = new Thickness(0, 0, 5, 0);
        var editText = new TextBlock { Text = "Edit as text", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        editText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        var editContent = new StackPanel { Orientation = Orientation.Horizontal };
        editContent.Children.Add(editIcon);
        editContent.Children.Add(editText);
        var editLink = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = editContent,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };
        editLink.MouseEnter += (_, _) => editText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        editLink.MouseLeave += (_, _) => editText.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");
        body.Children.Add(editLink);

        outer.Children.Add(card);

        // Text fallback (hidden until "Edit as text")
        box.Visibility = Visibility.Collapsed;
        if (box.ToolTip == null || box.ToolTip is string)
            box.ToolTip = "Steps separated by spaces, pauses as wait:ms, e.g. ctrl+1 wait:500 2";
        outer.Children.Add(box);
        editLink.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool show = box.Visibility != Visibility.Visible;
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            editText.Text = show ? "Hide text" : "Edit as text";
            if (show) box.Focus();
        };

        void RefreshLabel()
        {
            recLabel.Text = builder.Selected >= 0 ? "Replace step"
                : "Record";
        }

        void Refresh()
        {
            // Skip when the box already holds what the builder produced, so the selection survives.
            var value = CurrentValue(box);
            if (value != builder.Sequence) builder.Load(value);
            RefreshLabel();
        }

        builder.Changed += () => SetBox(box, builder.Sequence);
        builder.SelectionChanged += RefreshLabel;

        void Record(int insertAt = -1)
        {
            var result = Capture(box, CurrentValue(box), insertAt >= 0 ? -1 : builder.Selected, insertAt);
            if (result != null) SetBox(box, result);
            builder.CancelInsert();
        }
        builder.RecordRequested += idx => Record(idx);
        rec.MouseLeftButtonUp += (_, e) => { e.Handled = true; Record(); };

        // Callers set Text and Foreground separately (placeholder handling), so refresh after both land.
        box.TextChanged += (_, _) => box.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Refresh));
        box.Loaded += (_, _) => Refresh();
        Refresh();
        return outer;
    }

    private static string CurrentValue(TextBox box)
    {
        var dim = Application.Current.TryFindResource("TextDimBrush");
        if (box.Tag is string ph && box.Text == ph && Equals(box.Foreground, dim)) return "";
        return box.Text.Trim();
    }

    private static void SetBox(TextBox box, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            // Same as ButtonsView.SetTextBoxValue(box, ""): show the dim placeholder.
            box.Foreground = Res("TextDimBrush");
            box.Text = box.Tag as string ?? "";
        }
        else
        {
            box.Foreground = Res("TextPrimaryBrush");
            box.Text = value;
        }
    }

    private static Brush Res(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
}
