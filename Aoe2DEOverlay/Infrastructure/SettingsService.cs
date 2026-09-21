using System.IO;
using System.Text.Json;

namespace Aoe2DEOverlay;

public sealed class OverlaySettings
{
    public double X { get; set; } = 24;
    public double Y { get; set; } = 24;
    public double Opacity { get; set; } = 0.78;
    public bool Locked { get; set; } = true;
    public bool Hidden { get; set; }
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string SettingsPath { get; } = Path.Combine(AppLogger.RootDirectory, "settings.json");

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new OverlaySettings();
            return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new OverlaySettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings load failed; defaults are in use", exception);
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.RootDirectory);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Settings save failed", exception);
        }
    }
}

