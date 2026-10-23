using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Themes;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private List<ReleaseInfo>? _releases;
        private string? _releasesError;
        private (string Version, ReleaseAsset? Asset, string Notes)? _ddLatest;
        private DownloadService.HmUpdateInfo? _hmUpdate;
        private DateTime? _updatesCheckedAt;
        private bool _installingUpdate;
        private bool _bannerDismissed;
        private string _bannerVersion = "";

        private ReleaseInfo? LatestRelease => _releases == null ? null : UpdateService.Latest(_releases);
        private bool LauncherUpdateAvailable => LatestRelease is { } r && AppInfo.IsNewer(r.Version, AppInfo.Version);

        private bool DepotDownloaderUpdateAvailable =>
            _ddLatest is { Asset: not null } dd && AppInfo.IsNewer(dd.Version, UpdateService.InstalledDepotDownloaderVersion(_downloadService) ?? "0");

        private bool HeatedMetalUpdateAvailable =>
            _hmUpdate != null && _hmUpdate.Version != S.HmSkipVersion && _patches.Any(p => p.IsHeatedMetal && p.IsInstalled);

        private int UpdateCount =>
            (LauncherUpdateAvailable ? 1 : 0) + (DepotDownloaderUpdateAvailable ? 1 : 0) + (HeatedMetalUpdateAvailable ? 1 : 0);

        // ══ Checking ═════════════════════════════════════════════════════════

        private Task? _updateCheck;

        private bool UpdatesLoading => _updateCheck is { IsCompleted: false };

        /// <summary>Checks GitHub for updates. Callers during a check share the check in flight.</summary>
        private Task LoadUpdateInfoAsync()
        {
            if (UpdatesLoading) return _updateCheck!;
            return _updateCheck = LoadUpdateInfoCoreAsync();
        }

        private async Task LoadUpdateInfoCoreAsync()
        {
            var releasesTask = UpdateService.FetchLauncherReleasesAsync();
            var ddTask = UpdateService.FetchDepotDownloaderLatestAsync();
            var hmTask = _patches.Any(p => p.IsHeatedMetal && p.IsInstalled)
                ? _downloadService.CheckHmUpdateAsync(S.HmVersion)
                : Task.FromResult<DownloadService.HmUpdateInfo?>(null);
            try { _releases = await releasesTask; _releasesError = null; }
            catch (Exception ex) { _releasesError = ex.Message; }
            try { _ddLatest = await ddTask; } catch { _ddLatest = null; }
            try { _hmUpdate = await hmTask; } catch { _hmUpdate = null; }
            _updatesCheckedAt = DateTime.Now;
            RefreshUpdateIndicators();
        }

        /// <summary>Badge count on the Updates tab and the banner above every page.</summary>
        private void RefreshUpdateIndicators()
        {
            int count = UpdateCount;
            UpdatesBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdatesBadgeText.Text = count.ToString();
            NavUpdates.ToolTip = count switch { 0 => null, 1 => "1 update available", _ => $"{count} updates available" };

            if (LauncherUpdateAvailable && !_bannerDismissed && !_installingUpdate) ShowUpdateBanner(LatestRelease!);
            else HideUpdateBanner();
        }

        // ══ Startup: what's new, update popup, announcements ═════════════════

        private async Task RunStartupUpdateFlowAsync()
        {
            try
            {
                var broadcastsTask = BroadcastService.FetchAsync();
                bool upgraded = NoteRunVersion();
                if (S.CheckUpdatesOnStartup || upgraded) await LoadUpdateInfoAsync();

                if (upgraded) await ShowWhatsNewAsync();
                if (S.CheckUpdatesOnStartup && LauncherUpdateAvailable) await ShowUpdateAvailablePopupAsync(LatestRelease!);

                var now = DateTime.UtcNow;
                foreach (var b in await broadcastsTask)
                {
                    if (!BroadcastService.Applies(b, AppInfo.Version, now)) continue;
                    if (!b.ShowEveryLaunch && S.DismissedBroadcasts.Contains(b.Id)) continue;
                    await ShowBroadcastAsync(b);
                }

                await CheckHmUpdateOnStartupAsync();
            }
            catch { }
        }

        /// <summary>Records this version as the last one run. True when the launcher was just updated.</summary>
        private bool NoteRunVersion()
        {
            string last = S.LastRunVersion;
            bool upgraded = last.Length > 0 ? AppInfo.IsNewer(AppInfo.Version, last) : _settingsService.LoadedExisting;
            if (last != AppInfo.Version)
            {
                S.LastRunVersion = AppInfo.Version;
                SaveSettings();
            }
            return upgraded;
        }

        private async Task ShowWhatsNewAsync()
        {
            var release = _releases?.FirstOrDefault(r => r.Version == AppInfo.Version);
            string notes = release != null ? Markdownish(release.Body) : "";
            if (notes.Length == 0)
                notes = "Your profile and stats are now tied to your Steam account, friends and chat live on their own page, the leaderboard opens player profiles, and updates install with one click.";
            var result = await ShowPopupAsync(PopupKind.Update, "WHAT'S NEW", $"You're on version {AppInfo.Version}", NotesBody(notes, 300),
                new PopupAction("Release history", "history"),
                new PopupAction("Let's go", "ok", Primary: true));
            if (result == "history") Nav_Click(NavUpdates, new RoutedEventArgs());
        }

        private async Task ShowUpdateAvailablePopupAsync(ReleaseInfo release)
        {
            var asset = UpdateService.PickAsset(release);
            var body = new StackPanel();
            string meta = $"You have {AppInfo.Version}";
            if (release.Published.HasValue) meta += $"  ·  Released {release.Published.Value.ToLocalTime():MMM d}";
            if (asset is { Size: > 0 }) meta += $"  ·  {UiFactory.FormatBytes(asset.Size)}";
            body.Children.Add(UiFactory.Text(meta, 12, "Brush.Fg3").Margin(0, 0, 0, 12));
            string notes = Markdownish(release.Body);
            if (notes.Length > 0) body.Children.Add(NotesBody(notes, 220));

            var actions = new List<PopupAction> { new("Later", "later"), new("Release notes", "notes") };
            if (asset != null) actions.Add(new PopupAction("Update now", "update", Primary: true, Icon: Icons.Download));
            else actions.Add(new PopupAction("Open GitHub", "github", Primary: true, Icon: Icons.OpenExternal));

            string? result = await ShowPopupAsync(PopupKind.Update, "UPDATE AVAILABLE", $"Version {release.Version} is here", body, actions.ToArray());
            if (result == "update" && asset != null) await InstallLauncherUpdateAsync(release, asset);
            else if (result == "github") OpenUrl(release.HtmlUrl.Length > 0 ? release.HtmlUrl : AppInfo.RepoUrl + "/releases");
            else if (result == "notes") Nav_Click(NavUpdates, new RoutedEventArgs());
        }

        private async Task ShowBroadcastAsync(Broadcast b)
        {
            var kind = b.Type switch { "update" => PopupKind.Update, "warning" => PopupKind.Warning, _ => PopupKind.Info };
            string eyebrow = b.Type switch { "update" => "UPDATE", "warning" => "IMPORTANT", _ => "ANNOUNCEMENT" };
            bool hasLink = BroadcastService.IsSafeUrl(b.Url);
            bool offerUpdate = b.Type == "update" && !hasLink && LauncherUpdateAvailable && LatestRelease is { } r && UpdateService.PickAsset(r) != null;

            var actions = new List<PopupAction>();
            if (hasLink || offerUpdate) actions.Add(new PopupAction("Close", "close"));
            if (hasLink) actions.Add(new PopupAction(b.ButtonText.Length > 0 ? b.ButtonText : "Open link", "link", Primary: true, Icon: Icons.OpenExternal));
            else if (offerUpdate) actions.Add(new PopupAction(b.ButtonText.Length > 0 ? b.ButtonText : "Update now", "update", Primary: true, Icon: Icons.Download));
            else actions.Add(new PopupAction(b.ButtonText.Length > 0 ? b.ButtonText : "Got it", "close", Primary: true));

            string title = b.Title.Length > 0 ? b.Title : eyebrow[..1] + eyebrow[1..].ToLowerInvariant();
            string? result = await ShowPopupAsync(kind, eyebrow, title, NotesBody(b.Message, 300), actions.ToArray());

            if (!b.ShowEveryLaunch && !S.DismissedBroadcasts.Contains(b.Id))
            {
                S.DismissedBroadcasts.Add(b.Id);
                if (S.DismissedBroadcasts.Count > 200) S.DismissedBroadcasts.RemoveRange(0, S.DismissedBroadcasts.Count - 200);
                SaveSettings();
            }
            if (result == "link") OpenUrl(b.Url);
            else if (result == "update" && LatestRelease is { } latest && UpdateService.PickAsset(latest) is { } asset)
                await InstallLauncherUpdateAsync(latest, asset);
        }

        private static UIElement NotesBody(string text, double maxHeight)
        {
            var tb = UiFactory.Text(text, 13, "Brush.Fg2", wrap: true);
            tb.LineHeight = 20;
            return new ScrollViewer { Content = tb, MaxHeight = maxHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        // ══ Banner ═══════════════════════════════════════════════════════════

        private void ShowUpdateBanner(ReleaseInfo release)
        {
            if (UpdateBannerHost.Visibility == Visibility.Visible && _bannerVersion == release.Version) return;
            _bannerVersion = release.Version;
            var asset = UpdateService.PickAsset(release);

            var card = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(36, 2, 36, 10), ClipToBounds = true };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.StrokeStrong");
            var layers = new Grid();
            var stripes = new Rectangle { Opacity = 0.07, IsHitTestVisible = false };
            stripes.SetResourceReference(Shape.FillProperty, "Brush.ProgressStripes");
            layers.Children.Add(stripes);

            var g = new Grid { Margin = new Thickness(14, 10, 10, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Grid { Width = 40, Height = 40 };
            var ring = new Ellipse { StrokeThickness = 1.5, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1) };
            ring.SetResourceReference(Shape.StrokeProperty, "Brush.Fg2");
            badge.Children.Add(ring);
            var disc = new Border { CornerRadius = new CornerRadius(20) }.Bg("Brush.Accent");
            badge.Children.Add(disc);
            var arrow = UiFactory.Icon(Icons.Download, 17, "Brush.OnAccent");
            arrow.RenderTransform = new TranslateTransform();
            badge.Children.Add(arrow);
            g.Children.Add(badge);
            if (!ThemeManager.ReduceMotion)
            {
                var pulse = TimeSpan.FromSeconds(2);
                var grow = new DoubleAnimation(1, 1.55, pulse) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                ring.BeginAnimation(OpacityProperty, new DoubleAnimation(0.8, 0, pulse) { RepeatBehavior = RepeatBehavior.Forever });
                ((TranslateTransform)arrow.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-1.5, 1.5, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 12, 0) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(UiFactory.Chip("NEW UPDATE", "Brush.Accent", "Brush.OnAccent"));
            top.Children.Add(UiFactory.Text($"Version {release.Version} is ready to install", 14, "Brush.Fg", FontWeights.SemiBold).Margin(10, 0, 0, 0));
            text.Children.Add(top);
            string firstLine = FirstNoteLine(release.Body);
            text.Children.Add(UiFactory.Text(firstLine.Length > 0 ? firstLine : $"You have {AppInfo.Version}.", 12, "Brush.Fg3").Margin(0, 4, 0, 0));
            g.Children.Add(text.Col(1));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(UiFactory.Button("What's new", "Btn.Ghost", (_, e) => Nav_Click(NavUpdates, e)));
            if (asset != null)
                actions.Children.Add(UiFactory.Button("Update now", "Btn.Primary", async (_, _) => await InstallLauncherUpdateAsync(release, asset), Icons.Download).Margin(6, 0, 0, 0));
            actions.Children.Add(UiFactory.IconButton(Icons.Close, "Hide until next launch", (_, _) => { _bannerDismissed = true; HideUpdateBanner(); }).Margin(6, 0, 0, 0));
            g.Children.Add(actions.Col(2));

            layers.Children.Add(g);
            card.Child = layers;
            UpdateBannerHost.Child = card;
            UpdateBannerHost.Visibility = Visibility.Visible;

            if (ThemeManager.ReduceMotion) return;
            var slide = new TranslateTransform(0, -14);
            card.RenderTransform = slide;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } });
        }

        private void HideUpdateBanner()
        {
            UpdateBannerHost.Visibility = Visibility.Collapsed;
            UpdateBannerHost.Child = null;
            _bannerVersion = "";
        }

        // ══ Updates page ═════════════════════════════════════════════════════

        private void BuildUpdatesView()
        {
            UpdatesContent.Children.Clear();
            var check = UiFactory.Button("Check again", "Btn.Secondary", async (_, _) =>
            {
                UpdatesContent.Children.Clear();
                UpdatesContent.Children.Add(UiFactory.Text("Checking for updates", 13, "Brush.Fg3"));
                await LoadUpdateInfoAsync();
                if (_currentView == UpdatesView) BuildUpdatesView();
            }, Icons.Refresh);
            check.IsEnabled = !UpdatesLoading;

            if (_releases == null && _releasesError == null)
            {
                UpdatesContent.Children.Add(PageHeader("Updates", "Checking for updates", check));
                _ = RebuildUpdatesWhenCheckedAsync();
                return;
            }

            int count = UpdateCount;
            string subtitle = count switch { 0 => "Everything is up to date.", 1 => "1 update available.", _ => $"{count} updates available." };
            if (_updatesCheckedAt is DateTime at) subtitle += $"  Last checked {at:HH:mm}.";
            UpdatesContent.Children.Add(PageHeader("Updates", subtitle, check));

            UpdatesContent.Children.Add(LauncherUpdateCard());
            UpdatesContent.Children.Add(DepotDownloaderCard());
            if (_patches.Any(p => p.IsHeatedMetal && p.IsInstalled)) UpdatesContent.Children.Add(HeatedMetalUpdateCard());

            if (_releases is { Count: > 0 })
            {
                UpdatesContent.Children.Add(UiFactory.Section("RELEASE HISTORY").Margin(0, 14, 0, 12));
                foreach (var r in _releases.Take(10))
                    UpdatesContent.Children.Add(ReleaseCard(r));
            }
        }

        private async Task RebuildUpdatesWhenCheckedAsync()
        {
            await LoadUpdateInfoAsync();
            if (_currentView == UpdatesView && (_releases != null || _releasesError != null)) BuildUpdatesView();
        }

        private Border LauncherUpdateCard()
        {
            if (!LauncherUpdateAvailable || LatestRelease is not { } latest)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var ok = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = UiFactory.Icon(_releasesError != null ? Icons.Warning : Icons.Check, 17, "Brush.Fg") }.Bg("Brush.Surface3");
                g.Children.Add(ok);
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
                sp.Children.Add(UiFactory.Text("Swerve2k Downloader", 15, "Brush.Fg", FontWeights.SemiBold));
                sp.Children.Add(UiFactory.Text(_releasesError != null ? $"Could not reach GitHub: {_releasesError}" : $"You're on the latest version ({AppInfo.Version}).", 13, "Brush.Fg2", wrap: true).Margin(0, 3, 0, 0));
                g.Children.Add(sp.Col(1));
                return UiFactory.Card(g).Margin(0, 0, 0, 14);
            }

            var asset = UpdateService.PickAsset(latest);
            var card = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14), ClipToBounds = true };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.StrokeStrong");
            var layers = new Grid();
            var stripes = new Rectangle { Opacity = 0.06, IsHitTestVisible = false };
            stripes.SetResourceReference(Shape.FillProperty, "Brush.ProgressStripes");
            layers.Children.Add(stripes);

            var grid = new Grid { Margin = new Thickness(28, 24, 28, 26) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel();
            var newChip = UiFactory.Chip("NEW VERSION", "Brush.Accent", "Brush.OnAccent").Margin(0, 0, 0, 10);
            newChip.HorizontalAlignment = HorizontalAlignment.Left;
            left.Children.Add(newChip);
            var version = UiFactory.Text(latest.Version, 40, "Brush.Fg", FontWeights.Bold);
            version.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            left.Children.Add(version);
            string meta = $"You have {AppInfo.Version}";
            if (latest.Published.HasValue) meta += $"  ·  Released {latest.Published.Value.ToLocalTime():MMM d, yyyy}";
            if (asset is { Size: > 0 }) meta += $"  ·  {UiFactory.FormatBytes(asset.Size)}";
            left.Children.Add(UiFactory.Text(meta, 13, "Brush.Fg2").Margin(0, 4, 0, 0));
            string notes = Markdownish(latest.Body);
            if (notes.Length > 0)
            {
                var preview = UiFactory.Text(notes, 13, "Brush.Fg2", wrap: true);
                preview.MaxHeight = 120;
                preview.TextTrimming = TextTrimming.WordEllipsis;
                preview.Margin = new Thickness(0, 14, 0, 0);
                left.Children.Add(preview);
            }
            if (asset == null)
                left.Children.Add(UiFactory.Text("This release has no installer attached. Download it from GitHub instead.", 12, "Brush.Fg3", wrap: true).Margin(0, 10, 0, 0));
            grid.Children.Add(left);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 0, 0, 0) };
            var install = UiFactory.Button(_installingUpdate ? "Updating" : "Update now", "Btn.Primary", null, Icons.Download);
            install.Height = 44;
            install.MinWidth = 170;
            install.IsEnabled = !_installingUpdate && asset != null;
            install.Click += async (_, _) => await InstallLauncherUpdateAsync(latest, asset!);
            right.Children.Add(install);
            var github = UiFactory.Button("View on GitHub", "Btn.Ghost", (_, _) => OpenUrl(latest.HtmlUrl.Length > 0 ? latest.HtmlUrl : AppInfo.RepoUrl + "/releases"), Icons.OpenExternal);
            github.Margin = new Thickness(0, 8, 0, 0);
            right.Children.Add(github);
            grid.Children.Add(right.Col(1));

            layers.Children.Add(grid);
            card.Child = layers;
            return card;
        }

        private Border DepotDownloaderCard()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            var title = UiFactory.HStack(8, UiFactory.Text("DepotDownloader", 15, "Brush.Fg", FontWeights.SemiBold));
            if (DepotDownloaderUpdateAvailable) title.Children.Add(UiFactory.Chip("UPDATE", "Brush.Accent", "Brush.OnAccent").Margin(8, 0, 0, 0));
            sp.Children.Add(title);
            string installed = UpdateService.InstalledDepotDownloaderVersion(_downloadService) ?? "unknown";
            sp.Children.Add(UiFactory.Text($"Downloads season files from Steam. Using {installed}, {_downloadService.DepotDownloaderLabel.ToLowerInvariant()}.", 13, "Brush.Fg2", wrap: true).Margin(0, 4, 0, 0));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (_ddLatest is { } latest)
            {
                bool newer = AppInfo.IsNewer(latest.Version, installed);
                sp.Children.Add(UiFactory.Text(newer ? $"Version {latest.Version} is available." : $"Latest release is {latest.Version}.", 12, "Brush.Fg3").Margin(0, 4, 0, 0));
                if (newer && latest.Asset != null)
                {
                    var get = UiFactory.Button("Get latest", "Btn.Secondary", null, Icons.Download);
                    get.Click += async (_, _) =>
                    {
                        if (_downloads.IsRunning) { ShowToast("Wait for the current download to finish.", Icons.Warning); return; }
                        get.IsEnabled = false;
                        get.Content = "Downloading";
                        try
                        {
                            await UpdateService.InstallDepotDownloaderAsync(latest.Asset, null);
                            ShowToast($"DepotDownloader {latest.Version} installed", Icons.Check);
                        }
                        catch (Exception ex) { ShowToast("Could not update DepotDownloader: " + ex.Message, Icons.Warning); }
                        RefreshUpdateIndicators();
                        BuildUpdatesView();
                    };
                    actions.Children.Add(get);
                }
            }
            if (System.IO.File.Exists(DownloadService.UpdatedDepotDownloaderExe))
            {
                var revert = UiFactory.Button("Use bundled", "Btn.Ghost", (_, _) =>
                {
                    if (_downloads.IsRunning) { ShowToast("Wait for the current download to finish.", Icons.Warning); return; }
                    try { UpdateService.RemoveUpdatedDepotDownloader(); } catch (Exception ex) { ShowToast(ex.Message, Icons.Warning); }
                    RefreshUpdateIndicators();
                    BuildUpdatesView();
                });
                revert.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(revert);
            }
            g.Children.Add(sp);
            g.Children.Add(actions.Col(1));
            return UiFactory.Card(g).Margin(0, 0, 0, 14);
        }

        private Border HeatedMetalUpdateCard()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            var title = UiFactory.HStack(8, UiFactory.Text("Heated Metal", 15, "Brush.Fg", FontWeights.SemiBold));
            if (HeatedMetalUpdateAvailable) title.Children.Add(UiFactory.Chip("UPDATE", "Brush.Accent", "Brush.OnAccent").Margin(8, 0, 0, 0));
            sp.Children.Add(title);
            sp.Children.Add(UiFactory.Text(_hmUpdate != null ? $"Installed mod version {S.HmVersion}. Version {_hmUpdate.Version} is available." : $"Installed mod version {S.HmVersion}.", 13, "Brush.Fg2").Margin(0, 4, 0, 0));
            g.Children.Add(sp);
            var check = UiFactory.Button(_hmUpdate != null ? "Update" : "Check", _hmUpdate != null ? "Btn.Primary" : "Btn.Secondary", async (_, _) =>
            {
                var info = _hmUpdate ?? await _downloadService.CheckHmUpdateAsync(S.HmVersion);
                if (info == null) ShowToast("Heated Metal is up to date.", Icons.Check);
                else await OfferHmUpdateAsync(info, force: true);
            }, _hmUpdate != null ? Icons.Download : Icons.Refresh);
            check.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(check.Col(1));
            return UiFactory.Card(g).Margin(0, 0, 0, 14);
        }

        private static Border ReleaseCard(ReleaseInfo r)
        {
            var sp = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = UiFactory.HStack(8, UiFactory.Text(r.Name, 14, "Brush.Fg", FontWeights.SemiBold));
            if (r.Version == AppInfo.Version) name.Children.Add(UiFactory.Chip("INSTALLED").Margin(8, 0, 0, 0));
            top.Children.Add(name);
            if (r.Published.HasValue) top.Children.Add(UiFactory.Text(r.Published.Value.ToLocalTime().ToString("MMM d, yyyy"), 12, "Brush.Fg3").Col(1));
            sp.Children.Add(top);
            string body = Markdownish(r.Body);
            if (body.Length > 0)
                sp.Children.Add(UiFactory.Text(body, 13, "Brush.Fg2", wrap: true).Margin(0, 10, 0, 0));
            var card = UiFactory.Card(sp, 18);
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }

        /// <summary>The first line of release notes that isn't a heading, as plain text.</summary>
        private static string FirstNoteLine(string md)
        {
            string? line = (md ?? "").Replace("\r", "").Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
            return line == null ? "" : Markdownish(line).Trim(' ', '–');
        }

        /// <summary>Flattens GitHub release markdown into readable plain text.</summary>
        private static string Markdownish(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "";
            var lines = md.Replace("\r", "").Split('\n').Select(l =>
            {
                string t = l.TrimEnd();
                t = System.Text.RegularExpressions.Regex.Replace(t, @"^#{1,6}\s*", "");
                t = System.Text.RegularExpressions.Regex.Replace(t, @"^\s*[-*]\s+", "  –  ");
                t = t.Replace("**", "").Replace("`", "");
                t = System.Text.RegularExpressions.Regex.Replace(t, @"\[([^\]]+)\]\([^)]+\)", "$1");
                return t;
            });
            return string.Join("\n", lines).Trim();
        }

        // ══ Installing ═══════════════════════════════════════════════════════

        private async Task InstallLauncherUpdateAsync(ReleaseInfo release, ReleaseAsset asset)
        {
            if (_installingUpdate) return;
            if (AnyGameRunning) { ShowToast("Close the game before updating.", Icons.Warning); return; }
            if (_downloads.IsRunning && !ThemedDialog.Confirm(this, "Download in progress",
                    "Updating closes the launcher and pauses the current download. It picks up where it left off afterwards.", "Update now"))
                return;

            _installingUpdate = true;
            RefreshUpdateIndicators();
            if (_currentView == UpdatesView) BuildUpdatesView();
            var screen = new UpdateScreen(this, release.Version);
            using var cts = new CancellationTokenSource();
            screen.CancelRequested += () => cts.Cancel();
            screen.Show();

            try
            {
                // 1. Download
                screen.SetStep(0, "Downloading");
                var speed = new SpeedMeter();
                var bytes = new Progress<(long Done, long Total)>(p => screen.SetDownload(p.Done, p.Total > 0 ? p.Total : asset.Size, speed.Sample(p.Done)));
                string path = await UpdateService.DownloadAsync(asset.Url, $"SwerveUpdate_{release.Version}_{asset.Name}", null, cts.Token, bytes);

                // 2. Verify
                screen.SetStep(1, "Checking the download");
                screen.SetIndeterminate(true);
                await Task.Delay(500);
                long size = new System.IO.FileInfo(path).Length;
                if (asset.Size > 0 && size != asset.Size)
                    throw new InvalidOperationException($"The download is incomplete ({UiFactory.FormatBytes(size)} of {UiFactory.FormatBytes(asset.Size)}). Try again.");
                bool installer = UpdateService.IsInstaller(path);

                // 3. Install
                screen.SetStep(2, installer ? "Starting the installer. Windows may ask for permission." : "Replacing the launcher");
                screen.HideCancel();
                await Task.Delay(400);
                _downloads.Pause();
                UpdateService.StartUpdate(path);

                // 4. Restart
                screen.SetStep(3, $"Restarting into version {release.Version}");
                screen.Complete();
                await Task.Delay(1400);
                ExitApplication();
            }
            catch (OperationCanceledException)
            {
                screen.Close();
                _installingUpdate = false;
                RefreshUpdateIndicators();
                if (_currentView == UpdatesView) BuildUpdatesView();
                ShowToast("Update cancelled", Icons.Info);
            }
            catch (Exception ex)
            {
                string message = ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }
                    ? "Windows permission was declined, so the installer didn't start."
                    : ex.Message;
                bool retry = await screen.FailAsync(message);
                screen.Close();
                _installingUpdate = false;
                RefreshUpdateIndicators();
                if (_currentView == UpdatesView) BuildUpdatesView();
                if (retry) await InstallLauncherUpdateAsync(release, asset);
            }
        }

        /// <summary>Smoothed transfer speed in bytes per second.</summary>
        private sealed class SpeedMeter
        {
            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
            private long _lastBytes;
            private double _lastTime;
            private double _speed;

            public double Sample(long bytes)
            {
                double now = _clock.Elapsed.TotalSeconds;
                double dt = now - _lastTime;
                if (dt < 0.4) return _speed;
                double instant = (bytes - _lastBytes) / dt;
                _speed = _speed <= 0 ? instant : _speed * 0.7 + instant * 0.3;
                _lastBytes = bytes;
                _lastTime = now;
                return _speed;
            }
        }

        // ══ Heated Metal update prompt (on startup) ══════════════════════════

        private async Task CheckHmUpdateOnStartupAsync()
        {
            if (!_patches.Any(p => p.IsHeatedMetal && p.IsInstalled)) return;
            var info = _hmUpdate ?? await _downloadService.CheckHmUpdateAsync(S.HmVersion);
            if (info == null || S.HmSkipVersion == info.Version) return;
            await OfferHmUpdateAsync(info, force: false);
        }

        private async Task OfferHmUpdateAsync(DownloadService.HmUpdateInfo info, bool force)
        {
            var dlg = new HmUpdateDialog(S.HmVersion, info.Version, info.Notes) { Owner = this };
            dlg.ShowDialog();
            switch (dlg.Result)
            {
                case HmUpdateDialog.UpdateResult.UpdateNow:
                    var target = _patches.FirstOrDefault(p => p.IsHeatedMetal && p.IsInstalled);
                    if (target == null) return;
                    try
                    {
                        ShowToast($"Downloading Heated Metal {info.Version}", Icons.Download);
                        await _downloadService.ApplyHmUpdateAsync(info.DownloadUrl, target.InstallDir, msg => Dispatcher.Invoke(() => ShowToast(msg, Icons.Archive)));
                        S.HmVersion = info.Version;
                        S.HmSkipVersion = "";
                        SaveSettings();
                        _hmUpdate = null;
                        RefreshUpdateIndicators();
                        ShowToast($"Heated Metal updated to {info.Version}", Icons.Check);
                    }
                    catch (Exception ex) { ShowToast("Heated Metal update failed: " + ex.Message, Icons.Warning); }
                    break;
                case HmUpdateDialog.UpdateResult.Skip when !force:
                    S.HmSkipVersion = info.Version;
                    SaveSettings();
                    break;
            }
        }
    }
}
