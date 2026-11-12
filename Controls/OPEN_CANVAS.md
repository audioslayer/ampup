# Open canvas layout

The replacement for "every item is its own card". Two approved directions:

- **A. Open canvas**: no boxes. Sections are a small caps title over a 1px hairline; items are plain rows on the page background separated by faint 1px lines.
- **C. Side list + open detail**: slim glass list on the left, open pane on the right (A-style sections inside). Use when each item has many settings (pattern: Integrations hub in `Views/SettingsView.Integrations.cs`).

Code: `Controls/OpenCanvas.cs`. XAML styles: `Styles/GlassStyles.xaml` (`OpenSection`, `OpenSectionTitle`, `OpenSectionRule`, `OpenRow`, `OpenRowLast`). Reference: Settings > General > Startup.

## Visual rules

- No boxes: rows have no fill, no corner radius, no shadow, no border except a 1px bottom hairline (`CardBorderBrush`). The last row in a section has no hairline.
- Section title: 11px SemiBold, `TextDimBrush`, UPPERCASE text; optional dim count; then a 1px hairline.
- Rows: min height 52px, padding 4,10. Layout `[optional icon tile] [title 13px + dim 11px sub line] [trailing controls, right-aligned]`.
- Section spacing: 22px above each section (`OpenCanvas.SectionSpacing`).
- Toggles, sliders, buttons and values sit right-aligned in the trailing slot.
- Hover: white 0x08 background, no animation.
- Readable width: settings-style content should cap around 760-900px (`MaxWidth` on the page StackPanel, left-aligned) so labels and controls don't drift apart on wide windows. Grids of media/tiles may use full width.
- Selected list row (C): accent 0x28 fill + accent 0x96 1px border, radius 10, no glow.
- Keep cards only where a surface genuinely groups live content (e.g. a hero/now-playing panel); never one card per item.

## API (`AmpUp.Controls.OpenCanvas`)

| Member | Returns |
|-|-|
| `Section(title, count?, actions?)` | header: caps title, dim count, right actions, hairline (22px top margin) |
| `Row(leading?, title, sub?, trailing?, divider = true, minHeight = 52)` | `Border` item row with hover |
| `SettingRow(title, hint?, control, divider = true)` | label/hint left, control right. Replaces boxed `GlassStyle.SettingRow` |
| `RowShell(content, divider, minHeight)` | open row around custom content |
| `Divider()` | 1px rule, live `CardBorderBrush` |
| `AttachHover(border)` | subtle hover background |
| `HoverBrush`, `SelectedFill`, `SelectedBorder` | cached frozen brushes (accent ones rebuild on accent change) |
| `new MasterDetail()` | C layout: `AddItem(key, label, MaterialIconKind, statusDot?)`, `Select(key)`, `SelectedKey`, `SelectionChanged`, `ContentHost` (right pane `Border`); list 248px; Up/Down/Home/End when focused; below 700px wide the list moves on top as a horizontal strip |

## Cheat sheet

```csharp
var page = new StackPanel { MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
page.Children.Add(OpenCanvas.Section("Startup"));
page.Children.Add(OpenCanvas.SettingRow("Start with Windows", "Launch on sign-in.", toggle));
page.Children.Add(OpenCanvas.SettingRow("Check for updates", null, updatesToggle, divider: false));

page.Children.Add(OpenCanvas.Section("Devices", count: "3", actions: addButton));
foreach (var d in devices)
    page.Children.Add(OpenCanvas.Row(UiKit.IconTile(d.Color, d.Icon), d.Name, d.Status, MakeMenu(d)));

var md = new OpenCanvas.MasterDetail();
md.AddItem("ha", "Home Assistant", MaterialIconKind.HomeAssistant, statusDot);
md.SelectionChanged += key => md.ContentHost.Child = BuildDetail(key);
md.ContentHost.Child = BuildDetail(md.SelectedKey!); // first AddItem auto-selects (event fires before you subscribe)
```

```xml
<StackPanel Style="{StaticResource OpenSection}">
    <TextBlock Text="STARTUP" Style="{StaticResource OpenSectionTitle}" />
    <Border Style="{StaticResource OpenSectionRule}" />
    <Border Style="{StaticResource OpenRow}">
        <local:SettingsSwitch x:Name="ChkFoo" Text="Foo" Description="What foo does." />
    </Border>
    <Border Style="{StaticResource OpenRowLast}">
        <local:SettingsSwitch x:Name="ChkBar" Text="Bar" />
    </Border>
</StackPanel>
```

## Performance rules

- No `Effect`s (DropShadow/Blur), no animations on rows, no per-row brush allocation: use the cached frozen brushes or `SetResourceReference`/`DynamicResource` theme brushes.
- Hairlines use `CardBorderBrush` via resource reference, so theme changes apply live without rebuilding.
- `SnapsToDevicePixels` stays on for crisp 1px lines.
- `GlassCard` / `CardPanel` are unchanged; switch pages over one at a time and drop their card usage as you go.
