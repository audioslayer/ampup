using System.Windows;
using System.Windows.Controls;
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

        var rightHost = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(12, 0, 0, 0),
        };
        var rightStack = new StackPanel();
        _v2PreviewPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        _v2ActionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        _v2ActionFieldsPanel = new StackPanel();
        rightStack.Children.Add(_v2PreviewPanel);
        rightStack.Children.Add(_v2ActionPanel);
        rightStack.Children.Add(_v2ActionFieldsPanel);
        rightHost.Content = rightStack;
        Grid.SetColumn(rightHost, 1);
        _v2Root.Children.Add(rightHost);

        // Agents fill each region:
        FillV2LeftPanel();
        FillV2PreviewPanel();
        FillV2ActionPanel();
        FillV2ActionFieldsPanel();
        ApplyV2Styling();
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
