using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MicCheck.Audio;
using MicCheck.Controls;
using MicCheck.Effects;
using NAudio.CoreAudioApi;

namespace MicCheck;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RecordLength = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeakHoldTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ClipHoldTime = TimeSpan.FromSeconds(2);
    private const double FallDbPerSecond = 24;
    private const float ClipThreshold = 0.99f;

    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MicrophoneMonitor _monitor = new();
    private readonly VoicePipeline _pipeline = new();
    private readonly ClipPlayer _player = new();
    private readonly TrayIcon _tray = new();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _reconnectTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private List<DeviceItem> _devices = [];
    private List<OutputItem> _outputs = [];
    private bool _loadingDevices;
    private MMDevice? _volumeDevice;
    private bool _syncingVolume;
    private bool _exiting;
    private string? _lastReconnectIds;

    public bool IsClosed { get; private set; }

    private TimeSpan _lastTick;
    private double _rmsDb = LevelMeter.MinDb;
    private double _peakDb = LevelMeter.MinDb;
    private double _holdDb = LevelMeter.MinDb;
    private TimeSpan _holdUntil;
    private TimeSpan _clipUntil;

    private float[]? _clip;
    private int _clipSampleRate;
    private bool? _playing;

    public MainWindow()
    {
        InitializeComponent();

        NoiseSuppressionCheck.IsChecked = _settings.NoiseFilter;
        VirtualMicCheck.IsChecked = _settings.VirtualMicEnabled;
        StartupCheck.IsChecked = StartupRegistration.IsEnabled;
        if (_settings.Effects != null)
            _pipeline.Chain.Apply(_settings.Effects);
        EffectsPanel.Initialize(_pipeline, _settings.Effects != null ? _settings.PresetName : EffectPreset.BuiltIn[0].Name);

        _monitor.Faulted += OnMonitorFaulted;
        _monitor.RecordingCompleted += OnRecordingCompleted;
        _monitor.SamplesCaptured += (_, samples) => _pipeline.Process(samples, _monitor.SampleRate);
        _pipeline.VirtualMic.Faulted += (_, ex) => UpdateVirtualMicStatus(ex);
        _player.Finished += (_, _) => SetPlaying(null);
        EffectsPanel.PlayRecordingClicked += OnPlayWithEffectsClick;

        _tray.OpenRequested += (_, _) => ShowFromTray();
        _tray.ExitRequested += (_, _) => ExitApp();
        _tray.VirtualMicToggled += (_, enabled) =>
        {
            VirtualMicCheck.IsChecked = enabled;
            ApplyVirtualMic();
            SaveSettings();
        };

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;
        _reconnectTimer = new DispatcherTimer { Interval = ReconnectInterval };
        _reconnectTimer.Tick += OnReconnectTick;

        Closing += OnClosing;
        Closed += (_, _) =>
        {
            IsClosed = true;
            Shutdown();
        };
        Deactivated += (_, _) => SaveSettings();
        Application.Current.SessionEnding += (_, _) => SaveSettings();

        LoadOutputs(_settings.VirtualMicDeviceId);
        LoadDevices(_settings.MicrophoneId);
        _timer.Start();
        _reconnectTimer.Start();
    }

    private sealed record DeviceItem(MMDevice Device, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record OutputItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    // ---- Tray ----

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    // While the virtual mic is on, closing the window keeps it running in the tray.
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        if (_exiting || VirtualMicCheck.IsChecked != true)
            return;

        e.Cancel = true;
        Hide();
        _tray.ShowStillRunningHint();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void SaveSettings()
    {
        _settings.NoiseFilter = NoiseSuppressionCheck.IsChecked == true;
        _settings.VirtualMicEnabled = VirtualMicCheck.IsChecked == true;
        _settings.PresetName = EffectsPanel.SelectedPresetName;
        _settings.Effects = _pipeline.Chain.ToPreset("Current");
        _settings.Save();
    }

    // Brings the mic and virtual mic back after a device is unplugged/replugged, e.g. while hidden in the tray.
    private void OnReconnectTick(object? sender, EventArgs e)
    {
        if (!_monitor.IsRunning)
        {
            // Reloading resets the UI (and stops clip playback), so while the window is visible only retry
            // when the set of microphones has actually changed. A mic that's present but won't open
            // (privacy settings, exclusive use) would otherwise be retried in a loop under the user.
            var activeIds = ActiveCaptureIds();
            if (IsVisible && activeIds == _lastReconnectIds)
                return;
            _lastReconnectIds = activeIds;

            var wanted = _settings.MicrophoneId;
            if (wanted == null || activeIds.Contains(wanted, StringComparison.Ordinal))
                LoadDevices(wanted);
        }
        else if (VirtualMicCheck.IsChecked == true && !_pipeline.VirtualMic.IsRunning
                 && VirtualMicCombo.SelectedItem is OutputItem)
        {
            ApplyVirtualMic();
        }
    }

    private string ActiveCaptureIds()
    {
        var ids = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d =>
            {
                using (d)
                    return d.ID;
            })
            .Order(StringComparer.Ordinal);
        return string.Join("|", ids);
    }

    // ---- Virtual microphone ----

    private const string CableInputName = "CABLE Input";

    private static bool IsVirtualCable(string name) => name.Contains(CableInputName, StringComparison.OrdinalIgnoreCase);

    private void LoadOutputs(string? preferredId)
    {
        _outputs = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d =>
            {
                using (d)
                    return new OutputItem(d.ID, d.FriendlyName);
            })
            .OrderByDescending(o => IsVirtualCable(o.Name))
            .ThenBy(o => o.Name)
            .ToList();

        _loadingDevices = true;
        VirtualMicCombo.ItemsSource = _outputs;
        // Never default to real speakers: only a saved choice or a virtual cable.
        VirtualMicCombo.SelectedItem = _outputs.FirstOrDefault(o => o.Id == preferredId)
            ?? _outputs.FirstOrDefault(o => IsVirtualCable(o.Name));
        _loadingDevices = false;
    }

    private void OnVirtualMicClick(object sender, RoutedEventArgs e)
    {
        ApplyVirtualMic();
        SaveSettings();
    }

    private void OnVirtualMicDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDevices)
            return;
        // Like the mic, only a choice the user makes is remembered, not an automatic fallback.
        _settings.VirtualMicDeviceId = (VirtualMicCombo.SelectedItem as OutputItem)?.Id;
        ApplyVirtualMic();
        SaveSettings();
    }

    private void ApplyVirtualMic()
    {
        _pipeline.VirtualMic.Stop();
        var output = VirtualMicCombo.SelectedItem as OutputItem;
        var sampleRate = _monitor.IsRunning ? _monitor.SampleRate : 0;

        if (VirtualMicCheck.IsChecked == true && output != null && sampleRate > 0)
        {
            try
            {
                _pipeline.VirtualMic.Start(sampleRate, output.Id);
            }
            catch (Exception ex)
            {
                UpdateVirtualMicStatus(ex);
                return;
            }
        }
        UpdateVirtualMicStatus(null);
    }

    private void UpdateVirtualMicStatus(Exception? error)
    {
        var output = VirtualMicCombo.SelectedItem as OutputItem;
        var running = _pipeline.VirtualMic.IsRunning;
        var cableInstalled = _outputs.Any(o => IsVirtualCable(o.Name));

        VirtualMicStatus.Text = (VirtualMicCheck.IsChecked == true, running) switch
        {
            (_, true) => $"On. In Discord, Zoom, Teams, OBS and so on, choose \"{RecordingSideName(output!.Name)}\" as the microphone.",
            (true, false) when error != null => $"Couldn't send to {output?.Name ?? "the device"}: {error.Message}. Retrying every few seconds.",
            (true, false) when output == null => cableInstalled
                ? "Choose the virtual cable to send your voice to."
                : "VB-CABLE isn't installed. Get it free from vb-audio.com/Cable, restart Windows, then press Refresh.",
            (true, false) => "Waiting for the microphone…",
            (false, _) when !cableInstalled =>
                "Off. To use your processed voice in other apps, install VB-CABLE (free, vb-audio.com/Cable), restart Windows, then press Refresh.",
            (false, _) => "Off. Other apps hear your microphone without Mic Check's effects.",
        };

        _tray.SetVirtualMicState(VirtualMicCheck.IsChecked == true,
            running ? $"Mic Check: virtual mic on ({EffectsPanel.SelectedPresetName ?? "custom effects"})" : "Mic Check");
    }

    // VB-CABLE's playback side is "CABLE Input"; apps record from its paired "CABLE Output".
    private static string RecordingSideName(string outputName) =>
        IsVirtualCable(outputName) ? "CABLE Output (VB-Audio Virtual Cable)" : $"the recording side of {outputName}";

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupRegistration.SetEnabled(StartupCheck.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            StartupCheck.IsChecked = StartupRegistration.IsEnabled;
            VirtualMicStatus.Text = $"Couldn't change the startup setting: {ex.Message}";
        }
    }

    // ---- Devices ----

    private void LoadDevices(string? preferredId)
    {
        StopMonitoring();
        foreach (var item in _devices)
            item.Device.Dispose();

        string? defaultId = null;
        try
        {
            if (_enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console))
            {
                using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                defaultId = def.ID;
            }
        }
        catch { /* no default device */ }

        _devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => new DeviceItem(d, d.ID == defaultId ? $"{d.FriendlyName}  (Windows default)" : d.FriendlyName))
            .OrderByDescending(d => d.Device.ID == defaultId)
            .ThenBy(d => d.Name)
            .ToList();

        DeviceCombo.ItemsSource = _devices;
        if (_devices.Count == 0)
        {
            FormatText.Text = "No active microphones found. Plug one in and press Refresh.";
            return;
        }

        _loadingDevices = true;
        DeviceCombo.SelectedItem = _devices.FirstOrDefault(d => d.Device.ID == preferredId) ?? _devices[0];
        _loadingDevices = false;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        LoadOutputs((VirtualMicCombo.SelectedItem as OutputItem)?.Id ?? _settings.VirtualMicDeviceId);
        LoadDevices((DeviceCombo.SelectedItem as DeviceItem)?.Device.ID);
    }

    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        // Only a choice the user makes is remembered; automatic fallbacks (saved mic missing) are not.
        if (!_loadingDevices && DeviceCombo.SelectedItem is DeviceItem item)
        {
            _settings.MicrophoneId = item.Device.ID;
            SaveSettings();
        }
        StartSelectedDevice();
    }

    private void OnNoiseSuppressionClick(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        StartSelectedDevice();
    }

    private void StartSelectedDevice()
    {
        StopMonitoring();
        if (DeviceCombo.SelectedItem is not DeviceItem item)
            return;

        try
        {
            var wantProcessing = NoiseSuppressionCheck.IsChecked == true;
            _monitor.Start(item.Device, wantProcessing);
            var f = _monitor.Format!;
            var kind = f.Encoding == NAudio.Wave.WaveFormatEncoding.IeeeFloat ? "float" : "PCM";
            var processing = (wantProcessing, _monitor.VoiceProcessingEnabled) switch
            {
                (false, _) => "raw signal",
                (true, true) => "noise filtering on",
                (true, false) => "noise filtering unavailable for this device",
            };
            FormatText.Text = $"Monitoring  ·  {f.SampleRate:N0} Hz  ·  {f.Channels} ch  ·  {f.BitsPerSample}-bit {kind}  ·  {processing}";
            RecordButton.IsEnabled = true;
            EffectsPanel.SetInput(f.SampleRate);
            ApplyVirtualMic();
        }
        catch (Exception ex)
        {
            FormatText.Text = $"Couldn't open this microphone: {ex.Message}";
            RecordButton.IsEnabled = false;
        }

        AttachVolume(item.Device);
    }

    private void StopMonitoring()
    {
        _monitor.Stop();
        _player.Stop();
        EffectsPanel.SetInput(0);
        ApplyVirtualMic();
        DetachVolume();
        ResetLevels();
        SetPlaying(null);
        ResetRecordingUi();
    }

    private void OnMonitorFaulted(object? sender, Exception? ex)
    {
        ResetLevels();
        ResetRecordingUi();
        EffectsPanel.SetInput(0);
        ApplyVirtualMic();
        RecordButton.IsEnabled = false;
        FormatText.Text = $"Microphone stopped{(ex != null ? $": {ex.Message}" : "")}. Reconnecting automatically when it's back.";
    }

    // ---- Windows input volume ----

    private void AttachVolume(MMDevice device)
    {
        try
        {
            device.AudioEndpointVolume.OnVolumeNotification += OnEndpointVolumeChanged;
            _volumeDevice = device;
            ShowVolume(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
            VolumeSlider.IsEnabled = MuteCheck.IsEnabled = true;
        }
        catch
        {
            VolumeSlider.IsEnabled = MuteCheck.IsEnabled = false;
            VolumeText.Text = "n/a";
        }
    }

    private void DetachVolume()
    {
        if (_volumeDevice == null)
            return;
        try { _volumeDevice.AudioEndpointVolume.OnVolumeNotification -= OnEndpointVolumeChanged; }
        catch { /* device already gone */ }
        _volumeDevice = null;
    }

    // Raised on a COM thread when the volume changes anywhere (this app, Windows settings, hardware keys).
    private void OnEndpointVolumeChanged(AudioVolumeNotificationData data) =>
        Dispatcher.InvokeAsync(() => ShowVolume(data.MasterVolume, data.Muted));

    private void ShowVolume(float scalar, bool muted)
    {
        _syncingVolume = true;
        VolumeSlider.Value = Math.Round(scalar * 100);
        MuteCheck.IsChecked = muted;
        _syncingVolume = false;
        VolumeText.Text = $"{Math.Round(scalar * 100)}%";
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VolumeText.Text = $"{Math.Round(e.NewValue)}%";
        if (_syncingVolume || _volumeDevice == null)
            return;
        try { _volumeDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100); }
        catch { /* device may have disappeared */ }
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (_volumeDevice == null)
            return;
        try { _volumeDevice.AudioEndpointVolume.Mute = MuteCheck.IsChecked == true; }
        catch { /* device may have disappeared */ }
    }

    // ---- Metering ----

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Nothing to draw while hidden in the tray.
        if (!_monitor.IsRunning || !IsVisible)
            return;

        var (peak, rms) = _monitor.ReadLevels();
        var peakDb = ToDb(peak);
        var rmsDb = ToDb(rms);
        var fall = FallDbPerSecond * dt;

        // Meter ballistics: jump up instantly, fall back smoothly.
        _rmsDb = Math.Max(rmsDb, _rmsDb - fall);
        _peakDb = Math.Max(peakDb, _peakDb - fall);

        if (peakDb >= _holdDb)
        {
            _holdDb = peakDb;
            _holdUntil = now + PeakHoldTime;
        }
        else if (now > _holdUntil)
        {
            _holdDb = Math.Max(LevelMeter.MinDb, _holdDb - fall);
        }

        if (peak >= ClipThreshold)
            _clipUntil = now + ClipHoldTime;
        var clipping = now < _clipUntil;

        Meter.RmsDb = _rmsDb;
        Meter.PeakDb = _peakDb;
        Meter.PeakHoldDb = _holdDb;
        History.Push(rmsDb);

        RmsText.Text = FormatDb(_rmsDb);
        PeakText.Text = FormatDb(_holdDb);
        ClipBadge.Visibility = clipping ? Visibility.Visible : Visibility.Hidden;
        UpdateStatus(clipping);

        if (_monitor.IsRecording)
            RecordProgress.Value = _monitor.RecordingProgress;
    }

    // Judged on the held peak so the verdict doesn't flicker between syllables.
    private void UpdateStatus(bool clipping)
    {
        var (text, hint, brushKey) = (clipping, _holdDb) switch
        {
            (true, _) => ("Clipping", "Your voice is distorting. Lower the input volume or move back from the mic.", "SystemFillColorCriticalBrush"),
            (_, < -55) => ("No signal", "Nothing is coming through. Check you've picked the right mic and that it isn't muted.", "SystemFillColorCriticalBrush"),
            (_, < -30) => ("Very quiet", "There's a signal, but it's faint. Speak up, move closer or raise the input volume.", "SystemFillColorCautionBrush"),
            (_, < -18) => ("A bit quiet", "Usable, but raising the input volume a little would help.", "SystemFillColorCautionBrush"),
            (_, < -3) => ("Good level", "Normal speech peaks between -18 and -3 dB. You're in the sweet spot.", "SystemFillColorSuccessBrush"),
            _ => ("Loud", "Close to clipping. Consider lowering the input volume a little.", "SystemFillColorCautionBrush"),
        };

        StatusText.Text = text;
        StatusHint.Text = hint;
        StatusText.Foreground = TryFindResource(brushKey) as Brush ?? Brushes.Gray;
    }

    private void ResetLevels()
    {
        _rmsDb = _peakDb = _holdDb = LevelMeter.MinDb;
        _clipUntil = TimeSpan.Zero;
        Meter.RmsDb = Meter.PeakDb = Meter.PeakHoldDb = LevelMeter.MinDb;
        History.Clear();
        RmsText.Text = PeakText.Text = StatusText.Text = "—";
        StatusHint.Text = "";
        ClipBadge.Visibility = Visibility.Hidden;
    }

    private static double ToDb(float amplitude) =>
        amplitude <= 1e-6f ? LevelMeter.MinDb : Math.Max(LevelMeter.MinDb, 20 * Math.Log10(amplitude));

    private static string FormatDb(double db) => db <= LevelMeter.MinDb ? "-∞ dB" : $"{db:0.0} dB";

    // ---- Record & play back ----

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        SetPlaying(null);
        PlayButton.IsEnabled = false;
        EffectsPanel.CanPlayRecording = false;
        RecordButton.IsEnabled = false;
        RecordButton.Content = "Recording…";
        RecordStatus.Text = "Speak now";
        RecordProgress.Value = 0;
        _clipSampleRate = _monitor.SampleRate;
        _monitor.BeginRecording(RecordLength);
    }

    // Capture stopping discards an unfinished recording; the previous clip, if any, stays playable.
    private void ResetRecordingUi()
    {
        RecordButton.Content = "Record 5 s";
        RecordProgress.Value = 0;
        RecordStatus.Text = "";
        PlayButton.IsEnabled = EffectsPanel.CanPlayRecording = _clip != null;
    }

    // Raised on the capture thread.
    private void OnRecordingCompleted(object? sender, float[] samples) => Dispatcher.InvokeAsync(() =>
    {
        _clip = samples;
        RecordButton.Content = "Record 5 s";
        RecordButton.IsEnabled = true;
        PlayButton.IsEnabled = true;
        EffectsPanel.CanPlayRecording = true;
        RecordProgress.Value = 1;
        RecordStatus.Text = $"Recorded {samples.Length / (double)_clipSampleRate:0.0} s";
    });

    private void OnPlayClick(object sender, RoutedEventArgs e) => TogglePlayback(withEffects: false);

    private void OnPlayWithEffectsClick(object? sender, EventArgs e) => TogglePlayback(withEffects: true);

    // Pressing the button for what's currently playing stops it; otherwise switch to that version.
    private void TogglePlayback(bool withEffects)
    {
        var wasPlaying = _playing;
        _player.Stop();
        SetPlaying(null);
        if (wasPlaying == withEffects || _clip == null)
            return;

        try
        {
            var samples = withEffects ? _pipeline.Chain.Render(_clip, _clipSampleRate) : _clip;
            _player.Play(samples, _clipSampleRate);
            SetPlaying(withEffects);
        }
        catch (Exception ex)
        {
            RecordStatus.Text = $"Playback failed: {ex.Message}";
        }
    }

    /// <summary>null = nothing playing; otherwise whether the effects version is playing.</summary>
    private void SetPlaying(bool? withEffects)
    {
        _playing = withEffects;
        PlayButton.Content = withEffects == false ? "Stop" : "Play back";
        EffectsPanel.IsPlayingRecording = withEffects == true;
    }

    private void Shutdown()
    {
        _timer.Stop();
        _reconnectTimer.Stop();
        DetachVolume();
        _monitor.Dispose();
        _pipeline.Dispose();
        _player.Dispose();
        _tray.Dispose();
        foreach (var item in _devices)
            item.Device.Dispose();
        _enumerator.Dispose();
        Application.Current.Shutdown();
    }
}
