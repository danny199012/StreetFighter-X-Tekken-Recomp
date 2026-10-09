using System.IO;
using System.Text.Json;
using RecompLauncher.Models;

namespace RecompLauncher.Services;

/// <summary>One mod entry in the per-profile mod list.</summary>
public class ModEntry
{
    public string Path { get; set; } = "";

    /// <summary>false = folder inside the profile's mods directory,
    /// true = a folder referenced from anywhere on disk.</summary>
    public bool External { get; set; }

    public bool Enabled { get; set; }
}

/// <summary>Per-profile user state persisted under %APPDATA%.</summary>
public class ProfileState
{
    public string ExtraArgs { get; set; } = "";

    /// <summary>Ordered mod list; index 0 has the highest override priority.</summary>
    public List<ModEntry> Mods { get; set; } = new();
}

/// <summary>Launcher-wide state stored in recent.json.</summary>
public class AppState
{
    public string? LastProfile { get; set; }

    public Dictionary<string, ProfileState> Profiles { get; set; } = new();
}

/// <summary>
/// Loads launcher.json profiles and persists per-profile user state
/// (extra launch arguments, mod list) under %APPDATA%.
/// </summary>
public class ProfileService
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RecompLauncher");

    private static readonly string RecentPath = Path.Combine(AppDataDir, "recent.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Finds a profile to load: --profile argument, launcher.json next
    /// to the launcher exe, the working directory, or the last used one.</summary>
    public string? LocateProfile(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] is "--profile" or "-p" && File.Exists(args[i + 1]))
                return Path.GetFullPath(args[i + 1]);
        }

        var adjacent = Path.Combine(AppContext.BaseDirectory, "launcher.json");
        if (File.Exists(adjacent))
            return adjacent;

        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "launcher.json");
        if (File.Exists(cwd))
            return cwd;

        var state = LoadState();
        if (state.LastProfile is { } last && File.Exists(last))
            return last;

        return null;
    }

    public LauncherProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<LauncherProfile>(json, JsonOptions)
                      ?? throw new InvalidDataException($"Profile is empty: {path}");

        if (string.IsNullOrWhiteSpace(profile.Game.Exe))
            throw new InvalidDataException($"Profile has no game.exe entry: {path}");

        return profile;
    }

    /// <summary>Resolves a profile-relative path (blank -> null).</summary>
    public string? Resolve(string? relativePath, string profilePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var combined = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilePath)) ?? ".", relativePath);
        return Path.GetFullPath(combined);
    }

    public string ConfigPathFor(LauncherProfile profile, string profilePath)
    {
        var exePath = Resolve(profile.Game.Exe, profilePath)!;
        var explicitConfig = Resolve(profile.Game.Config, profilePath);
        if (explicitConfig is not null)
            return explicitConfig;

        var exeDir = Path.GetDirectoryName(exePath)!;
        return Path.Combine(exeDir, Path.GetFileNameWithoutExtension(exePath) + ".toml");
    }

    public string ModsDirFor(LauncherProfile profile, string profilePath)
        => Resolve(profile.Game.ModsDir, profilePath) ?? Path.Combine(Path.GetDirectoryName(profilePath)!, "mods");

    // ------------------------------------------------------------------
    // Persisted state
    // ------------------------------------------------------------------

    /// <summary>Loads recent.json, migrating the old { extraArgs: { path: str } } layout.</summary>
    public AppState LoadState()
    {
        try
        {
            if (!File.Exists(RecentPath))
                return new AppState();

            using var doc = JsonDocument.Parse(File.ReadAllText(RecentPath));
            var state = new AppState();
            if (doc.RootElement.TryGetProperty("lastProfile", out var lastEl))
                state.LastProfile = lastEl.GetString();

            if (doc.RootElement.TryGetProperty("profiles", out var profilesEl) &&
                profilesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in profilesEl.EnumerateObject())
                    state.Profiles[prop.Name] = prop.Value.Deserialize<ProfileState>(JsonOptions) ?? new ProfileState();
            }

            // Legacy v1: { "extraArgs": { "<profile>": "args" } }
            if (doc.RootElement.TryGetProperty("extraArgs", out var legacyEl) &&
                legacyEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in legacyEl.EnumerateObject())
                {
                    if (!state.Profiles.ContainsKey(prop.Name))
                        state.Profiles[prop.Name] = new ProfileState();
                    state.Profiles[prop.Name].ExtraArgs = prop.Value.GetString() ?? "";
                }
            }

            return state;
        }
        catch
        {
            return new AppState();
        }
    }

    public void SaveState(AppState state)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(RecentPath, JsonSerializer.Serialize(state, JsonOptions));
    }

    /// <summary>Gets (or creates) the persisted state for a profile path.</summary>
    public static ProfileState StateFor(AppState state, string profilePath)
    {
        if (!state.Profiles.TryGetValue(profilePath, out var profileState))
        {
            profileState = new ProfileState();
            state.Profiles[profilePath] = profileState;
        }

        return profileState;
    }
}
