using MicCheck.Effects;

namespace MicCheck.Tests;

public class VoiceEffectChainTests
{
    public static TheoryData<string, int> PresetsAndRates()
    {
        var data = new TheoryData<string, int>();
        foreach (var preset in EffectPreset.BuiltIn)
        {
            data.Add(preset.Name, 16000);
            data.Add(preset.Name, 48000);
        }
        return data;
    }

    private static VoiceEffectChain ChainFor(string presetName)
    {
        var chain = new VoiceEffectChain();
        chain.Apply(EffectPreset.BuiltIn.Single(p => p.Name == presetName));
        return chain;
    }

    private static VoiceEffectChain ChainWith(string effect, params (string Key, double Value)[] values)
    {
        var chain = new VoiceEffectChain();
        chain.Apply(new("test", new() { [effect] = new(true, values.ToDictionary(v => v.Key, v => v.Value)) }));
        return chain;
    }

    private static T Effect<T>(VoiceEffectChain chain) where T : VoiceEffect => chain.Effects.OfType<T>().Single();

    private static double Param(VoiceEffect effect, string key) => effect.Parameters.Single(p => p.Key == key).Value;

    [Theory]
    [MemberData(nameof(PresetsAndRates))]
    public void BuiltInPresetRendersFiniteBoundedOutput(string presetName, int sampleRate)
    {
        var output = ChainFor(presetName).Render(Signal.Sine(220, sampleRate), sampleRate);

        Assert.All(output, s => Assert.True(float.IsFinite(s) && s is >= -1 and <= 1, $"sample {s}"));
    }

    [Theory]
    [InlineData("No effects", false)]
    [InlineData("Chipmunk", false)]
    [InlineData("Telephone", false)]
    [InlineData("Robot", true)]
    [InlineData("Cave", true)]
    [InlineData("Alien", true)]
    public void RenderAppendsTailOnlyForEchoOrReverb(string presetName, bool expectTail)
    {
        var input = Signal.Sine(220, 16000);

        var output = ChainFor(presetName).Render(input, 16000);

        Assert.Equal(input.Length + (expectTail ? 24000 : 0), output.Length);
    }

    [Fact]
    public void RenderLeavesInputAndLiveChainUntouched()
    {
        const int rate = 16000;
        var chain = ChainFor("Cave");
        var reference = ChainFor("Cave");
        var block = Signal.Sine(220, rate, 0.5);
        chain.Process(block.ToArray(), rate);
        reference.Process(block.ToArray(), rate);

        var input = Signal.Sine(330, rate);
        var copy = input.ToArray();
        chain.Render(input, rate);
        Assert.Equal(copy, input);

        var next = block.ToArray();
        var expected = block.ToArray();
        chain.Process(next, rate);
        reference.Process(expected, rate);
        Assert.Equal(expected, next);
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(-5)]
    [InlineData(5)]
    [InlineData(12)]
    public void PitchShiftMovesFrequency(double semitones)
    {
        const int rate = 48000;
        var samples = Signal.Sine(220, rate);

        ChainWith("pitch", ("semitones", semitones)).Process(samples, rate);

        var expected = 220 * Math.Pow(2, semitones / 12);
        var measured = Signal.Frequency(samples.AsSpan(samples.Length / 2), rate);
        Assert.InRange(measured, expected * 0.9, expected * 1.1);
    }

    [Fact]
    public void NoEffectsIsIdentity()
    {
        var input = Signal.Sine(220, 48000);

        var output = ChainFor("No effects").Render(input, 48000);

        Assert.Equal(input, output);
    }

    [Fact]
    public void GainBoostsAndOutputIsClamped()
    {
        const int rate = 16000;
        var chain = ChainWith("gain", ("gain", 6));

        var quiet = Signal.Sine(220, rate, 0.5, 0.25f);
        chain.Process(quiet, rate);
        Assert.InRange(Signal.Peak(quiet), 0.48f, 0.52f);

        var loud = Signal.Sine(220, rate, 0.5, 0.9f);
        chain.Process(loud, rate);
        Assert.Equal(1f, Signal.Peak(loud));
    }

    [Fact]
    public void BandPassAtLowSampleRateWithHighCutAboveNyquistStaysStable()
    {
        const int rate = 16000;
        var samples = Signal.Sine(220, rate);

        ChainWith("bandpass", ("lowCut", 100), ("highCut", 12000)).Process(samples, rate);

        Assert.All(samples, s => Assert.True(float.IsFinite(s)));
        Assert.InRange(Signal.Peak(samples.AsSpan(rate)), 0.3f, 0.7f);
    }

    [Fact]
    public void ApplyToPresetRoundTrips()
    {
        var preset = ChainFor("Stadium announcer").ToPreset("copy");

        var target = new VoiceEffectChain();
        target.Apply(preset);

        Assert.Equivalent(preset, target.ToPreset("copy"), strict: true);
        Assert.True(Effect<EchoEffect>(target).Enabled);
        Assert.False(Effect<PitchShiftEffect>(target).Enabled);
        Assert.Equal(140, Param(Effect<EchoEffect>(target), "delay"));
    }

    [Fact]
    public void ApplyDisablesMissingEffectsRestoresDefaultsAndIgnoresUnknownKeys()
    {
        var chain = ChainFor("Cave");
        var echo = Effect<EchoEffect>(chain);
        var reverb = Effect<ReverbEffect>(chain);

        chain.Apply(new("partial", new()
        {
            ["echo"] = new(true, new() { ["feedback"] = 0.2, ["bogus"] = 42 }),
            ["nonexistent"] = new(true, new() { ["x"] = 1 }),
        }));

        Assert.False(reverb.Enabled);
        Assert.All(reverb.Parameters, p => Assert.Equal(p.Default, p.Value));
        Assert.True(echo.Enabled);
        Assert.Equal(0.2, Param(echo, "feedback"));
        Assert.Equal(300, Param(echo, "delay"));
    }
}
