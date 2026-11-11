# Integration setup guides

All five share `IntegrationSetupGuide` (Controls/IntegrationSetupGuide.cs), which copies the
SpotifySetupGuide chrome and flow. Each has a parameterless constructor, is shown with
`ShowDialog()`, and exposes `bool WasSuccessful`. The pattern is the same as Spotify and Discord:

```csharp
var guide = new AmpUp.Controls.XxxSetupGuide { Owner = Window.GetWindow(this) };
if (guide.ShowDialog() != true || !guide.WasSuccessful) return;
// copy results into the fields/config, then run the integration's Connect/Test
```

Each wiring needs a `<ui:Button x:Name="BtnXxxSetupGuide" Content="Setup guide" Style="{StaticResource SButton}" />`
in the section, plus `BtnXxxSetupGuide.Click += OnXxxSetupGuide;` next to that section's other handlers.

| Guide | Results | Button | Settings section | On Finish |
|-|-|-|-|-|
| `HomeAssistantSetupGuide` | `Url`, `Token` (const `DefaultUrl`) | `BtnHaSetupGuide` | Home Assistant (`HaSettingsPanel`) | `ChkHaEnabled.IsChecked = true; TxtHaUrl.Text = guide.Url; TxtHaToken.Password = guide.Token; CollectAndSave(); OnHaTest(this, e);` |
| `ObsSetupGuide` | `Host`, `Port` (int), `Password` (consts `DefaultHost`, `DefaultPort`) | `BtnObsSetupGuide` | OBS Studio (`ObsSettingsPanel`) | `ChkObsEnabled.IsChecked = true; TxtObsHost.Text = guide.Host; TxtObsPort.Text = guide.Port.ToString(); TxtObsPassword.Password = guide.Password; CollectAndSave(); OnObsTest(this, e);` |
| `VoiceMeeterSetupGuide` | none (const `DefaultInstallFolder`) | `BtnVmSetupGuide` | VoiceMeeter (`VmSettingsPanel`; could replace the `VmSetupGuide` Expander, whose text it covers) | `ChkVmEnabled.IsChecked = true;` (the Checked handler saves) |
| `CorsairSetupGuide` | none | `BtnCorsairSetupGuide` | Corsair iCUE (card header; `CorsairDeviceList` is hidden until enabled, so put the button where it shows while disabled) | `ChkCorsairEnabled.IsChecked = true;` |
| `SignalRgbSetupGuide` | none | `BtnSignalRgbSetupGuide` | SignalRGB (`SignalRgbSettingsPanel`, next to `BtnSignalRgbInstallPlugin`) | `ChkSignalRgbEnabled.IsChecked = true; OnSignalRgbInstallPlugin(this, e);` |

Notes:
- Set the enable switch before CollectAndSave so the saved config has `Enabled = true`. Check whether
  `_loading` guards apply to that handler.
- The guide text says Finish saves, tests, or installs, so the wiring must do that.
- HA and OBS validate on Finish: empty URL/token or host is rejected, and the port must be 1-65535.
  The OBS password may be empty.
- Not compiled yet (written on Linux). Build on Windows before wiring.
