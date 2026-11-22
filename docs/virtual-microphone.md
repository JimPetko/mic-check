# Using your processed voice in other apps

Windows doesn't let one app change what other apps hear from a real microphone. That would need a signed kernel audio driver. Mic Check instead uses a **virtual audio cable**, a pair of virtual devices where anything played into one end comes out the other end as a microphone:

```mermaid
flowchart LR
    Mic["Your microphone"] --> MC["Mic Check<br/>noise filter + voice effects"]
    MC --> In["CABLE Input<br/>(virtual speaker)"]
    In -. VB-CABLE .-> Out["CABLE Output<br/>(virtual microphone)"]
    Out --> Apps["Discord · Zoom · Teams · OBS · …"]
```

Mic Check is built around [VB-CABLE](https://vb-audio.com/Cable/), a free (donationware) virtual cable by VB-Audio Software. Any similar product works too, because you can pick any output device in Mic Check.

- [1. Install VB-CABLE](#1-install-vb-cable)
- [2. Turn on sending in Mic Check](#2-turn-on-sending-in-mic-check)
- [3. Choose CABLE Output in your apps](#3-choose-cable-output-in-your-apps)
- [4. Keep it running](#4-keep-it-running)
- [What other apps hear](#what-other-apps-hear)
- [Latency](#latency)
- [Uninstalling](#uninstalling)

---

## 1. Install VB-CABLE

### With the included script (recommended)

From the repository folder:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Install-VBCable.ps1
```

The script:

1. Checks whether a **CABLE Input** device already exists, and stops if it does. Use `-Force` to reinstall.
2. Asks for administrator rights (a UAC prompt), because it's installing a driver.
3. Downloads `VBCABLE_Driver_Pack45.zip` from `download.vb-audio.com` into a fresh temporary folder.
4. Checks the download's **SHA-256 hash** against the one recorded in the script, and checks that the installer has a **valid code signature from VB-Audio's developer (Vincent Burel)**. If either check fails, it stops without running anything.
5. Runs the 64-bit installer silently (`-i -h`). Windows will still ask whether to install the device software; approve it. Drivers can't be installed without this prompt.
6. Deletes the temporary files and tells you what to do next.

Options:

| Option | Use |
| --- | --- |
| `-Interactive` | Open VB-CABLE's own setup window instead of installing silently. Use this if the silent install doesn't work, since the silent flags are community-documented rather than official. |
| `-Force` | Reinstall even if VB-CABLE is already present. |

If VB-Audio publishes a newer driver pack, the hash check fails on purpose. Update `$PackUrl` and `$PackSha256` at the top of the script after checking the new package on [vb-audio.com/Cable](https://vb-audio.com/Cable/).

### Manually

Download the driver pack from [vb-audio.com/Cable](https://vb-audio.com/Cable/), extract it, right-click `VBCABLE_Setup_x64.exe` → **Run as administrator**, and press **Install Driver**.

### Then restart Windows

The CABLE devices usually only appear after a restart.

## 2. Turn on sending in Mic Check

1. Open Mic Check and press **Refresh** (top left).
2. In **Use in other apps**, the device list should show **CABLE Input (VB-Audio Virtual Cable)**. It's selected automatically.
3. Tick **Send my processed voice to**.
4. The status line should say: *On. In Discord, Zoom, Teams, OBS and so on, choose "CABLE Output (VB-Audio Virtual Cable)" as the microphone.*

Set up the sound you want first: noise filter on or off, and a voice effect preset in the right-hand panel. Use **Hear myself live** with headphones to hear exactly what other apps will get.

## 3. Choose CABLE Output in your apps

In each app, change the **microphone / input device** to **CABLE Output (VB-Audio Virtual Cable)**. Leave the app's **speaker / output** set to your real headphones or speakers.

| App | Where to change the microphone |
| --- | --- |
| Discord | User Settings → Voice & Video → Input Device |
| Zoom | Settings → Audio → Microphone |
| Microsoft Teams | Settings → Devices → Microphone (or the device menu before joining a call) |
| OBS Studio | Settings → Audio → Mic/Auxiliary Audio, or add an *Audio Input Capture* source using CABLE Output |
| Google Meet / browsers | The microphone selector in the meeting's settings, or the browser's site permissions |
| Windows (all apps) | Settings → System → Sound → Input → choose CABLE Output to make it the default microphone |

Recommended app settings:

- **Turn off the app's own noise suppression** (Discord's Krisp, Zoom's "Suppress background noise", and so on) if you're using Mic Check's noise filter. Running two suppressors can make your voice sound choppy.
- **Turn off automatic gain / "automatically adjust microphone volume"** if your effects sound pumped or inconsistent.
- **Don't** choose CABLE Input as an app's speaker. Anything played into CABLE Input is mixed into the virtual microphone.

## 4. Keep it running

Other apps only hear your voice while Mic Check is running.

- **Close to tray:** while **Send my processed voice to** is ticked, closing the window hides Mic Check in the system tray instead of quitting. A notification tells you the first time each session.
- **Tray menu:** right-click the microphone icon to reopen the window, toggle **Send voice to virtual mic**, or **Exit**.
- **Start with Windows:** tick **Start with Windows (in the system tray)** so Mic Check starts hidden in the tray when you sign in, with your last mic, noise filter and effects.
- **Automatic reconnect:** if your mic or the cable is unplugged, disabled, or briefly disappears (for example a Bluetooth headset reconnecting), Mic Check retries every 5 seconds and resumes on its own.

"Start with Windows" records the path of the Mic Check program you're running. If you move or republish the app, untick and re-tick the box so it points to the new location.

## What other apps hear

Other apps get exactly what **Hear myself live** plays:

1. Your microphone, through Windows' noise filtering if **Filter background noise** is on.
2. The voice effects, in their [fixed order](voice-effects.md), including any changes you make while it's running.
3. A limiter that keeps the level from going above 0 dBFS.

The **Input volume** slider (Windows' level for your real mic) affects everything. Your app's own input volume for CABLE Output adjusts only what that app hears.

To send a clean, noise-filtered voice with no effects, pick the **No effects** preset.

## Latency

Routing through Mic Check adds roughly **60–100 ms** of delay: about 20 ms of capture buffering, up to about 40 ms of output buffering, plus VB-CABLE's own buffering. That's fine for calls, meetings and streaming. Mic Check deliberately drops any backlog over 150 ms rather than letting the delay grow.

If you hear yourself through **Hear myself live** at the same time, expect a similar delay there.

## Uninstalling

1. In Mic Check, untick **Start with Windows**, then right-click the tray icon → **Exit**.
2. Switch your apps back to your real microphone.
3. To remove VB-CABLE, run `VBCABLE_Setup_x64.exe` from the driver pack as administrator and press **Remove Driver**, then restart.
4. To remove Mic Check's settings, delete `%AppData%\MicCheck`.
