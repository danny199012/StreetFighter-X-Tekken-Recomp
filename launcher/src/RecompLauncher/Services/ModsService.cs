using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace RecompLauncher.Services;

/// <summary>
/// Builds a staged copy of the game data with enabled mods overlaid.
///
/// The ReXGlue runtime mounts update_data_root as a separate device (it does
/// not layer over game:\ paths), so file-replacement mods are applied by
/// staging: the real game root is cloned with hard links (instant, same
/// volume, with a copy fallback) and enabled mod files are copied over the
/// links. Deleting a link never affects the original data, and mods are
/// always re-applied from the pristine source, so the real game root is
/// never modified.
/// </summary>
public class ModsService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    /// <summary>Immediate subfolders of the mods directory, skipping dot-folders.</summary>
    public List<string> DiscoverInstalled(string modsDir)
    {
        if (!Directory.Exists(modsDir))
            return new List<string>();

        return Directory.GetDirectories(modsDir)
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Select(Path.GetFullPath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Rebuilds (or reuses) the staging folder. Reused without touching
    /// anything when the game root and enabled mod list are unchanged.
    /// Returns the staging path to launch against.
    /// </summary>
    public string Stage(string gameRoot, IReadOnlyList<string> enabledModsTopFirst, string stagingRoot)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        var mods = enabledModsTopFirst.Select(Path.GetFullPath).ToList();
        var manifestPath = Path.Combine(stagingRoot, ".launcher_manifest.json");
        var manifest = JsonSerializer.Serialize(new Manifest(gameRoot, mods));

        Directory.CreateDirectory(stagingRoot);
        if (File.Exists(manifestPath))
        {
            try
            {
                var current = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath));
                if (current is not null &&
                    current.GameRoot == gameRoot &&
                    current.Mods.SequenceEqual(mods, StringComparer.OrdinalIgnoreCase) &&
                    Directory.EnumerateDirectories(stagingRoot).Any())
                {
                    return stagingRoot;
                }
            }
            catch
            {
                // Fall through to a full rebuild.
            }
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(stagingRoot))
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, true);
            else
                File.Delete(entry);
        }

        CloneTree(gameRoot, stagingRoot);

        // Apply bottom-of-list first so the top entry wins conflicts.
        for (var i = mods.Count - 1; i >= 0; i--)
            OverlayTree(mods[i], stagingRoot);

        File.WriteAllText(manifestPath, manifest);
        return stagingRoot;
    }

    private record Manifest(string GameRoot, List<string> Mods);

    /// <summary>Clones a directory tree, hard-linking files when possible.</summary>
    private static void CloneTree(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var targetDir = Path.Combine(target, Path.GetFileName(dir));
            Directory.CreateDirectory(targetDir);
            CloneTree(dir, targetDir);
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var targetFile = Path.Combine(target, Path.GetFileName(file));
            try
            {
                if (!CreateHardLink(targetFile, file, IntPtr.Zero))
                    File.Copy(file, targetFile);
            }
            catch
            {
                File.Copy(file, targetFile, true);
            }
        }
    }

    /// <summary>Copies a mod's files over the staging tree. Existing files are
    /// deleted first: staging files may be hard links, and overwriting a link
    /// in place would write through to the original game data.</summary>
    private static void OverlayTree(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var targetDir = Path.Combine(target, Path.GetFileName(dir));
            Directory.CreateDirectory(targetDir);
            OverlayTree(dir, targetDir);
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var targetFile = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(targetFile))
                File.Delete(targetFile);
            File.Copy(file, targetFile);
        }
    }
}
