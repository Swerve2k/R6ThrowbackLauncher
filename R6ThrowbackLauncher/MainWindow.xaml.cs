using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Themes;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow : Window, IScrimHost
    {
        private readonly SettingsService   _settingsService = new();
        private readonly DownloadService   _downloadService = new();
        private readonly LibraryService    _libraryService  = new();
        private readonly DiscordRpcService _discord         = new();
        private readonly CommunityService  _community       = new();
        private readonly PlaytimeService   _playtime;
        private readonly InstallLocator    _locator;
        private DownloadManager _downloads = null!;

        private readonly List<SeasonPatch> _patches = new();
        private Grid? _currentView;
        private Grid? _previousView;
        private SeasonPatch? _detailPatch;

        private UserSettings S => _settingsService.Settings;

        public MainWindow()
        {
            InitializeComponent();

            _settingsService.Load();
            ThemeManager.Apply(S.ThemeMode);
            ThemeManager.SetReduceMotion(S.ReduceMotion);
            ArtworkCache.Mode = ParseArtworkMode(S.ArtworkMode);

            _playtime = new PlaytimeService(AppPaths.DataDir);
            _locator  = new InstallLocator(() => S.LibraryFolders);

            SidebarVersion.Text = $"v{AppInfo.Version}";
            ApplyScale();

            PreviewKeyDown    += MainWindow_PreviewKeyDown;
            Loaded            += MainWindow_Loaded;
            Closing           += MainWindow_Closing;
            StateChanged      += (_, _) => OnWindowStateChanged();
            SourceInitialized += (_, _) => EnableRoundedCorners();
        }

        // ── Startup ──────────────────────────────────────────────────────────

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SplashWindow.Current?.UpdateStatus("Loading seasons");
                var loaded = await Task.Run(SeasonCatalog.Load);
                _patches.Clear();
                _patches.AddRange(loaded);

                SplashWindow.Current?.UpdateStatus("Scanning libraries");
                await Task.Run(() => _locator.RefreshAll(_patches));

                _downloads = new DownloadManager(_downloadService, _settingsService, FindPatch, Dispatcher)
                {
                    CredentialsProvider = RequestSteamCredentialsAsync,
                    PromptHandler       = PromptForSteamInputAsync,
                    InGameName          = EffectiveInGameName,
                    LoginOutcome        = OnLoginOutcome,
                };
                _downloads.Changed  += OnDownloadsChanged;
                _downloads.Finished += OnTransferFinished;
                _downloads.LogAppended += OnTransferLog;
                _downloads.Restore();

                InitLibraryControls();
                RefreshProfileArea();
                InitTrayIcon();
                StartRunningGamePoll();

                ShowView(HomeView, "Home", NavHome);
                RefreshAllViews();

                if (_settingsService.LoadWarning != null)
                    ShowToast(_settingsService.LoadWarning);

                SplashWindow.Current?.UpdateStatus("Ready");

                await ScanForExistingInstallsAsync();

                StartCommunity();
                _ = RunStartupUpdateFlowAsync();
                if (S.DiscordPresence)
                    _ = Task.Run(async () =>
                    {
                        await _discord.ConnectAsync();
                        await _discord.SetLauncherTabAsync("Home");
                    });
            }
            catch (Exception ex)
            {
                ThemedDialog.Alert(this, "Could not start", "The season list could not be loaded.\n\n" + ex.Message);
            }
        }

        private SeasonPatch? FindPatch(string id) => _patches.FirstOrDefault(p => p.Id == id);

        private IEnumerable<SeasonPatch> Installed => _patches.Where(p => p.IsInstalled);

        private void RefreshInstallState(SeasonPatch? only = null)
        {
            if (only != null) _locator.Refresh(only);
            else _locator.RefreshAll(_patches);
            InvalidateSizes(only);
        }

        private void RefreshAllViews()
        {
            BuildHome();
            BuildBrowse();
            UpdateStatusBar();
            UpdateDownloadsBadge();
            if (_currentView == DetailView && _detailPatch != null) BuildDetail(_detailPatch);
            else if (_currentView == DownloadsView) BuildDownloadsView(force: true);
            else if (_currentView == StorageView) BuildStorageView();
            else if (_currentView == StatsView) BuildStatsView();
        }

        // ── Navigation ───────────────────────────────────────────────────────

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender == NavHome)        { ShowView(HomeView, "Home", NavHome); BuildHome(); }
            else if (sender == NavBrowse) { ShowView(BrowseView, "Browse seasons", NavBrowse); BuildBrowse(); }
            else if (sender == NavDownloads) { ShowView(DownloadsView, "Downloads", NavDownloads); BuildDownloadsView(force: true); }
            else if (sender == NavMods)   { ShowView(ModsView, "Modifications", NavMods); BuildModsView(); }
            else if (sender == NavFriends) { ShowView(FriendsView, "Friends", NavFriends); BuildFriendsView(); }
            else if (sender == NavLeaderboard) { ShowView(LeaderboardView, "Leaderboard", NavLeaderboard); _ = LoadLeaderboardAsync(); }
            else if (sender == NavStats)  { ShowView(StatsView, "Stats", NavStats); BuildStatsView(); }
            else if (sender == NavStorage) { ShowView(StorageView, "Storage", NavStorage); BuildStorageView(); }
            else if (sender == NavUpdates) { ShowView(UpdatesView, "Updates", NavUpdates); BuildUpdatesView(); }
            else if (sender == NavSettings) { ShowView(SettingsView, "Settings", NavSettings); BuildSettingsView(); }
        }

        /// <summary>Pages opened from inside another page; Back returns to where they came from.</summary>
        private bool IsSubPage(Grid view) => view == DetailView || view == ProfileView;

        private void ShowView(Grid view, string title, RadioButton? nav)
        {
            if (nav != null) nav.IsChecked = true;
            else foreach (var rb in NavItems()) rb.IsChecked = false;

            PageTitle.Text = title;
            BackButton.Visibility = IsSubPage(view) ? Visibility.Visible : Visibility.Collapsed;
            if (S.DiscordPresence) _ = _discord.SetLauncherTabAsync(title);
            if (view != FriendsView) StopChatPolling();

            if (_currentView == view) return;
            if (IsSubPage(view) && _currentView != null && !IsSubPage(_currentView)) _previousView = _currentView;

            foreach (UIElement child in ViewHost.Children)
            {
                if (child == view) continue;
                child.BeginAnimation(OpacityProperty, null);
                child.Visibility = Visibility.Collapsed;
            }
            view.Visibility = Visibility.Visible;
            _currentView = view;

            if (ThemeManager.ReduceMotion) { view.Opacity = 1; return; }
            var tt = new TranslateTransform(0, 10);
            view.RenderTransform = tt;
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }

        private IEnumerable<RadioButton> NavItems() => new[]
        {
            NavHome, NavBrowse, NavDownloads, NavMods, NavFriends, NavLeaderboard, NavStats, NavStorage, NavUpdates, NavSettings,
        };

        private void GoBack()
        {
            if (_currentView == null || !IsSubPage(_currentView)) return;
            _detailPatch = null;
            if (_previousView == BrowseView) { ShowView(BrowseView, "Browse seasons", NavBrowse); BuildBrowse(); }
            else if (_previousView == DownloadsView) { ShowView(DownloadsView, "Downloads", NavDownloads); BuildDownloadsView(force: true); }
            else if (_previousView == StorageView) { ShowView(StorageView, "Storage", NavStorage); BuildStorageView(); }
            else if (_previousView == LeaderboardView) { ShowView(LeaderboardView, "Leaderboard", NavLeaderboard); RenderLeaderboard(); }
            else if (_previousView == FriendsView) { ShowView(FriendsView, "Friends", NavFriends); BuildFriendsView(); }
            else if (_previousView == SettingsView) { ShowView(SettingsView, "Settings", NavSettings); BuildSettingsView(); }
            else if (_previousView == StatsView) { ShowView(StatsView, "Stats", NavStats); BuildStatsView(); }
            else { ShowView(HomeView, "Home", NavHome); BuildHome(); }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => GoBack();

        private void OpenDetail(SeasonPatch patch, string tab = "overview")
        {
            _detailPatch = patch;
            _detailTab = tab;
            ShowView(DetailView, patch.ShortLabel, null);
            BuildDetail(patch);
            DetailScroll.ScrollToTop();
        }

        // ── Status bar ───────────────────────────────────────────────────────

        private void UpdateStatusBar()
        {
            int installed = Installed.Count();
            StatusLeft.Text = $"{installed} installed  ·  {_patches.Count(p => !p.IsHeatedMetal)} seasons available";
            long total = _playtime.GetTotalSeconds();
            StatusRight.Text = total > 0 ? $"Total playtime {PlaytimeService.Format(total)}" : "";
        }

        private static ArtworkMode ParseArtworkMode(string mode) => mode switch
        {
            "hover" => ArtworkMode.Hover,
            "mono"  => ArtworkMode.Mono,
            _       => ArtworkMode.Color,
        };
    }
}
