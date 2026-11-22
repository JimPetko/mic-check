using NAudio.Dsp;

namespace MicCheck.Effects;

public sealed class GainEffect : VoiceEffect
{
    private readonly EffectParameter _gain;

    public GainEffect() : base("gain", "Volume", "Boost or cut the level before the other effects.")
    {
        _gain = AddParameter("gain", "Gain", -12, 24, 6, 0.5, "{0:+0.0;-0.0;0} dB");
    }

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        var gain = DbToGain(_gain.Value);
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }
}

/// <summary>Soft-clipping overdrive. Output is partly compensated so more drive isn't just louder.</summary>
public sealed class DistortionEffect : VoiceEffect
{
    private readonly EffectParameter _drive;
    private readonly EffectParameter _mix;

    public DistortionEffect() : base("distortion", "Distortion", "Overdrives the signal for a gritty, saturated sound.")
    {
        _drive = AddParameter("drive", "Drive", 0, 40, 20, 1, "{0:0} dB");
        _mix = AddParameter("mix", "Mix", 0, 1, 1, 0.05, "{0:P0}");
    }

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        var pre = DbToGain(_drive.Value);
        var post = 1 / MathF.Sqrt(pre);
        var mix = (float)_mix.Value;
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            var y = MathF.Tanh(x * pre) * post;
            samples[i] = x + mix * (y - x);
        }
    }
}

/// <summary>Steep high-pass + low-pass pair: telephone, radio and megaphone sounds.</summary>
public sealed class BandPassEffect : VoiceEffect
{
    private const float Q = 0.707f;
    private readonly EffectParameter _lowCut;
    private readonly EffectParameter _highCut;
    private readonly BiQuadFilter[] _highPass = new BiQuadFilter[2];
    private readonly BiQuadFilter[] _lowPass = new BiQuadFilter[2];
    private double _appliedLowCut = double.NaN;
    private double _appliedHighCut = double.NaN;

    public BandPassEffect() : base("bandpass", "Telephone filter", "Removes lows and highs, keeping only the middle of the voice.")
    {
        _lowCut = AddParameter("lowCut", "Low cut", 20, 2000, 400, 10, "{0:0} Hz");
        _highCut = AddParameter("highCut", "High cut", 1000, 12000, 3000, 100, "{0:0} Hz");
    }

    protected override void Reset(int sampleRate)
    {
        for (var i = 0; i < 2; i++)
        {
            _highPass[i] = BiQuadFilter.HighPassFilter(sampleRate, (float)_lowCut.Value, Q);
            _lowPass[i] = BiQuadFilter.LowPassFilter(sampleRate, HighCutFor(sampleRate), Q);
        }
        _appliedLowCut = _lowCut.Value;
        _appliedHighCut = _highCut.Value;
    }

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        if (_lowCut.Value != _appliedLowCut)
        {
            _appliedLowCut = _lowCut.Value;
            foreach (var f in _highPass)
                f.SetHighPassFilter(sampleRate, (float)_appliedLowCut, Q);
        }
        if (_highCut.Value != _appliedHighCut)
        {
            _appliedHighCut = _highCut.Value;
            foreach (var f in _lowPass)
                f.SetLowPassFilter(sampleRate, HighCutFor(sampleRate), Q);
        }

        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            x = _highPass[1].Transform(_highPass[0].Transform(x));
            samples[i] = _lowPass[1].Transform(_lowPass[0].Transform(x));
        }
    }

    // Headsets often capture at 16 kHz, so keep the cutoff safely below Nyquist.
    private float HighCutFor(int sampleRate) => (float)Math.Min(_highCut.Value, sampleRate * 0.45);
}

/// <summary>Multiplies the voice by a sine wave: the classic metallic robot sound.</summary>
public sealed class RingModulatorEffect : VoiceEffect
{
    private readonly EffectParameter _frequency;
    private readonly EffectParameter _mix;
    private double _phase;

    public RingModulatorEffect() : base("ring", "Robot (ring modulator)", "Low frequencies sound robotic, high ones sound alien.")
    {
        _frequency = AddParameter("frequency", "Frequency", 20, 1000, 60, 5, "{0:0} Hz");
        _mix = AddParameter("mix", "Mix", 0, 1, 1, 0.05, "{0:P0}");
    }

    protected override void Reset(int sampleRate) => _phase = 0;

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        var increment = 2 * Math.PI * _frequency.Value / sampleRate;
        var mix = (float)_mix.Value;
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            samples[i] = x + mix * (x * (float)Math.Sin(_phase) - x);
            _phase += increment;
            if (_phase > 2 * Math.PI)
                _phase -= 2 * Math.PI;
        }
    }
}
