using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicCheck.Audio;

/// <summary>
/// Captures from a WASAPI input device, accumulates level statistics for the UI to poll,
/// and optionally records a fixed-length mono clip.
/// </summary>
public sealed class MicrophoneMonitor : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;

    // Level stats accumulated since the last ReadLevels call.
    private float _peak;
    private double _sumSquares;
    private long _sampleCount;

    private List<float>? _recording;
    private int _recordingTarget;

    /// <summary>Raised on the UI thread when capture stops unexpectedly (e.g. device unplugged).</summary>
    public event EventHandler<Exception?>? Faulted;

    /// <summary>Raised on the capture thread with the recorded mono samples.</summary>
    public event EventHandler<float[]>? RecordingCompleted;

    /// <summary>Raised on the capture thread with each block of mono samples. Handlers must not modify the array.</summary>
    public event EventHandler<float[]>? SamplesCaptured;

    public WaveFormat? Format { get; private set; }
    public int SampleRate => Format?.SampleRate ?? 0;
    public bool IsRunning => _capture != null;

    public bool IsRecording
    {
        get { lock (_gate) return _recording != null; }
    }

    public double RecordingProgress
    {
        get { lock (_gate) return _recording == null ? 0 : (double)_recording.Count / _recordingTarget; }
    }

    public void Start(MMDevice device)
    {
        Stop();

        var capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 20);
        var format = capture.WaveFormat is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : capture.WaveFormat;
        if (!IsSupported(format))
        {
            capture.Dispose();
            throw new NotSupportedException($"Unsupported capture format: {format}");
        }

        Format = format;
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        _capture = capture;
        capture.StartRecording();
    }

    public void Stop()
    {
        var capture = _capture;
        if (capture == null)
            return;

        _capture = null;
        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        capture.Dispose();
        Reset();
    }

    public void BeginRecording(TimeSpan duration)
    {
        lock (_gate)
        {
            _recordingTarget = (int)(SampleRate * duration.TotalSeconds);
            _recording = new List<float>(_recordingTarget);
        }
    }

    /// <summary>Returns the peak and RMS amplitude (0..1) since the previous call, then resets them.</summary>
    public (float Peak, float Rms) ReadLevels()
    {
        lock (_gate)
        {
            var rms = _sampleCount == 0 ? 0f : (float)Math.Sqrt(_sumSquares / _sampleCount);
            var peak = _peak;
            _peak = 0;
            _sumSquares = 0;
            _sampleCount = 0;
            return (peak, rms);
        }
    }

    public void Dispose() => Stop();

    private void Reset()
    {
        lock (_gate)
        {
            _peak = 0;
            _sumSquares = 0;
            _sampleCount = 0;
            _recording = null;
        }
        Format = null;
    }

    private static bool IsSupported(WaveFormat f) =>
        (f.Encoding == WaveFormatEncoding.IeeeFloat && f.BitsPerSample == 32) ||
        (f.Encoding == WaveFormatEncoding.Pcm && f.BitsPerSample is 16 or 24 or 32);

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var format = Format;
        if (format == null)
            return;

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        var bits = format.BitsPerSample;
        var bytesPerSample = bits / 8;
        var channels = format.Channels;
        var frameSize = bytesPerSample * channels;
        var frames = e.BytesRecorded / frameSize;
        var data = e.Buffer.AsSpan(0, frames * frameSize);

        float peak = 0;
        double sumSquares = 0;
        var mono = new float[frames];

        for (var i = 0; i < frames; i++)
        {
            // Levels use the loudest channel so a mic that only feeds one channel still reads correctly;
            // the recording uses the channel average.
            float loudest = 0, sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var s = ReadSample(data.Slice(i * frameSize + c * bytesPerSample, bytesPerSample), bits, isFloat);
                sum += s;
                if (Math.Abs(s) > Math.Abs(loudest))
                    loudest = s;
            }

            mono[i] = sum / channels;
            peak = Math.Max(peak, Math.Abs(loudest));
            sumSquares += loudest * loudest;
        }

        float[]? completed = null;
        lock (_gate)
        {
            _peak = Math.Max(_peak, peak);
            _sumSquares += sumSquares;
            _sampleCount += frames;

            if (_recording != null)
            {
                var take = Math.Min(frames, _recordingTarget - _recording.Count);
                _recording.AddRange(mono.AsSpan(0, take));
                if (_recording.Count >= _recordingTarget)
                {
                    completed = _recording.ToArray();
                    _recording = null;
                }
            }
        }

        SamplesCaptured?.Invoke(this, mono);
        if (completed != null)
            RecordingCompleted?.Invoke(this, completed);
    }

    private static float ReadSample(ReadOnlySpan<byte> b, int bits, bool isFloat) => (isFloat, bits) switch
    {
        (true, 32) => BinaryPrimitives.ReadSingleLittleEndian(b),
        (false, 16) => BinaryPrimitives.ReadInt16LittleEndian(b) / 32768f,
        (false, 24) => (b[0] | b[1] << 8 | (sbyte)b[2] << 16) / 8388608f,
        (false, 32) => BinaryPrimitives.ReadInt32LittleEndian(b) / 2147483648f,
        _ => 0f,
    };

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (sender != _capture)
            return;

        Stop();
        Faulted?.Invoke(this, e.Exception);
    }
}
