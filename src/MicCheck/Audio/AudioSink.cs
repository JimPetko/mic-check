using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicCheck.Audio;

/// <summary>
/// Plays a stream of mono float samples to an output device, converting to the device's
/// sample rate and channel count. Used for live monitoring and for feeding a virtual cable.
/// </summary>
public sealed class AudioSink : IDisposable
{
    // If output falls behind (e.g. a stall), drop the backlog rather than letting latency build up.
    private static readonly TimeSpan MaxBacklog = TimeSpan.FromMilliseconds(150);

    private MMDevice? _device;
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;

    /// <summary>Raised on the UI thread when the device stops unexpectedly (e.g. unplugged or disabled).</summary>
    public event EventHandler<Exception?>? Faulted;

    public bool IsRunning => _output != null;
    public string? DeviceName { get; private set; }

    /// <param name="deviceId">Render endpoint ID, or null for the Windows default output.</param>
    public void Start(int inputSampleRate, string? deviceId)
    {
        Stop();

        using var enumerator = new MMDeviceEnumerator();
        var device = deviceId == null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);

        try
        {
            var buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(inputSampleRate, 1))
            {
                BufferDuration = TimeSpan.FromSeconds(1),
                DiscardOnBufferOverflow = true,
            };

            // MMDevice.AudioClient creates a new client on every access, so dispose this one.
            WaveFormat mix;
            using (var client = device.AudioClient)
                mix = client.MixFormat;
            ISampleProvider source = buffer.ToSampleProvider();
            if (mix.SampleRate != inputSampleRate)
                source = new WdlResamplingSampleProvider(source, mix.SampleRate);
            if (mix.Channels > 1)
                source = new MultiplexingSampleProvider([source], mix.Channels);

            var output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 40);
            try
            {
                output.Init(source.ToWaveProvider());
            }
            catch
            {
                output.Dispose();
                throw;
            }

            output.PlaybackStopped += (_, e) =>
            {
                if (_output != output)
                    return;
                Stop();
                Faulted?.Invoke(this, e.Exception);
            };

            _device = device;
            _buffer = buffer;
            _output = output;
            DeviceName = device.FriendlyName;
            output.Play();
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    /// <summary>Called on the capture thread.</summary>
    public void Write(float[] samples)
    {
        var buffer = _buffer;
        if (buffer == null)
            return;

        if (buffer.BufferedDuration > MaxBacklog)
            buffer.ClearBuffer();
        var bytes = MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
        buffer.AddSamples(bytes, 0, bytes.Length);
    }

    public void Stop()
    {
        _buffer = null;
        var output = _output;
        _output = null;
        output?.Dispose();
        _device?.Dispose();
        _device = null;
        DeviceName = null;
    }

    public void Dispose() => Stop();
}
