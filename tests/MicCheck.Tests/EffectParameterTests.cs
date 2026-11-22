using MicCheck.Effects;

namespace MicCheck.Tests;

public class EffectParameterTests
{
    [Fact]
    public void ValueIsClampedAndRaisesChangeNotifications()
    {
        var parameter = new EffectParameter("k", "K", -12, 12, 0, 1, "{0:0}");
        var changed = new List<string?>();
        parameter.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        parameter.Value = 50;
        Assert.Equal(12, parameter.Value);
        Assert.Equal("12", parameter.DisplayValue);
        Assert.Equal([nameof(EffectParameter.Value), nameof(EffectParameter.DisplayValue)], changed);

        parameter.Value = -50;
        Assert.Equal(-12, parameter.Value);
        Assert.Equal(4, changed.Count);

        parameter.Value = -13;
        Assert.Equal(4, changed.Count);
    }
}
