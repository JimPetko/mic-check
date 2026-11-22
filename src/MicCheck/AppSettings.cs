using System.IO;
using System.Text.Json;
using MicCheck.Effects;
using Microsoft.Win32;

namespace MicCheck;

/// <summary>Settings that must survive restarts so the virtual mic comes back the same after sign-in.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicCheck", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? MicrophoneId { get; set; }
    public bool NoiseFilter { get; set; } = true;
    public bool VirtualMicEnabled { get; set; }
    public string? VirtualMicDeviceId { get; set; }
    public string? PresetName { get; set; }
    public EffectPreset? Effects { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
                settings.Effects = EffectPreset.Sanitize(settings.Effects);
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing settings isn't worth interrupting the user over.
        }
    }
}

/// <summary>Per-user "run at sign-in" entry that starts Mic Check hidden in the tray.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MicCheck";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
