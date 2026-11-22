using System.ComponentModel;
using System.Globalization;

namespace MicCheck.Effects;

/// <summary>A user-adjustable effect setting. Written by the UI, read by the audio thread.</summary>
public sealed class EffectParameter(string key, string label, double minimum, double maximum, double defaultValue, double step, string format)
    : INotifyPropertyChanged
{
    private double _value = defaultValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = key;
    public string Label { get; } = label;
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
    public double Default { get; } = defaultValue;
    public double Step { get; } = step;

    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, Minimum, Maximum);
            if (clamped == _value)
                return;
            _value = clamped;
            PropertyChanged?.Invoke(this, new(nameof(Value)));
            PropertyChanged?.Invoke(this, new(nameof(DisplayValue)));
        }
    }

    public string DisplayValue => string.Format(CultureInfo.CurrentCulture, format, _value);
}

/// <summary>
/// Base for a mono effect. Process runs on the audio thread; Enabled and parameters are set from the UI.
/// State is (re)initialised on the audio thread whenever the effect is switched on or the sample rate changes.
/// </summary>
public abstract class VoiceEffect(string key, string name, string description) : INotifyPropertyChanged
{
    private readonly List<EffectParameter> _parameters = [];
    private volatile bool _enabled;
    private volatile bool _resetPending = true;
    private int _sampleRate;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; } = key;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public IReadOnlyList<EffectParameter> Parameters => _parameters;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value == _enabled)
                return;
            if (value)
                _resetPending = true;
            _enabled = value;
            PropertyChanged?.Invoke(this, new(nameof(Enabled)));
        }
    }

    public void Process(Span<float> samples, int sampleRate)
    {
        if (!_enabled)
            return;

        if (_resetPending || sampleRate != _sampleRate)
        {
            _resetPending = false;
            _sampleRate = sampleRate;
            Reset(sampleRate);
        }

        ProcessCore(samples, sampleRate);
    }

    public void RestoreDefaults()
    {
        foreach (var p in _parameters)
            p.Value = p.Default;
    }

    protected EffectParameter AddParameter(string key, string label, double minimum, double maximum, double defaultValue, double step, string format)
    {
        var parameter = new EffectParameter(key, label, minimum, maximum, defaultValue, step, format);
        _parameters.Add(parameter);
        return parameter;
    }

    protected virtual void Reset(int sampleRate) { }

    protected abstract void ProcessCore(Span<float> samples, int sampleRate);

    protected static float DbToGain(double db) => (float)Math.Pow(10, db / 20);

    // Feedback paths decaying on pure digital silence reach denormal floats, which are very slow on x64.
    protected static float FlushDenormal(float x) => MathF.Abs(x) < 1e-15f ? 0f : x;
}
