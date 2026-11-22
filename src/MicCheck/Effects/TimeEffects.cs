namespace MicCheck.Effects;

/// <summary>
/// Delay-line pitch shifter: two read taps sweep through a short window at the shifted rate
/// and are crossfaded with sin² weights (which always sum to 1) to hide the wrap-around.
/// </summary>
public sealed class PitchShiftEffect : VoiceEffect
{
    private const double WindowSeconds = 0.05;
    private readonly EffectParameter _semitones;
    private float[] _buffer = [];
    private int _window;
    private int _write;
    private double _phase;

    public PitchShiftEffect() : base("pitch", "Pitch shift", "Raise for a chipmunk voice, lower for a deep one.")
    {
        _semitones = AddParameter("semitones", "Pitch", -12, 12, 5, 0.5, "{0:+0.0;-0.0;0} semitones");
    }

    protected override void Reset(int sampleRate)
    {
        _window = (int)(sampleRate * WindowSeconds);
        _buffer = new float[_window + 4];
        _write = 0;
        _phase = 0;
    }

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        var ratio = Math.Pow(2, _semitones.Value / 12);
        var step = (1 - ratio) / _window;

        for (var i = 0; i < samples.Length; i++)
        {
            _buffer[_write] = samples[i];

            _phase += step;
            _phase -= Math.Floor(_phase);
            var other = _phase + 0.5;
            other -= Math.Floor(other);

            samples[i] = Tap(_phase) * Weight(_phase) + Tap(other) * Weight(other);
            _write = (_write + 1) % _buffer.Length;
        }
    }

    private float Tap(double phase)
    {
        var length = _buffer.Length;
        var read = _write - phase * _window;
        if (read < 0)
            read += length;
        var index = (int)read;
        var frac = (float)(read - index);
        return _buffer[index % length] * (1 - frac) + _buffer[(index + 1) % length] * frac;
    }

    private static float Weight(double phase)
    {
        var s = Math.Sin(Math.PI * phase);
        return (float)(s * s);
    }
}

public sealed class EchoEffect : VoiceEffect
{
    private const double MaxDelaySeconds = 1.0;
    private readonly EffectParameter _delay;
    private readonly EffectParameter _feedback;
    private readonly EffectParameter _mix;
    private float[] _buffer = [];
    private int _write;

    public EchoEffect() : base("echo", "Echo", "Repeats of your voice that fade away.")
    {
        _delay = AddParameter("delay", "Delay", 20, 1000, 300, 10, "{0:0} ms");
        _feedback = AddParameter("feedback", "Repeats", 0, 0.9, 0.4, 0.05, "{0:P0}");
        _mix = AddParameter("mix", "Mix", 0, 1, 0.4, 0.05, "{0:P0}");
    }

    protected override void Reset(int sampleRate)
    {
        _buffer = new float[(int)(sampleRate * MaxDelaySeconds) + 1];
        _write = 0;
    }

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        var length = _buffer.Length;
        var delay = Math.Clamp((int)(_delay.Value * sampleRate / 1000), 1, length - 1);
        var feedback = (float)_feedback.Value;
        var mix = (float)_mix.Value;

        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            var delayed = _buffer[(_write - delay + length) % length];
            _buffer[_write] = FlushDenormal(x + feedback * delayed);
            samples[i] = x + mix * delayed;
            _write = (_write + 1) % length;
        }
    }
}

/// <summary>Freeverb-style reverb: parallel damped comb filters followed by series all-pass filters.</summary>
public sealed class ReverbEffect : VoiceEffect
{
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllPassTunings = [556, 441, 341, 225];
    private const float InputGain = 0.015f;
    private const float WetScale = 3f;

    private readonly EffectParameter _roomSize;
    private readonly EffectParameter _damping;
    private readonly EffectParameter _mix;
    private Comb[] _combs = [];
    private AllPass[] _allPasses = [];

    public ReverbEffect() : base("reverb", "Reverb", "Puts your voice in a room, from a small booth to a cathedral.")
    {
        _roomSize = AddParameter("roomSize", "Room size", 0, 1, 0.7, 0.05, "{0:P0}");
        _damping = AddParameter("damping", "Damping", 0, 1, 0.5, 0.05, "{0:P0}");
        _mix = AddParameter("mix", "Mix", 0, 1, 0.3, 0.05, "{0:P0}");
    }

    protected override void Reset(int sampleRate)
    {
        // Tunings are in samples at 44.1 kHz.
        var scale = sampleRate / 44100.0;
        _combs = CombTunings.Select(t => new Comb((int)(t * scale))).ToArray();
        _allPasses = AllPassTunings.Select(t => new AllPass((int)(t * scale))).ToArray();
    }

    protected override void ProcessCore(Span<float> samples, int sampleRate)
    {
        var feedback = (float)(_roomSize.Value * 0.28 + 0.7);
        var damp = (float)(_damping.Value * 0.4);
        var mix = (float)_mix.Value;

        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            var input = x * InputGain;
            float wet = 0;
            foreach (var comb in _combs)
                wet += comb.Process(input, feedback, damp);
            foreach (var allPass in _allPasses)
                wet = allPass.Process(wet);

            samples[i] = x * (1 - mix / 2) + wet * WetScale * mix;
        }
    }

    private sealed class Comb(int length)
    {
        private readonly float[] _buffer = new float[Math.Max(1, length)];
        private int _index;
        private float _store;

        public float Process(float input, float feedback, float damp)
        {
            var output = _buffer[_index];
            _store = FlushDenormal(output * (1 - damp) + _store * damp);
            _buffer[_index] = input + _store * feedback;
            _index = (_index + 1) % _buffer.Length;
            return output;
        }
    }

    private sealed class AllPass(int length)
    {
        private readonly float[] _buffer = new float[Math.Max(1, length)];
        private int _index;

        public float Process(float input)
        {
            var buffered = _buffer[_index];
            _buffer[_index] = FlushDenormal(input + buffered * 0.5f);
            _index = (_index + 1) % _buffer.Length;
            return buffered - input;
        }
    }
}
