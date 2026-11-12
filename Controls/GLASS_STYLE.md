# Glass style foundation

The app's card/pill/glow look is defined once in `Controls/GlassStyle.cs` (code-built UI) and
`Styles/GlassStyles.xaml` (XAML). Use these instead of re-implementing card chrome inline.

## API (`AmpUp.Controls.GlassStyle`)

| Member | What it does |
|-|-|
| `ApplyGlassCard(Border b, double radius = 16, bool accentBorder = false)` | Flat card: one opaque theme card colour + 1px solid theme border (no gradient or sheen layers) (accent-tinted when `accentBorder`). Clears `Effect`. |
| `AttachHoverLift(Border b)` | 2px `TranslateTransform` lift on hover (140 ms). No effects. Off when Reduce effects is on. |
| `CardFill` / `CardBorder(bool accent)` | The cached frozen brushes behind `ApplyGlassCard`. |
| `AccentPillBrushes(Color)` / `AccentPillBrushes()` | Cached frozen `(Fill, Border)` for accent pills (0x28 fill, 0x96 border). |
| `FauxGlow(Color c, double radius)` | Frozen `RadialGradientBrush` (c @0x30 centre → 0 edge). Put it on a `radius*2` Border behind an element instead of a `DropShadowEffect`. Transparent when Reduce effects is on. |
| `GlowEffect(Color c, double blur, double opacity = 0.3)` | The only place to create a glow `DropShadowEffect`. Returns `null` when Reduce effects is on. |
| `SectionTitle(text, count?)` | Same as `UiKit.SectionHeader`. |
| `SettingRow(title, hint, control)` | Glass row: title + dim hint left, control right. |
| `Init()` | Called once in `App.xaml.cs`; publishes `Glass*Brush` app resources and keeps them current. |

`ThemeManager.ReduceEffects` + `ThemeManager.OnReduceEffectsChanged` (Settings > General >
Appearance > "Reduce effects", saved as `AppConfig.ReduceEffects`). Caches rebuild on card theme,
accent and Reduce effects changes. `ApplyGlassCard`, `SettingRow` and the XAML styles use resource
references, so they update live. Brushes you assign directly (`CardFill`, `AccentPillBrushes`,
`FauxGlow`, `GlowEffect`) do not: repaint on these events (as views already do for accent).

## XAML (`Styles/GlassStyles.xaml`, merged in `App.xaml`)

Named styles only: `GlassCard`, `GlassCardAccent`, `GlassSettingRow` (Border), `GlassPill`
(Border), `GlassPrimaryButton` (plain `Button`), `GlassSectionTitle` (TextBlock).
Resource brushes (use with `DynamicResource`): `GlassCardFillBrush`, `GlassCardBorderBrush`,
`GlassCardBorderAccentBrush`, `GlassPillFillBrush`, `GlassPillBorderBrush`.

## Performance rules

1. Freeze every brush/pen you create (or take one from `GlassStyle`). Never allocate brushes per paint/hover when a cached one exists.
2. No `DropShadowEffect` / `BlurEffect` on repeated items (cards, list rows, chips, tiles). Use `FauxGlow` or nothing.
3. Glow effects only on single active elements (selected tab, hero tile), always via `GlassStyle.GlowEffect` so Reduce effects removes them.
4. Never animate an effect property (Opacity/BlurRadius). Animate a transform or a brush opacity instead.
5. No `RenderTransform` / hover lift on huge containers (pages, scroll hosts, whole lists); only on individual cards.
6. Prefer one opaque baked brush over stacking translucent layers.

## Cheat sheet

```csharp
using AmpUp.Controls;

var card = new Border { Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(16) };
GlassStyle.ApplyGlassCard(card);            // or ApplyGlassCard(card, 12, accentBorder: true)
GlassStyle.AttachHoverLift(card);

var (fill, border) = GlassStyle.AccentPillBrushes();
pill.Background = fill; pill.BorderBrush = border;

activeTab.Effect = GlassStyle.GlowEffect(ThemeManager.Accent, 9);   // null when Reduce effects

var glow = new Border { Width = 120, Height = 120, Background = GlassStyle.FauxGlow(color, 60), IsHitTestVisible = false };
// put `glow` in the same Grid cell, behind the icon tile
```

```xml
<Border Style="{StaticResource GlassCard}"> ... </Border>
<Border Style="{StaticResource GlassPill}"><TextBlock Text="Active" Foreground="{DynamicResource AccentBrush}" /></Border>
<Button Style="{StaticResource GlassPrimaryButton}" Content="Save" />
```

Reference conversions: `Controls/IconDock.cs` (active pill glow) and
`Views/GroupsView.xaml.cs` `BuildCardShell` (glass card + hover lift).
