using System.Diagnostics;
using System.IO;
using RecompLauncher.Models;

namespace RecompLauncher.Services;

/// <summary>
/// Starts the recompiled game with the profile's fixed arguments plus the
/// quoted --game_data_root and any user extra arguments. All graphics
/// settings travel through the cvar config TOML, not the command line, so
/// the in-game F4 settings dialog and the launcher stay in sync.
/// </summary>
public class GameLauncherService
{
    /// <summary>
    /// Builds the full argument string for the launch command.
    /// gameRootOverride replaces the profile's game data root (used when mods
    /// are staged).
    /// </summary>
    public static string BuildArguments(LauncherProfile profile, string profilePath, string extraArgs,
        string? gameRootOverride = null)
    {
        var args = new List<string>(profile.Game.Args);

        var gameRoot = gameRootOverride ?? new ProfileService().Resolve(profile.Game.GameDataRoot, profilePath);
        if (!string.IsNullOrEmpty(gameRoot) && !args.Contains("--game_data_root"))
        {
            args.Add("--game_data_root");
            args.Add(gameRoot);
        }

        args.AddRange(SplitArgs(extraArgs));
        return string.Join(" ", args.Select(Quote));
    }

    /// <summary>Launches the game. Throws with a readable message when paths are missing.</summary>
    public Process Start(LauncherProfile profile, string profilePath, string extraArgs,
        string? gameRootOverride = null)
    {
        var resolver = new ProfileService();
        var exePath = resolver.Resolve(profile.Game.Exe, profilePath)!;
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Game executable not found:{Environment.NewLine}{exePath}");

        var gameRoot = resolver.Resolve(profile.Game.GameDataRoot, profilePath);
        if (!string.IsNullOrEmpty(gameRoot) && !Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException($"Game data root not found:{Environment.NewLine}{gameRoot}");

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = BuildArguments(profile, profilePath, extraArgs, gameRootOverride),
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = true
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the game process.");
    }

    /// <summary>Splits an extra-args string honoring double quotes, e.g. --flag "a b".</summary>
    internal static IEnumerable<string> SplitArgs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var inQuote = false;
        var sb = new System.Text.StringBuilder();
        foreach (var c in text.Trim())
        {
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (char.IsWhiteSpace(c) && !inQuote)
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static string Quote(string arg)
    {
        return arg.Contains(' ') && !arg.StartsWith('"')
            ? $"\"{arg}\""
            : arg;
    }
}
