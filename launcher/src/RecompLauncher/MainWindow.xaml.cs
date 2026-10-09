using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RecompLauncher.Models;
using RecompLauncher.Services;

namespace RecompLauncher;

/// <summary>
/// Profile-driven launcher window. All per-game knowledge lives in
/// launcher.json; this code stays game-agnostic.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ProfileService _profiles = new();
    private readonly GameLauncherService _launcher = new();
    private readonly ModsService _mods = new();

    private LauncherProfile _profile = new();
    private string? _profilePath;
    private string? _exePath;
    private string? _gameRootPath;
    private string? _configPath;
    private string? _bannerPath;
    private string? _modsDir;
    private AppState _state = new();

    private Dictionary<string, string> _configValues = new();
    private readonly ObservableCollection<string> _navItems = new();
    private readonly Dictionary<string, Panel> _tabPanels = new();
    private readonly List<ItemUi> _items = new();
    private readonly List<ModUi> _modEntries = new();
    private ListBox? _modsList;
    private TextBlock? _modsStatus;
    private TextBlock? _modsNote;
    private bool _loading;

    /// <summary>One settings row: schema item + live control + dirty tracking.</summary>
    private class ItemUi
    {
        public required SettingItem Item { get; init; }
        public required FrameworkElement Row { get; set; }
        public required Func<string> GetValue { get; set; }
        public required Action<string> SetValue { get; set; }
        /// <summary>Error message, or null when the current value is valid.</summary>
        public Func<string?, string?> Validate { get; set; } = _ => null;
        public string Initial = "";
        public bool Existed;
        public bool Dirty;
    }

    /// <summary>One row in the mods list.</summary>
    private class ModUi
    {
        public string Path = "";
        public bool External;
        public bool Enabled;
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    }

    public MainWindow()
    {
        InitializeComponent();
        NavList.ItemsSource = _navItems;
        _state = _profiles.LoadState();

        var located = _profiles.LocateProfile(Environment.GetCommandLineArgs());
        if (located is null)
        {
            ShowWelcome();
        }
        else
        {
            TryLoadProfile(located, quiet: true);
        }
    }

    // ------------------------------------------------------------------
    // Profile loading / UI construction
    // ------------------------------------------------------------------

    private void ShowWelcome()
    {
        _navItems.Clear();
        _tabPanels.Clear();
        _items.Clear();
        _modEntries.Clear();
        SettingsHost.Children.Clear();
        NavList.IsEnabled = false;

        var open = new Button { Content = "Open launcher.json profile...", Style = FindResource("PrimaryButton") as Style };
        open.Click += (_, _) => OpenProfileDialog();
        var create = new Button { Content = "Create a profile for a new game...", Margin = new Thickness(0, 8, 0, 0) };
        create.Click += (_, _) => NewProfileDialog();
        SettingsHost.Children.Add(new StackPanel
        {
            Margin = new Thickness(0, 24, 0, 0),
            Children =
            {
                new TextBlock
                {
                    Text = "No profile loaded",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                new TextBlock
                {
                    Text = "Pick the launcher.json that belongs to your recomp, or create one. The launcher remembers it for next time.",
                    Foreground = FindResource("MutedBrush") as Brush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                },
                open,
                create
            }
        });
    }

    private void TryLoadProfile(string path, bool quiet = false)
    {
        try
        {
            LoadProfile(path);
            Status("Profile loaded.");
        }
        catch (Exception ex)
        {
            ShowWelcome();
            Status($"Failed to load profile: {ex.Message}", error: true);
            if (!quiet)
                MessageBox.Show(this, ex.Message, "Recomp Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProfile(string path)
    {
        _profilePath = path;
        _profile = _profiles.Load(path);

        _exePath = _profiles.Resolve(_profile.Game.Exe, path);
        _gameRootPath = _profiles.Resolve(_profile.Game.GameDataRoot, path);
        _configPath = _profiles.ConfigPathFor(_profile, path);
        _bannerPath = _profiles.Resolve(_profile.Game.Banner, path);
        _modsDir = _profiles.ModsDirFor(_profile, path);

        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        _configValues = CvarToml.Read(_configPath);

        LoadModEntries();
        BuildUi();
        RefreshHeader();
        RememberProfile();
        LaunchButton.IsEnabled = true;
        SaveButton.IsEnabled = false;
    }

    private void LoadModEntries()
    {
        if (_profilePath is null || _modsDir is null)
            return;

        var persisted = ProfileService.StateFor(_state, _profilePath).Mods;
        var installed = _mods.DiscoverInstalled(_modsDir);

        var merged = new List<ModUi>();
        // Persisted entries first (keeps order); drop entries whose folder vanished.
        foreach (var entry in persisted)
        {
            if (Directory.Exists(entry.Path))
                merged.Add(new ModUi { Path = entry.Path, External = entry.External, Enabled = entry.Enabled });
        }

        // Newly dropped-in mod folders appear at the end, disabled.
        foreach (var folder in installed.Where(f => merged.All(m =>
                     !string.Equals(m.Path, f, StringComparison.OrdinalIgnoreCase))))
            merged.Add(new ModUi { Path = folder, External = false, Enabled = false });

        _modEntries.Clear();
        _modEntries.AddRange(merged);
        PersistMods();
    }

    private void PersistMods()
    {
        if (_profilePath is null)
            return;
        ProfileService.StateFor(_state, _profilePath).Mods = _modEntries
            .Select(m => new ModEntry { Path = m.Path, External = m.External, Enabled = m.Enabled })
            .ToList();
        _state.LastProfile = _profilePath;
        _profiles.SaveState(_state);
    }

    private void BuildUi()
    {
        _loading = true;
        _navItems.Clear();
        _tabPanels.Clear();
        _items.Clear();

        var tabs = new List<SettingsTab>(_profile.Tabs);

        // Synthesized Profile tab when the game declares username cvars.
        var profileItems = new List<SettingItem>();
        if (!string.IsNullOrWhiteSpace(_profile.Game.UsernameCvar))
        {
            profileItems.Add(new SettingItem
            {
                Cvar = _profile.Game.UsernameCvar,
                Label = "Profile name",
                Type = "text",
                Default = "",
                Hint = "The gamertag / profile name the game shows (leave empty for the runtime default)."
            });
        }

        if (!string.IsNullOrWhiteSpace(_profile.Game.OnlineUsernameCvar))
        {
            profileItems.Add(new SettingItem
            {
                Cvar = _profile.Game.OnlineUsernameCvar,
                Label = "Online username",
                Type = "text",
                Default = "",
                Hint = "Used by online features when the recomp implements them."
            });
        }

        if (profileItems.Count > 0)
            tabs.Insert(0, new SettingsTab { Name = "Profile", Settings = profileItems });

        foreach (var tab in tabs)
        {
            var panel = new StackPanel();
            foreach (var item in tab.Settings)
                panel.Children.Add(BuildRow(item));
            _tabPanels[tab.Name] = panel;
            _navItems.Add(tab.Name);
        }

        _tabPanels["Mods"] = BuildModsPanel();
        _navItems.Add("Mods");

        _tabPanels["Advanced"] = BuildAdvancedPanel();
        _navItems.Add("Advanced");

        NavList.IsEnabled = true;
        NavList.SelectedIndex = 0;
        ApplyAdvancedVisibility();
        _loading = false;
        UpdateButtonStates();
    }

    private Border BuildRow(SettingItem item)
    {
        string InitialValue() => _configValues.TryGetValue(item.Cvar, out var v)
            ? v
            : item.Default ?? "";

        var value = InitialValue();
        var ui = new ItemUi
        {
            Item = item,
            Row = null!,
            GetValue = () => "",
            SetValue = _ => { },
            Initial = value,
            Existed = _configValues.ContainsKey(item.Cvar),
            Dirty = false
        };

        var labelBlock = new TextBlock
        {
            Text = item.Label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        };
        if (item.Advanced)
            labelBlock.Text += "  •";

        var control = BuildControl(item, ui);
        var hintBlock = new TextBlock
        {
            Text = item.Hint ?? "",
            Style = FindResource("HintText") as Style,
            Margin = new Thickness(0, 3, 0, 0)
        };
        if (string.IsNullOrEmpty(item.Hint))
            hintBlock.Visibility = Visibility.Collapsed;

        var content = new StackPanel();
        content.Children.Add(labelBlock);
        content.Children.Add(control);
        content.Children.Add(hintBlock);

        var border = new Border
        {
            Padding = new Thickness(0, 0, 0, 16),
            Child = content
        };
        ui.Row = border;
        _items.Add(ui);
        return border;
    }

    private Control BuildControl(SettingItem item, ItemUi ui)
    {
        switch (item.Type)
        {
            case "bool":
            {
                var box = new CheckBox { Content = "" };
                box.IsChecked = ui.Initial.Equals("true", StringComparison.OrdinalIgnoreCase);
                box.Checked += (_, _) => MarkDirty(ui);
                box.Unchecked += (_, _) => MarkDirty(ui);
                ui.GetValue = () => box.IsChecked == true ? "true" : "false";
                ui.SetValue = v => box.IsChecked = v.Equals("true", StringComparison.OrdinalIgnoreCase);
                return box;
            }

            case "int":
            {
                var box = new TextBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
                box.Text = ui.Initial;
                box.TextChanged += (_, _) => MarkDirty(ui);
                ui.GetValue = () => box.Text.Trim();
                ui.SetValue = v => box.Text = v;
                ui.Validate = text =>
                {
                    if (!int.TryParse(text, out var n))
                        return "must be a whole number";
                    if (item.Min is { } min && n < min) return $"minimum {min}";
                    if (item.Max is { } max && n > max) return $"maximum {max}";
                    return null;
                };
                return box;
            }

            case "double":
            {
                var box = new TextBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
                box.Text = ui.Initial;
                box.TextChanged += (_, _) => MarkDirty(ui);
                ui.GetValue = () => box.Text.Trim();
                ui.SetValue = v => box.Text = v;
                ui.Validate = text =>
                {
                    if (!double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var n))
                        return "must be a number";
                    if (item.Min is { } min && n < min) return $"minimum {min}";
                    if (item.Max is { } max && n > max) return $"maximum {max}";
                    return null;
                };
                return box;
            }

            case "text":
            {
                var box = new TextBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
                box.Text = ui.Initial;
                box.TextChanged += (_, _) => MarkDirty(ui);
                ui.GetValue = () => box.Text.Trim();
                ui.SetValue = v => box.Text = v;
                ui.Validate = text =>
                {
                    if (text is not null && text.Contains('"'))
                        return "quotes are not allowed";
                    return null;
                };
                return box;
            }

            case "choice":
            {
                var combo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var choice in item.Choices)
                    combo.Items.Add(new ComboEntry(choice.Value, choice.Label));
                var current = new ComboEntry(ui.Initial,
                    item.Choices.FirstOrDefault(c => c.Value == ui.Initial)?.Label ?? ui.Initial);
                combo.SelectedItem = current;
                if (combo.SelectedItem is null && !string.IsNullOrEmpty(ui.Initial))
                    combo.Items.Add(current); // value present in config but not offered by the profile
                combo.SelectionChanged += (_, _) => MarkDirty(ui);
                ui.GetValue = () => (combo.SelectedItem as ComboEntry)?.Value ?? "";
                ui.SetValue = v =>
                {
                    var match = combo.Items.OfType<ComboEntry>().FirstOrDefault(e => e.Value == v);
                    if (match is not null)
                        combo.SelectedItem = match;
                };
                ui.Validate = text => string.IsNullOrEmpty(text) ? "choose a value" : null;
                return combo;
            }

            case "resolution":
            case "string":
            default:
            {
                var combo = new ComboBox
                {
                    Width = 260,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    IsEditable = true
                };
                foreach (var preset in item.Presets)
                    combo.Items.Add(preset);
                if (item.Type != "resolution" && item.Choices.Count > 0)
                    foreach (var choice in item.Choices)
                        combo.Items.Add(choice.Value);
                combo.Text = ui.Initial;
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((_, _) => MarkDirty(ui)));
                ui.GetValue = () => combo.Text.Trim();
                ui.SetValue = v => combo.Text = v;
                if (item.Type == "resolution")
                    ui.Validate = text =>
                    {
                        if (string.IsNullOrEmpty(text))
                            return "enter WIDTHxHEIGHT";
                        var parts = text.Split('x', 'X');
                        return parts.Length == 2 && parts.All(p => p.Length > 0 && p.All(char.IsDigit))
                            ? null
                            : "format: WIDTHxHEIGHT, e.g. 1280x720";
                    };
                return combo;
            }
        }
    }

    private void MarkDirty(ItemUi ui)
    {
        if (_loading)
            return;
        ui.Dirty = true;
        UpdateButtonStates();
    }

    private record ComboEntry(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    // ------------------------------------------------------------------
    // Mods tab
    // ------------------------------------------------------------------

    private Panel BuildModsPanel()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Mods",
            FontWeight = FontWeights.Bold,
            FontSize = 16,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Each mod is a folder of files that replaces or adds files in the game data. " +
                   "On launch, the launcher builds a staged copy of the game (hard links - fast, and the " +
                   "original data is never modified) and applies enabled mods on top. Mods higher in the " +
                   "list win file conflicts.",
            Style = FindResource("HintText") as Style,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _modsList = new ListBox
        {
            MinHeight = 180,
            MaxHeight = 320,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        RebuildModsList();
        panel.Children.Add(_modsList);

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var add = new Button { Content = "Add mod folder...", Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
        add.Click += (_, _) => AddModFolder();
        var remove = new Button { Content = "Remove", Padding = new Thickness(10, 5, 10, 5), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        remove.Click += (_, _) => RemoveSelectedMod();
        var up = new Button { Content = "▲", Padding = new Thickness(10, 5, 10, 5), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        up.Click += (_, _) => MoveSelectedMod(-1);
        var down = new Button { Content = "▼", Padding = new Thickness(10, 5, 10, 5), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        down.Click += (_, _) => MoveSelectedMod(1);
        var open = new Button { Content = "Open folder", Padding = new Thickness(10, 5, 10, 5), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        open.Click += (_, _) => OpenSelectedModFolder();
        row1.Children.Add(add);
        row1.Children.Add(remove);
        row1.Children.Add(up);
        row1.Children.Add(down);
        row1.Children.Add(open);
        panel.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var openModsDir = new Button { Content = "Open mods folder", Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
        openModsDir.Click += (_, _) =>
        {
            if (_modsDir is null) return;
            Directory.CreateDirectory(_modsDir);
            Process.Start(new ProcessStartInfo(_modsDir) { UseShellExecute = true });
        };
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(10, 5, 10, 5), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        refresh.Click += (_, _) =>
        {
            LoadModEntries();
            RebuildModsList();
            UpdateModsStatus();
        };
        _modsStatus = new TextBlock
        {
            Style = FindResource("HintText") as Style,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        row2.Children.Add(openModsDir);
        row2.Children.Add(refresh);
        row2.Children.Add(_modsStatus);
        panel.Children.Add(row2);

        UpdateModsStatus();
        return panel;
    }

    private void RebuildModsList()
    {
        if (_modsList is null)
            return;
        _modsList.Items.Clear();
        foreach (var mod in _modEntries)
        {
            var modRef = mod;
            var check = new CheckBox { VerticalContentAlignment = VerticalAlignment.Center, IsChecked = mod.Enabled };
            check.Checked += (_, _) => { modRef.Enabled = true; PersistMods(); UpdateModsStatus(); };
            check.Unchecked += (_, _) => { modRef.Enabled = false; PersistMods(); UpdateModsStatus(); };

            var name = new TextBlock { Text = mod.Name, FontWeight = FontWeights.SemiBold };
            var path = new TextBlock
            {
                Text = (mod.External ? "[external]  " : "") + mod.Path,
                Style = FindResource("PathText") as Style
            };
            var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(name);
            text.Children.Add(path);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(check);
            row.Children.Add(text);

            _modsList.Items.Add(row);
        }
    }

    private void UpdateModsStatus()
    {
        if (_modsStatus is null)
            return;
        var enabled = _modEntries.Count(m => m.Enabled);
        _modsStatus.Text = _modEntries.Count == 0
            ? $"No mods found. Drop mod folders into:{Environment.NewLine}{_modsDir}"
            : $"{enabled} of {_modEntries.Count} enabled";
        if (_modsNote is not null)
        {
            _modsNote.Text = enabled > 0
                ? $"Mods: {enabled} enabled - the game launches from a staged copy of the game data."
                : "Mods: none enabled.";
        }
    }

    private void AddModFolder()
    {
        if (_modsDir is null)
            return;
        var dialog = new OpenFolderDialog { Title = "Select a mod folder" };
        if (dialog.ShowDialog(this) == true)
        {
            var path = dialog.FolderName;
            if (_modEntries.Any(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
                return;
            _modEntries.Add(new ModUi { Path = path, External = true, Enabled = true });
            PersistMods();
            RebuildModsList();
            UpdateModsStatus();
        }
    }

    private void RemoveSelectedMod()
    {
        if (_modsList?.SelectedItem is null || _modsList.SelectedIndex >= _modEntries.Count)
            return;
        var mod = _modEntries[_modsList.SelectedIndex];
        if (!mod.External)
        {
            MessageBox.Show(this,
                "This mod lives in the mods folder - removing it would delete the folder.\n" +
                "Delete it in Explorer instead (or just untick it).",
                "Installed mod", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _modEntries.RemoveAt(_modsList.SelectedIndex);
        PersistMods();
        RebuildModsList();
        UpdateModsStatus();
    }

    private void MoveSelectedMod(int delta)
    {
        if (_modsList is null || _modsList.SelectedIndex < 0)
            return;
        var index = _modsList.SelectedIndex;
        var target = index + delta;
        if (target < 0 || target >= _modEntries.Count)
            return;
        (_modEntries[index], _modEntries[target]) = (_modEntries[target], _modEntries[index]);
        PersistMods();
        RebuildModsList();
        _modsList.SelectedIndex = target;
        UpdateModsStatus();
    }

    private void OpenSelectedModFolder()
    {
        if (_modsList?.SelectedItem is null || _modsList.SelectedIndex >= _modEntries.Count)
            return;
        var mod = _modEntries[_modsList.SelectedIndex];
        if (Directory.Exists(mod.Path))
            Process.Start(new ProcessStartInfo(mod.Path) { UseShellExecute = true });
    }

    // ------------------------------------------------------------------
    // Advanced tab (same for every game)
    // ------------------------------------------------------------------

    private Panel BuildAdvancedPanel()
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = "Launch command",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var exeRun = new TextBlock { Style = FindResource("PathText") as Style, TextWrapping = TextWrapping.Wrap };
        exeRun.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ExePathDisplay))
        {
            Source = this
        });
        panel.Children.Add(exeRun);

        var fixedArgs = _profile.Game.Args.Count > 0
            ? string.Join(" ", _profile.Game.Args)
            : "(none)";
        panel.Children.Add(new TextBlock
        {
            Text = $"Fixed args:  {fixedArgs}",
            Style = FindResource("PathText") as Style,
            Margin = new Thickness(0, 4, 0, 0)
        });

        _modsNote = new TextBlock
        {
            Style = FindResource("HintText") as Style,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_modsNote);

        panel.Children.Add(new TextBlock
        {
            Text = "Extra arguments",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 6)
        });
        var extraBox = new TextBox();
        var existing = _profilePath is not null
            ? ProfileService.StateFor(_state, _profilePath).ExtraArgs
            : "";
        extraBox.Text = existing;
        extraBox.TextChanged += (_, _) =>
        {
            if (_profilePath is null) return;
            ProfileService.StateFor(_state, _profilePath).ExtraArgs = extraBox.Text;
            _state.LastProfile = _profilePath;
            _profiles.SaveState(_state);
        };
        panel.Children.Add(extraBox);
        panel.Children.Add(new TextBlock
        {
            Text = "Appended to the launch command. Quote values with spaces, e.g. --user_data_root \"D:\\saves\".",
            Style = FindResource("HintText") as Style,
            Margin = new Thickness(0, 4, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Config file",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 6)
        });
        var configLine = new TextBlock { Style = FindResource("PathText") as Style, TextWrapping = TextWrapping.Wrap };
        configLine.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ConfigPathDisplay))
        {
            Source = this
        });
        panel.Children.Add(configLine);
        panel.Children.Add(new TextBlock
        {
            Text = "This is the runtime's own cvar file - the in-game F4 settings dialog reads and writes the same values, so the two stay in sync.",
            Style = FindResource("HintText") as Style,
            Margin = new Thickness(0, 4, 0, 0)
        });

        UpdateModsStatus();
        return panel;
    }

    public string ExePathDisplay => _exePath is null ? "" : $"\"{_exePath}\"";

    public string ConfigPathDisplay => _configPath ?? "";

    // ------------------------------------------------------------------
    // Save / launch / reset
    // ------------------------------------------------------------------

    private string? FirstValidationError()
    {
        foreach (var ui in _items)
        {
            var error = ui.Validate(ui.GetValue());
            if (error is not null)
                return $"'{ui.Item.Label}': {error}";
        }

        return null;
    }

    private void UpdateButtonStates()
    {
        if (FirstValidationError() is { } message)
        {
            Status(message, error: true);
            SaveButton.IsEnabled = false;
            LaunchButton.IsEnabled = false;
        }
        else
        {
            SaveButton.IsEnabled = _items.Any(i => i.Dirty);
            LaunchButton.IsEnabled = true;
            if (SaveButton.IsEnabled)
                Status("Unsaved changes.");
        }
    }

    private bool Save()
    {
        if (FirstValidationError() is { } message)
        {
            Status(message, error: true);
            return false;
        }

        if (_configPath is null)
            return false;

        var toWrite = new Dictionary<string, string>();
        foreach (var ui in _items)
        {
            if (ui.Dirty || ui.Existed)
                toWrite[ui.Item.Cvar] = ui.GetValue();
        }

        // Untouched, never-present settings stay out of the file so the
        // runtime's compiled-in default applies.
        if (toWrite.Count > 0)
        {
            try
            {
                CvarToml.Write(_configPath, toWrite);
            }
            catch (Exception ex)
            {
                Status($"Could not write config: {ex.Message}", error: true);
                return false;
            }
        }

        // Refresh state so future saves are incremental.
        _configValues = CvarToml.Read(_configPath);
        foreach (var ui in _items)
        {
            if (_configValues.TryGetValue(ui.Item.Cvar, out var current))
            {
                ui.Initial = current;
                ui.Existed = true;
            }
            else
            {
                ui.Existed = false;
            }

            ui.Dirty = false;
        }

        SaveButton.IsEnabled = false;
        Status("Settings saved.");
        return true;
    }

    private void Launch()
    {
        if (_profilePath is null)
            return;
        if (!Save())
            return;

        // write out the embedded game binaries on first launch / when missing
        try
        {
            var payloadDir = string.IsNullOrWhiteSpace(_profile.Game.PayloadDir)
                ? "game"
                : _profile.Game.PayloadDir;
            var written = PayloadService.ExtractIfNeeded(_profilePath, payloadDir);
            if (written > 0)
                Status($"Extracted {written} game file(s) from launcher payload.");
        }
        catch (Exception ex)
        {
            Status($"Payload extraction failed: {ex.Message}", error: true);
            MessageBox.Show(this,
                "Could not extract the embedded game files - the game was not launched.\n\n" + ex.Message,
                "Payload", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string? rootOverride = null;
        var enabledMods = _modEntries.Where(m => m.Enabled).Select(m => m.Path).ToList();
        if (enabledMods.Count > 0 && _gameRootPath is not null && _modsDir is not null)
        {
            try
            {
                var staging = Path.Combine(_modsDir, ".staging");
                rootOverride = _mods.Stage(_gameRootPath, enabledMods, staging);
                Status($"Staged {enabledMods.Count} mod(s).");
            }
            catch (Exception ex)
            {
                Status($"Mod staging failed: {ex.Message}", error: true);
                MessageBox.Show(this,
                    "Mod staging failed - the game was not launched.\n\n" + ex.Message,
                    "Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            var extra = _profilePath is not null
                ? ProfileService.StateFor(_state, _profilePath).ExtraArgs
                : "";
            var process = _launcher.Start(_profile, _profilePath!, extra, rootOverride);
            Status($"Launched {process.ProcessName} (PID {process.Id}).");
        }
        catch (Exception ex)
        {
            Status(ex.Message, error: true);
            MessageBox.Show(this, ex.Message, "Launch failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnAddDisc(object sender, RoutedEventArgs e)
    {
        if (_profilePath is null)
        {
            MessageBox.Show(this, "Open a profile first.", "Add disc",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select your Xbox 360 disc image",
            Filter = "Xbox 360 images (*.iso)|*.iso|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var extractor = XisoService.ResolveExtractor(_profile.Game.ExtractXisoPath, _profilePath);
        if (extractor is null)
        {
            MessageBox.Show(this,
                "extract-xiso.exe was not found.\n\n" +
                "Put extract-xiso.exe next to the launcher (or inside the profile folder), " +
                "or set game.extractXisoPath in launcher.json.\n\n" +
                "extract-xiso is a free, open-source Xbox 360 image extractor.",
                "Add disc", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var iso = dialog.FileName;
        var root = new ProfileService().Resolve(_profile.Game.GameDataRoot, _profilePath);
        if (string.IsNullOrEmpty(root))
        {
            MessageBox.Show(this,
                "The profile has no game data root set (game.gameDataRoot in launcher.json).",
                "Add disc", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AddDiscButton.IsEnabled = false;
        Status("Extracting disc content - this can take several minutes...");

        Task.Run(() =>
        {
            var lines = 0;
            var lastLine = "";
            XisoService.Extract(extractor, iso, root, line =>
            {
                lastLine = line;
                if (Interlocked.Increment(ref lines) % 50 == 0)
                    Dispatcher.BeginInvoke(() => Status($"Extracting: {line}"));
            });
            return lastLine;
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                AddDiscButton.IsEnabled = true;
                if (task.IsFaulted)
                {
                    var message = task.Exception?.InnerException?.Message ?? task.Exception?.Message ?? "unknown error";
                    Status($"Disc extraction failed: {message}", error: true);
                    MessageBox.Show(this, message, "Add disc failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    Status($"Disc content extracted to {root}");
                    MessageBox.Show(this,
                        $"Disc content extracted to:\n{root}\n\nThe game is ready to launch.",
                        "Add disc", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ResetAll()
    {
        if (_configPath is null || _items.Count == 0)
            return;

        var confirm = MessageBox.Show(this,
            "Remove every launcher-managed setting from the config file?\n" +
            "The game falls back to its built-in defaults.",
            "Reset to defaults", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        CvarToml.Remove(_configPath, _items.Select(i => i.Item.Cvar));
        TryLoadProfile(_profilePath!, quiet: true);
        Status("Reset to built-in defaults.");
    }

    // ------------------------------------------------------------------
    // Header / status helpers
    // ------------------------------------------------------------------

    private void RefreshHeader()
    {
        GameTitle.Text = _profile.Game.Name;
        Title = string.IsNullOrWhiteSpace(_profile.Game.Name)
            ? "Recomp Launcher"
            : $"{_profile.Game.Name} — Recomp Launcher";
        GameSubtitle.Text = _profile.Game.Subtitle
                            ?? (_profilePath is null ? "" : Path.GetFileName(_profilePath));
        GameSubtitle.ToolTip = _profilePath;

        ExeStatus.Text = _exePath is not null && File.Exists(_exePath)
            ? "exe ✓"
            : "exe ✗ not found";
        ExeStatus.Foreground = _exePath is not null && File.Exists(_exePath)
            ? FindResource("OkBrush") as Brush
            : FindResource("ErrorBrush") as Brush;

        if (_gameRootPath is null)
        {
            DataRootStatus.Text = "";
        }
        else
        {
            var ok = Directory.Exists(_gameRootPath);
            DataRootStatus.Text = ok ? "game data ✓" : "game data ✗ not found";
            DataRootStatus.Foreground = ok
                ? FindResource("OkBrush") as Brush
                : FindResource("ErrorBrush") as Brush;
        }

        OverlayHint.Visibility = Visibility.Visible;

        if (_bannerPath is not null && File.Exists(_bannerPath))
        {
            try
            {
                BannerImage.Source = new BitmapImage(new Uri(_bannerPath));
                BannerImage.Visibility = Visibility.Visible;
            }
            catch
            {
                BannerImage.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            BannerImage.Visibility = Visibility.Collapsed;
        }
    }

    private void Status(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? FindResource("ErrorBrush") as Brush
            : FindResource("MutedBrush") as Brush;
    }

    private void RememberProfile()
    {
        if (_profilePath is null)
            return;
        _state.LastProfile = _profilePath;
        _profiles.SaveState(_state);
    }

    private void ApplyAdvancedVisibility()
    {
        var show = ShowAdvanced.IsChecked == true;
        foreach (var ui in _items)
            ui.Row.Visibility = ui.Item.Advanced && !show ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Event handlers
    // ------------------------------------------------------------------

    private void OnNavSelected(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not string name || !_tabPanels.TryGetValue(name, out var panel))
            return;

        SettingsHost.Children.Clear();
        SettingsHost.Children.Add(panel);
    }

    private void OnAdvancedChanged(object sender, RoutedEventArgs e) => ApplyAdvancedVisibility();

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private void OnLaunch(object sender, RoutedEventArgs e) => Launch();

    private void OnReset(object sender, RoutedEventArgs e) => ResetAll();

    private void OpenProfileDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select launcher.json",
            Filter = "Launcher profiles (launcher.json, *.json)|launcher.json;*.json|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
            TryLoadProfile(dialog.FileName);
    }

    private void OnOpenProfile(object sender, RoutedEventArgs e) => OpenProfileDialog();

    private void NewProfileDialog()
    {
        var wizard = new NewProfileWindow { Owner = this };
        wizard.ShowDialog();
        if (wizard.CreatedProfilePath is { } created && File.Exists(created))
            TryLoadProfile(created);
    }

    private void OnNewProfile(object sender, RoutedEventArgs e) => NewProfileDialog();

    private void OnReloadProfile(object sender, RoutedEventArgs e)
    {
        if (_profilePath is not null)
            TryLoadProfile(_profilePath);
    }

    private void OnOpenConfig(object sender, RoutedEventArgs e)
    {
        if (_configPath is null)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        if (!File.Exists(_configPath))
            File.WriteAllText(_configPath, "# RecompLauncher managed cvar configuration." + Environment.NewLine);
        Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true });
    }

    private void OnOpenConfigFolder(object sender, RoutedEventArgs e)
    {
        if (_configPath is null)
            return;
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Recomp Launcher\n" +
            "A profile-driven launcher for ReXGlue recomp projects.\n\n" +
            "Drop a launcher.json next to the game (or create one via File - New profile)\n" +
            "to configure settings, manage mods, and launch. The same executable works\n" +
            "for any recomp.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F5)
            {
                OnReloadProfile(this, e);
                e.Handled = true;
            }
        };
    }
}
