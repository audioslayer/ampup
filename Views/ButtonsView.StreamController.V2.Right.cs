using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

/// <summary>
/// V2 Stream Controller — right-pane Preview + Action picker regions.
///
/// Owns two of the three vertical sections in the right-hand editor:
///
///   1. FillV2PreviewPanel — header ("Key 3" / "Button 1" / "Encoder Press 2"),
///      live LCD preview (or tile-based preview for hardware), and the common
///      display fields (Title / Icon / Text Position / Text Color) which are
///      only shown for LCD keys.
///
///   2. FillV2ActionPanel — hosts a <see cref="QuickActionPicker"/> wired to
///      the action catalog, favorites, and recents.
///
/// The action-specific sub-panels (path / macro / toggle / multi / folder /
/// text / url) live in <c>_v2ActionFieldsPanel</c> and are populated by a
/// separate agent — this file does not touch them.
///
/// <para>
/// The V2 designer REUSES the existing <c>_sc*</c> WPF elements so a single
/// source of truth backs both designers until the V1 designer is removed.
/// When this file re-hosts an existing element under a new parent it first
/// detaches the element from its old parent.
/// </para>
/// </summary>
public partial class ButtonsView
{
    // ── Common-fields wrapper (LCD-only) ────────────────────────────────
    // Grouped so we can collapse them together for hardware selections.
    private StackPanel? _v2CommonFieldsPanel;
    private Border? _v2PreviewCard;
    private StackPanel? _v2PreviewRow;

    // Inline-editable header that mirrors the Mixer tab's channel label:
    // always-editable TextBox with transparent chrome until focus reveals an
    // accent underline. On blur the rename is persisted back to the selected
    // key/button config.
    private TextBox? _v2HeaderBox;

    // ICON COLOR label tracker — shown only when the selected key uses a
    // preset vector icon (bitmap images cannot be tinted).
    private TextBlock? _v2IconColorLabel;
    private TextBlock? _v2GlowColorLabel;
    private ListPicker? _v2FontPicker;
    private StyledSlider? _v2BrightnessSlider;
    private TextBlock? _v2BrightnessLabel;

    // Cache key for the action picker item set. Repopulate only when any
    // integration-enabled flag flips — otherwise every RefreshV2RightPanel
    // (fired per config save, debounced 300 ms) would churn 40+ AddItem
    // calls plus 3 full RebuildAll passes, which is the biggest
    // contributor to the tab feeling sluggish.
    private string? _v2ActionItemCacheKey;

    partial void FillV2PreviewPanel()
    {
        if (_v2PreviewPanel == null) return;

        // ── 1. Header row ("Key 3" / "Button 1" / ...) ───────────────────
        // Re-host the shared title TextBlock from the V1 designer. If V1
        // already mounted it, detach first.
        // Matches the Mixer tab's channel-label pattern: invisible TextBox
        // that turns into an input with an accent underline on focus.
        // Clicking the header text edits it directly — no separate pencil
        // affordance needed, consistent with the rest of the app.
        _v2HeaderBox = new TextBox
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextPrimaryBrush"),
            CaretBrush = FindBrush("AccentBrush"),
            SelectionBrush = FindBrush("AccentDimBrush"),
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 0, 12),
            MaxLength = 30,
            Cursor = System.Windows.Input.Cursors.IBeam,
            ToolTip = "Click to rename",
        };
        _v2HeaderBox.GotFocus += (_, _) =>
        {
            _v2HeaderBox.Background = FindBrush("InputBgBrush");
            _v2HeaderBox.BorderThickness = new Thickness(0, 0, 0, 1);
            _v2HeaderBox.BorderBrush = FindBrush("AccentBrush");
            _v2HeaderBox.SelectAll();
        };
        _v2HeaderBox.LostFocus += (_, _) =>
        {
            _v2HeaderBox.Background = System.Windows.Media.Brushes.Transparent;
            _v2HeaderBox.BorderThickness = new Thickness(0);
            if (!_loading) CommitV2HeaderRename();
        };
        _v2HeaderBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                System.Windows.Input.Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        _v2PreviewPanel.Children.Add(_v2HeaderBox);

        // ── 2. Preview + Choose Icon row ─────────────────────────────────
        _v2PreviewCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            Width = 180,
            Height = 180,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (_scEditorPreview == null)
        {
            _scEditorPreview = new Image
            {
                Stretch = Stretch.Uniform,
            };
        }
        DetachFromParent(_scEditorPreview);
        _scEditorPreview.Stretch = Stretch.Uniform;
        _v2PreviewCard.Child = _scEditorPreview;

        _v2PreviewRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
        };
        _v2PreviewRow.Children.Add(_v2PreviewCard);

        var chooseIconInlineBtn = MakeEditorButton("Choose Icon", (_, _) => ChooseStreamControllerIcon());
        chooseIconInlineBtn.Margin = new Thickness(14, 0, 0, 0);
        chooseIconInlineBtn.VerticalAlignment = VerticalAlignment.Center;
        _v2PreviewRow.Children.Add(chooseIconInlineBtn);

        _v2PreviewPanel.Children.Add(_v2PreviewRow);

        // ── 3. Common fields (LCD-only) ──────────────────────────────────
        // One DESIGN card containing display type, title, text layout +
        // color, clock format, and dynamic state — "icon" is now just the
        // Choose Icon button next to the preview above.
        _v2CommonFieldsPanel = new StackPanel();

        // _scIconBox still exists as a field because legacy load/save code
        // writes its .Text property. It is NOT parented to any visible
        // container — the preview image at the top of the panel already
        // shows the icon, so the filename readout would just be noise.
        if (_scIconBox == null)
        {
            _scIconBox = MakeEditorTextBox("No icon selected");
            _scIconBox.IsReadOnly = true;
        }
        DetachFromParent(_scIconBox);

        var designContent = new StackPanel();

        if (_scDisplayTypePicker != null)
        {
            designContent.Children.Add(MakeEditorLabel("DISPLAY TYPE"));
            DetachFromParent(_scDisplayTypePicker);
            _scDisplayTypePicker.Margin = new Thickness(0, 0, 0, 12);
            _scDisplayTypePicker.Visibility = Visibility.Visible;
            designContent.Children.Add(_scDisplayTypePicker);
        }

        designContent.Children.Add(MakeEditorLabel("TITLE"));
        if (_scTitleBox == null)
        {
            _scTitleBox = MakeEditorTextBox("Display title");
            // Allow up to 3 lines of title text so users can break labels
            // over multiple rows. Enter inserts a newline; the renderer
            // splits on newline and stacks the lines vertically.
            _scTitleBox.AcceptsReturn = true;
            _scTitleBox.MaxLines = 3;
            _scTitleBox.TextWrapping = TextWrapping.Wrap;
            _scTitleBox.VerticalContentAlignment = VerticalAlignment.Top;
            _scTitleBox.MinHeight = 36;
            _scTitleBox.MaxLength = 120;
            _scTitleBox.ToolTip = "Press Enter for a new line (up to 3 lines)";
        }
        DetachFromParent(_scTitleBox);
        _scTitleBox.Visibility = Visibility.Visible;
        designContent.Children.Add(_scTitleBox);

        // Font picker — writes to key.FontFamily so the device-JPEG renderer
        // can switch typeface per key. Populated with a curated list of
        // fonts that ship on Windows + a few stylistic extras.
        var fontLabel = MakeEditorLabel("FONT");
        fontLabel.Margin = new Thickness(0, 10, 0, 4);
        designContent.Children.Add(fontLabel);
        if (_v2FontPicker == null)
        {
            _v2FontPicker = new ListPicker();
            foreach (var name in StreamControllerFontPresets)
                _v2FontPicker.AddItem(name, name);
            _v2FontPicker.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                var display = GetSelectedDisplayKeyConfig();
                if (display == null || _v2FontPicker == null) return;
                var fontName = _v2FontPicker.SelectedTag as string ?? "Segoe UI";
                display.FontFamily = fontName;
                UpdateEditorPreviewOnly();
                QueueSave();
            };
        }
        DetachFromParent(_v2FontPicker);
        _v2FontPicker.Margin = new Thickness(0, 0, 0, 4);
        designContent.Children.Add(_v2FontPicker);

        // Per-key BRIGHTNESS — final multiply pass on the composed bitmap.
        // 100 = unchanged (default), 0 = black.
        if (_v2BrightnessLabel == null)
        {
            _v2BrightnessLabel = new TextBlock
            {
                Text = "Brightness: 100%",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextDimBrush"),
                Margin = new Thickness(0, 12, 0, 4),
            };
        }
        DetachFromParent(_v2BrightnessLabel);
        designContent.Children.Add(_v2BrightnessLabel);

        if (_v2BrightnessSlider == null)
        {
            _v2BrightnessSlider = new StyledSlider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 100,
                Height = 28,
                ShowLabel = false,
            };
            _v2BrightnessSlider.ValueChanged += (_, _) =>
            {
                if (_v2BrightnessLabel != null)
                    _v2BrightnessLabel.Text = $"Brightness: {(int)_v2BrightnessSlider.Value}%";
                if (_loading) return;
                var display = GetSelectedDisplayKeyConfig();
                if (display == null) return;
                display.Brightness = (int)Math.Round(_v2BrightnessSlider.Value);
                UpdateEditorPreviewOnly();
                QueueSave();
            };
        }
        DetachFromParent(_v2BrightnessSlider);
        _v2BrightnessSlider.Margin = new Thickness(0, 0, 0, 10);
        designContent.Children.Add(_v2BrightnessSlider);

        designContent.Children.Add(MakeEditorLabel("TEXT POSITION"));
        if (_scTextPositionPicker == null)
        {
            _scTextPositionPicker = new SegmentedControl { HorizontalAlignment = HorizontalAlignment.Left };
            _scTextPositionPicker.AddSegment("Top", DisplayTextPosition.Top);
            _scTextPositionPicker.AddSegment("Middle", DisplayTextPosition.Middle);
            _scTextPositionPicker.AddSegment("Bottom", DisplayTextPosition.Bottom);
            _scTextPositionPicker.AddSegment("Hidden", DisplayTextPosition.Hidden);
            _scTextPositionPicker.SelectionChanged += (_, _) =>
            {
                if (!_loading) { UpdateEditorPreviewOnly(); QueueSave(); }
            };
        }
        DetachFromParent(_scTextPositionPicker);
        _scTextPositionPicker.Margin = new Thickness(0, 0, 0, 10);
        _scTextPositionPicker.Visibility = Visibility.Visible;
        designContent.Children.Add(_scTextPositionPicker);

        if (_scTextSizeSlider != null)
        {
            if (_scTextSizeLabel == null)
            {
                _scTextSizeLabel = new TextBlock
                {
                    Text = "Font Size: 14",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FindBrush("TextDimBrush"),
                    Margin = new Thickness(0, 0, 0, 4),
                };
            }
            DetachFromParent(_scTextSizeLabel);
            _scTextSizeLabel.Visibility = Visibility.Visible;
            designContent.Children.Add(_scTextSizeLabel);

            DetachFromParent(_scTextSizeSlider);
            _scTextSizeSlider.Margin = new Thickness(0, 0, 0, 10);
            _scTextSizeSlider.Visibility = Visibility.Visible;
            designContent.Children.Add(_scTextSizeSlider);
        }

        designContent.Children.Add(MakeEditorLabel("TEXT COLOR"));
        if (_scTextColorSwatchPanel == null) _scTextColorSwatchPanel = new WrapPanel();
        DetachFromParent(_scTextColorSwatchPanel);
        _scTextColorSwatchPanel.Margin = new Thickness(0, 0, 0, 0);
        _scTextColorSwatchPanel.Visibility = Visibility.Visible;
        designContent.Children.Add(_scTextColorSwatchPanel);

        // ICON COLOR swatches — only meaningful when a preset vector icon
        // is in use. Hidden at build time; RefreshV2RightPanel shows it
        // when the selected key has a PresetIconKind (not a user bitmap).
        var iconColorLabel = MakeEditorLabel("ICON COLOR");
        iconColorLabel.Margin = new Thickness(0, 10, 0, 4);
        designContent.Children.Add(iconColorLabel);
        if (_scIconColorSwatchPanel == null) _scIconColorSwatchPanel = new WrapPanel();
        DetachFromParent(_scIconColorSwatchPanel);
        _scIconColorSwatchPanel.Margin = new Thickness(0, 0, 0, 0);
        designContent.Children.Add(_scIconColorSwatchPanel);
        _v2IconColorLabel = iconColorLabel;

        // GLOW COLOR swatches — drives the radial accent glow behind the
        // icon. Same preset visibility rule as ICON COLOR (no meaning for
        // bitmap images since the glow only renders for preset icons).
        var glowColorLabel = MakeEditorLabel("GLOW COLOR");
        glowColorLabel.Margin = new Thickness(0, 10, 0, 4);
        designContent.Children.Add(glowColorLabel);
        if (_scGlowColorSwatchPanel == null) _scGlowColorSwatchPanel = new WrapPanel();
        DetachFromParent(_scGlowColorSwatchPanel);
        _scGlowColorSwatchPanel.Margin = new Thickness(0, 0, 0, 0);
        designContent.Children.Add(_scGlowColorSwatchPanel);
        _v2GlowColorLabel = glowColorLabel;

        if (_scClockPanel != null)
        {
            DetachFromParent(_scClockPanel);
            _scClockPanel.Margin = new Thickness(0, 10, 0, 0);
            designContent.Children.Add(_scClockPanel);
        }
        if (_scDynamicPanel != null)
        {
            DetachFromParent(_scDynamicPanel);
            _scDynamicPanel.Margin = new Thickness(0, 10, 0, 0);
            designContent.Children.Add(_scDynamicPanel);
        }

        // The DESIGN tab itself is the section header now — drop the
        // redundant in-card "DESIGN" accent-bar header.
        var designCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = designContent,
        };
        designCard.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        designCard.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        _v2CommonFieldsPanel.Children.Add(designCard);

        // _v2CommonFieldsPanel is NOT added to _v2PreviewPanel — the
        // V2 layout (BuildStreamControllerDesignerV2) places it inside
        // the DESIGN tab content so the right pane can swap it for the
        // ACTION tab via a tab bar.
    }

    /// <summary>
    /// Section card for the Preview panel with the shared V2 chrome (accent
    /// bar + uppercase header, CardBgBrush, rounded border, 14px inner padding).
    /// </summary>
    private Border MakeV2CommonFieldCard(string label, UIElement content)
    {
        var stack = new StackPanel();

        var barLabelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(ThemeManager.Accent),
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        barLabelRow.Children.Add(bar);
        barLabelRow.Children.Add(text);
        stack.Children.Add(barLabelRow);
        stack.Children.Add(content);

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack,
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBgBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return card;
    }

    partial void FillV2ActionPanel()
    {
        if (_v2ActionPanel == null) return;

        // The ACTION tab itself is the section header now — drop the
        // redundant in-panel "ACTION" accent-bar header.
        _v2ActionPicker = new QuickActionPicker
        {
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        PopulateV2ActionPickerItems();

        // ── Event wiring ─────────────────────────────────────────────────
        // Favorites + recents are intentionally removed from the picker UI
        // so there is no per-action star toggle or recents row to wire up.

        // Selection changed — persist to the selected button + refresh
        // conditional sub-panels so Multi-Action / Toggle / Folder / etc.
        // show/hide for the new action.
        _v2ActionPicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null || _v2ActionPicker == null) return;

            var value = _v2ActionPicker.SelectedValue;
            var button = GetOrCreateSelectedV2Button();
            if (button != null)
            {
                button.Action = value;
            }

            // Keep the legacy _scActionPicker in sync — CollectAndSave →
            // UpdateStreamControllerSelection reads button.Action from it
            // and would otherwise revert our write on the debounced save.
            if (_scActionPicker != null)
            {
                bool prev = _loading;
                _loading = true;
                try { SelectCombo(_scActionPicker, value); }
                finally { _loading = prev; }
            }

            QueueSave();
            RefreshV2ActionFieldsVisibility();
            RefreshV2LeftPanel();
        };

        _v2ActionPanel.Children.Add(_v2ActionPicker);
    }

    /// <summary>
    /// Re-populates the QuickActionPicker item list, respecting the same
    /// integration-enabled rules as <see cref="PopulateActionPicker"/>.
    /// Safe to call multiple times; clears first.
    /// </summary>
    private void PopulateV2ActionPickerItems()
    {
        if (_v2ActionPicker == null || _config == null) return;

        _v2ActionPicker.ClearItems();

        bool haEnabled = _config.HomeAssistant.Enabled;
        bool goveeEnabled = _config.Ambience.GoveeEnabled && _config.Ambience.GoveeDevices.Count > 0;
        bool obsEnabled = _config.Obs.Enabled;
        bool vmEnabled = _config.VoiceMeeter.Enabled;
        bool groupsExist = _config.Groups.Count > 0;

        bool anyHaConfigured = _config.Buttons.Concat(_config.N3.Buttons).Any(b =>
            IsHaAction(b.Action) || IsHaAction(b.DoublePressAction) || IsHaAction(b.HoldAction));
        bool anyGoveeConfigured = _config.Buttons.Concat(_config.N3.Buttons).Any(b =>
            IsGoveeAction(b.Action) || IsGoveeAction(b.DoublePressAction) || IsGoveeAction(b.HoldAction));
        bool anyObsConfigured = _config.Buttons.Concat(_config.N3.Buttons).Any(b =>
            IsObsAction(b.Action) || IsObsAction(b.DoublePressAction) || IsObsAction(b.HoldAction));
        bool anyVmConfigured = _config.Buttons.Concat(_config.N3.Buttons).Any(b =>
            IsVmAction(b.Action) || IsVmAction(b.DoublePressAction) || IsVmAction(b.HoldAction));
        bool anyGroupConfigured = _config.Buttons.Concat(_config.N3.Buttons).Any(b =>
            b.Action == "group_toggle" || b.DoublePressAction == "group_toggle" || b.HoldAction == "group_toggle");

        bool corsairEnabled = _config.Corsair.Enabled;
        bool anyCorsairConfigured = _config.Buttons.Concat(_config.N3.Buttons).Any(b =>
            IsCorsairAction(b.Action) || IsCorsairAction(b.DoublePressAction) || IsCorsairAction(b.HoldAction));

        // Always show Stream Controller page actions inside the N3 designer.
        const bool showScPageActions = true;

        foreach (var (category, values) in ActionCategories)
        {
            foreach (var value in values)
            {
                if (!ActionLookup.TryGetValue(value, out var action)) continue;

                bool isHa = IsHaAction(value);
                bool isGovee = IsGoveeAction(value);
                bool isObs = IsObsAction(value);
                bool isVm = IsVmAction(value);
                bool isCorsair = IsCorsairAction(value);
                bool isGroup = value == "group_toggle";
                bool isScPage = IsScPageAction(value);

                if (isHa && !haEnabled && !anyHaConfigured) continue;
                if (isGovee && !goveeEnabled && !anyGoveeConfigured) continue;
                if (isObs && !obsEnabled && !anyObsConfigured) continue;
                if (isVm && !vmEnabled && !anyVmConfigured) continue;
                if (isCorsair && !corsairEnabled && !anyCorsairConfigured) continue;
                if (isGroup && !groupsExist && !anyGroupConfigured) continue;
                if (isScPage && !showScPageActions) continue;

                string displayName = action.Display;
                if (isHa && !haEnabled) displayName = $"{action.Display} (HA disabled)";
                if (isGovee && !goveeEnabled) displayName = $"{action.Display} (Govee disabled)";
                if (isObs && !obsEnabled) displayName = $"{action.Display} (OBS disabled)";
                if (isVm && !vmEnabled) displayName = $"{action.Display} (VM disabled)";
                if (isCorsair && !corsairEnabled) displayName = $"{action.Display} (iCUE disabled)";

                var icon = ActionIcons.GetValueOrDefault(value, "—");
                var color = (isHa && !haEnabled) || (isGovee && !goveeEnabled) || (isObs && !obsEnabled) || (isVm && !vmEnabled) || (isCorsair && !corsairEnabled)
                    ? Color.FromRgb(0x55, 0x55, 0x55)
                    : ActionColors.GetValueOrDefault(value, Color.FromRgb(0x88, 0x88, 0x88));
                var tooltip = ActionTooltips.GetValueOrDefault(value, action.Display);

                _v2ActionPicker.AddItem(value, displayName, icon, color, category, tooltip);
            }
        }
    }

    /// <summary>
    /// Refresh the preview image, common fields, and action picker state to
    /// match the currently selected button. Called when the selection
    /// changes and after QuickActionPicker bookkeeping mutates.
    /// </summary>
    public void RefreshV2RightPanel()
    {
        if (_config == null) return;
        if (_v2PreviewPanel == null && _v2ActionPanel == null) return;

        var selection = DescribeSelection(_scSelectedButtonIdx);

        // ── Header label ────────────────────────────────────────────────
        // Prefer the user's custom label if set, otherwise fall back to the
        // default "Key N" / "Button N" / "Encoder Press N".
        if (_v2HeaderBox != null)
        {
            string headerText = selection.Label;
            if (IsN3PagedKeySelection() && selection.DisplayIdx.HasValue)
            {
                var key = GetActiveN3DisplayKeys().FirstOrDefault(k => k.Idx == selection.DisplayIdx.Value);
                if (key != null && !string.IsNullOrWhiteSpace(key.Title))
                    headerText = key.Title;
            }
            else
            {
                var btn = _config.N3.Buttons.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
                if (btn != null && !string.IsNullOrWhiteSpace(btn.Label))
                    headerText = btn.Label;
            }
            _v2HeaderBox.Text = headerText;
        }
        if (_scEditorTitle != null)
            _scEditorTitle.Text = selection.Label;

        // ── Common-fields visibility (LCD-only) ─────────────────────────
        bool isLcd = selection.DisplayIdx.HasValue;
        if (_v2CommonFieldsPanel != null)
            _v2CommonFieldsPanel.Visibility = isLcd ? Visibility.Visible : Visibility.Collapsed;
        // Preview card + Choose Icon live above the common fields. Hardware
        // controls (encoders / side buttons) have no LCD, so hide the whole
        // row for them — just the title "Encoder Press N" / "Button N" stays
        // as the header above the action picker.
        if (_v2PreviewRow != null)
            _v2PreviewRow.Visibility = isLcd ? Visibility.Visible : Visibility.Collapsed;

        // DESIGN tab only applies to LCD keys. For side buttons / encoders,
        // collapse the DESIGN tab entirely and force-select ACTION.
        if (_v2DesignTab != null)
            _v2DesignTab.Visibility = isLcd ? Visibility.Visible : Visibility.Collapsed;
        if (!isLcd && _v2RightTabIndex != 1)
        {
            _v2RightTabIndex = 1;
            ApplyV2RightTabSelection();
        }

        // Legacy UpdateDisplayTypeVisibility may have collapsed the Normal-only
        // rows based on the key's DisplayType. In V2 we surface the Display
        // Type picker separately and always want the editable rows visible
        // regardless of Normal/Clock/Dynamic — the preview itself renders
        // the current effect, the editor keeps them available for tweaking.
        if (isLcd)
        {
            if (_scTitleBox != null) _scTitleBox.Visibility = Visibility.Visible;
            // _scIconBox intentionally stays hidden — see FillV2PreviewPanel.
            if (_scTextPositionPicker != null) _scTextPositionPicker.Visibility = Visibility.Visible;
            if (_scTextSizeSlider != null) _scTextSizeSlider.Visibility = Visibility.Visible;
            if (_scTextSizeLabel != null) _scTextSizeLabel.Visibility = Visibility.Visible;
            if (_scTextColorSwatchPanel != null) _scTextColorSwatchPanel.Visibility = Visibility.Visible;
            if (_scDisplayTypePicker != null) _scDisplayTypePicker.Visibility = Visibility.Visible;
        }

        // ── Preview ─────────────────────────────────────────────────────
        if (isLcd)
        {
            var key = GetSelectedDisplayKeyConfig()
                      ?? new StreamControllerDisplayKeyConfig { Idx = selection.DisplayIdx!.Value };
            if (_scEditorPreview != null)
                _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateEditorPreview(key, 360);

            if (_scTitleBox != null)
                _scTitleBox.Text = key.Title;
            if (_scIconBox != null)
            {
                _scIconBox.Text = !string.IsNullOrWhiteSpace(key.ImagePath)
                    ? System.IO.Path.GetFileName(key.ImagePath)
                    : !string.IsNullOrWhiteSpace(key.PresetIconKind)
                        ? key.PresetIconKind
                        : "No icon selected";
            }
            if (_scTextPositionPicker != null)
            {
                _scTextPositionPicker.SelectedIndex = key.TextPosition switch
                {
                    DisplayTextPosition.Top => 0,
                    DisplayTextPosition.Middle => 1,
                    DisplayTextPosition.Bottom => 2,
                    DisplayTextPosition.Hidden => 3,
                    _ => 2,
                };
            }
            if (_v2FontPicker != null)
            {
                var currentFont = string.IsNullOrWhiteSpace(key.FontFamily) ? "Segoe UI" : key.FontFamily;
                int fontIdx = -1;
                for (int i = 0; i < _v2FontPicker.ItemCount; i++)
                {
                    if (string.Equals(_v2FontPicker.GetTagAt(i) as string, currentFont, StringComparison.OrdinalIgnoreCase))
                    { fontIdx = i; break; }
                }
                if (fontIdx < 0) fontIdx = 0; // fall back to Segoe UI
                _v2FontPicker.SelectedIndex = fontIdx;
            }
            if (_v2BrightnessSlider != null)
            {
                _v2BrightnessSlider.Value = Math.Clamp(key.Brightness <= 0 ? 100 : key.Brightness, 0, 100);
                if (_v2BrightnessLabel != null)
                    _v2BrightnessLabel.Text = $"Brightness: {(int)_v2BrightnessSlider.Value}%";
            }
            BuildTextColorSwatches();

            // ICON COLOR only matters for preset vector icons. The glow row
            // also doubles as the fill picker for Solid display type so users
            // keep one familiar color control instead of a separate concept.
            bool hasPresetIcon = !string.IsNullOrWhiteSpace(key.PresetIconKind)
                                 && string.IsNullOrWhiteSpace(key.ImagePath);
            bool showGlowRow = hasPresetIcon || key.DisplayType == DisplayKeyType.Solid;
            if (_v2IconColorLabel != null)
                _v2IconColorLabel.Visibility = hasPresetIcon ? Visibility.Visible : Visibility.Collapsed;
            if (_scIconColorSwatchPanel != null)
                _scIconColorSwatchPanel.Visibility = hasPresetIcon ? Visibility.Visible : Visibility.Collapsed;
            if (_v2GlowColorLabel != null)
            {
                _v2GlowColorLabel.Text = key.DisplayType == DisplayKeyType.Solid ? "SOLID COLOR" : "GLOW COLOR";
                _v2GlowColorLabel.Visibility = showGlowRow ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_scGlowColorSwatchPanel != null)
                _scGlowColorSwatchPanel.Visibility = showGlowRow ? Visibility.Visible : Visibility.Collapsed;
            if (hasPresetIcon)
                BuildIconColorSwatches();
            if (showGlowRow)
                BuildGlowColorSwatches();
        }
        else
        {
            // Hardware selection: no LCD preview image — clear it. The other
            // agent (or a future revision) can swap in a StreamControllerTile
            // preview; for now we keep the preview card empty.
            if (_scEditorPreview != null)
                _scEditorPreview.Source = null;
        }

        // ── Action picker ──────────────────────────────────────────────
        if (_v2ActionPicker != null)
        {
            // FillV2ActionPanel runs before config loads (ctor time), so the
            // picker starts empty. Repopulate only when a visibility-affecting
            // flag changes — not every refresh.
            var cacheKey = $"{_config.HomeAssistant.Enabled}|{_config.Ambience.GoveeEnabled}|{_config.Obs.Enabled}|{_config.VoiceMeeter.Enabled}|{_config.Groups.Count}";
            if (_v2ActionItemCacheKey != cacheKey)
            {
                PopulateV2ActionPickerItems();
                _v2ActionItemCacheKey = cacheKey;
            }

            var buttonList = IsN3PagedKeySelection() ? GetActiveN3ButtonList() : _config.N3.Buttons;
            var button = buttonList.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx)
                         ?? new ButtonConfig { Idx = _scSelectedButtonIdx };

            bool prev = _loading;
            _loading = true;
            try
            {
                _v2ActionPicker.Select(string.IsNullOrEmpty(button.Action) ? "none" : button.Action);
            }
            finally
            {
                _loading = prev;
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private bool IsN3PagedKeySelection()
    {
        return _scSelectedButtonIdx >= StreamControllerDisplayKeyBase
               && _scSelectedButtonIdx < StreamControllerSideButtonBase;
    }

    // ── Header rename (Mixer-style inline textbox) ───────────────────────────

    /// <summary>Persist the currently-typed header text back to the selected
    /// config — ButtonConfig.Label for hardware, StreamControllerDisplayKeyConfig.Title
    /// for LCD keys. Called by _v2HeaderBox.LostFocus.</summary>
    private void CommitV2HeaderRename()
    {
        if (_v2HeaderBox == null || _config == null) return;
        var newLabel = _v2HeaderBox.Text.Trim();

        if (IsN3PagedKeySelection())
        {
            int globalIdx = _scSelectedButtonIdx - StreamControllerDisplayKeyBase;
            var keys = GetActiveN3DisplayKeys();
            var key = keys.FirstOrDefault(k => k.Idx == globalIdx);
            if (key == null)
            {
                key = new StreamControllerDisplayKeyConfig { Idx = globalIdx };
                keys.Add(key);
            }
            key.Title = newLabel;
            if (_scTitleBox != null) _scTitleBox.Text = newLabel;
            UpdateEditorPreviewOnly();
        }
        else
        {
            var btn = GetOrCreateSelectedV2Button();
            if (btn != null) btn.Label = newLabel;
        }

        QueueSave();
        RefreshV2LeftPanel();
    }

    /// <summary>
    /// Returns the ButtonConfig for the current selection, creating and
    /// inserting a new entry into the appropriate list if none exists.
    /// Returns null only when there is no loaded config.
    /// </summary>
    private ButtonConfig? GetOrCreateSelectedV2Button()
    {
        if (_config == null) return null;

        var buttonList = IsN3PagedKeySelection() ? GetActiveN3ButtonList() : _config.N3.Buttons;
        var existing = buttonList.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
        if (existing != null) return existing;

        var fresh = new ButtonConfig { Idx = _scSelectedButtonIdx };
        buttonList.Add(fresh);
        return fresh;
    }

    /// <summary>
    /// Detach an element from its current parent (Panel or ContentControl)
    /// so it can be safely re-hosted under a new parent.
    /// </summary>
    private static void DetachFromParent(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case ContentControl cc when ReferenceEquals(cc.Content, element):
                cc.Content = null;
                break;
            case Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
            case Decorator dec when ReferenceEquals(dec.Child, element):
                dec.Child = null;
                break;
            case ItemsControl ic:
                ic.Items.Remove(element);
                break;
        }
    }
}
