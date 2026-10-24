using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Themes;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private void BuildSettingsView()
        {
            double offset = SettingsScroll.VerticalOffset;
            SettingsContent.Children.Clear();
            SettingsContent.Children.Add(PageHeader("Settings", ""));

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(SettingsCard("Profile", ProfileSection()));
            left.Children.Add(SettingsCard("Steam login for downloads", AccountSection()));
            left.Children.Add(SettingsCard("In-game name", InGameNameSection()));
            left.Children.Add(SettingsCard("Maintenance", MaintenanceSection()));

            var right = new StackPanel();
            right.Children.Add(SettingsCard("Appearance", AppearanceSection()));
            right.Children.Add(SettingsCard("Downloads", DownloadsSection()));
            right.Children.Add(SettingsCard("Behavior", BehaviorSection()));
            right.Children.Add(SettingsCard("About", AboutSection()));

            cols.Children.Add(left);
            cols.Children.Add(right.Col(2));
            SettingsContent.Children.Add(cols);
            SettingsScroll.ScrollToVerticalOffset(offset);
        }

        private static Border SettingsCard(string title, UIElement content)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text(title, 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 8));
            sp.Children.Add(content);
            return UiFactory.Card(sp).Margin(0, 0, 0, 16);
        }

        // ── Profile ──────────────────────────────────────────────────────────

        private UIElement ProfileSection()
        {
            var sp = new StackPanel();

            // Display name (works with or without Steam sign-in)
            sp.Children.Add(UiFactory.Text("Display name", 13, "Brush.Fg"));
            sp.Children.Add(UiFactory.Text("Shown in the sidebar, on your profile and on the leaderboard. Leave it empty to use your Steam name.", 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 0));
            var nameBox = UiFactory.Input(S.DisplayName, string.IsNullOrWhiteSpace(S.SteamPersona) ? "Display name" : S.SteamPersona);
            nameBox.MaxLength = MaxDisplayName;
            nameBox.Margin = new Thickness(0, 10, 0, 0);
            var saveName = UiFactory.Button("Save", "Btn.Secondary", (_, _) =>
            {
                string name = nameBox.Text.Trim();
                if (ValidateDisplayName(name) is string problem) { ShowToast(problem, Icons.Warning); return; }
                S.DisplayName = name;
                SaveSettings();
                RefreshProfileArea();
                OnIdentityChanged();
                ShowToast(name.Length == 0 ? "Display name cleared" : "Display name saved", Icons.Check);
            });
            saveName.Margin = new Thickness(8, 10, 0, 0);
            sp.Children.Add(InputWithButton(nameBox, saveName));

            sp.Children.Add(UiFactory.Divider(16, 14));

            if (!_community.IsSignedIn)
            {
                sp.Children.Add(UiFactory.Text("Steam account", 13, "Brush.Fg"));
                sp.Children.Add(UiFactory.Text("Sign in through Steam to get a profile, add friends and appear on the leaderboard. Your stats are tied to your Steam account, so nobody else can take them by typing your name. Steam only shares your public Steam ID with the launcher, never your password.", 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 12));
                var signIn = UiFactory.Button(_steamSignInCts != null ? "Waiting for Steam" : "Sign in through Steam", "Btn.Primary", async (_, _) => await SignInThroughSteamAsync(), Icons.Contact);
                signIn.IsEnabled = _steamSignInCts == null;
                var row = UiFactory.HStack(8, signIn);
                if (_steamSignInCts != null)
                    row.Children.Add(UiFactory.Button("Cancel", "Btn.Ghost", (_, _) => _steamSignInCts?.Cancel()).Margin(8, 0, 0, 0));
                sp.Children.Add(row);
                if (_steamSignInCts != null)
                    sp.Children.Add(UiFactory.Text("Finish signing in on the Steam page that opened in your browser.", 12, "Brush.Fg3", wrap: true).Margin(0, 10, 0, 0));
                return sp;
            }

            var who = new Grid();
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            who.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            who.Children.Add(Avatar(40));
            var whoText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
            whoText.Children.Add(UiFactory.Text(string.IsNullOrWhiteSpace(S.SteamPersona) ? "Steam account" : S.SteamPersona, 14, "Brush.Fg", FontWeights.SemiBold));
            var idText = UiFactory.Text($"Signed in through Steam  ·  {S.SteamId}", 11, "Brush.Fg3").Margin(0, 2, 0, 0);
            whoText.Children.Add(idText);
            who.Children.Add(whoText.Col(1));
            var whoActions = UiFactory.HStack(4,
                UiFactory.IconButton(Icons.Contact, "View your profile", (_, _) => _ = OpenProfileAsync(S.SteamId)),
                UiFactory.IconButton(Icons.OpenExternal, "Open Steam profile", (_, _) => OpenUrl(SteamAuthService.ProfileUrl(S.SteamId))));
            whoActions.VerticalAlignment = VerticalAlignment.Center;
            who.Children.Add(whoActions.Col(2));
            sp.Children.Add(who);

            sp.Children.Add(UiFactory.Text("Bio", 13, "Brush.Fg").Margin(0, 16, 0, 0));
            var bio = new TextBox { Text = S.ProfileBio, Tag = "A line or two about you", MaxLength = MaxBio, Height = 64, Margin = new Thickness(0, 8, 0, 0) };
            bio.Styled("Input.Multiline");
            sp.Children.Add(bio);
            var saveBio = UiFactory.Button("Save bio", "Btn.Secondary", (_, _) =>
            {
                S.ProfileBio = bio.Text.Trim();
                SaveSettings();
                _ = PublishProfileAsync();
                ShowToast("Bio saved", Icons.Check);
            });
            saveBio.HorizontalAlignment = HorizontalAlignment.Left;
            saveBio.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(saveBio);

            sp.Children.Add(UiFactory.Divider(14, 10));
            sp.Children.Add(UiFactory.Text("Stats from before 3.0", 13, "Brush.Fg"));
            sp.Children.Add(UiFactory.Text(LegacyStatusText(), 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 0));

            var signOut = UiFactory.Button("Sign out of Steam", "Btn.Ghost", (_, _) => SignOutOfCommunity(), Icons.Power);
            signOut.HorizontalAlignment = HorizontalAlignment.Left;
            signOut.Margin = new Thickness(-10, 10, 0, 0);
            sp.Children.Add(signOut);
            return sp;
        }

        // ── Steam login for downloads ────────────────────────────────────────

        private UIElement AccountSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text("The Steam account that owns Rainbow Six Siege, used by DepotDownloader. Enter the name you sign in with, not your display name. It is never shown to other players.", 12, "Brush.Fg3", wrap: true));

            var box = UiFactory.Input(S.SteamUsername, "Steam account name");
            box.Margin = new Thickness(0, 12, 0, 0);
            var save = UiFactory.Button("Save", "Btn.Secondary", (_, _) =>
            {
                string name = box.Text.Trim();
                if (name == S.SteamUsername) return;
                bool changed = !string.Equals(name, S.SteamUsername, StringComparison.OrdinalIgnoreCase);
                S.SteamUsername = name;
                if (changed)
                {
                    S.SteamPasswordProtected = "";
                    S.SteamTokenUser = "";
                }
                SaveSettings();
                BuildSettingsView();
                _ = TryLinkLegacyProfileAsync();
                ShowToast(name.Length == 0 ? "Steam account removed" : "Steam account saved", Icons.Check);
            });
            save.Margin = new Thickness(8, 12, 0, 0);
            sp.Children.Add(InputWithButton(box, save));

            sp.Children.Add(UiFactory.SettingRow("Stay signed in", "Lets Steam remember this PC so you don't have to enter your password every time.",
                UiFactory.Switch(S.RememberSteamLogin, on => { S.RememberSteamLogin = on; if (!on) S.SteamTokenUser = ""; SaveSettings(); })).Margin(0, 10, 0, 0));

            bool hasSaved = !string.IsNullOrEmpty(S.SteamPasswordProtected);
            var clear = UiFactory.Button("Forget", "Btn.Secondary", (_, _) =>
            {
                S.SteamPasswordProtected = "";
                SaveSettings();
                BuildSettingsView();
                ShowToast("Saved password removed", Icons.Check);
            });
            clear.IsEnabled = hasSaved;
            sp.Children.Add(UiFactory.SettingRow("Saved password", hasSaved ? "Stored on this PC, encrypted with your Windows account." : "Not saved. You can save it when you next sign in.", clear));

            var signOut = UiFactory.Button("Sign out", "Btn.Secondary", (_, _) =>
            {
                if (_downloads.IsRunning) { ShowToast("A download is using your Steam session.", Icons.Warning); return; }
                if (!ThemedDialog.Confirm(this, "Sign out of Steam", "Removes the saved Steam session and password from this PC. You'll be asked to sign in on your next download.", "Sign out")) return;
                int removed = SteamSession.SignOut();
                S.SteamPasswordProtected = "";
                S.SteamTokenUser = "";
                SaveSettings();
                BuildSettingsView();
                ShowToast(removed > 0 ? "Signed out of Steam" : "No saved Steam session found", Icons.Check);
            });
            sp.Children.Add(UiFactory.SettingRow("Steam session", "Clear everything DepotDownloader remembers about your login.", signOut));
            return sp;
        }

        // ── In-game name ─────────────────────────────────────────────────────

        private UIElement InGameNameSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text($"Used for new installs and when you apply it to all seasons. Up to {LoaderConfig.MaxUsernameLength} characters, no quotes or backslashes.", 12, "Brush.Fg3", wrap: true));
            var box = UiFactory.Input(S.InGameUsername, EffectiveInGameName());
            box.MaxLength = LoaderConfig.MaxUsernameLength;
            box.Margin = new Thickness(0, 12, 0, 0);
            var save = UiFactory.Button("Save", "Btn.Secondary", (_, _) =>
            {
                string name = box.Text.Trim();
                if (name.Length > 0 && LoaderConfig.Validate(name) is string problem) { ShowToast(problem, Icons.Warning); return; }
                S.InGameUsername = name;
                SaveSettings();
                ShowToast("In-game name saved", Icons.Check);
            });
            save.Margin = new Thickness(8, 12, 0, 0);
            sp.Children.Add(InputWithButton(box, save));

            var apply = UiFactory.Button("Apply to all installed seasons", "Btn.Ghost", (_, _) =>
            {
                string name = EffectiveInGameName();
                int ok = 0, failed = 0;
                foreach (var p in Installed)
                {
                    try { LoaderConfig.Write(p, name); ok++; } catch { failed++; }
                }
                ShowToast(failed == 0 ? $"Set {name} on {ok} install{(ok == 1 ? "" : "s")}" : $"Updated {ok}, {failed} failed", Icons.Check);
            }, Icons.Sync);
            apply.HorizontalAlignment = HorizontalAlignment.Left;
            apply.Margin = new Thickness(-10, 8, 0, 0);
            sp.Children.Add(apply);
            return sp;
        }

        // ── Downloads ────────────────────────────────────────────────────────

        private UIElement DownloadsSection()
        {
            var sp = new StackPanel();
            var parallel = new ComboBox { Width = 110 };
            parallel.Styled("Combo");
            foreach (int n in new[] { 4, 8, 16, 24, 32, 48, 64 })
            {
                var item = new ComboBoxItem { Content = n.ToString(), Tag = n };
                parallel.Items.Add(item);
                if (n == S.MaxDownloads) parallel.SelectedItem = item;
            }
            if (parallel.SelectedItem == null)
            {
                var custom = new ComboBoxItem { Content = S.MaxDownloads.ToString(), Tag = S.MaxDownloads };
                parallel.Items.Insert(0, custom);
                parallel.SelectedItem = custom;
            }
            parallel.SelectionChanged += (_, _) =>
            {
                if (parallel.SelectedItem is ComboBoxItem { Tag: int n }) { S.MaxDownloads = n; SaveSettings(); }
            };
            sp.Children.Add(UiFactory.SettingRow("Parallel chunks", "How many pieces download at once. Higher can be faster on good connections.", parallel));

            sp.Children.Add(UiFactory.Text("Library folders", 13, "Brush.Fg").Margin(0, 14, 0, 2));
            sp.Children.Add(UiFactory.Text("New downloads go to the default folder. Seasons in any folder show up in your library.", 12, "Brush.Fg3", wrap: true).Margin(0, 0, 0, 10));
            for (int i = 0; i < S.LibraryFolders.Count; i++)
            {
                string lib = S.LibraryFolders[i];
                bool isDefault = i == 0;
                bool exists = Directory.Exists(lib);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var name = UiFactory.HStack(8, UiFactory.Text(ShortPath(lib), 13, exists ? "Brush.Fg" : "Brush.Fg3"));
                if (isDefault) name.Children.Add(UiFactory.Chip("DEFAULT").Margin(8, 0, 0, 0));
                if (!exists) name.Children.Add(UiFactory.Chip("NOT FOUND").Margin(8, 0, 0, 0));
                text.Children.Add(name);
                text.Children.Add(UiFactory.Text(FreeSpaceText(lib), 11, "Brush.Fg3"));
                row.Children.Add(text);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(UiFactory.IconButton(Icons.Folder, "Open", (_, _) => { Directory.CreateDirectory(lib); OpenFolder(lib); }));
                if (!isDefault)
                {
                    actions.Children.Add(UiFactory.IconButton(Icons.Pin, "Make default", (_, _) =>
                    {
                        S.LibraryFolders.Remove(lib);
                        S.LibraryFolders.Insert(0, lib);
                        SaveSettings();
                        BuildSettingsView();
                    }));
                    actions.Children.Add(UiFactory.IconButton(Icons.Remove, "Remove from list", (_, _) => RemoveLibrary(lib)));
                }
                row.Children.Add(actions.Col(1));
                sp.Children.Add(row);
            }
            var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            buttons.Children.Add(UiFactory.Button("Add folder", "Btn.Secondary", (_, _) => AddLibrary(), Icons.Add).Margin(0, 0, 8, 8));
            buttons.Children.Add(UiFactory.Button("Move all downloads", "Btn.Secondary", async (_, _) => await MoveDownloadsAsync(), Icons.Folder).Margin(0, 0, 8, 8));
            sp.Children.Add(buttons);
            return sp;
        }

        private void AddLibrary()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder for Rainbow Six seasons", UseDescriptionForTitle = true };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            string path = dlg.SelectedPath.TrimEnd('\\');
            if (System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"^Y\d+S\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                path = Path.GetDirectoryName(path)!;
            if (S.LibraryFolders.Any(l => string.Equals(l, path, StringComparison.OrdinalIgnoreCase)))
            {
                ShowToast("That folder is already a library.");
                return;
            }
            if (S.LibraryFolders.Any(l => path.StartsWith(l + "\\", StringComparison.OrdinalIgnoreCase) || l.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase)))
            {
                ThemedDialog.Alert(this, "Overlapping folders", "Library folders can't be inside each other.");
                return;
            }
            S.LibraryFolders.Add(path);
            SaveSettings();
            RefreshInstallState();
            RefreshAllViews();
            BuildSettingsView();
            ShowToast("Library folder added", Icons.Check);
        }

        private void RemoveLibrary(string lib)
        {
            if (_downloads.Queue.Any(q => q.TargetDir.StartsWith(lib, StringComparison.OrdinalIgnoreCase)) ||
                (_downloads.Active?.TargetDir.StartsWith(lib, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                ShowToast("A download is using this folder.", Icons.Warning);
                return;
            }
            if (!ThemedDialog.Confirm(this, "Remove library folder", $"Stop showing seasons from\n{lib}\n\nNo files are deleted.", "Remove")) return;
            S.LibraryFolders.RemoveAll(l => string.Equals(l, lib, StringComparison.OrdinalIgnoreCase));
            SaveSettings();
            RefreshInstallState();
            RefreshAllViews();
            BuildSettingsView();
        }

        private async Task MoveDownloadsAsync()
        {
            if (_downloads.IsRunning || _downloads.Queue.Count > 0) { ShowToast("Finish or clear downloads before moving files.", Icons.Warning); return; }
            if (AnyGameRunning) { ShowToast("Close the game first.", Icons.Warning); return; }
            using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Move every season in the default library to this folder", UseDescriptionForTitle = true };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            string source = _locator.DefaultLibrary;
            string dest = dlg.SelectedPath.TrimEnd('\\');
            if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(source)) { ShowToast("The default library folder doesn't exist yet."); return; }
            if (!ThemedDialog.Confirm(this, "Move downloads", $"Move every season folder from\n{source}\nto\n{dest}\n\nThis can take a while across drives. The new folder becomes your default library.", "Move")) return;

            ShowToast("Moving downloads. This can take a while.", Icons.Folder);
            int moved = 0;
            string? error = null;
            await Task.Run(() =>
            {
                Directory.CreateDirectory(dest);
                foreach (var dir in Directory.GetDirectories(source))
                {
                    string target = Path.Combine(dest, Path.GetFileName(dir));
                    if (Directory.Exists(target)) continue;
                    try
                    {
                        if (string.Equals(Path.GetPathRoot(dir), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
                            Directory.Move(dir, target);
                        else
                        {
                            Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(dir, target);
                        }
                        moved++;
                    }
                    catch (Exception ex) { error = ex.Message; }
                }
            });
            S.LibraryFolders.RemoveAll(l => string.Equals(l, dest, StringComparison.OrdinalIgnoreCase));
            S.LibraryFolders.Insert(0, dest);
            SaveSettings();
            RefreshInstallState();
            RefreshAllViews();
            BuildSettingsView();
            ShowToast(error == null ? $"Moved {moved} folder{(moved == 1 ? "" : "s")}" : $"Moved {moved}, some failed: {error}", error == null ? Icons.Check : Icons.Warning);
        }

        // ── Appearance ───────────────────────────────────────────────────────

        private UIElement AppearanceSection()
        {
            var sp = new StackPanel();

            sp.Children.Add(UiFactory.SettingRow("Theme", null, Segmented(new[] { ("dark", "Dark"), ("light", "Light") }, S.ThemeMode, v =>
            {
                S.ThemeMode = v;
                SaveSettings();
                ThemeManager.Apply(v);
                EnableRoundedCorners();
                RefreshAllViews();
                BuildSettingsView();
            })));

            sp.Children.Add(UiFactory.SettingRow("Artwork color", "Season art in color, greyscale until you point at it, or always greyscale.",
                Segmented(new[] { ("color", "Always"), ("hover", "On hover"), ("mono", "Never") }, S.ArtworkMode, v =>
                {
                    S.ArtworkMode = v;
                    ArtworkCache.Mode = ParseArtworkMode(v);
                    SaveSettings();
                    RefreshAllViews();
                })));

            var scale = new ComboBox { Width = 110 };
            scale.Styled("Combo");
            foreach (int v in SettingsService.AllowedScales)
            {
                var item = new ComboBoxItem { Content = $"{v}%", Tag = v };
                scale.Items.Add(item);
                if (v == S.UiScale) scale.SelectedItem = item;
            }
            scale.SelectionChanged += (_, _) =>
            {
                if (scale.SelectedItem is not ComboBoxItem { Tag: int v }) return;
                S.UiScale = v;
                SaveSettings();
                ApplyScale();
            };
            sp.Children.Add(UiFactory.SettingRow("Interface size", null, scale));

            sp.Children.Add(UiFactory.SettingRow("Reduce motion", "Turns off transitions, hover zoom and animated progress bars.",
                UiFactory.Switch(S.ReduceMotion, on => { S.ReduceMotion = on; SaveSettings(); ThemeManager.SetReduceMotion(on); })));
            return sp;
        }

        private static Border Segmented((string Key, string Label)[] options, string current, Action<string> changed)
        {
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            string group = Guid.NewGuid().ToString("N");
            foreach (var (key, label) in options)
            {
                var rb = new RadioButton { Content = label, GroupName = group, IsChecked = key == current };
                rb.Styled("Segment");
                string k = key;
                rb.Click += (_, _) => changed(k);
                seg.Children.Add(rb);
            }
            var host = new Border { Child = seg };
            host.Styled("SegmentHost");
            return host;
        }

        // ── Behavior ─────────────────────────────────────────────────────────

        private UIElement BehaviorSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.SettingRow("Close to tray", "Keep running in the notification area when the window is closed.",
                UiFactory.Switch(S.CloseToTray, on => { S.CloseToTray = on; SaveSettings(); })));
            sp.Children.Add(UiFactory.SettingRow("Notifications", "Windows notifications when downloads finish.",
                UiFactory.Switch(S.ShowTrayNotifications, on => { S.ShowTrayNotifications = on; SaveSettings(); })));
            sp.Children.Add(UiFactory.SettingRow("Discord activity", "Show the season you're playing on your Discord profile.",
                UiFactory.Switch(S.DiscordPresence, on =>
                {
                    S.DiscordPresence = on;
                    SaveSettings();
                    if (on) _ = Task.Run(async () => { await _discord.ConnectAsync(); await _discord.SetLauncherTabAsync("Settings"); });
                    else _ = _discord.ClearAsync();
                })));
            if (MemoryOffsets.IsConfigured)
                sp.Children.Add(UiFactory.SettingRow("Match details", "Read the current map and operator from the game for Discord and the overlay.",
                    UiFactory.Switch(S.GameStateReader, on => { S.GameStateReader = on; SaveSettings(); })));
            sp.Children.Add(UiFactory.SettingRow("In-game overlay", "Shift + F2 shows friends and session info over the game.",
                UiFactory.Switch(S.InGameOverlay, on => { S.InGameOverlay = on; SaveSettings(); })));
            sp.Children.Add(UiFactory.SettingRow("Check for updates", "Look for a new launcher version when it starts and show a popup when one is out.",
                UiFactory.Switch(S.CheckUpdatesOnStartup, on => { S.CheckUpdatesOnStartup = on; SaveSettings(); })));
            return sp;
        }

        // ── Maintenance ──────────────────────────────────────────────────────

        private UIElement MaintenanceSection()
        {
            var sp = new WrapPanel();
            sp.Children.Add(UiFactory.Button("Open data folder", "Btn.Secondary", (_, _) => OpenFolder(AppPaths.DataDir), Icons.Folder).Margin(0, 0, 8, 8));
            sp.Children.Add(UiFactory.Button("Import game folder", "Btn.Secondary", (_, _) => ImportFolder(), Icons.FolderOpen).Margin(0, 0, 8, 8));
            sp.Children.Add(UiFactory.Button("Find existing installs", "Btn.Secondary", async (_, _) =>
            {
                await ScanForExistingInstallsAsync();
                ShowToast("Scan finished", Icons.Check);
            }, Icons.Search).Margin(0, 0, 8, 8));
            sp.Children.Add(UiFactory.Button("Clear cache", "Btn.Secondary", (_, _) => ClearCache(), Icons.Delete).Margin(0, 0, 8, 8));
            return sp;
        }

        private void ClearCache()
        {
            if (_downloads.IsRunning) { ShowToast("Wait for the current download to finish.", Icons.Warning); return; }
            if (!ThemedDialog.Confirm(this, "Clear cache", "Removes downloaded helper files (7-Zip, prerequisites and any updated DepotDownloader). They are fetched again when needed. Your games and settings are not touched.", "Clear")) return;
            long freed = 0;
            foreach (var dir in new[] { AppPaths.PrerequisitesDir, AppPaths.BinDir })
            {
                if (!Directory.Exists(dir)) continue;
                freed += InstallLocator.DirectorySize(dir);
                try { Directory.Delete(dir, true); } catch { }
            }
            ShowToast($"Cleared {UiFactory.FormatBytes(freed)}", Icons.Check);
        }

        // ── About ────────────────────────────────────────────────────────────

        private UIElement AboutSection()
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text($"{AppInfo.Name} {AppInfo.Version}", 13, "Brush.Fg"));
            sp.Children.Add(UiFactory.Text("Download, manage and play older seasons of Rainbow Six Siege. Not affiliated with, endorsed by or sponsored by Ubisoft.", 12, "Brush.Fg3", wrap: true).Margin(0, 4, 0, 4));
            sp.Children.Add(UiFactory.Text("Unlock all uses Liberator by xeralin, licensed under GPLv3.", 12, "Brush.Fg3", wrap: true).Margin(0, 0, 0, 12));
            var buttons = new WrapPanel();
            buttons.Children.Add(UiFactory.Button("GitHub", "Btn.Secondary", (_, _) => OpenUrl(AppInfo.RepoUrl), Icons.OpenExternal).Margin(0, 0, 8, 8));
            buttons.Children.Add(UiFactory.Button("Updates", "Btn.Secondary", (_, e) => Nav_Click(NavUpdates, e), Icons.Sync).Margin(0, 0, 8, 8));
            sp.Children.Add(buttons);
            return sp;
        }
    }
}
