using System.Reflection;
using System.IO;

namespace RecompLauncher.Services;

/// <summary>
/// Extracts the recompiled game binaries (exe + runtime DLLs) that are
/// embedded inside the launcher executable into the profile's game folder.
/// This lets a single launcher file be distributed without shipping loose
/// binaries; the payload is written out on first launch (or when missing).
/// </summary>
public static class PayloadService
{
    private const string Prefix = "RecompLauncher.payload.";

    public static IReadOnlyList<string> EmbeddedFiles()
    {
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..])
            .ToList();
    }

    /// <summary>
    /// Writes any missing payload files into <paramref name="payloadDir"/>
    /// (relative paths resolve against <paramref name="profileDir"/>).
    /// Returns the number of files written; 0 when the launcher has no
    /// embedded payload or everything is already present.
    /// </summary>
    public static int ExtractIfNeeded(string profileDir, string payloadDir)
    {
        var names = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .ToList();
        if (names.Count == 0)
            return 0;

        var target = Path.IsPathRooted(payloadDir)
            ? payloadDir
            : Path.Combine(profileDir, payloadDir);
        Directory.CreateDirectory(target);

        var written = 0;
        foreach (var name in names)
        {
            // resource names map 1:1 to file names (files sit directly in
            // payload/, so "RecompLauncher.payload.SFxT.exe" -> "SFxT.exe")
            var file = name[Prefix.Length..];
            var dest = Path.Combine(target, file);
            if (File.Exists(dest))
                continue;

            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded payload '{name}' is missing.");
            using var output = File.Create(dest);
            stream.CopyTo(output);
            written++;
        }

        return written;
    }
}
