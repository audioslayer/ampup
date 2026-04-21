using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

/// <summary>
/// V2 Stream Controller — action-specific sub-panel wrappers.
///
/// This file owns ONLY the V2 section-card chrome around the existing
/// legacy sub-panels (path, macro, text snippet, screenshot, device,
/// knob, toggle, multi-action, folder). The legacy widgets themselves
/// are re-hosted, not rebuilt — their internal wiring and field references
/// (_scPathBox, _scBrowsePathButton, _scAppChip, etc.) stay intact.
/// </summary>
public partial class ButtonsView
{
    // ── V2 section-card wrappers (one per action-specific sub-panel) ────
    private Border? _v2PathCard;
    private TextBlock? _v2PathCardLabel;
    private Border? _v2MacroCard;
    private Border? _v2TextSnippetCard;
    private Border? _v2ScreenshotCard;
    private Border? _v2DeviceCard;
    private Border? _v2GoveeCard;
    private Border? _v2RoomEffectCard;
    private Border? _v2KnobCard;
    private Border? _v2ToggleCard;
    private Border? _v2MultiActionCard;
    private Border? _v2FolderCard;
    private Border? _v2ProfileCard;
    private ListPicker? _v2ProfilePicker;

    // Track the accent-colored header parts so theme changes can refresh them.
    private readonly List<(Border bar, TextBlock label)> _v2SectionHeaders = new();

    /// <summary>
    /// [Agent D] Wrap each legacy action sub-panel in a consistent V2 section card
    /// and add them (collapsed) to <see cref="_v2ActionFieldsPanel"/>.
    /// </summary>
    partial void FillV2ActionFieldsPanel()
    {
        if (_v2ActionFieldsPanel == null) return;
        _v2ActionFieldsPanel.Children.Clear();

        // 1. Path / URL / keystroke-chain card — dynamic label, default "PATH".
        _v2PathCard = MakeV2SectionCard("PATH", out _v2PathCardLabel, _scPathPanel);
        _v2ActionFieldsPanel.Children.Add(_v2PathCard);

        // 2. Macro keys textbox.
        _v2MacroCard = MakeV2SectionCard("MACRO", out _, _scMacroPanel);
        _v2ActionFieldsPanel.Children.Add(_v2MacroCard);

        // 3. Text snippet (multi-line type-text).
        _v2TextSnippetCard = MakeV2SectionCard("TEXT TO TYPE", out _, _scTextSnippetPanel);
        _v2ActionFieldsPanel.Children.Add(_v2TextSnippetCard);

        // 4. Screenshot info blurb.
        _v2ScreenshotCard = MakeV2SectionCard("SCREENSHOT", out _, _scScreenshotInfoPanel);
        _v2ActionFieldsPanel.Children.Add(_v2ScreenshotCard);

        // 5. Device picker (select_output / select_input / mute_device).
        _v2DeviceCard = MakeV2SectionCard("DEVICE", out _, _scDevicePanel);
        _v2ActionFieldsPanel.Children.Add(_v2DeviceCard);

        // Govee device picker (govee_toggle / govee_white_toggle / govee_color)
        _v2GoveeCard = MakeV2SectionCard("GOVEE DEVICE", out _, _scGoveePanel);
        _v2ActionFieldsPanel.Children.Add(_v2GoveeCard);

        _v2RoomEffectCard = MakeV2SectionCard("ROOM EFFECT", out _, _scRoomEffectPanel);
        _v2ActionFieldsPanel.Children.Add(_v2RoomEffectCard);

        // 6. Linked Turn Up knob (mute_app_group).
        _v2KnobCard = MakeV2SectionCard("LINKED TURN UP KNOB", out _, _scKnobPanel);
        _v2ActionFieldsPanel.Children.Add(_v2KnobCard);

        // 7. A / B toggle editor.
        _v2ToggleCard = MakeV2SectionCard("A / B TOGGLE", out _, _scTogglePanel);
        _v2ActionFieldsPanel.Children.Add(_v2ToggleCard);

        // 8. Multi-action sequence editor.
        _v2MultiActionCard = MakeV2SectionCard("MULTI-ACTION", out _, _scMultiActionPanel);
        _v2ActionFieldsPanel.Children.Add(_v2MultiActionCard);

        // 9. Folder picker (open_folder).
        _v2FolderCard = MakeV2SectionCard("SPACE", out _, _scFolderPanel);
        _v2ActionFieldsPanel.Children.Add(_v2FolderCard);

        // 10. Profile picker (switch_profile).
        _v2ProfilePicker = new ListPicker();
        _v2ProfilePicker.SelectionChanged += (_, _) =>
        {
            if (_loading || _config == null || _v2ProfilePicker == null) return;
            var profileName = _v2ProfilePicker.SelectedTag as string ?? "";
            var list = GetOwningButtonList();
            var btn = list.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
            if (btn == null) return;
            btn.Action = "switch_profile";
            btn.ProfileName = profileName;

            // Keep legacy picker's SubTag in sync so CollectAndSave's
            // UpdateStreamControllerSelection doesn't clobber ProfileName.
            if (_scActionPicker != null && !string.IsNullOrEmpty(profileName))
            {
                bool prev = _loading;
                _loading = true;
                try { SelectProfileSubTag(_scActionPicker, "switch_profile", profileName); }
                finally { _loading = prev; }
            }
            QueueSave();
            RefreshV2LeftPanel();
        };
        _v2ProfileCard = MakeV2SectionCard("PROFILE TO SWITCH TO", out _, _v2ProfilePicker);
        _v2ActionFieldsPanel.Children.Add(_v2ProfileCard);

        // Start everything hidden — visibility gets driven by the action picker.
        foreach (var child in _v2ActionFieldsPanel.Children)
        {
            if (child is UIElement el)
                el.Visibility = Visibility.Collapsed;
        }

        RefreshV2ActionFieldsVisibility();
    }

    /// <summary>
    /// V2 analogue of <c>UpdateStreamControllerActionVisibility</c>. Toggles the
    /// V2 wrapper cards (not the inner legacy panels) based on the currently
    /// selected action. Also drives the dynamic label on the path card and
    /// delegates to the legacy helpers that re-configure the inner widgets
    /// (<c>ApplyPathLabelAndButtons</c>, <c>RefreshFolderPickerItems</c>, etc.).
    /// </summary>
    public void RefreshV2ActionFieldsVisibility()
    {
        if (_v2ActionFieldsPanel == null || _scActionPicker == null) return;

        // Make sure the legacy inner panels are *visible* — the V2 wrappers
        // now own the show/hide decision. If they were left Collapsed from
        // an earlier legacy render pass, the wrapper would show an empty card.
        ShowInner(_scPathPanel);
        ShowInner(_scMacroPanel);
        ShowInner(_scTextSnippetPanel);
        ShowInner(_scScreenshotInfoPanel);
        ShowInner(_scDevicePanel);
        ShowInner(_scGoveePanel);
        ShowInner(_scRoomEffectPanel);
        ShowInner(_scKnobPanel);
        ShowInner(_scTogglePanel);
        ShowInner(_scMultiActionPanel);
        ShowInner(_scFolderPanel);

        // Prefer the V2 picker — it's the source of truth for whichever
        // gesture is currently being edited. The legacy _scActionPicker is
        // only synced on the Tap gesture, so reading it on Double/Hold
        // would show the wrong sub-option cards (path / folder / etc.).
        var action = _v2ActionPicker?.SelectedValue;
        if (string.IsNullOrEmpty(action)) action = GetComboActionValue(_scActionPicker);

        bool needsPath = PathActions.Contains(action)
            || action is "ha_service" or "govee_color" or "obs_scene" or "obs_mute"
                      or "vm_mute_strip" or "vm_mute_bus";

        SetCardVisible(_v2PathCard, needsPath);
        SetCardVisible(_v2MacroCard, action == "macro");
        SetCardVisible(_v2TextSnippetCard, action == "type_text");
        SetCardVisible(_v2ScreenshotCard, action == "screenshot");
        SetCardVisible(_v2DeviceCard, action is "select_output" or "select_input" or "mute_device");
        SetCardVisible(_v2GoveeCard, action is "govee_toggle" or "govee_white_toggle" or "govee_color");
        if (action is "govee_toggle" or "govee_white_toggle" or "govee_color")
            RefreshGoveePickerItems();
        SetCardVisible(_v2RoomEffectCard, action == "room_effect");
        if (action == "room_effect") RefreshRoomEffectPickerItems();
        SetCardVisible(_v2KnobCard, action == "mute_app_group");
        SetCardVisible(_v2ToggleCard, action == "toggle_action");
        SetCardVisible(_v2MultiActionCard, action == "multi_action");
        SetCardVisible(_v2FolderCard, action == "open_folder");
        SetCardVisible(_v2ProfileCard, action == "switch_profile");

        if (action == "switch_profile")
            RefreshV2ProfilePickerItems();

        // Delegate inner-widget reconfig to the legacy helpers so we don't
        // duplicate URL / page-number / app-chip / folder / toggle wiring.
        if (needsPath && _scPathLabel != null && _scPathBox != null
            && _scBrowsePathButton != null && _scPickPathButton != null && _scAppChip != null)
        {
            if (action == "sc_go_to_page")
            {
                _scPathLabel.Text = "PAGE NUMBER";
                _scPathBox.Tag = "Page number (1-based)";
                _scBrowsePathButton.Visibility = Visibility.Collapsed;
                _scPickPathButton.Visibility = Visibility.Collapsed;
                SetPathHeader("PAGE NUMBER");
            }
            else if (action == "open_url")
            {
                _scPathLabel.Text = "URL";
                _scPathBox.Tag = "https://example.com";
                _scPathBox.ToolTip = "URL to open in the default browser";
                _scBrowsePathButton.Visibility = Visibility.Collapsed;
                _scPickPathButton.Visibility = Visibility.Collapsed;
                if (_scPathBox.Parent is Border inputBorder)
                    inputBorder.Visibility = Visibility.Visible;
                SetPathHeader("URL");
            }
            else
            {
                ApplyPathLabelAndButtons(_scPathLabel, _scPathBox, _scBrowsePathButton, _scPickPathButton, action, _scAppChip);
                SetPathHeader(HeaderTextForAction(action, _scPathLabel.Text));
            }

            _scAppChip.Visibility = action is "close_program" or "mute_program"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (action == "toggle_action")
            UpdateStreamControllerToggleVisibility();
        if (action == "open_folder")
            RefreshFolderPickerItems();
        if (action == "multi_action")
            RebuildMultiActionList();
    }

    // ── V2 section-card helpers ─────────────────────────────────────────

    /// <summary>
    /// Repopulates the V2 profile picker from <c>_config.Profiles</c> and
    /// selects the current <c>ProfileName</c> for the selected button.
    /// </summary>
    private void RefreshV2ProfilePickerItems()
    {
        if (_v2ProfilePicker == null || _config == null) return;

        bool prev = _loading;
        _loading = true;
        try
        {
            _v2ProfilePicker.ClearItems();
            foreach (var name in _config.Profiles)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    _v2ProfilePicker.AddItem(name, name);
            }

            var list = GetOwningButtonList();
            var btn = list.FirstOrDefault(b => b.Idx == _scSelectedButtonIdx);
            var current = btn?.ProfileName ?? "";

            int foundIdx = -1;
            for (int i = 0; i < _v2ProfilePicker.ItemCount; i++)
            {
                if (_v2ProfilePicker.GetTagAt(i) as string == current)
                {
                    foundIdx = i;
                    break;
                }
            }
            _v2ProfilePicker.SelectedIndex = foundIdx;
        }
        finally
        {
            _loading = prev;
        }
    }

    /// <summary>
    /// Inline wrapper around a legacy action-specific panel. Renders a
    /// FOLDERS-style sub-header (accent bar + bold label) at the top, then
    /// re-hosts the legacy content. The legacy panels bake their own small
    /// "DEVICE" / "PATH" / etc. label as their first child, so we suppress
    /// that first label when it matches our outer header to avoid a doubled
    /// heading like "DEVICE / DEVICE".
    /// </summary>
    private Border MakeV2SectionCard(string label, out TextBlock headerLabel, UIElement? content)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };

        // FOLDERS-style header — 3px accent bar + bold label.
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 2, 10, 2),
            Background = new SolidColorBrush(ThemeManager.Accent),
        };
        headerLabel = new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        headerRow.Children.Add(bar);
        headerRow.Children.Add(headerLabel);
        stack.Children.Add(headerRow);
        _v2SectionHeaders.Add((bar, headerLabel));

        if (content != null)
        {
            if (content is FrameworkElement fe && fe.Parent is Panel oldParent)
                oldParent.Children.Remove(fe);

            if (content is FrameworkElement feChild)
                feChild.Margin = new Thickness(0);

            // Legacy panels prepend their own small uppercase label as the
            // first TextBlock child. Hide it when it matches our outer
            // header so we don't render the same word twice.
            if (content is Panel innerPanel)
                SuppressDuplicateLegacyLabel(innerPanel, label);

            stack.Children.Add(content);
        }

        return new Border
        {
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0),
            Child = stack,
        };
    }

    /// <summary>
    /// Collapse the first TextBlock child of <paramref name="panel"/> if its
    /// text (case/whitespace insensitive) matches <paramref name="outerLabel"/>
    /// or a small set of legacy aliases. Keeps the field alive so its
    /// consumers (e.g. ApplyPathLabelAndButtons) can still mutate it.
    /// </summary>
    private static void SuppressDuplicateLegacyLabel(Panel panel, string outerLabel)
    {
        string norm(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        string want = norm(outerLabel);

        foreach (var child in panel.Children)
        {
            if (child is TextBlock tb)
            {
                var got = norm(tb.Text ?? "");
                if (got == want
                    || (want == "PATH" && (got == "FILE" || got == "APPLICATION" || got == "URL" || got == "PAGENUMBER"
                                           || got == "PROCESSNAME" || got == "SERVICECALL" || got == "DEVICE/COLOR"
                                           || got == "OBSSCENE" || got == "OBSSOURCE"
                                           || got == "VOICEMEETERSTRIP" || got == "VOICEMEETERBUS")))
                {
                    tb.Visibility = Visibility.Collapsed;
                }
                // Only the very first TextBlock is the built-in header; stop after that.
                return;
            }
        }
    }

    /// <summary>
    /// Small accent-colored header pair (3px vertical accent bar + uppercase
    /// SemiBold label) used across all V2 section cards. Matches Room tab.
    /// </summary>
    private (Border bar, TextBlock label) MakeV2SectionLabel(string text)
    {
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(ThemeManager.Accent),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeManager.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _v2SectionHeaders.Add((bar, label));
        return (bar, label);
    }

    // ── Local helpers ───────────────────────────────────────────────────

    private static void SetCardVisible(UIElement? card, bool visible)
    {
        if (card != null)
            card.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ShowInner(UIElement? inner)
    {
        if (inner != null)
            inner.Visibility = Visibility.Visible;
    }

    private void SetPathHeader(string header)
    {
        if (_v2PathCardLabel != null)
            _v2PathCardLabel.Text = header.ToUpperInvariant();
    }

    /// <summary>
    /// Map a selected action to the card header text. Falls back to the
    /// legacy inline label when no specific mapping exists.
    /// </summary>
    private static string HeaderTextForAction(string action, string fallback) => action switch
    {
        "open_url"       => "URL",
        "sc_go_to_page"  => "PAGE NUMBER",
        "ha_service"     => "SERVICE CALL",
        "govee_color"    => "DEVICE / COLOR",
        "obs_scene"      => "OBS SCENE",
        "obs_mute"       => "OBS SOURCE",
        "vm_mute_strip"  => "VOICEMEETER STRIP",
        "vm_mute_bus"    => "VOICEMEETER BUS",
        "launch_exe"     => "PATH",
        "close_program"  => "PROCESS NAME",
        "mute_program"   => "PROCESS NAME",
        _                => fallback,
    };
}
