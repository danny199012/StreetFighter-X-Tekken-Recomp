using System.IO;
using System.Windows;
using RecompLauncher.Services;

namespace RecompLauncher;

public partial class App : Application
{
    /// <summary>
    /// Headless helper for testing/power users:
    ///   RecompLauncher.exe --extract-iso &lt;profileDir&gt; &lt;image.iso&gt; [destDir]
    /// Runs the Add Disc extraction without showing the UI.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        var args = e.Args;
        if (args.Length >= 3 && args[0] == "--extract-iso")
        {
            var profileDir = Path.GetFullPath(args[1]);
            var iso = Path.GetFullPath(args[2]);
            var profile = new ProfileService().Load(Path.Combine(profileDir, "launcher.json"));
            var root = !string.IsNullOrEmpty(profile.Game.GameDataRoot)
                ? Path.IsPathRooted(profile.Game.GameDataRoot)
                    ? profile.Game.GameDataRoot
                    : Path.Combine(profileDir, profile.Game.GameDataRoot)
                : Path.Combine(profileDir, profile.Game.PayloadDir is "" ? "game" : profile.Game.PayloadDir);
            var dest = args.Length >= 4 ? Path.GetFullPath(args[3]) : root;

            var extractor = XisoService.ResolveExtractor(profile.Game.ExtractXisoPath, profileDir)
                ?? throw new FileNotFoundException(
                    "extract-xiso.exe not found (launcher folder, profile folder, or PATH).");

            XisoService.Extract(extractor, iso, dest, line => Console.WriteLine(line));
            Console.WriteLine($"Extracted to {dest}");
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }
}
