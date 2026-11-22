using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MicCheck.Audio;
using MicCheck.Effects;

namespace MicCheck.Controls;

/// <summary>Effect editor with presets, live monitoring and a request to play the recorded clip through the effects.</summary>
public partial class VoiceEffectsPanel : UserControl
{
    private readonly List<EffectPreset> _userPresets = PresetStore.Load();
    private VoicePipeline _pipeline = null!;
    private int _sampleRate;
    private bool _refreshingPresets;
    private bool _applyingPreset;

    public VoiceEffectsPanel()
    {
        InitializeComponent();
    }

    /// <summary>Must be called once before use. <paramref name="presetName"/> reselects the preset the settings came from.</summary>
    public void Initialize(VoicePipeline pipeline, string? presetName)
    {
        _pipeline = pipeline;
        _pipeline.Monitor.Faulted += (_, ex) =>
        {
            LiveToggle.IsChecked = false;
            StatusText.Text = $"Live listening stopped{(ex != null ? $": {ex.Message}" : "")}.";
        };

        EffectsList.ItemsSource = Chain.Effects;
        RefreshPresets(presetName);

        foreach (var effect in Chain.Effects)
        {
            effect.PropertyChanged += (_, _) => OnEffectEdited();
            foreach (var parameter in effect.Parameters)
                parameter.PropertyChanged += (_, _) => OnEffectEdited();
        }
    }

    private VoiceEffectChain Chain => _pipeline.Chain;
    private AudioSink Live => _pipeline.Monitor;

    /// <summary>The preset the current settings came from, or null once they've been edited.</summary>
    public string? SelectedPresetName => (PresetCombo.SelectedItem as PresetItem)?.Preset.Name;

    public event EventHandler? PlayRecordingClicked;

    public bool CanPlayRecording { set => PlayRecordingButton.IsEnabled = value; }

    public bool IsPlayingRecording { set => PlayRecordingButton.Content = value ? "Stop" : "Play recording with effects"; }

    /// <summary>Tells the panel the current capture sample rate, or 0 when there's no working input.</summary>
    public void SetInput(int sampleRate)
    {
        _sampleRate = sampleRate;
        Live.Stop();
        if (sampleRate > 0 && LiveToggle.IsChecked == true && !StartLive())
            return;
        UpdateLiveStatus();
    }

    // ---- Live monitoring ----

    private void OnLiveToggled(object sender, RoutedEventArgs e)
    {
        if (LiveToggle.IsChecked == true && _sampleRate > 0)
        {
            if (!StartLive())
                return;
        }
        else
        {
            Live.Stop();
        }
        UpdateLiveStatus();
    }

    /// <summary>On failure, unchecks the toggle and leaves the error showing; callers must not overwrite the status.</summary>
    private bool StartLive()
    {
        try
        {
            Live.Start(_sampleRate, deviceId: null);
            return true;
        }
        catch (Exception ex)
        {
            LiveToggle.IsChecked = false;
            StatusText.Text = $"Couldn't open the speakers/headphones: {ex.Message}";
            return false;
        }
    }

    private void UpdateLiveStatus()
    {
        if (LiveToggle.IsChecked != true)
            StatusText.Text = "";
        else if (Live.IsRunning)
            StatusText.Text = $"Listening live on {Live.DeviceName}. Changes apply instantly. Expect a slight delay.";
        else
            StatusText.Text = "Live listening will start once a microphone is available.";
    }

    private void OnPlayRecordingClick(object sender, RoutedEventArgs e) =>
        PlayRecordingClicked?.Invoke(this, EventArgs.Empty);

    // ---- Presets ----

    private sealed record PresetItem(EffectPreset Preset, bool IsCustom)
    {
        public override string ToString() => IsCustom ? $"{Preset.Name}  (saved)" : Preset.Name;
    }

    private void RefreshPresets(string? select)
    {
        var items = EffectPreset.BuiltIn.Select(p => new PresetItem(p, false))
            .Concat(_userPresets.OrderBy(p => p.Name).Select(p => new PresetItem(p, true)))
            .ToList();

        _refreshingPresets = true;
        PresetCombo.ItemsSource = items;
        PresetCombo.SelectedItem = items.LastOrDefault(i => string.Equals(i.Preset.Name, select, StringComparison.OrdinalIgnoreCase));
        _refreshingPresets = false;
        DeletePresetButton.IsEnabled = (PresetCombo.SelectedItem as PresetItem)?.IsCustom == true;
    }

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = PresetCombo.SelectedItem as PresetItem;
        DeletePresetButton.IsEnabled = item?.IsCustom == true;
        if (_refreshingPresets || item == null)
            return;

        _applyingPreset = true;
        Chain.Apply(item.Preset);
        _applyingPreset = false;
        PresetNameBox.Text = item.IsCustom ? item.Preset.Name : "";
    }

    // Once the settings drift from the chosen preset, deselect it so picking it again re-applies it.
    private void OnEffectEdited()
    {
        if (_applyingPreset || _refreshingPresets || PresetCombo.SelectedItem == null)
            return;

        _refreshingPresets = true;
        PresetCombo.SelectedItem = null;
        _refreshingPresets = false;
    }

    private void OnPresetNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SavePreset();
    }

    private void OnSavePresetClick(object sender, RoutedEventArgs e) => SavePreset();

    private void SavePreset()
    {
        var name = PresetNameBox.Text.Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "Type a name for your preset first.";
            PresetNameBox.Focus();
            return;
        }
        if (EffectPreset.BuiltIn.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = $"\"{name}\" is a built-in preset. Pick a different name.";
            return;
        }

        _userPresets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _userPresets.Add(Chain.ToPreset(name));
        if (TryPersist())
            StatusText.Text = $"Saved \"{name}\".";
        RefreshPresets(name);
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not PresetItem { IsCustom: true } item)
            return;

        _userPresets.Remove(item.Preset);
        if (TryPersist())
            StatusText.Text = $"Deleted \"{item.Preset.Name}\".";
        PresetNameBox.Text = "";
        RefreshPresets(null);
    }

    private bool TryPersist()
    {
        try
        {
            PresetStore.Save(_userPresets);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Couldn't save presets: {ex.Message}";
            return false;
        }
    }
}
