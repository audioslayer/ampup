# Device photos

Product images for Settings → Devices. Each card loads `Assets/devices/<deviceId>.png`
(bundled as a WPF Resource by `AmpUp.csproj`). If the file is missing, the card shows a
drawn placeholder instead, so nothing breaks.

| Device | File |
|-|-|
| Turn Up mixer | `turnup.png` |
| Stream Controller (TreasLin N3) | `n3.png` |

- Recommended: 800x600 PNG with a transparent background, device centred with a little padding.
- Only use photos you own or have rights to (no copied store or manufacturer images).
- Rebuild after adding a file; it appears automatically.
