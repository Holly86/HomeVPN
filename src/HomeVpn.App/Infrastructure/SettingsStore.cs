using System.Text.Json;
using HomeVpn.Models;

namespace HomeVpn.Infrastructure;

public sealed class SettingsStore
{
    private readonly Func<string?> _generation;
    public SettingsStore(string? directory = null, Func<string?>? generation = null)
    {
        DataDirectory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HomeVPN");
        _generation = generation ?? (directory is null ? SettingsGeneration.Read : () => null);
    }
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DataDirectory { get; }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            if (new FileInfo(SettingsPath).Length > 1048576) throw new InvalidDataException();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            if (settings.SettingsGeneration != _generation())
            {
                // Only this normal user rewrites their own settings; no privileged user-path traversal.
                var reset = new AppSettings();
                Save(reset);
                return reset;
            }
            if (settings.Profiles is null || settings.ExcludedNetworks is null || settings.SchemaVersion > 2 ||
                settings.Profiles.Any(p => p is null || p.Id == Guid.Empty || p.HomeCidrs is null || !Enum.IsDefined(p.RoutingMode) || !Enum.IsDefined(p.Backend)) ||
                settings.Profiles.Select(p => p.Id).Distinct().Count() != settings.Profiles.Count)
                throw new InvalidDataException();
            foreach (var profile in settings.Profiles) profile.SplitDns = SplitDns.Normalize(profile.SplitDns, profile.HomeCidrs);
            settings.NormalizeProfileSelection();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NullReferenceException)
        {
            throw new InvalidDataException("Die gespeicherten Einstellungen sind beschädigt oder stammen aus einer neueren Version. Sie wurden nicht überschrieben. Bitte eine Sicherung von settings.json wiederherstellen.");
        }
    }

    public void Save(AppSettings settings)
    {
        settings.SettingsGeneration = _generation();
        settings.NormalizeProfileSelection();
        Directory.CreateDirectory(DataDirectory);
        var tempPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, SettingsPath, true);
    }
}
