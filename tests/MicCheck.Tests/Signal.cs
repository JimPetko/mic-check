namespace MicCheck.Tests;

internal static class Signal
{
    public static float[] Sine(double frequency, int sampleRate, double seconds = 2, float amplitude = 0.5f) =>
        Enumerable.Range(0, (int)(sampleRate * seconds))
            .Select(i => amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate))
            .ToArray();

    public static double Frequency(ReadOnlySpan<float> samples, int sampleRate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
                crossings++;
        }
        return crossings * (double)sampleRate / samples.Length;
    }

    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var s in samples)
            peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }
}
