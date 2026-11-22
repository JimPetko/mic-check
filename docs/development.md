# Development guide

How to build, run, test, extend and publish Mic Check. For how the pieces fit together (audio path, threading, DSP), read [architecture.md](architecture.md) first.

Related docs: [README](../README.md) · [user guide](user-guide.md) · [voice effects](voice-effects.md) · [virtual microphone](virtual-microphone.md) · [troubleshooting](troubleshooting.md)

## Prerequisites

- **Windows 10 or 11.** The app is WPF, uses WASAPI through NAudio, and the test project also targets `net10.0-windows`, so neither builds nor runs elsewhere.
- **.NET 10 SDK** (or newer). The projects target `net10.0-windows`. A .NET 11 SDK also builds and tests them without changes; this repo has been built with SDK 11.0.100.
- Optional: Visual Studio 2022 17.13 or later, or Rider. Any editor works with the `dotnet` CLI. The solution file is the XML-based `MicCheck.slnx`, which needs a recent Visual Studio or SDK.
- Optional, to test the virtual microphone: [VB-CABLE](https://vb-audio.com/Cable/). `scripts\Install-VBCable.ps1` downloads the official driver pack, checks its SHA-256 and the installer's Authenticode signer, and runs it elevated (`-Interactive` shows VB-CABLE's own setup window, `-Force` reinstalls). A restart is required afterwards. See [virtual-microphone.md](virtual-microphone.md).

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Install-VBCable.ps1
```

The only package dependency of the app is `NAudio` 2.2.1, pinned on purpose (see [Upgrading NAudio](#upgrading-naudio)).

## Clone, build, run

```powershell
git clone <repo-url> mic-check
cd mic-check
dotnet build MicCheck.slnx
dotnet run --project src/MicCheck
```

Start it the way Windows does at sign-in (hidden, tray icon only):

```powershell
dotnet run --project src/MicCheck -- --tray
```

Things that catch people out:

- **Only one instance runs.** If Mic Check is already running (check the tray, it may be hidden there with the virtual mic on), a new launch, including `dotnet run`, just brings the existing window to the front and exits. You will be looking at the old build. Exit the running copy from its tray menu first.
- **The running app locks `src\MicCheck\bin`.** Building while it runs fails to overwrite `MicCheck.exe`/`MicCheck.dll`. Either exit it, or build somewhere else with `--artifacts-path` (next section).
- Closing the window only exits the app when "Send my processed voice to" is off; otherwise it hides to the tray.

## Project layout

```text
mic-check/
├── MicCheck.slnx                          Solution: src/MicCheck and tests/MicCheck.Tests
├── .gitignore
├── docs/                                  These docs
├── scripts/
│   └── Install-VBCable.ps1                Verified download + elevated install of VB-CABLE
├── src/MicCheck/
│   ├── MicCheck.csproj                    WinExe, net10.0-windows, WPF (+WinForms for NotifyIcon only), NAudio 2.2.1
│   ├── App.xaml                           Fluent ThemeMode="System", OnExplicitShutdown, shared Card/SectionTitle/Caption styles
│   ├── App.xaml.cs                        Single-instance mutex + show event, --tray handling
│   ├── AssemblyInfo.cs                    WPF ThemeInfo attribute (template boilerplate)
│   ├── AppSettings.cs                     settings.json load/save; StartupRegistration (HKCU Run key)
│   ├── MainWindow.xaml                    Left column: device, virtual mic, live level, history, record & play back
│   ├── MainWindow.xaml.cs                 Composition root: devices, volume, metering, recording, virtual mic, tray, reconnect
│   ├── TrayIcon.cs                        NotifyIcon with a runtime-drawn icon and Open / virtual mic / Exit menu
│   ├── Audio/
│   │   ├── MicrophoneMonitor.cs           WasapiCapture, decode to mono, level stats, 5 s recording
│   │   ├── CommunicationsStream.cs        IAudioClient2::SetClientProperties(Communications) via reflection
│   │   ├── VoicePipeline.cs               Runs the effect chain once and feeds both sinks
│   │   ├── AudioSink.cs                   Mono float → WasapiOut with resampling and channel fan-out
│   │   └── ClipPlayer.cs                  WaveOutEvent playback of a recorded clip
│   ├── Effects/
│   │   ├── VoiceEffect.cs                 EffectParameter and the VoiceEffect base class
│   │   ├── BasicEffects.cs                GainEffect, DistortionEffect, BandPassEffect, RingModulatorEffect
│   │   ├── TimeEffects.cs                 PitchShiftEffect, EchoEffect, ReverbEffect
│   │   ├── VoiceEffectChain.cs            Fixed effect order, output clamp, Render, ToPreset/Apply
│   │   └── EffectPreset.cs                Preset records, built-in presets, Sanitize, PresetStore (voice-presets.json)
│   └── Controls/
│       ├── LevelMeter.cs                  dBFS bar meter drawn in OnRender
│       ├── LevelHistory.cs                10-second scrolling level graph
│       ├── VoiceEffectsPanel.xaml         Right column: presets, live toggle, data-templated effect cards
│       └── VoiceEffectsPanel.xaml.cs      Preset select/save/delete, live monitoring sink
└── tests/MicCheck.Tests/
    ├── MicCheck.Tests.csproj              xUnit 2.9, references the app project
    ├── Signal.cs                          Test helpers: sine generator, zero-crossing frequency, peak
    ├── EffectParameterTests.cs            Clamping and change notifications
    ├── PresetSerializationTests.cs        JSON round trip of preset lists
    └── VoiceEffectChainTests.cs           Chain/effect DSP behaviour and Apply/ToPreset semantics
```

## Running the tests

```powershell
dotnet test MicCheck.slnx
```

If Mic Check is running, its exe and DLLs in `src\MicCheck\bin` are locked and the build step of `dotnet test` fails. Put all build output in a throwaway folder instead:

```powershell
dotnet test MicCheck.slnx --artifacts-path "$env:TEMP\miccheck-artifacts"
```

`--artifacts-path` moves both `bin` and `obj` for every project under that folder, so it never touches the copy you are running. The same flag works for `dotnet build` and `dotnet publish`.

The suite is pure in-memory DSP and serialization; it opens no audio devices, so it runs on a machine with no sound hardware (and in CI on a Windows runner). There are 36 test cases:

| Test | What it checks |
| --- | --- |
| `EffectParameterTests.ValueIsClampedAndRaisesChangeNotifications` | `Value` clamps to min/max, raises `Value` then `DisplayValue`, and raises nothing when the clamped value is unchanged. |
| `PresetSerializationTests.PresetListRoundTripsThroughJson` | A list of a user preset plus all built-ins survives `System.Text.Json` serialize/deserialize unchanged (the `voice-presets.json` format). |
| `VoiceEffectChainTests.BuiltInPresetRendersFiniteBoundedOutput` | Every built-in preset at 16 kHz and 48 kHz renders a 2 s sine to finite samples within [-1, 1]. New built-in presets are picked up automatically. |
| `RenderAppendsTailOnlyForEchoOrReverb` | `Render` adds exactly 1.5 s only when echo or reverb is enabled. |
| `RenderLeavesInputAndLiveChainUntouched` | `Render` doesn't modify its input array or the live chain's state. |
| `PitchShiftMovesFrequency` | -12, -5, +5, +12 semitones move a 220 Hz sine to within ±10 % of the target (zero-crossing count). |
| `NoEffectsIsIdentity` | With everything off, output equals input exactly. |
| `GainBoostsAndOutputIsClamped` | +6 dB doubles amplitude; the output clamp caps a loud signal at 1.0. |
| `BandPassAtLowSampleRateWithHighCutAboveNyquistStaysStable` | High cut 12 kHz at 16 kHz is capped below Nyquist and stays stable. |
| `ApplyToPresetRoundTrips` | `ToPreset` → `Apply` reproduces enabled flags and values. |
| `ApplyDisablesMissingEffectsRestoresDefaultsAndIgnoresUnknownKeys` | `Apply` disables and defaults effects missing from a preset, keeps defaults for unspecified parameters, and ignores unknown effect/parameter keys. |

Not covered by tests: capture/output, `CommunicationsStream`, the UI, settings file I/O and `Sanitize`, the registry. Check those by running the app.

## Adding a new effect

The UI, presets and settings are all driven by the effect's keys and parameter list, so a new effect is mostly one class.

1. **Subclass `VoiceEffect`** in `Effects/BasicEffects.cs` (stateless or filter-style effects) or `Effects/TimeEffects.cs` (delay-based effects). Make it `sealed`. Pass a short lowercase `key`, the display `name`, and a one-sentence `description` to the base constructor.

2. **Declare parameters with `AddParameter`** in the constructor and keep the returned `EffectParameter` in a readonly field:

   ```csharp
   public sealed class TremoloEffect : VoiceEffect
   {
       private readonly EffectParameter _rate;
       private readonly EffectParameter _depth;
       private double _phase;

       public TremoloEffect() : base("tremolo", "Tremolo", "Pulses the volume up and down.")
       {
           _rate = AddParameter("rate", "Rate", 1, 20, 5, 0.5, "{0:0.0} Hz");
           _depth = AddParameter("depth", "Depth", 0, 1, 0.5, 0.05, "{0:P0}");
       }

       protected override void Reset(int sampleRate) => _phase = 0;

       protected override void ProcessCore(Span<float> samples, int sampleRate)
       {
           var increment = 2 * Math.PI * _rate.Value / sampleRate;
           var depth = (float)_depth.Value;
           for (var i = 0; i < samples.Length; i++)
           {
               samples[i] *= 1 - depth * 0.5f * (1 - (float)Math.Cos(_phase));
               _phase += increment;
               if (_phase > 2 * Math.PI)
                   _phase -= 2 * Math.PI;
           }
       }
   }
   ```

   Arguments are `(key, label, minimum, maximum, default, step, format)`. The slider snaps to `step`; `format` is a composite format string for `DisplayValue` (existing effects use `"{0:0} Hz"`, `"{0:0} ms"`, `"{0:P0}"`, `"{0:+0.0;-0.0;0} dB"`). Parameter keys are what presets and `settings.json` store: once shipped, don't rename them, or saved presets silently fall back to the default for that parameter.

3. **Respect the threading rules** (see [architecture.md, Threading model](architecture.md#threading-model)):
   - Allocate buffers and build filters in `Reset(sampleRate)`, never from the UI thread. `Reset` runs on the audio thread when the effect is switched on and whenever the sample rate changes.
   - Read each parameter's `Value` once per block at the top of `ProcessCore`; don't cache derived values across blocks unless you recompute them when the parameter changes (see `BandPassEffect`'s `_appliedLowCut`).
   - Use `FlushDenormal` on anything written back into a feedback path, and `DbToGain` for dB parameters.
   - Size everything from `sampleRate`; the app sees anything from 16 kHz headsets to 48 kHz (or higher) interfaces. Avoid per-block allocations: `ProcessCore` runs every ~20 ms.

4. **Add it to the chain.** Insert an instance into the `_effects` array in `Effects/VoiceEffectChain.cs`. The array position is both the processing order and the order of the cards in the UI. Think about where it belongs: level and pitch changes first, nonlinear and filtering stages in the middle, time-based effects (echo, reverb) last so their tails carry everything before them.

5. **If it produces a tail** (output continues after the input stops), add it to the `hasTail` check in `VoiceEffectChain.Render`, which currently only knows `EchoEffect` and `ReverbEffect`. Otherwise "Play recording with effects" cuts the tail off.

6. **Use it in presets** if you want: built-in presets reference the effect key and parameter keys, e.g. `("tremolo", [("rate", 6), ("depth", 0.7)])`. See the next section.

7. **Add tests** in `tests/MicCheck.Tests/VoiceEffectChainTests.cs`. `ChainWith("tremolo", ("rate", 6))` builds a chain with only your effect enabled; `Signal.Sine`, `Signal.Peak` and `Signal.Frequency` cover most assertions. At minimum test finite, bounded output at 16 kHz and 48 kHz, and one property that proves the effect does what it says. If you added it to a built-in preset, `BuiltInPresetRendersFiniteBoundedOutput` already covers it.

No XAML changes are needed: `VoiceEffectsPanel` generates the card, checkbox and sliders from `Chain.Effects`. Also update [voice-effects.md](voice-effects.md) with the new effect and its parameters.

## Adding a built-in preset

Add a `Create(...)` entry to `EffectPreset.BuiltIn` in `Effects/EffectPreset.cs`:

```csharp
Create("Walkie-talkie",
    ("bandpass", [("lowCut", 600), ("highCut", 2800)]),
    ("distortion", [("drive", 18), ("mix", 0.6)]),
    ("gain", [("gain", 5)])),
```

- Each tuple is `(effectKey, [(parameterKey, value), ...])`. Listed effects are enabled; every other effect is switched off when the preset is applied, and unlisted parameters of a listed effect use their defaults.
- The order within the preset doesn't matter; the chain order does.
- Values are clamped to the parameter range on apply, so a typo'd out-of-range value fails quietly. A typo'd key is ignored entirely. Double-check keys against the table in [architecture.md](architecture.md#processing-order).
- Names must be unique (case-insensitive). Users can't save a preset under a built-in name, so adding a built-in with the same name as someone's saved preset makes theirs win in the combo (last match).
- Keep `"No effects"` first: `BuiltIn[0]` is what a fresh install selects.
- The list order is the order in the preset combo, before the user's saved presets.
- `BuiltInPresetRendersFiniteBoundedOutput` tests the new preset automatically. If it uses echo or reverb you can also add an `InlineData` to `RenderAppendsTailOnlyForEchoOrReverb`. Document it in [voice-effects.md](voice-effects.md).

## Coding conventions

Observed throughout the repo; please follow them:

- **C# style**: file-scoped namespaces (`namespace MicCheck.Audio;`), `Nullable` and `ImplicitUsings` enabled, `sealed` classes by default, records for data (`EffectPreset`, `DeviceItem`), primary constructors (`EffectParameter`, `VoiceEffect`, the reverb's `Comb`/`AllPass`), collection expressions (`[]`, `[.. x]`), `var`, `_camelCase` private fields, PascalCase constants and static readonly fields, exception filters (`catch (Exception ex) when (ex is IOException or ...)`).
- **Comments are terse and explain why**, not what: e.g. "MMDevice.AudioClient creates a new client on every access, so dispose this one." XML doc comments are short one-liners and state which thread raises an event or calls a method ("Raised on the UI thread...", "Called on the capture thread."). Keep doing that for anything crossing threads.
- **Code-behind, not MVVM.** `MainWindow.xaml.cs` and `VoiceEffectsPanel.xaml.cs` handle events directly and set control properties. Data binding is used only where it pays: the effect cards bind to `VoiceEffect` / `EffectParameter`, which implement `INotifyPropertyChanged`. Don't introduce a view-model layer for a single feature.
- **Theme**: WPF's Fluent theme via `ThemeMode="System"` on the `Application`, so the app follows Windows light/dark mode. Use `DynamicResource` with the Fluent brush keys (`CardBackgroundFillColorDefaultBrush`, `TextFillColorSecondaryBrush`, `SystemFillColorCriticalBrush`, `AccentFillColorDefaultBrush`, `AccentButtonStyle`, ...) rather than hard-coded colours, so a theme switch updates live. Reuse the `Card`, `SectionTitle` and `Caption` styles from `App.xaml`. The level meter's green-to-red gradient is the one intentional exception.
- **WinForms only for the tray.** `UseWindowsForms` exists for `NotifyIcon`; `System.Windows.Forms` and `System.Drawing` are removed from implicit usings in the csproj so `Application`, `UserControl` and friends resolve to WPF. Import them explicitly only in `TrayIcon.cs`.
- **Fail soft on device and file errors.** Audio device failures show a message in the relevant status line and are retried where it makes sense; settings save failures are swallowed. Don't throw dialogs at the user.
- **User-facing strings** are plain sentences, with exact UI labels in quotes.

## Debugging tips

- **Inspect the saved state.** Settings and presets are plain JSON:

  ```powershell
  Get-Content "$env:APPDATA\MicCheck\settings.json"
  Get-Content "$env:APPDATA\MicCheck\voice-presets.json"
  ```

  Remember `settings.json` is written when the window loses focus or closes, so click away from the window before reading it. Hand edits are safe-ish: malformed JSON resets to defaults, null entries are dropped by `EffectPreset.Sanitize`, values are clamped. Edit with the app exited, or it will overwrite your changes on its next save.
- **Reset to a clean first run**: exit Mic Check (tray → Exit), then delete the folder:

  ```powershell
  Remove-Item -Recurse "$env:APPDATA\MicCheck"
  ```

  This removes settings and saved presets. It doesn't touch the "Start with Windows" entry.
- **Check or remove the startup entry**:

  ```powershell
  reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v MicCheck
  reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v MicCheck /f
  ```

  If you tick "Start with Windows" from a dev build, it points at `src\MicCheck\bin\...\MicCheck.exe`. Untick it before you clean or move that folder.
- **Is Windows voice processing on?** The format line under the mic picker ends with "noise filtering on", "noise filtering unavailable for this device" or "raw signal". "Unavailable" usually means the device's driver rejected the communications category, or (after an NAudio upgrade) the reflected fields no longer exist; set a breakpoint in `CommunicationsStream.TryEnable`.
- **Is audio reaching the virtual cable?** Open Windows Sound settings → Input → "CABLE Output" and watch its level meter while you talk with the virtual mic on. If it moves there, any problem is in the receiving app. More in [troubleshooting.md](troubleshooting.md).
- **Audio-thread code**: breakpoints in `ProcessCore` or `OnDataAvailable` stall the capture thread; when you resume, `AudioSink.Write` drops the backlog (over 150 ms) and you'll hear a gap. That's expected. Prefer `Debug.WriteLine` or conditional breakpoints there.
- **Device faults**: unplugging the mic should show "Microphone stopped... Reconnecting automatically" and recover within ~5 s of plugging it back in. While the window is visible, recovery only retries when the set of active mics changes; press **Refresh** to force it.
- **Second instance does nothing?** That's the single-instance guard signalling the first instance. Look for the tray icon.

## Publishing a single-file exe

A self-contained, single-file build runs on any Windows 10/11 x64 PC without the .NET runtime installed:

```powershell
dotnet publish src/MicCheck -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

What we verified with the .NET 11 SDK (targeting `net10.0-windows`):

| Command | Output |
| --- | --- |
| `... -p:PublishSingleFile=true` only | `MicCheck.exe` (~165 MB) **plus** five native WPF DLLs next to it: `D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll`. Not really single-file: the exe needs those DLLs beside it. |
| `+ -p:IncludeNativeLibrariesForSelfExtract=true` | `MicCheck.exe` only (~174 MB), plus `MicCheck.pdb`. The native DLLs are bundled and extracted on first run. |
| `+ -p:EnableCompressionInSingleFile=true` | `MicCheck.exe` only, ~76 MB. Slightly slower start-up while it decompresses. |

All three publish successfully. The published exe's start-up was not exercised as part of that check (a copy of Mic Check was already running, and a second instance only signals the first), so smoke-test it on a clean machine before distributing. The `.pdb` is optional; ship just the `.exe`. `-r win-arm64` should work for ARM64 PCs (NAudio is fully managed) but has not been tried. Add `--artifacts-path <temp dir>` if the app is running from your build folder.

Moving the exe: the "Start with Windows" entry stores the exe's full path (`"<path>\MicCheck.exe" --tray`) and the checkbox only checks that the entry exists. After copying or moving the published exe to its final location, run it from there, untick and re-tick "Start with Windows" so the Run key points at the new path. Settings in `%AppData%\MicCheck` are per user, not per exe, so they carry over.

## Upgrading NAudio

`CommunicationsStream` reads NAudio's private fields `WasapiCapture.audioClient` and `AudioClient.audioClientInterface` by reflection to call `IAudioClient2::SetClientProperties`. Before bumping the `NAudio` version in `MicCheck.csproj`:

1. Check those two field names still exist with the same types in the new version (or that NAudio now exposes a public way to set the audio category, in which case delete the reflection).
2. Run the app with "Filter background noise" ticked and confirm the format line says "noise filtering on" on a device that supported it before.
3. Re-check that `WasapiCapture.Dispose` still joins the capture thread, and that `RecordingStopped`/`PlaybackStopped` are still posted to the constructing thread's `SynchronizationContext`; the threading design relies on both (see [architecture.md](architecture.md#threading-model)).
