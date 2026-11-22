using System.Runtime.InteropServices;
using MicCheck.Effects;
using NAudio.Wave;

namespace MicCheck.Audio;

/// <summary>Routes captured mic audio through an effect chain to the default output so you can hear yourself.</summary>
public sealed class LiveEffectMonitor(VoiceEffectChain chain) : IDisposable
{
    // If output falls behind (e.g. a stall), drop the backlog rather than letting latency build up.
    private static readonly TimeSpan MaxBacklog = TimeSpan.FromMilliseconds(200);

    private WaveOutEvent? _output;
    private BufferedWaveProvider? _buffer;
    private int _sampleRate;

    public bool IsRunning => _output != null;

    public void Start(int sampleRate)
    {
        Stop();

        var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
        };
        var output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
        try
        {
            output.Init(buffer.ToSampleProvider().ToWaveProvider16());
        }
        catch
        {
            output.Dispose();
            throw;
        }

        _sampleRate = sampleRate;
        _buffer = buffer;
        _output = output;
        output.Play();
    }

    /// <summary>Called on the capture thread.</summary>
    public void Feed(float[] samples)
    {
        var buffer = _buffer;
        if (buffer == null)
            return;

        var processed = samples.ToArray();
        chain.Process(processed, _sampleRate);

        if (buffer.BufferedDuration > MaxBacklog)
            buffer.ClearBuffer();
        var bytes = MemoryMarshal.AsBytes(processed.AsSpan()).ToArray();
        buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Stop()
    {
        _buffer = null;
        var output = _output;
        _output = null;
        output?.Dispose();
    }

    public void Dispose() => Stop();
}
