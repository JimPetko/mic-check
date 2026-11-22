using System.Reflection;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MicCheck.Audio;

/// <summary>
/// Tags a capture stream as voice communications, so Windows applies its communications processing:
/// Voice Clarity (AI noise suppression, e.g. for keyboard and mouse clicks) on Windows 11, or the
/// driver's own noise suppression/echo cancellation. Must be called before the stream starts.
/// </summary>
internal static class CommunicationsStream
{
    private const int AudioCategoryCommunications = 3;

    // NAudio 2.2.1 has no public way to call IAudioClient2::SetClientProperties on WasapiCapture's
    // audio client, so reach the private fields. Any failure just leaves processing off.
    private static readonly FieldInfo? CaptureClientField =
        typeof(WasapiCapture).GetField("audioClient", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ClientInterfaceField =
        typeof(AudioClient).GetField("audioClientInterface", BindingFlags.NonPublic | BindingFlags.Instance);

    public static bool TryEnable(WasapiCapture capture)
    {
        try
        {
            var client = CaptureClientField?.GetValue(capture);
            if (client == null || ClientInterfaceField?.GetValue(client) is not IAudioClient2 client2)
                return false;

            var properties = new AudioClientProperties
            {
                Size = Marshal.SizeOf<AudioClientProperties>(),
                Category = AudioCategoryCommunications,
            };
            client2.SetClientProperties(ref properties);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    [ComImport, Guid("726778CD-F60A-4eda-82DE-E47610CD78AA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient2
    {
        void _VtblGap1_12(); // the 12 inherited IAudioClient methods
        void IsOffloadCapable(int category, [MarshalAs(UnmanagedType.Bool)] out bool offloadCapable);
        void SetClientProperties(ref AudioClientProperties properties);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioClientProperties
    {
        public int Size;
        public int IsOffload;
        public int Category;
        public int Options;
    }
}
