using LuminaCore.Core.Interfaces;
using LuminaCore.Core.Models;
using Newtonsoft.Json;

namespace LuminaCore.Core.Services;

public sealed class ProfileRepository : IProfileRepository
{
    private readonly object _ioLock = new();
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    public string DataDirectory { get; }
    public string ProfilesFilePath => Path.Combine(DataDirectory, "profiles.json");
    public string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    public ProfileRepository(string? customDataDirectory = null)
    {
        DataDirectory = customDataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuminaCore");
        Directory.CreateDirectory(DataDirectory);
    }

    public List<LightingProfile> LoadProfiles()
    {
        lock (_ioLock)
        {
            try
            {
                if (File.Exists(ProfilesFilePath))
                {
                    string json = File.ReadAllText(ProfilesFilePath);
                    var loaded = JsonConvert.DeserializeObject<List<LightingProfile>>(json, JsonSettings);
                    if (loaded is { Count: > 0 })
                    {
                        foreach (var p in loaded)
                        {
                            if (p.Zones is not { Count: > 0 })
                                p.Zones = ZoneLayout.CreateGeneric3ZoneLayout();
                            else
                                p.Zones = p.Zones.GroupBy(z => z.ZoneIndex).Select(g => g.Last()).ToList();

                            p.Parameters ??= new EffectParameters();
                            p.AssociatedProcesses ??= [];
                        }
                        return loaded;
                    }
                }
            }
            catch
            {
                // Fall back to default profiles if JSON is corrupted
            }

            var defaults = LightingProfile.CreateDefaultProfiles();
            SaveProfilesInternal(defaults);
            return defaults;
        }
    }

    public void SaveProfiles(IEnumerable<LightingProfile> profiles)
    {
        lock (_ioLock)
        {
            SaveProfilesInternal(profiles.ToList());
        }
    }

    private void SaveProfilesInternal(List<LightingProfile> profiles)
    {
        Directory.CreateDirectory(DataDirectory);
        string json = JsonConvert.SerializeObject(profiles, JsonSettings);
        string tmp = ProfilesFilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, ProfilesFilePath, overwrite: true);
    }

    public AppSettings LoadSettings()
    {
        lock (_ioLock)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json, JsonSettings);
                    if (settings is not null)
                        return settings;
                }
            }
            catch
            {
                // Fall back to defaults
            }

            var defaults = new AppSettings();
            SaveSettingsInternal(defaults);
            return defaults;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        lock (_ioLock)
        {
            SaveSettingsInternal(settings);
        }
    }

    private void SaveSettingsInternal(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        string json = JsonConvert.SerializeObject(settings, JsonSettings);
        string tmp = SettingsFilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, SettingsFilePath, overwrite: true);
    }
}
