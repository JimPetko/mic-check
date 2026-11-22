using System.Text.Json;
using MicCheck.Effects;

namespace MicCheck.Tests;

public class PresetSerializationTests
{
    [Fact]
    public void PresetListRoundTripsThroughJson()
    {
        var chain = new VoiceEffectChain();
        chain.Apply(EffectPreset.BuiltIn.Single(p => p.Name == "Robot"));
        List<EffectPreset> presets = [chain.ToPreset("My robot"), .. EffectPreset.BuiltIn];
        var options = new JsonSerializerOptions { WriteIndented = true };

        var json = JsonSerializer.Serialize(presets, options);
        var restored = JsonSerializer.Deserialize<List<EffectPreset>>(json, options);

        Assert.NotNull(restored);
        Assert.Equivalent(presets, restored, strict: true);
        Assert.Equal("My robot", restored[0].Name);
        Assert.False(restored[0].Effects["reverb"].Enabled);
        Assert.Equal(0.4, restored[0].Effects["echo"].Values["feedback"]);
    }
}
