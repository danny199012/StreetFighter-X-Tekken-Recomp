using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using RecompLauncher.Models;

namespace RecompLauncher;

/// <summary>
/// Wizard that generates a launcher.json for a new recomp. Writes paths
/// relative to the profile when possible and copies the banner image next
/// to it, so the resulting folder is self-contained.
/// </summary>
public partial class NewProfileWindow : Window
{
    /// <summary>Set when a profile was created; the owner loads it.</summary>
    public string? CreatedProfilePath { get; private set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public NewProfileWindow()
    {
        InitializeComponent();
    }

    private void OnBrowseExe(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the recomp game executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            ExeBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void OnBrowseDataRoot(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the extracted game data folder" };
        if (dialog.ShowDialog(this) == true)
            DataRootBox.Text = dialog.FolderName;
    }

    private void OnBrowseBanner(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a banner image",
            Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            BannerBox.Text = dialog.FileName;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void Fail(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var exe = ExeBox.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            Fail("Enter a game name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            Fail("Select the game executable (it must exist).");
            return;
        }

        var dataRoot = DataRootBox.Text.Trim().Trim('"');
        if (dataRoot.Length > 0 && !Directory.Exists(dataRoot))
        {
            Fail("Game data root does not exist.");
            return;
        }

        var banner = BannerBox.Text.Trim().Trim('"');
        if (banner.Length > 0 && !File.Exists(banner))
        {
            Fail("Banner image does not exist.");
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Save launcher.json",
            Filter = "Launcher profile (launcher.json)|launcher.json",
            FileName = "launcher.json",
            InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))!
        };
        if (saveDialog.ShowDialog(this) != true)
            return;

        var profilePath = Path.GetFullPath(saveDialog.FileName);
        var outDir = Path.GetDirectoryName(profilePath)!;
        Directory.CreateDirectory(outDir);

        var profile = new LauncherProfile
        {
            Schema = 1,
            Game = new GameInfo
            {
                Name = NameBox.Text.Trim(),
                Subtitle = string.IsNullOrWhiteSpace(SubtitleBox.Text) ? null : SubtitleBox.Text.Trim(),
                Exe = Rel(outDir, exe),
                GameDataRoot = dataRoot.Length > 0 ? Rel(outDir, dataRoot) : null,
                Args = new List<string>()
            }
        };

        var gpu = GpuBox.Text.Trim();
        if (gpu.Length > 0)
        {
            profile.Game.Args.Add("--gpu_plugin");
            profile.Game.Args.Add(gpu);
        }

        if (banner.Length > 0)
        {
            var bannerName = "banner" + Path.GetExtension(banner).ToLowerInvariant();
            File.Copy(banner, Path.Combine(outDir, bannerName), true);
            profile.Game.Banner = bannerName;
        }

        // User profile / online username fields. profile_name matches the
        // synthetic-profile cvar convention used by the recomp projects;
        // online_username is adopted by online features when implemented.
        profile.Game.UsernameCvar = "profile_name";
        profile.Game.OnlineUsernameCvar = "online_username";

        profile.Tabs = DefaultTabs.Tabs;

        try
        {
            File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, JsonOptions));
        }
        catch (Exception ex)
        {
            Fail($"Could not write the profile: {ex.Message}");
            return;
        }

        CreatedProfilePath = profilePath;
        Close();
    }

    private static string Rel(string baseDir, string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            var relative = Path.GetRelativePath(baseDir, full);
            return relative.StartsWith('.') ? full : relative;
        }
        catch
        {
            return full;
        }
    }
}
