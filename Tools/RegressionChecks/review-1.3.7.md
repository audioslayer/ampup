AmpUp 1.3.7 review — September 19, 2026

This was a focused review of mute handling, RGB rendering, the pending room/audio changes, and installer packaging. It is not an exhaustive audit of every integration.

Changes made during review:

- App-group mute lighting now shares volume control's space-insensitive name matcher and considers WASAPI display names. Previously a group such as Apple Music could control volume correctly but fail to show mute.
- Clearing an app group's last member clears its cached target-mute lighting instead of leaving it stuck muted.
- Corrected the new 4096-point FFT normalization. NAudio's forward transform already normalizes by its size; dividing the output by another four weakened the signal. Broad-band averaging now compensates for the increased bin count and matches the previous sensitivity within 3% in the synthetic-tone check.
- Added 50% FFT window overlap: approximately 23 updates/second at 48 kHz instead of approximately 12, retaining the larger window's bass resolution. Removed redundant square-root-then-square work in both spectrum loops.
- Fixed CopySpectrum's upper-bound clamp, which could throw when the requested minimum reached 20 kHz.
- Allowed an explicit Inno Setup PublishDir so the installer can use a fresh staging directory without clearing existing publish output.

Validation:

- Initial Release build: zero warnings, zero errors.
- Final regression runner: 23 checks passed.
- Self-contained win-x64 publish and Inno Setup compile: succeeded.
- Published app file version: 1.3.7.0; installer product version: 1.3.7.
- git diff --check: passed.

Installer at the end of review: installer/output/AmpUp-Setup-1.3.7.exe (66,059,980 bytes), with an adjacent SHA-256 file. It includes the pre-existing room-lighting, tuning-dial, and audio-analyzer work in this checkout. At that point source changes were uncommitted and nothing had been published to GitHub. The installer was built but not installed or launched.

Live smoke checks still needed before publishing:

| Check | Expected result |
| --- | --- |
| Tap VM strip and bus mute twice | First tap mutes; next tap unmutes |
| AmpUp VM mute with both effects | Linked dial updates on the next RGB frame |
| Change mute manually in VoiceMeeter | Linked dial follows in roughly one second |
| Master, mic, app group, specific input/output device | Each effect follows the assigned target |
| Blend+Mute at several knob positions | Whole dial follows gradient; mute matches zero-volume color |
| Pos+Mute | Progressive fill while unmuted; dim high-end color when muted |
| Room VU effects at the normal playback sample rate | Sensitivity and beat response remain useful; paired-device direction looks correct |
| Install over the current version | App starts as 1.3.7 and retains configuration/profiles |

VoiceMeeter was not running during this review. Its cache refresh before toggling was inspected, and notification routing/polling cadence were tested without the Remote API. Actual VM behavior, physical LEDs, and installer upgrade behavior remain unverified.
