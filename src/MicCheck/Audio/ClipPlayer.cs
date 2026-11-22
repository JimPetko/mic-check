using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MicCheck.Audio;

/// <summary>Plays a mono float clip through the default output device.</summary>
public sealed class ClipPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private WaveStream? _stream;

    /// <summary>Raised on the UI thread when playback reaches the end.</summary>
    public event EventHandler? Finished;

    public bool IsPlaying => _output != null;

    public void Play(float[] samples, int sampleRate)
    {
        Stop();

        var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        var stream = new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));
        var output = new WaveOutEvent();
        output.Init(stream.ToSampleProvider().ToWaveProvider16());
        output.PlaybackStopped += (_, _) =>
        {
            if (_output != output)
                return;
            Stop();
            Finished?.Invoke(this, EventArgs.Empty);
        };

        _output = output;
        _stream = stream;
        output.Play();
    }

    public void Stop()
    {
        var output = _output;
        _output = null;
        output?.Dispose();
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Stop();
}
