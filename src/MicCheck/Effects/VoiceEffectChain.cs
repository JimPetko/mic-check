namespace MicCheck.Effects;

/// <summary>All available effects in their fixed processing order.</summary>
public sealed class VoiceEffectChain
{
    private static readonly TimeSpan RenderTail = TimeSpan.FromSeconds(1.5);

    private readonly VoiceEffect[] _effects =
    [
        new GainEffect(),
        new PitchShiftEffect(),
        new DistortionEffect(),
        new BandPassEffect(),
        new RingModulatorEffect(),
        new EchoEffect(),
        new ReverbEffect(),
    ];

    public IReadOnlyList<VoiceEffect> Effects => _effects;

    public void Process(Span<float> samples, int sampleRate)
    {
        foreach (var effect in _effects)
            effect.Process(samples, sampleRate);

        // Hard ceiling so a runaway setting can't blast your ears.
        for (var i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i], -1f, 1f);
    }

    /// <summary>Processes a whole clip on a separate copy of the chain, leaving room for echo/reverb tails.</summary>
    public float[] Render(float[] input, int sampleRate)
    {
        var chain = new VoiceEffectChain();
        chain.Apply(ToPreset(""));

        var hasTail = chain._effects.Any(e => e.Enabled && e is EchoEffect or ReverbEffect);
        var output = new float[input.Length + (hasTail ? (int)(sampleRate * RenderTail.TotalSeconds) : 0)];
        input.CopyTo(output, 0);
        chain.Process(output, sampleRate);
        return output;
    }

    public EffectPreset ToPreset(string name) => new(name, _effects.ToDictionary(
        e => e.Key,
        e => new EffectPresetEntry(e.Enabled, e.Parameters.ToDictionary(p => p.Key, p => p.Value))));

    /// <summary>Effects missing from the preset are switched off and reset to defaults.</summary>
    public void Apply(EffectPreset preset)
    {
        foreach (var effect in _effects)
        {
            effect.RestoreDefaults();
            if (!preset.Effects.TryGetValue(effect.Key, out var entry))
            {
                effect.Enabled = false;
                continue;
            }

            foreach (var parameter in effect.Parameters)
            {
                if (entry.Values.TryGetValue(parameter.Key, out var value))
                    parameter.Value = value;
            }
            effect.Enabled = entry.Enabled;
        }
    }
}
