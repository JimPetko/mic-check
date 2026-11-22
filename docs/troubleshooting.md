# Troubleshooting

- [The meter shows "No signal"](#the-meter-shows-no-signal)
- ["Couldn't open this microphone"](#couldnt-open-this-microphone)
- ["Noise filtering unavailable for this device"](#noise-filtering-unavailable-for-this-device)
- [The noise filter doesn't remove typing or clicks](#the-noise-filter-doesnt-remove-typing-or-clicks)
- [Music and other sounds get quieter while Mic Check runs](#music-and-other-sounds-get-quieter-while-mic-check-runs)
- [Howling or echo when using Hear myself live](#howling-or-echo-when-using-hear-myself-live)
- [VB-CABLE isn't found](#vb-cable-isnt-found)
- [Other apps hear nothing through CABLE Output](#other-apps-hear-nothing-through-cable-output)
- [My voice sounds choppy or robotic in other apps](#my-voice-sounds-choppy-or-robotic-in-other-apps)
- [Bluetooth headset sounds bad or switches modes](#bluetooth-headset-sounds-bad-or-switches-modes)
- [Mic Check doesn't start with Windows](#mic-check-doesnt-start-with-windows)
- [The install script fails](#the-install-script-fails)
- [Mic Check won't open a second window](#mic-check-wont-open-a-second-window)
- [Resetting everything](#resetting-everything)

---

## The meter shows "No signal"

Work through these in order:

1. **Speak.** With **Filter background noise** on, Windows' noise suppression can output complete digital silence between words, so -∞ dB while you're quiet is normal. The meter should jump when you talk.
2. **Check the selected microphone.** The list shows every active recording device; make sure it's the one you're speaking into.
3. **Check Muted and Input volume** at the top left. These are the real Windows settings for the device.
4. **Check Windows privacy settings:** Settings → Privacy & security → Microphone. Both **Microphone access** and **Let desktop apps access your microphone** must be on.
5. **Turn off Filter background noise** to rule out the device's voice processing. Some drivers' communications processing misbehaves.
6. **Check for a hardware mute** switch or button on the mic or headset.
7. Press **Refresh**.

## "Couldn't open this microphone"

- Another app may be using the mic in **exclusive mode**. Close audio software such as DAWs or other mic tools, or turn off exclusive mode: Control Panel → Sound → Recording → the device → Properties → Advanced → untick **Allow applications to take exclusive control**.
- The device may have been unplugged or disabled. Plug it back in and press **Refresh**. Mic Check also retries on its own when the set of microphones changes.
- Very unusual capture formats (anything other than 16-, 24- or 32-bit PCM or 32-bit float) aren't supported. Set a standard format under the device's Properties → Advanced → **Default Format**.

## "Noise filtering unavailable for this device"

Mic Check couldn't ask Windows to treat the stream as a voice call. The mic still works; you're hearing it unfiltered. This can happen with unusual drivers, or if a future NAudio version changes its internals (see [architecture.md](architecture.md)). Other apps that use their own noise suppression are unaffected.

## The noise filter doesn't remove typing or clicks

- Voice Clarity is a **Windows 11** feature and has been rolling out gradually. On Windows 10, or on Windows 11 builds without it, filtering depends on whether your mic's driver provides its own noise suppression, and many USB mics don't.
- Check Windows settings: Settings → System → Sound → your microphone → **Audio enhancements**, and make sure Voice Clarity or "Microsoft Windows Studio Effects" isn't switched off there.
- Compare by watching the **Last 10 seconds** graph while typing, with the filter on and then off.
- Move the mic closer to your mouth and further from the keyboard, and lower the input volume. That improves the ratio of voice to noise for any suppressor.

## Music and other sounds get quieter while Mic Check runs

Because Mic Check's stream is marked as a voice call (for noise filtering), Windows "ducks" other sounds as it does during calls. Fix it in **Control Panel → Sound → Communications** → **Do nothing**. Alternatively, turn off **Filter background noise**.

## Howling or echo when using Hear myself live

Your speakers are feeding back into the mic. Use headphones, or turn off **Hear myself live**. The same applies to **Play back** and **Play recording with effects**.

## VB-CABLE isn't found

The status line says "VB-CABLE isn't installed" when no output device has "CABLE Input" in its name.

1. Install it: `powershell -ExecutionPolicy Bypass -File scripts\Install-VBCable.ps1` (see [virtual-microphone.md](virtual-microphone.md)).
2. **Restart Windows.**
3. Press **Refresh** in Mic Check.
4. Still missing? Check Control Panel → Sound → Playback for **CABLE Input**. If it's there but disabled, right-click → Enable.

## Other apps hear nothing through CABLE Output

1. Is **Send my processed voice to** ticked, with **CABLE Input** selected? The status line should start with "On."
2. Is Mic Check still running? Look for the microphone icon in the system tray. Other apps only hear you while it runs.
3. Is the other app's **microphone** set to **CABLE Output (VB-Audio Virtual Cable)**? Not CABLE Input, and not your real mic.
4. Does the Mic Check meter move when you speak? If not, fix the mic first (see [No signal](#the-meter-shows-no-signal)).
5. Check the levels in Control Panel → Sound → Recording → **CABLE Output** → Properties → Levels, and on the Playback tab for **CABLE Input**. Neither should be muted or at 0.
6. To hear what other apps get: Control Panel → Sound → Recording → CABLE Output → Properties → **Listen** → tick **Listen to this device**. Remember to untick it afterwards.

## My voice sounds choppy or robotic in other apps

(When you haven't picked the Robot preset.)

- **Two noise suppressors:** turn off the other app's noise suppression (Discord Krisp, Zoom background noise suppression, and so on) while Mic Check's filter is on.
- **Automatic gain control** in the other app can pump your level up and down; turn it off.
- **CPU load:** very heavy system load can cause dropouts. Reverb is the most expensive effect.
- **Sample-rate mismatches** are handled automatically, but setting your mic and CABLE Input to the same default format (for example 48,000 Hz) under each device's Properties → Advanced avoids resampling.

## Bluetooth headset sounds bad or switches modes

Bluetooth headsets (AirPods and similar) have two modes. When any app opens the headset's **microphone**, Windows switches it to the "Hands-Free" mode, which has low-quality 16 kHz audio in both directions. This is a Bluetooth limitation, not a Mic Check problem. Options:

- Use a wired or USB microphone as the input, and keep the Bluetooth headset for listening only.
- Accept the lower quality while using the headset mic.

## Mic Check doesn't start with Windows

- "Start with Windows" stores the path of the program you ran when you ticked the box. If you've moved the app, rebuilt it to a different folder, or deleted the build, untick and re-tick the box.
- Check Settings → Apps → Startup: **Mic Check / MicCheck** must be **On**.
- It starts hidden. Look for the tray icon (you may need to expand the hidden-icons arrow).

## The install script fails

| Message | Cause and fix |
| --- | --- |
| "running scripts is disabled on this system" | Run it with `powershell -ExecutionPolicy Bypass -File scripts\Install-VBCable.ps1`. |
| "Downloaded file doesn't match the expected SHA-256" | VB-Audio replaced the package. Check [vb-audio.com/Cable](https://vb-audio.com/Cable/), then update `$PackUrl` and `$PackSha256` in the script. |
| "isn't validly signed by VB-Audio" | The download is corrupt or not genuine. Don't install it; try again later, or download manually from the official site. |
| Installer finished but no CABLE devices | Restart Windows. If they're still missing, run the script with `-Interactive -Force` and use the setup window. |
| UAC prompt didn't appear / was declined | The script needs administrator rights to install a driver; run it again and approve. |

## Mic Check won't open a second window

Only one copy of Mic Check runs at a time, because two copies would fight over the mic and the cable. Launching it again shows the existing window. If it's hidden in the tray, the launch brings it back.

## Resetting everything

1. Exit Mic Check (tray icon → **Exit**).
2. Delete the folder `%AppData%\MicCheck`. This removes settings and saved presets.
3. To remove the startup entry, untick **Start with Windows** before exiting, or delete the `MicCheck` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.
