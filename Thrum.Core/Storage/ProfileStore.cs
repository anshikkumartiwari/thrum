using System.Text.Json;
using Thrum.Core.Models;

namespace Thrum.Core.Storage;

public sealed class ProfileStore
{
    private readonly string _baseDirectory;
    private readonly string _profilesDirectory;
    private readonly string _activeProfilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ProfileStore(string? customBaseDirectory = null)
    {
        _baseDirectory = customBaseDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thrum");

        _profilesDirectory = Path.Combine(_baseDirectory, "profiles");
        _activeProfilePath = Path.Combine(_baseDirectory, "profile.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_baseDirectory)) Directory.CreateDirectory(_baseDirectory);
        if (!Directory.Exists(_profilesDirectory)) Directory.CreateDirectory(_profilesDirectory);
    }

    public List<Profile> LoadAllProfiles()
    {
        EnsureDirectoriesExist();
        var list = new List<Profile>();
        var files = Directory.GetFiles(_profilesDirectory, "*.json");

        foreach (var file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<Profile>(json, _jsonOptions);
                if (profile != null) list.Add(profile);
            }
            catch
            {
                // Ignore corrupt file or backup
            }
        }

        if (list.Count == 0)
        {
            // Create initial default profile
            var defaultProfile = CreateDefaultProfile("Default Setup");
            SaveProfile(defaultProfile);
            list.Add(defaultProfile);
        }

        return list;
    }

    public Profile LoadActiveProfile()
    {
        EnsureDirectoriesExist();
        if (File.Exists(_activeProfilePath))
        {
            try
            {
                string json = File.ReadAllText(_activeProfilePath);
                var profile = JsonSerializer.Deserialize<Profile>(json, _jsonOptions);
                if (profile != null) return profile;
            }
            catch
            {
                // Fallback to default
            }
        }

        var all = LoadAllProfiles();
        var active = all.FirstOrDefault() ?? CreateDefaultProfile("Default Setup");
        SaveActiveProfile(active);
        return active;
    }

    public void SaveProfile(Profile profile)
    {
        EnsureDirectoriesExist();
        string profileFilePath = Path.Combine(_profilesDirectory, $"{profile.Id}.json");
        string json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(profileFilePath, json);
    }

    public void SaveActiveProfile(Profile profile)
    {
        EnsureDirectoriesExist();
        SaveProfile(profile);
        string json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(_activeProfilePath, json);
    }

    public void DeleteProfile(string profileId)
    {
        string path = Path.Combine(_profilesDirectory, $"{profileId}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    public static Profile CreateDefaultProfile(string name)
    {
        var profile = new Profile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            SchemaVersion = 1,
            CreatedAt = DateTime.UtcNow,
            Zones = new List<Zone>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Left Palm Rest",
                    Color = "#3B82F6", // Modern Blue
                    X = 0.25,
                    Y = 0.78,
                    Action = new ZoneAction
                    {
                        Type = ActionType.MediaPlayPause,
                        CooldownMs = 500
                    }
                },
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Right Palm Rest",
                    Color = "#10B981", // Modern Emerald
                    X = 0.75,
                    Y = 0.78,
                    Action = new ZoneAction
                    {
                        Type = ActionType.VolumeUp,
                        CooldownMs = 500
                    }
                }
            }
        };

        return profile;
    }
}
