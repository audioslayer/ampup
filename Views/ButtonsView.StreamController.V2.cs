using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AmpUp.Controls;

namespace AmpUp.Views;

/// <summary>
/// V2 Stream Controller designer. Stream Deck-inspired layout that unifies
/// the 6 LCD keys + 3 side buttons + 3 encoders into a single device canvas,
/// with a simplified right pane driven by <see cref="QuickActionPicker"/> and
/// <see cref="StreamControllerTile"/>.
///
/// This file is the integration seam. Parallel agents own specific regions:
///
///   - Left canvas (keys + hardware strip)    => FillV2LeftPanel
///   - Right preview + common fields          => FillV2PreviewPanel
///   - Right action editor                    => FillV2ActionPanel
///   - Right action-specific sub-panels       => FillV2ActionFieldsPanel
///   - Styling polish                         => ApplyV2Styling
///
/// <c>BuildStreamControllerDesigner</c> creates all <c>_sc*</c> widgets via
/// <c>BuildStreamControllerWidgetFactory</c>, then composes this V2 layout
/// and swaps the StreamControllerRoot content to <c>_v2Root</c>.
/// </summary>
public partial class ButtonsView
{
    // ── V2 hosts (populated by parallel agents) ─────────────────────────
    private Grid? _v2Root;

    // Right-pane tab state (0 = DESIGN, 1 = ACTION). The Play header +
    // preview card sit ABOVE the tab bar (always visible); the tab bar
    // toggles visibility between the design content and the action content.
    private int _v2RightTabIndex;
    private StackPanel? _v2DesignTabContent;
    private StackPanel? _v2ActionTabContent;
    private Border? _v2DesignTab;
    private Border? _v2ActionTab;
    private StackPanel? _v2LeftPanel;
    private StackPanel? _v2PreviewPanel;
    private StackPanel? _v2ActionPanel;
    private StackPanel? _v2ActionFieldsPanel;

    // ── V2 components ──────────────────────────────────────────────────
    // These are created in BuildStreamControllerDesignerV2 and wired by
    // the relevant agents. Nullable until that build step completes.
    private QuickActionPicker? _v2ActionPicker;
    private readonly List<StreamControllerTile> _v2KeyTiles = new();
    private readonly List<StreamControllerTile> _v2ButtonTiles = new();
    private readonly List<StreamControllerTile> _v2EncoderTiles = new();

    /// <summary>
    /// Entry point: compose the V2 designer. Parallel agents fill individual
    /// regions via the FillV2XxxPanel methods below.
    /// </summary>
    public void BuildStreamControllerDesignerV2()
    {
        // Two-column layout: left = device canvas, right = editor.
        _v2Root = new Grid();
        _v2Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        _v2Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });

        _v2LeftPanel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(_v2LeftPanel, 0);
        _v2Root.Children.Add(_v2LeftPanel);

        // No inner ScrollViewer — the outer page ScrollViewer in
        // ButtonsView.xaml handles vertical scrolling. A nested ScrollViewer
        // here grows unbounded inside the outer one and never actually
        // scrolls, leaving a dead zone over the right pane.
        var rightStack = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };

        // _v2PreviewPanel holds the always-visible "Play" header + preview
        // + Choose Icon row. The DESIGN content (display type, title, text
        // layout, clock, dynamic state) lives in _v2CommonFieldsPanel and
        // is placed in the DESIGN tab; the ACTION picker + action-specific
        // sub-panels live in the ACTION tab. Each tab is a StackPanel whose
        // Visibility is toggled by the tab bar below.
        _v2PreviewPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        _v2ActionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        _v2ActionFieldsPanel = new StackPanel();
        rightStack.Children.Add(_v2PreviewPanel);

        // Tab bar — Material-style underline, matches RoomView tabs.
        rightStack.Children.Add(BuildV2RightTabBar());

        _v2DesignTabContent = new StackPanel();
        _v2ActionTabContent = new StackPanel();
        rightStack.Children.Add(_v2DesignTabContent);
        rightStack.Children.Add(_v2ActionTabContent);

        _v2ActionTabContent.Children.Add(_v2ActionPanel);
        _v2ActionTabContent.Children.Add(_v2ActionFieldsPanel);

        Grid.SetColumn(rightStack, 1);
        _v2Root.Children.Add(rightStack);

        // Agents fill each region:
        FillV2LeftPanel();
        FillV2PreviewPanel();
        FillV2ActionPanel();
        FillV2ActionFieldsPanel();
        ApplyV2Styling();

        // FillV2PreviewPanel builds _v2CommonFieldsPanel but doesn't parent
        // it — we own the placement so it can live inside the DESIGN tab.
        if (_v2CommonFieldsPanel != null && _v2DesignTabContent != null)
            _v2DesignTabContent.Children.Add(_v2CommonFieldsPanel);

        ApplyV2RightTabSelection();
    }

    /// <summary>
    /// Material-style underline tab bar with DESIGN / ACTION. Visibility
    /// of the content panels is driven by <see cref="ApplyV2RightTabSelection"/>.
    /// </summary>
    private Border BuildV2RightTabBar()
    {
        var host = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Margin = new Thickness(0, 0, 0, 12),
        };
        host.SetResourceReference(Border.BorderBrushProperty, "InputBgBrush");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        _v2DesignTab = BuildV2RightTab("DESIGN", 0);
        _v2ActionTab = BuildV2RightTab("ACTION", 1);
        row.Children.Add(_v2DesignTab);
        row.Children.Add(_v2ActionTab);
        host.Child = row;
        return host;
    }

    private Border BuildV2RightTab(string label, int idx)
    {
        var tab = new Border
        {
            Padding = new Thickness(26, 10, 26, 10),
            Cursor = Cursors.Hand,
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = FindBrush("TextSecBrush"),
        };
        tab.Child = text;

        tab.MouseEnter += (_, _) =>
        {
            if (_v2RightTabIndex != idx && tab.Child is TextBlock t)
                t.Foreground = FindBrush("TextPrimaryBrush");
        };
        tab.MouseLeave += (_, _) =>
        {
            if (_v2RightTabIndex != idx && tab.Child is TextBlock t)
                t.Foreground = FindBrush("TextSecBrush");
        };
        tab.MouseLeftButtonDown += (_, _) =>
        {
            _v2RightTabIndex = idx;
            ApplyV2RightTabSelection();
        };
        return tab;
    }

    private void ApplyV2RightTabSelection()
    {
        SetV2RightTabActive(_v2DesignTab, _v2RightTabIndex == 0);
        SetV2RightTabActive(_v2ActionTab, _v2RightTabIndex == 1);

        if (_v2DesignTabContent != null)
            _v2DesignTabContent.Visibility = _v2RightTabIndex == 0
                ? Visibility.Visible : Visibility.Collapsed;
        if (_v2ActionTabContent != null)
            _v2ActionTabContent.Visibility = _v2RightTabIndex == 1
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetV2RightTabActive(Border? tab, bool active)
    {
        if (tab?.Child is not TextBlock label) return;
        if (active)
        {
            tab.BorderBrush = new SolidColorBrush(ThemeManager.Accent);
            label.Foreground = new SolidColorBrush(ThemeManager.Accent);
            label.FontWeight = FontWeights.Bold;
        }
        else
        {
            tab.BorderBrush = new SolidColorBrush(Colors.Transparent);
            label.Foreground = FindBrush("TextSecBrush");
            label.FontWeight = FontWeights.SemiBold;
        }
    }

    /// <summary>[Agent A] Populate the device canvas — key grid + hardware strip.</summary>
    partial void FillV2LeftPanel();

    /// <summary>[Agent B] Populate the preview + common fields (Title / Icon / color).</summary>
    partial void FillV2PreviewPanel();

    /// <summary>[Agent C] Populate the action picker area.</summary>
    partial void FillV2ActionPanel();

    /// <summary>[Agent D] Populate the action-specific sub-panels (path / macro / toggle / multi / folder / text / url).</summary>
    partial void FillV2ActionFieldsPanel();

    /// <summary>[Agent E] Apply shared styling constants matching Room tab.</summary>
    partial void ApplyV2Styling();
}
