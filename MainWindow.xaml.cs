using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow : Window
    {
        private readonly DownloadService _downloadService;
        private readonly LibraryService  _libraryService = new();
        private readonly SettingsService _settingsService = new();
        private readonly List<SeasonPatch> _allPatches = new();
        private SeasonPatch? _selectedPatch;
        private SeasonPatch? _editTargetPatch;
        private Grid? _currentView;

        private const string GitHubOwner    = "CeleDLuffy";
        private const string GitHubRepo     = "R6ThrowbackLauncher";
        private const string CurrentVersion = "0.2.0";

        public MainWindow()
        {
            InitializeComponent();

            string baseDir  = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OperationThrowback");

            _downloadService = new DownloadService(baseDir, dataDir);
            _settingsService.Load();

            Loaded += MainWindow_Loaded;
        }

        // =====================================================================
        //  STARTUP
        // =====================================================================

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await LoadManifestAsync();
                _allPatches.Clear();
                _allPatches.AddRange(result.Patches);

                for (int i = 0; i < _allPatches.Count; i++)
                    _allPatches[i] = _downloadService.WithInstallDir(_allPatches[i]);

                BuildSeasonPatchCombo();
                BuildLibraryCards();

                // Populate UI from settings
                var s = _settingsService.Settings;
                UsernameTextBox.Text          = s.SteamUsername;
                SettingsSteamUsernameBox.Text = s.SteamUsername;
                UsernameDisplay.Text          = string.IsNullOrWhiteSpace(s.SteamUsername) ? "Steam User" : s.SteamUsername;
                RedSlider.Value   = s.AccentR;
                GreenSlider.Value = s.AccentG;
                BlueSlider.Value  = s.AccentB;
                BorderToggle.IsChecked = s.ShowBorder;
                ApplyThemeColor(s.AccentR, s.AccentG, s.AccentB);
                MainWindowBorder.BorderBrush = s.ShowBorder
                    ? new SolidColorBrush(Color.FromRgb(s.AccentR, s.AccentG, s.AccentB))
                    : Brushes.Transparent;

                _currentView = LibraryView;

                await ScanForExistingInstallsAsync();
                _ = LoadChangelogsAsync();
                _ = SilentUpdateCheckAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to load manifest: " + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =====================================================================
        //  MANIFEST
        // =====================================================================

        private async Task<ManifestLoadResult> LoadManifestAsync()
        {
            string path = _downloadService.ManifestPath;
            if (!File.Exists(path))
                throw new FileNotFoundException("manifest.json not found", path);

            await using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream);

            var patches = new List<SeasonPatch>();
            foreach (var yearProp in doc.RootElement.EnumerateObject())
            {
                string yearKey = yearProp.Name;
                foreach (var seasonProp in yearProp.Value.EnumerateObject())
                {
                    string seasonKey = seasonProp.Name;
                    var seasonObj    = seasonProp.Value;
                    if (!seasonObj.TryGetProperty("name", out var nameEl)) continue;
                    string seasonName = nameEl.GetString() ?? "Unknown";

                    foreach (var patchProp in seasonObj.EnumerateObject())
                    {
                        if (patchProp.Name == "name") continue;
                        string patchKey = patchProp.Name;

                        DateTime? date = null;
                        if (patchProp.Value.TryGetProperty("date", out var dateEl))
                            if (DateTime.TryParse(dateEl.GetString(), out var parsed)) date = parsed;

                        string id      = $"{yearKey}.{seasonKey}.{patchKey}";
                        string display = $"{yearKey}{seasonKey} - {seasonName} (Patch {patchKey})";
                        if (date is DateTime d) display += $" [{d:yyyy-MM-dd}]";

                        string artFile = $"{yearKey}_{seasonKey}_{seasonName.Replace(' ', '_')}.png";
                        string artPath = Path.Combine(_downloadService.ResourcesDir, "Artwork", artFile);

                        patches.Add(new SeasonPatch
                        {
                            Id = id, YearKey = yearKey, SeasonKey = seasonKey,
                            PatchKey = patchKey, SeasonName = seasonName,
                            Date = date, DisplayName = display,
                            ArtworkPath = File.Exists(artPath) ? artPath : string.Empty
                        });
                    }
                }
            }
            return new ManifestLoadResult { Patches = patches };
        }

        // =====================================================================
        //  SEASON COMBO
        // =====================================================================

        private void BuildSeasonPatchCombo()
        {
            SeasonPatchListBox.ItemsSource = _allPatches;
            if (_allPatches.Count > 0)
            {
                _selectedPatch = _allPatches[0];
                SeasonPatchListBox.SelectedIndex = 0;
                UpdateSeasonPatchButtonText();
                UpdateDownloadArtwork(_allPatches[0]);
            }
        }

        private void UpdateSeasonPatchButtonText()
        {
            SeasonPatchButton.Content = new TextBlock
            {
                Text = _selectedPatch?.DisplayName ?? "Select season...",
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }

        private void UpdateDownloadArtwork(SeasonPatch patch)
        {
            DownloadArtworkSeason.Text = $"{patch.YearKey}{patch.SeasonKey} — {patch.SeasonName}";
            DownloadArtworkPatch.Text  = patch.Date.HasValue
                ? $"Patch {patch.PatchKey}  •  {patch.Date.Value:MMMM yyyy}"
                : $"Patch {patch.PatchKey}";

            if (!string.IsNullOrEmpty(patch.ArtworkPath) && File.Exists(patch.ArtworkPath))
            {
                DownloadArtworkImage.Source           = new BitmapImage(new Uri(patch.ArtworkPath, UriKind.Absolute));
                DownloadArtworkImage.Visibility       = Visibility.Visible;
                DownloadArtworkPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                DownloadArtworkImage.Visibility       = Visibility.Collapsed;
                DownloadArtworkPlaceholder.Visibility = Visibility.Visible;
            }
        }

        private void SeasonPatchButton_Click(object sender, RoutedEventArgs e) => SeasonPatchPopup.IsOpen = true;

        private void SeasonPatchListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        { if (SeasonPatchListBox.SelectedItem is SeasonPatch p) SelectPatch(p); }

        private void SeasonPatchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { if (SeasonPatchListBox.SelectedItem is SeasonPatch p) SelectPatch(p); }

        private void SelectPatch(SeasonPatch patch)
        {
            _selectedPatch = patch;
            UpdateSeasonPatchButtonText();
            UpdateDownloadArtwork(patch);
            SeasonPatchPopup.IsOpen = false;
        }

        // =====================================================================
        //  LIBRARY CARDS
        // =====================================================================

        private void BuildLibraryCards()
        {
            LibraryPanel.Children.Clear();

            foreach (var patch in _allPatches)
            {
                if (!patch.IsInstalled) continue;

                var border = new Border { Style = (Style)FindResource("GameCard"), Tag = patch };
                var grid   = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                if (!string.IsNullOrEmpty(patch.ArtworkPath) && File.Exists(patch.ArtworkPath))
                {
                    var artBorder = new Border { ClipToBounds = true };
                    var img = new Image
                    {
                        Stretch             = Stretch.UniformToFill,
                        Source              = new BitmapImage(new Uri(patch.ArtworkPath, UriKind.Absolute)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment   = VerticalAlignment.Center
                    };
                    artBorder.Child = img;
                    Grid.SetRow(artBorder, 0);
                    grid.Children.Add(artBorder);
                }
                else
                {
                    var ph = new Border { Background = new SolidColorBrush(Color.FromRgb(30, 30, 40)) };
                    Grid.SetRow(ph, 0); grid.Children.Add(ph);
                }

                var info = new StackPanel { Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Stretch };
                info.Children.Add(new TextBlock { Text = patch.SeasonName, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = $"{patch.YearKey}{patch.SeasonKey} • Patch {patch.PatchKey}", Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
                if (patch.Date is DateTime d)
                    info.Children.Add(new TextBlock { Text = d.ToString("yyyy-MM-dd"), Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 140)), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
                info.Children.Add(new Border { Height = 8, Background = Brushes.Transparent });

                var launchBtn = new Button { Content = "LAUNCH", Style = (Style)FindResource("RiotButton"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 8, 18, 8), FontSize = 13, Tag = patch };
                launchBtn.Click += LaunchButton_Click;
                info.Children.Add(launchBtn);
                Grid.SetRow(info, 1); grid.Children.Add(info);

                var pencil = new Button
                {
                    Content = "✏", Width = 32, Height = 32, FontSize = 14,
                    Background = new SolidColorBrush(Color.FromArgb(180, 15, 15, 25)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0), Tag = patch
                };
                pencil.Click += PencilButton_Click;
                Grid.SetRowSpan(pencil, 2); grid.Children.Add(pencil);

                border.Child = grid;
                border.MouseLeftButtonUp += async (_, __) => await LaunchPatchAsync(patch);
                LibraryPanel.Children.Add(border);
            }

            if (LibraryPanel.Children.Count == 0)
                LibraryPanel.Children.Add(new TextBlock
                {
                    Text = "No installed builds found. Download a season to populate your library.",
                    Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    FontSize = 14, Margin = new Thickness(0, 20, 0, 0)
                });
        }

        // =====================================================================
        //  LAUNCH
        // =====================================================================

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        { if (sender is Button btn && btn.Tag is SeasonPatch p) await LaunchPatchAsync(p); }

        private async Task LaunchPatchAsync(SeasonPatch patch)
        {
            bool ok = await _libraryService.LaunchSeasonAsync(patch);
            if (!ok) MessageBox.Show(this, "Could not find a launchable file in this season folder.",
                "Launch failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // =====================================================================
        //  NAVIGATION + ANIMATIONS
        // =====================================================================

        private void ResetNavSelection()
        {
            NavLibrary.Tag = null; NavDownload.Tag = null;
            NavTools.Tag   = null; NavChangelogs.Tag = null; NavSettings.Tag = null;
        }

        private void ShowView(Grid newView)
        {
            if (_currentView == newView) return;

            // Collapse all views except the new one
            foreach (var view in new[] { LibraryView, DownloadView, ToolsView, ChangelogsView, SettingsView, CreditsView })
            {
                if (view == newView) continue;
                view.BeginAnimation(OpacityProperty, null);
                view.Opacity    = 1;
                view.Visibility = Visibility.Collapsed;
            }

            // Always assign a fresh TranslateTransform so we never conflict with the XAML-defined one
            var tt = new TranslateTransform(0, 18);
            newView.RenderTransform = tt;
            newView.Visibility      = Visibility.Visible;
            newView.Opacity         = 0;

            newView.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
                { BeginTime = TimeSpan.FromMilliseconds(60) });

            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(18, 0, new Duration(TimeSpan.FromMilliseconds(220)))
                {
                    BeginTime      = TimeSpan.FromMilliseconds(60),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            _currentView = newView;
        }

        private void NavLibrary_Click(object sender, RoutedEventArgs e)    { ResetNavSelection(); NavLibrary.Tag    = "Selected"; ShowView(LibraryView);    }
        private void NavDownload_Click(object sender, RoutedEventArgs e)    { ResetNavSelection(); NavDownload.Tag   = "Selected"; ShowView(DownloadView);   }
        private void NavTools_Click(object sender, RoutedEventArgs e)       { ResetNavSelection(); NavTools.Tag      = "Selected"; ShowView(ToolsView);      }
        private void NavChangelogs_Click(object sender, RoutedEventArgs e)  { ResetNavSelection(); NavChangelogs.Tag = "Selected"; ShowView(ChangelogsView); }
        private void NavSettings_Click(object sender, RoutedEventArgs e)    { ResetNavSelection(); NavSettings.Tag   = "Selected"; ShowView(SettingsView);   }
        private void NavCredits_Click(object sender, RoutedEventArgs e)     { ResetNavSelection(); ShowView(CreditsView); }
        private void NavGuide_Click(object sender, RoutedEventArgs e)
        { MessageBox.Show(this, "Guide coming soon!", "Guide", MessageBoxButton.OK, MessageBoxImage.Information); }

        // =====================================================================
        //  WINDOW CHROME
        // =====================================================================

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal) { WindowState = WindowState.Maximized; MaximizeButton.Content = "❐"; }
            else { WindowState = WindowState.Normal; MaximizeButton.Content = "□"; }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // =====================================================================
        //  DOWNLOAD
        // =====================================================================

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatch is not { } selected)
            { MessageBox.Show(this, "Select a season/patch first.", "No selection", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            string username = UsernameTextBox.Text.Trim();
            if (!int.TryParse(MaxDownloadsTextBox.Text.Trim(), out int maxDl) || maxDl <= 0)
            { MessageBox.Show(this, "Max Downloads must be a positive integer.", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            DownloadButton.IsEnabled = false;
            DownloadButton.Content   = "DOWNLOADING...";

            var logLock = new object();
            var logBuf  = new StringBuilder();
            void Log(string line)
            {
                lock (logLock) { logBuf.AppendLine(line); if (logBuf.Length > 12000) logBuf.Remove(0, logBuf.Length - 12000); }
                Dispatcher.Invoke(() => UsernameDisplay.Text = line);
            }

            try
            {
                int code = await _downloadService.RunDownloadAsync(selected, username, maxDl, Log);
                if (code == 0)
                {
                    MessageBox.Show(this, "Download completed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    int idx = _allPatches.FindIndex(p => p.Id == selected.Id);
                    if (idx >= 0) _allPatches[idx] = _downloadService.WithInstallDir(_allPatches[idx]);
                    BuildLibraryCards();
                }
                else
                {
                    string tail; lock (logLock) tail = logBuf.ToString();
                    MessageBox.Show(this, $"Download failed (exit {code}).\n\n{tail}", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                string tail; lock (logLock) tail = logBuf.ToString();
                MessageBox.Show(this, $"Error: {ex.Message}\n\n{tail}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { DownloadButton.IsEnabled = true; DownloadButton.Content = "DOWNLOAD"; }
        }

        // =====================================================================
        //  TOOLS
        // =====================================================================

        private void ToolDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        // =====================================================================
        //  SCAN FOR EXISTING INSTALLS
        // =====================================================================

        private async Task ScanForExistingInstallsAsync()
        {
            var roots = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "R6Legacy"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "OperationThrowback"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "R6Legacy"),
                @"C:\R6Legacy", @"C:\OperationThrowback"
            };

            var found = new List<string>();
            await Task.Run(() =>
            {
                foreach (var root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(root))
                        {
                            if (!File.Exists(Path.Combine(sub, "RainbowSix.exe"))) continue;
                            if (!Directory.Exists(Path.Combine(_downloadService.OutputDir, Path.GetFileName(sub))))
                                found.Add(sub);
                        }
                    }
                    catch { }
                }
            });

            if (found.Count == 0) return;
            Dispatcher.Invoke(() =>
            {
                var dlg = new DetectedInstallsDialog(found) { Owner = this };
                if (dlg.ShowDialog() == true)
                { foreach (var f in dlg.SelectedFolders) ImportFolderPath(f); BuildLibraryCards(); }
            });
        }

        // =====================================================================
        //  IMPORT FOLDER
        // =====================================================================

        private void ImportFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            { Description = "Select a Rainbow Six Siege legacy game folder", UseDescriptionForTitle = true };

            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (!File.Exists(Path.Combine(dlg.SelectedPath, "RainbowSix.exe")))
            { MessageBox.Show(this, "RainbowSix.exe not found in that folder.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            ImportFolderPath(dlg.SelectedPath);
            BuildLibraryCards();
            MessageBox.Show(this, $"Imported: {Path.GetFileName(dlg.SelectedPath)}", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportFolderPath(string folder)
        {
            string link = Path.Combine(_downloadService.OutputDir, Path.GetFileName(folder));
            try
            {
                Directory.CreateDirectory(_downloadService.OutputDir);
                if (!Directory.Exists(link))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/C mklink /J \"{link}\" \"{folder}\"")
                    { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit();
                for (int i = 0; i < _allPatches.Count; i++)
                    _allPatches[i] = _downloadService.WithInstallDir(_allPatches[i]);
            }
            catch (Exception ex) { MessageBox.Show(this, "Import failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // =====================================================================
        //  PENCIL MENU
        // =====================================================================

        private void PencilButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not SeasonPatch patch) return;
            _editTargetPatch = patch;

            var menu = new ContextMenu();
            var ei = new MenuItem { Header = "✏  Edit Username" };   ei.Click += EditUsername_Click;
            var oi = new MenuItem { Header = "📁  Open File Location" }; oi.Click += OpenFolder_Click;
            menu.Items.Add(ei);
            menu.Items.Add(oi);

            // Only show delete for installs inside the launcher's own Downloads folder
            bool isLauncherDownload = patch.InstallDir.StartsWith(
                _downloadService.OutputDir, StringComparison.OrdinalIgnoreCase);
            if (isLauncherDownload)
            {
                var di = new MenuItem { Header = "🗑  Delete Folder" }; di.Click += DeleteGame_Click;
                menu.Items.Add(new Separator());
                menu.Items.Add(di);
            }

            menu.IsOpen = true;
        }

        private void EditUsername_Click(object sender, RoutedEventArgs e)
        {
            if (_editTargetPatch == null) return;

            // Try InstallDir first, then the actual AppData downloads location
            string toml = Path.Combine(_editTargetPatch.InstallDir, "ThrowbackLoader.toml");

            if (!File.Exists(toml))
            {
                string folderName = Path.GetFileName(_editTargetPatch.InstallDir);
                string altPath = Path.Combine(_downloadService.OutputDir, folderName, "ThrowbackLoader.toml");
                if (File.Exists(altPath)) toml = altPath;
            }

            var dlg = new EditUsernameDialog(_editTargetPatch.SeasonName) { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.NewUsername))
            {
                try
                {
                    if (!File.Exists(toml))
                    {
                        // Create a fresh toml with the username
                        File.WriteAllText(toml,
                            $"[general]\r\nusername = '{dlg.NewUsername}' # edit to change in-game username. Limit to 16 characters\r\n\r\n[advanced]\r\ncustom_user_id = '' # leave blank to use default\r\n");
                    }
                    else
                    {
                        File.WriteAllText(toml, Regex.Replace(
                            File.ReadAllText(toml),
                            @"username\s*=\s*'[^']*'",
                            $"username = '{dlg.NewUsername}'"));
                    }
                    MessageBox.Show(this, $"Username set to '{dlg.NewUsername}'.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void DeleteGame_Click(object sender, RoutedEventArgs e)
        {
            if (_editTargetPatch == null) return;
            if (MessageBox.Show(this, $"Send '{_editTargetPatch.SeasonName}' to Recycle Bin?\n{_editTargetPatch.InstallDir}",
                "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(_editTargetPatch.InstallDir,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                int idx = _allPatches.FindIndex(p => p.Id == _editTargetPatch.Id);
                if (idx >= 0) _allPatches[idx] = _downloadService.WithInstallDir(_allPatches[idx]);
                BuildLibraryCards();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_editTargetPatch != null && Directory.Exists(_editTargetPatch.InstallDir))
                System.Diagnostics.Process.Start("explorer.exe", _editTargetPatch.InstallDir);
        }

        // =====================================================================
        //  SETTINGS
        // =====================================================================

        private void SaveUsername_Click(object sender, RoutedEventArgs e)
        {
            string u = SettingsSteamUsernameBox.Text.Trim();
            _settingsService.Settings.SteamUsername = u;
            _settingsService.Save();
            UsernameTextBox.Text = u;
            UsernameDisplay.Text = string.IsNullOrWhiteSpace(u) ? "Steam User" : u;
            MessageBox.Show(this, "Username saved!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ThemeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (RedSlider == null || GreenSlider == null || BlueSlider == null) return;
            byte r = (byte)RedSlider.Value, g = (byte)GreenSlider.Value, b = (byte)BlueSlider.Value;
            if (RedValue   != null) RedValue.Text   = r.ToString();
            if (GreenValue != null) GreenValue.Text = g.ToString();
            if (BlueValue  != null) BlueValue.Text  = b.ToString();
            if (ThemeColorPreview != null) ThemeColorPreview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void ApplyTheme_Click(object sender, RoutedEventArgs e)
        {
            byte r = (byte)RedSlider.Value, g = (byte)GreenSlider.Value, b = (byte)BlueSlider.Value;
            ApplyThemeColor(r, g, b);
            _settingsService.Settings.AccentR = r;
            _settingsService.Settings.AccentG = g;
            _settingsService.Settings.AccentB = b;
            _settingsService.Save();
        }

        private void ApplyThemeColor(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            Application.Current.Resources["ThemeColor"] = Color.FromRgb(r, g, b);
            Application.Current.Resources["ThemeBrush"] = brush;
            foreach (var tb in FindVisualChildren<TextBlock>(this))
                if (tb.Text == "OPERATION") tb.Foreground = brush;

            // Update border color if border is enabled
            if (BorderToggle?.IsChecked == true && MainWindowBorder != null)
                MainWindowBorder.BorderBrush = brush;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject d) where T : DependencyObject
        {
            if (d == null) yield break;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is T t) yield return t;
                foreach (var x in FindVisualChildren<T>(c)) yield return x;
            }
        }

        // =====================================================================
        //  CHANGELOGS
        // =====================================================================

        private async void RefreshChangelogs_Click(object sender, RoutedEventArgs e) => await LoadChangelogsAsync();

        private async Task LoadChangelogsAsync()
        {
            try
            {
                Dispatcher.Invoke(() => ChangelogLastUpdated.Text = "Fetching...");
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("User-Agent", "R6ThrowbackLauncher");
                string json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases");
                using var doc = JsonDocument.Parse(json);
                var list = doc.RootElement.EnumerateArray().ToList();
                Dispatcher.Invoke(() =>
                {
                    ChangelogsPanel.Children.Clear();
                    if (list.Count == 0) { ChangelogsPanel.Children.Add(MakeChangelogCard("No Releases Yet", "", "No releases published yet.")); ChangelogLastUpdated.Text = "None found."; return; }
                    foreach (var r in list)
                    {
                        string tag  = r.TryGetProperty("tag_name",    out var t)  ? t.GetString()  ?? "" : "";
                        string name = r.TryGetProperty("name",         out var n)  ? n.GetString()  ?? tag : tag;
                        string body = r.TryGetProperty("body",         out var b)  ? b.GetString()  ?? "" : "";
                        string date = r.TryGetProperty("published_at", out var dt) && DateTime.TryParse(dt.GetString(), out var p) ? p.ToString("dd MMMM yyyy") : "";
                        ChangelogsPanel.Children.Add(MakeChangelogCard(name, date, body));
                    }
                    ChangelogLastUpdated.Text = $"Last fetched: {DateTime.Now:HH:mm}";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ChangelogsPanel.Children.Clear();
                    ChangelogsPanel.Children.Add(MakeChangelogCard("Could not load", "", ex.Message));
                    ChangelogLastUpdated.Text = "Failed.";
                });
            }
        }

        private Border MakeChangelogCard(string title, string date, string body)
        {
            var card  = new Border { Background = new SolidColorBrush(Color.FromRgb(21, 21, 31)), CornerRadius = new CornerRadius(8), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14) };
            var stack = new StackPanel();
            var row   = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var tb = new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.Bold };
            Grid.SetColumn(tb, 0); row.Children.Add(tb);
            if (!string.IsNullOrEmpty(date))
            {
                var db = new TextBlock { Text = date, Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 120)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(db, 1); row.Children.Add(db);
            }
            stack.Children.Add(row);
            if (!string.IsNullOrEmpty(body))
            {
                stack.Children.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(34, 34, 47)), Margin = new Thickness(0, 10, 0, 10) });
                stack.Children.Add(new TextBlock { Text = body, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 200)), FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
            }
            card.Child = stack; return card;
        }

        // =====================================================================
        //  CHECK FOR UPDATES
        // =====================================================================

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            var (has, ver, url) = await FetchLatestAsync();
            if (has)
            {
                if (MessageBox.Show(this, $"New version available!\n\nCurrent: v{CurrentVersion}\nLatest:  v{ver}\n\nOpen release page?",
                    "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes && !string.IsNullOrEmpty(url))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            else MessageBox.Show(this, $"You're up to date! (v{CurrentVersion})", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task SilentUpdateCheckAsync()
        {
            try
            {
                var (has, ver, _) = await FetchLatestAsync();
                if (!has) return;
                Dispatcher.Invoke(() => { UsernameDisplay.Text = $"⬆  Update available: v{ver}  —  click 'Check for Updates'"; UsernameDisplay.Foreground = new SolidColorBrush(Color.FromRgb(220, 20, 60)); });
            }
            catch { }
        }

        private async Task<(bool has, string ver, string url)> FetchLatestAsync()
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("User-Agent", "R6ThrowbackLauncher");

            // /releases/latest returns 404 if there are no releases yet — use /releases instead
            string json = await http.GetStringAsync($"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases");
            using var doc = JsonDocument.Parse(json);

            // No releases published yet
            if (doc.RootElement.GetArrayLength() == 0)
                return (false, CurrentVersion, "");

            // First item is the latest release
            var latest = doc.RootElement[0];
            string tag = latest.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = latest.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";

            string lv = tag.TrimStart('v'), cv = CurrentVersion.TrimStart('v');
            bool has = Version.TryParse(lv, out var lver) && Version.TryParse(cv, out var cver) && lver > cver;
            return (has, lv, url);
        }
        // =====================================================================
        //  OPEN DOWNLOAD FOLDER
        // =====================================================================

        private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
        {
            string dir = _downloadService.OutputDir;
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        // =====================================================================
        //  BORDER TOGGLE
        // =====================================================================

        private void BorderToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (BorderToggle == null || MainWindowBorder == null) return;

            bool show = BorderToggle.IsChecked == true;
            var s = _settingsService.Settings;
            MainWindowBorder.BorderBrush = show
                ? new SolidColorBrush(Color.FromRgb(s.AccentR, s.AccentG, s.AccentB))
                : Brushes.Transparent;

            s.ShowBorder = show;
            _settingsService.Save();
        }
    }
}
