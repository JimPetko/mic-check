using System.IO;
using System.Text.Json;

namespace MicCheck.Effects;

public sealed record EffectPresetEntry(bool Enabled, Dictionary<string, double> Values);

public sealed record EffectPreset(string Name, Dictionary<string, EffectPresetEntry> Effects)
{
    public static IReadOnlyList<EffectPreset> BuiltIn { get; } =
    [
        Create("No effects"),
        Create("Chipmunk", ("pitch", [("semitones", 9)])),
        Create("Deep voice", ("pitch", [("semitones", -6)]), ("gain", [("gain", 3)])),
        Create("Robot",
            ("ring", [("frequency", 60), ("mix", 1)]),
            ("distortion", [("drive", 8), ("mix", 0.5)]),
            ("echo", [("delay", 30), ("feedback", 0.4), ("mix", 0.4)])),
        Create("Telephone",
            ("bandpass", [("lowCut", 400), ("highCut", 3000)]),
            ("distortion", [("drive", 14), ("mix", 0.5)]),
            ("gain", [("gain", 6)])),
        Create("Megaphone",
            ("bandpass", [("lowCut", 700), ("highCut", 2500)]),
            ("distortion", [("drive", 28), ("mix", 1)]),
            ("gain", [("gain", 8)])),
        Create("Cave",
            ("echo", [("delay", 350), ("feedback", 0.45), ("mix", 0.35)]),
            ("reverb", [("roomSize", 0.9), ("damping", 0.3), ("mix", 0.45)])),
        Create("Stadium announcer",
            ("echo", [("delay", 140), ("feedback", 0.3), ("mix", 0.25)]),
            ("reverb", [("roomSize", 0.85), ("damping", 0.5), ("mix", 0.3)]),
            ("gain", [("gain", 4)])),
        Create("Alien",
            ("pitch", [("semitones", 4)]),
            ("ring", [("frequency", 420), ("mix", 0.5)]),
            ("reverb", [("roomSize", 0.6), ("damping", 0.2), ("mix", 0.25)])),
    ];

    /// <summary>
    /// Drops what a hand-edited file could contain that would crash Apply: JSON nulls deserialize fine.
    /// Returns null if the preset itself is unusable.
    /// </summary>
    public static EffectPreset? Sanitize(EffectPreset? preset)
    {
        if (string.IsNullOrWhiteSpace(preset?.Name) || preset.Effects == null)
            return null;

        return preset with
        {
            Effects = preset.Effects
                .Where(e => e.Value?.Values != null)
                .ToDictionary(e => e.Key, e => e.Value),
        };
    }

    private static EffectPreset Create(string name, params (string Effect, (string Key, double Value)[] Values)[] effects) =>
        new(name, effects.ToDictionary(
            e => e.Effect,
            e => new EffectPresetEntry(true, e.Values.ToDictionary(v => v.Key, v => v.Value))));
}

/// <summary>User-created presets, stored in %AppData%\MicCheck\voice-presets.json.</summary>
public static class PresetStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicCheck", "voice-presets.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<EffectPreset> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var presets = JsonSerializer.Deserialize<List<EffectPreset?>>(File.ReadAllText(FilePath), JsonOptions) ?? [];
            return presets.Select(EffectPreset.Sanitize).OfType<EffectPreset>().ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void Save(IEnumerable<EffectPreset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(presets, JsonOptions));
    }
}
