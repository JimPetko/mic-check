using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MicCheck.Audio;
using MicCheck.Controls;
using NAudio.CoreAudioApi;

namespace MicCheck;

public partial class MainWindow : Window
{
    private static readonly TimeSpan RecordLength = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeakHoldTime = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ClipHoldTime = TimeSpan.FromSeconds(2);
    private const double FallDbPerSecond = 24;
    private const float ClipThreshold = 0.99f;

    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MicrophoneMonitor _monitor = new();
    private readonly ClipPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private List<DeviceItem> _devices = [];
    private MMDevice? _volumeDevice;
    private bool _syncingVolume;

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

        _monitor.Faulted += OnMonitorFaulted;
        _monitor.RecordingCompleted += OnRecordingCompleted;
        _monitor.SamplesCaptured += (_, samples) => EffectsPanel.Feed(samples);
        _player.Finished += (_, _) => SetPlaying(null);
        EffectsPanel.PlayRecordingClicked += OnPlayWithEffectsClick;

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;

        Loaded += (_, _) =>
        {
            LoadDevices(preferredId: null);
            _timer.Start();
        };
        Closed += (_, _) => Shutdown();
    }

    private sealed record DeviceItem(MMDevice Device, string Name)
    {
        public override string ToString() => Name;
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

        DeviceCombo.SelectedItem = _devices.FirstOrDefault(d => d.Device.ID == preferredId) ?? _devices[0];
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) =>
        LoadDevices((DeviceCombo.SelectedItem as DeviceItem)?.Device.ID);

    private void OnDeviceChanged(object sender, SelectionChangedEventArgs e) => StartSelectedDevice();

    private void OnNoiseSuppressionClick(object sender, RoutedEventArgs e) => StartSelectedDevice();

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
        RecordButton.IsEnabled = false;
        FormatText.Text = $"Microphone stopped{(ex != null ? $": {ex.Message}" : "")}. Press Refresh to reconnect.";
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

        if (!_monitor.IsRunning)
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
            var samples = withEffects ? EffectsPanel.Chain.Render(_clip, _clipSampleRate) : _clip;
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
        DetachVolume();
        EffectsPanel.Shutdown();
        _monitor.Dispose();
        _player.Dispose();
        foreach (var item in _devices)
            item.Device.Dispose();
        _enumerator.Dispose();
    }
}
