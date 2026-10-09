using System.Text.Json.Serialization;

namespace RecompLauncher.Models;

/// <summary>
/// Root of a per-game launcher.json profile. The same launcher executable
/// works for any recomp: the profile supplies the game identity, the launch
/// recipe, and the settings schema (ReXGlue cvars) to expose in the UI.
/// </summary>
public class LauncherProfile
{
    public int Schema { get; set; } = 1;

    public GameInfo Game { get; set; } = new();

    public List<SettingsTab> Tabs { get; set; } = new();
}

public class GameInfo
{
    /// <summary>Display name shown in the launcher title/header.</summary>
    public string Name { get; set; } = "Untitled Recomp";

    /// <summary>Path to the recompiled game executable, relative to the profile file.</summary>
    public string Exe { get; set; } = "";

    /// <summary>
    /// Root of the extracted game data, relative to the profile file.
    /// Passed to the game as the quoted --game_data_root argument.
    /// Optional when the game does not need it.
    /// </summary>
    public string? GameDataRoot { get; set; }

    /// <summary>
    /// Fixed arguments always passed on the command line
    /// (e.g. [ "--gpu_plugin", "xenos" ]).
    /// </summary>
    public List<string> Args { get; set; } = new();

    /// <summary>
    /// Name of the cvar config TOML the runtime reads/writes, relative to the
    /// game exe folder. Defaults to "&lt;exe name&gt;.toml" (ReXGlue convention).
    /// </summary>
    public string? Config { get; set; }

    /// <summary>Optional subtitle (e.g. "Xbox 360 / XBLA recomp").</summary>
    public string? Subtitle { get; set; }

    /// <summary>Optional path to a cover/banner image, relative to the profile.</summary>
    public string? Banner { get; set; }

    /// <summary>
    /// cvar name that holds the player profile name / gamertag. When set, the
    /// launcher shows a Profile tab with an editable name field. Harmless on
    /// runtimes that don't know the cvar yet.
    /// </summary>
    public string? UsernameCvar { get; set; }

    /// <summary>
    /// cvar name that holds the online username (for recomp online features).
    /// Shown in the Profile tab when set.
    /// </summary>
    public string? OnlineUsernameCvar { get; set; }

    /// <summary>
    /// Optional path to extract-xiso.exe used by the Add Disc flow. When null
    /// the launcher probes the launcher folder, the profile folder, and PATH.
    /// </summary>
    public string? ExtractXisoPath { get; set; }

    /// <summary>
    /// Folder (relative to the profile) the embedded game binaries extract
    /// into on first launch; also the default extraction target of the Add
    /// Disc flow. Empty disables payload extraction.
    /// </summary>
    public string PayloadDir { get; set; } = "game";

    /// <summary>
    /// Mods directory relative to the profile. Each immediate subfolder is a
    /// mod; enabled mods overlay the staged game data (top of the list wins).
    /// </summary>
    public string ModsDir { get; set; } = "mods";
}

public class SettingsTab
{
    public string Name { get; set; } = "Settings";

    public List<SettingItem> Settings { get; set; } = new();
}

public class SettingItem
{
    /// <summary>cvar name as written into the game's config TOML / CLI.</summary>
    public string Cvar { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>bool | int | double | string | choice | resolution</summary>
    public string Type { get; set; } = "bool";

    /// <summary>Value shown when the cvar is absent from the config file.</summary>
    public string? Default { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }

    /// <summary>Allowed values for type=choice.</summary>
    public List<Choice> Choices { get; set; } = new();

    /// <summary>Free-form extra choices offered for type=resolution.</summary>
    public List<string> Presets { get; set; } = new();

    /// <summary>Short help text shown under the control.</summary>
    public string? Hint { get; set; }

    /// <summary>Hidden unless "Show advanced" is checked.</summary>
    public bool Advanced { get; set; }
}

public class Choice
{
    /// <summary>Value written to the config file.</summary>
    public string Value { get; set; } = "";

    /// <summary>Label shown in the combo box.</summary>
    public string Label { get; set; } = "";
}
