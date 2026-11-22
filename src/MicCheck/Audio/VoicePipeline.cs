using MicCheck.Effects;

namespace MicCheck.Audio;

/// <summary>
/// Runs captured mic audio through the effect chain once and hands the result to every active output:
/// your own headphones (live monitoring) and the virtual microphone other apps listen to.
/// </summary>
public sealed class VoicePipeline : IDisposable
{
    public VoiceEffectChain Chain { get; } = new();
    public AudioSink Monitor { get; } = new();
    public AudioSink VirtualMic { get; } = new();

    /// <summary>Called on the capture thread.</summary>
    public void Process(float[] samples, int sampleRate)
    {
        if (sampleRate <= 0 || (!Monitor.IsRunning && !VirtualMic.IsRunning))
            return;

        var processed = samples.ToArray();
        Chain.Process(processed, sampleRate);
        Monitor.Write(processed);
        VirtualMic.Write(processed);
    }

    public void Dispose()
    {
        Monitor.Dispose();
        VirtualMic.Dispose();
    }
}
