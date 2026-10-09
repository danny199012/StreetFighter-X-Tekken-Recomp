using System.Diagnostics;
using System.IO;

namespace RecompLauncher.Services;

/// <summary>
/// Extracts Xbox 360 game images into the game data root by driving the
/// extract-xiso command line tool (https://github.com/XboxDev/extract-xiso).
/// The launcher never ships game content itself: users provide their own
/// disc image (or an already-extracted folder) and this service unpacks the
/// files the recomp needs (default.xex, archive/, stream/, ...).
/// </summary>
public static class XisoService
{
    /// <summary>
    /// Locates extract-xiso.exe: explicit configured path first, then next to
    /// the launcher/profile, then PATH. Returns null when not found.
    /// </summary>
    public static string? ResolveExtractor(string? configured, string profileDir)
    {
        var candidates = new[]
        {
            configured,
            Path.Combine(AppContext.BaseDirectory, "extract-xiso.exe"),
            Path.Combine(profileDir, "extract-xiso.exe"),
            "extract-xiso.exe"
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                // probe PATH
                var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var file = Path.GetFileName(candidate);
                foreach (var dir in pathDirs)
                {
                    var probe = Path.Combine(dir, file);
                    if (File.Exists(probe))
                        return probe;
                }
            }
            catch
            {
                // invalid path characters etc. - try the next candidate
            }
        }

        return null;
    }

    /// <summary>Runs extract-xiso for a single image into destDir (created if missing).</summary>
    public static void Extract(string extractor, string iso, string destDir, Action<string>? onOutput = null)
    {
        if (!File.Exists(iso))
            throw new FileNotFoundException($"Disc image not found:{Environment.NewLine}{iso}");
        if (!File.Exists(extractor))
            throw new FileNotFoundException($"extract-xiso.exe not found:{Environment.NewLine}{extractor}");

        Directory.CreateDirectory(destDir);

        var psi = new ProcessStartInfo
        {
            FileName = extractor,
            Arguments = $"-x -d \"{destDir}\" \"{iso}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start extract-xiso.");
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                onOutput?.Invoke(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"extract-xiso exited with code {process.ExitCode}.");
    }
}
