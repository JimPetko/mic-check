# Voice effects

Mic Check has seven effects. Each one can be switched on and off and has its own settings. They always run in this order:

```
Volume → Pitch shift → Distortion → Telephone filter → Robot → Echo → Reverb → output limiter
```

The order matters. For example, the telephone filter comes after distortion, so distortion sounds like it's coming through a cheap speaker, and echo and reverb come last, so they repeat the fully processed voice.

The final **output limiter** keeps every sample between -1 and +1 (0 dBFS), so no combination of settings can produce a dangerously loud signal in your headphones or in other apps. Pushed too far, it will sound clipped rather than louder.

When an effect is switched on, its internal state (echo buffer, reverb tail and so on) starts fresh, so you never hear a stale tail from the last time it was on.

- [Volume](#volume)
- [Pitch shift](#pitch-shift)
- [Distortion](#distortion)
- [Telephone filter](#telephone-filter)
- [Robot (ring modulator)](#robot-ring-modulator)
- [Echo](#echo)
- [Reverb](#reverb)
- [Built-in presets](#built-in-presets)
- [Saved presets](#saved-presets)

---

## Volume

Boosts or cuts the level before everything else. It's useful for quiet microphones, or to drive the distortion harder.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Gain | -12 to +24 dB | +6 dB | 0.5 dB steps. +6 dB doubles the amplitude. |

## Pitch shift

Raises your voice for a chipmunk sound, or lowers it for a deep one, without changing its speed.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Pitch | -12 to +12 semitones | +5 | 0.5-semitone steps. 12 semitones is one octave. |

How it works: your voice goes into a 50 ms delay line that two read heads sweep through at the shifted speed. The two heads are crossfaded so the jump when each wraps around is hidden. This is cheap and low-latency, and accurate to within a few percent of the target pitch, but extreme settings have a slightly "warbly" character, which is typical of this kind of shifter.

## Distortion

Overdrives the signal for a gritty, saturated sound.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Drive | 0 to 40 dB | 20 dB | How hard the signal is pushed into saturation. |
| Mix | 0–100% | 100% | Blend of distorted and clean voice. |

How it works: the signal is boosted by the drive amount and passed through a smooth (tanh) soft-clipping curve. The output is partly turned back down, so more drive mostly means more grit, not just more volume. Even at 0 dB drive the curve adds a little gentle saturation; to bypass distortion completely, switch it off.

## Telephone filter

Removes low and high frequencies, keeping only the middle of the voice, like a phone line, radio or megaphone.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Low cut | 20 to 2000 Hz | 400 Hz | Frequencies below this are removed. |
| High cut | 1000 to 12000 Hz | 3000 Hz | Frequencies above this are removed. |

How it works: two high-pass and two low-pass filters in series, giving steep cut-offs. If your mic records at a low sample rate (Bluetooth headsets often use 16 kHz), the high cut is automatically limited to 45% of the sample rate so the filter stays stable. At 16 kHz that means anything above 7,200 Hz behaves the same.

The two ranges overlap, so it's possible to set **Low cut** above **High cut**. That removes almost everything and leaves near-silence; keep Low cut below High cut.

## Robot (ring modulator)

Multiplies your voice by a sine wave. Low frequencies give the classic metallic robot sound; higher ones sound alien or bell-like.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Frequency | 20 to 1000 Hz | 60 Hz | 5 Hz steps. 30–80 Hz is "robot"; 300+ Hz is "alien". |
| Mix | 0–100% | 100% | Blend of modulated and clean voice. |

## Echo

Repeats of your voice that fade away.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Delay | 20 to 1000 ms | 300 ms | Time between repeats. Very short delays (20–40 ms) sound metallic rather than like an echo. |
| Repeats | 0–90% | 40% | How much of each repeat is fed back. Higher means more, longer-lasting repeats. |
| Mix | 0–100% | 40% | How loud the repeats are relative to your voice. |

## Reverb

Puts your voice in a room, from a small booth to a cathedral.

| Setting | Range | Default | Notes |
| --- | --- | --- | --- |
| Room size | 0–100% | 70% | Bigger rooms have longer tails. |
| Damping | 0–100% | 50% | How quickly high frequencies die away. More damping sounds softer and more furnished; less sounds brighter and stonier. |
| Mix | 0–100% | 30% | Amount of reverb. Your dry voice is reduced by up to half at 100%. |

How it works: a "Freeverb"-style design, with eight damped feedback delays in parallel followed by four all-pass diffusers, tuned for any sample rate.

---

## Built-in presets

Selecting a preset switches on only the effects listed and resets every setting not listed to its default. For example, Chipmunk sets the pitch to +9 and switches every other effect off.

| Preset | Effects |
| --- | --- |
| **No effects** | Everything off. Your voice passes through unchanged, apart from the noise filter if that's on. |
| **Chipmunk** | Pitch +9 semitones |
| **Deep voice** | Pitch -6 semitones · Volume +3 dB |
| **Robot** | Robot 60 Hz, 100% · Distortion 8 dB drive, 50% mix · Echo 30 ms, 40% repeats, 40% mix |
| **Telephone** | Telephone filter 400–3000 Hz · Distortion 14 dB drive, 50% mix · Volume +6 dB |
| **Megaphone** | Telephone filter 700–2500 Hz · Distortion 28 dB drive, 100% mix · Volume +8 dB |
| **Cave** | Echo 350 ms, 45% repeats, 35% mix · Reverb room 90%, damping 30%, mix 45% |
| **Stadium announcer** | Echo 140 ms, 30% repeats, 25% mix · Reverb room 85%, damping 50%, mix 30% · Volume +4 dB |
| **Alien** | Pitch +4 semitones · Robot 420 Hz, 50% · Reverb room 60%, damping 20%, mix 25% |

A good way to make your own: start from the closest preset, adjust, then save it under a new name.

## Saved presets

Presets you save are stored in `%AppData%\MicCheck\voice-presets.json`. The file is plain JSON and can be backed up, shared or edited by hand:

```json
[
  {
    "Name": "Radio host",
    "Effects": {
      "bandpass": { "Enabled": true, "Values": { "lowCut": 120, "highCut": 7000 } },
      "reverb":   { "Enabled": true, "Values": { "roomSize": 0.4, "damping": 0.6, "mix": 0.15 } },
      "gain":     { "Enabled": true, "Values": { "gain": 4 } }
    }
  }
]
```

Mic Check saves every effect and setting, including switched-off ones. When writing a preset by hand you can leave things out: missing effects are switched off, missing settings use their defaults, and unknown names are ignored. Values outside an effect's range are clamped to it. Percentages are written as fractions (0.4 = 40%). Entries with missing or `null` names or effects are skipped when the file loads, rather than crashing the app.

Effect and setting names:

| Effect | Key | Settings |
| --- | --- | --- |
| Volume | `gain` | `gain` (dB) |
| Pitch shift | `pitch` | `semitones` |
| Distortion | `distortion` | `drive` (dB), `mix` (0–1) |
| Telephone filter | `bandpass` | `lowCut` (Hz), `highCut` (Hz) |
| Robot | `ring` | `frequency` (Hz), `mix` (0–1) |
| Echo | `echo` | `delay` (ms), `feedback` (0–0.9), `mix` (0–1) |
| Reverb | `reverb` | `roomSize` (0–1), `damping` (0–1), `mix` (0–1) |

Mic Check reads the file when it starts, so restart it after editing by hand.
