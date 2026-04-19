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
        if (_scEditorTitle == null)
        {
            _scEditorTitle = new TextBlock
            {
                Foreground = FindBrush("TextPrimaryBrush"),
            };
        }
        DetachFromParent(_scEditorTitle);
        _scEditorTitle.FontSize = 16;
        _scEditorTitle.FontWeight = FontWeights.Bold;
        _scEditorTitle.Margin = new Thickness(0, 0, 0, 12);
        _v2PreviewPanel.Children.Add(_scEditorTitle);

        // ── 2. Live preview card (280 × 160) ─────────────────────────────
        _v2PreviewCard = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
            BorderBrush = FindBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 14),
            Width = 220,
            Height = 220,
            HorizontalAlignment = HorizontalAlignment.Center,
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
        _v2PreviewPanel.Children.Add(_v2PreviewCard);

        // ── 3. Common fields (LCD-only) ──────────────────────────────────
        // Re-host the existing _sc* widgets so their handlers and state stay
        // live. The legacy "Normal-only" visibility toggle can leave some
        // inputs collapsed from an earlier Clock/Dynamic selection — force
        // Visible here since V2 surfaces the Display Type picker separately.
        _v2CommonFieldsPanel = new StackPanel();

        // DISPLAY TYPE (Normal / Clock / Dynamic) — key driver of layout.
        if (_scDisplayTypePicker != null)
        {
            _v2CommonFieldsPanel.Children.Add(MakeEditorLabel("DISPLAY TYPE"));
            DetachFromParent(_scDisplayTypePicker);
            _scDisplayTypePicker.Margin = new Thickness(0, 0, 0, 10);
            _scDisplayTypePicker.Visibility = Visibility.Visible;
            _v2CommonFieldsPanel.Children.Add(_scDisplayTypePicker);
        }

        // TITLE
        _v2CommonFieldsPanel.Children.Add(MakeEditorLabel("TITLE"));
        if (_scTitleBox == null) _scTitleBox = MakeEditorTextBox("Display title");
        DetachFromParent(_scTitleBox);
        _scTitleBox.Visibility = Visibility.Visible;
        _v2CommonFieldsPanel.Children.Add(_scTitleBox);

        // ICON (field + Choose Icon button)
        _v2CommonFieldsPanel.Children.Add(MakeEditorLabel("ICON"));
        if (_scIconBox == null)
        {
            _scIconBox = MakeEditorTextBox("No icon selected");
            _scIconBox.IsReadOnly = true;
        }
        DetachFromParent(_scIconBox);
        _scIconBox.Visibility = Visibility.Visible;
        _v2CommonFieldsPanel.Children.Add(_scIconBox);

        var chooseIconBtn = MakeEditorButton("Choose Icon", (_, _) => ChooseStreamControllerIcon());
        chooseIconBtn.Margin = new Thickness(0, 6, 0, 10);
        _v2CommonFieldsPanel.Children.Add(chooseIconBtn);

        // TEXT POSITION
        _v2CommonFieldsPanel.Children.Add(MakeEditorLabel("TEXT POSITION"));
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
        _v2CommonFieldsPanel.Children.Add(_scTextPositionPicker);

        // TEXT SIZE
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
            _v2CommonFieldsPanel.Children.Add(_scTextSizeLabel);

            DetachFromParent(_scTextSizeSlider);
            _scTextSizeSlider.Margin = new Thickness(0, 0, 0, 10);
            _scTextSizeSlider.Visibility = Visibility.Visible;
            _v2CommonFieldsPanel.Children.Add(_scTextSizeSlider);
        }

        // TEXT COLOR (palette swatches)
        _v2CommonFieldsPanel.Children.Add(MakeEditorLabel("TEXT COLOR"));
        if (_scTextColorSwatchPanel == null) _scTextColorSwatchPanel = new WrapPanel();
        DetachFromParent(_scTextColorSwatchPanel);
        _scTextColorSwatchPanel.Margin = new Thickness(0, 0, 0, 10);
        _scTextColorSwatchPanel.Visibility = Visibility.Visible;
        _v2CommonFieldsPanel.Children.Add(_scTextColorSwatchPanel);

        // CLOCK FORMAT + DYNAMIC (re-host so user can configure Clock/Dynamic in V2)
        if (_scClockPanel != null)
        {
            DetachFromParent(_scClockPanel);
            _v2CommonFieldsPanel.Children.Add(_scClockPanel);
        }
        if (_scDynamicPanel != null)
        {
            DetachFromParent(_scDynamicPanel);
            _v2CommonFieldsPanel.Children.Add(_scDynamicPanel);
        }

        _v2PreviewPanel.Children.Add(_v2CommonFieldsPanel);
    }

    partial void FillV2ActionPanel()
    {
        if (_v2ActionPanel == null) return;

        _v2ActionPanel.Children.Add(MakeEditorLabel("ACTION"));

        _v2ActionPicker = new QuickActionPicker
        {
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        PopulateV2ActionPickerItems();

        // Seed favorites + recents from config.
        if (_config != null)
        {
            _v2ActionPicker.SetFavorites(_config.N3.FavoriteActions);
            _v2ActionPicker.SetRecents(_config.N3.RecentActions);
        }

        // ── Event wiring ─────────────────────────────────────────────────

        // User star-toggled an action — flip the entry in FavoriteActions.
        _v2ActionPicker.OnToggleFavorite += (value) =>
        {
            if (_config == null || string.IsNullOrEmpty(value)) return;
            var favs = _config.N3.FavoriteActions;
            if (favs.Contains(value))
                favs.Remove(value);
            else
                favs.Add(value);
            _v2ActionPicker?.SetFavorites(favs);
            QueueSave();
        };

        // User picked an action — push to front of recents (deduped, cap 8).
        _v2ActionPicker.OnActionChosen += (value) =>
        {
            if (_config == null || string.IsNullOrEmpty(value)) return;
            var recents = _config.N3.RecentActions;
            recents.Remove(value);
            recents.Insert(0, value);
            while (recents.Count > 8) recents.RemoveAt(recents.Count - 1);
            _v2ActionPicker?.SetRecents(recents);
            QueueSave();
        };

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
                bool isGroup = value == "group_toggle";
                bool isScPage = IsScPageAction(value);

                if (isHa && !haEnabled && !anyHaConfigured) continue;
                if (isGovee && !goveeEnabled && !anyGoveeConfigured) continue;
                if (isObs && !obsEnabled && !anyObsConfigured) continue;
                if (isVm && !vmEnabled && !anyVmConfigured) continue;
                if (isGroup && !groupsExist && !anyGroupConfigured) continue;
                if (isScPage && !showScPageActions) continue;

                string displayName = action.Display;
                if (isHa && !haEnabled) displayName = $"{action.Display} (HA disabled)";
                if (isGovee && !goveeEnabled) displayName = $"{action.Display} (Govee disabled)";
                if (isObs && !obsEnabled) displayName = $"{action.Display} (OBS disabled)";
                if (isVm && !vmEnabled) displayName = $"{action.Display} (VM disabled)";

                var icon = ActionIcons.GetValueOrDefault(value, "—");
                var color = (isHa && !haEnabled) || (isGovee && !goveeEnabled) || (isObs && !obsEnabled) || (isVm && !vmEnabled)
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
        if (_scEditorTitle != null)
            _scEditorTitle.Text = selection.Label;

        // ── Common-fields visibility (LCD-only) ─────────────────────────
        bool isLcd = selection.DisplayIdx.HasValue;
        if (_v2CommonFieldsPanel != null)
            _v2CommonFieldsPanel.Visibility = isLcd ? Visibility.Visible : Visibility.Collapsed;

        // Legacy UpdateDisplayTypeVisibility may have collapsed the Normal-only
        // rows based on the key's DisplayType. In V2 we surface the Display
        // Type picker separately and always want the editable rows visible
        // regardless of Normal/Clock/Dynamic — the preview itself renders
        // the current effect, the editor keeps them available for tweaking.
        if (isLcd)
        {
            if (_scTitleBox != null) _scTitleBox.Visibility = Visibility.Visible;
            if (_scIconBox != null) _scIconBox.Visibility = Visibility.Visible;
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
                _scEditorPreview.Source = StreamControllerDisplayRenderer.CreateHardwarePreview(key);

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
            BuildTextColorSwatches();
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
            var cacheKey = $"{_config.HomeAssistant.Enabled}|{_config.Ambience.GoveeEnabled}|{_config.Obs.Enabled}|{_config.VoiceMeeter.Enabled}|{_config.Groups.Count}|{_config.N3.FavoriteActions.Count}|{string.Join(",", _config.N3.FavoriteActions)}|{string.Join(",", _config.N3.RecentActions)}";
            if (_v2ActionItemCacheKey != cacheKey)
            {
                PopulateV2ActionPickerItems();
                _v2ActionPicker.SetFavorites(_config.N3.FavoriteActions);
                _v2ActionPicker.SetRecents(_config.N3.RecentActions);
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
