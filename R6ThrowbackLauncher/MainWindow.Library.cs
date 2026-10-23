using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private string _librarySearch = "";
        private string _libraryFilter = "all";
        private readonly ConcurrentDictionary<string, long> _sizeCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sizePending = new(StringComparer.OrdinalIgnoreCase);
        private bool _libraryControlsReady;

        private void InitLibraryControls()
        {
            foreach (ComboBoxItem item in SortCombo.Items)
                if ((string)item.Tag == S.LibrarySortMode) SortCombo.SelectedItem = item;
            if (SortCombo.SelectedItem == null) SortCombo.SelectedIndex = 0;
            (S.LibraryView == "list" ? ViewListToggle : ViewGridToggle).IsChecked = true;
            _libraryControlsReady = true;
        }

        private void LibrarySearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _librarySearch = LibrarySearch.Text.Trim();
            if (_libraryControlsReady) BuildHome();
        }

        private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_libraryControlsReady || SortCombo.SelectedItem is not ComboBoxItem item) return;
            S.LibrarySortMode = (string)item.Tag;
            SaveSettings();
            BuildHome();
        }

        private void ViewToggle_Click(object sender, RoutedEventArgs e)
        {
            S.LibraryView = ViewListToggle.IsChecked == true ? "list" : "grid";
            SaveSettings();
            BuildHome();
        }

        private void LibraryMenu_Click(object sender, RoutedEventArgs e)
        {
            UiFactory.OpenMenu((UIElement)sender,
                UiFactory.MenuItem(S.ShowHeroBanner ? "Hide continue playing" : "Show continue playing", Icons.Game, () =>
                {
                    S.ShowHeroBanner = !S.ShowHeroBanner; SaveSettings(); BuildHome();
                }),
                UiFactory.MenuItem(S.ShowRecentlyPlayed ? "Hide recently played" : "Show recently played", Icons.Clock, () =>
                {
                    S.ShowRecentlyPlayed = !S.ShowRecentlyPlayed; SaveSettings(); BuildHome();
                }),
                new Separator(),
                UiFactory.MenuItem("Play a random season", Icons.Sync, () => _ = PlayRandomAsync(), Installed.Any()),
                UiFactory.MenuItem("Import a game folder", Icons.FolderOpen, ImportFolder),
                UiFactory.MenuItem("Rescan libraries", Icons.Refresh, () =>
                {
                    RefreshInstallState();
                    RefreshAllViews();
                    ShowToast("Libraries rescanned");
                }));
        }

        private void SaveSettings()
        {
            try { _settingsService.Save(); } catch (Exception ex) { ShowToast("Settings could not be saved: " + ex.Message, Icons.Warning); }
        }

        // ── Home ─────────────────────────────────────────────────────────────

        private IEnumerable<SeasonPatch> LibraryEntries()
        {
            var pinned = S.PinnedSeasons;
            IEnumerable<SeasonPatch> list = _patches.Where(p => p.IsInstalled || p.IsPartial);

            list = _libraryFilter switch
            {
                "favorites"  => list.Where(p => pinned.Contains(p.Id)),
                "hm"         => list.Where(p => p.IsHeatedMetal),
                "incomplete" => list.Where(p => p.IsPartial),
                _            => list,
            };

            if (!string.IsNullOrWhiteSpace(_librarySearch))
            {
                string q = _librarySearch.ToLowerInvariant();
                list = list.Where(p => p.SeasonName.ToLowerInvariant().Contains(q) || p.Code.ToLowerInvariant().Contains(q)
                                       || p.Id.ToLowerInvariant().Contains(q));
            }

            var sorted = S.LibrarySortMode switch
            {
                "recent"   => list.OrderByDescending(p => _playtime.GetLastPlayedForPatch(p.Id)).ThenBy(p => p.Rank),
                "playtime" => list.OrderByDescending(p => _playtime.GetSecondsForPatch(p.Id)).ThenBy(p => p.Rank),
                "date"     => list.OrderByDescending(p => p.Date ?? DateTime.MinValue),
                "name"     => list.OrderBy(p => p.BaseName).ThenBy(p => p.IsHeatedMetal),
                _          => list.OrderBy(p => p.Rank).ThenBy(p => p.IsHeatedMetal),
            };
            return sorted.OrderByDescending(p => pinned.Contains(p.Id));
        }

        private void BuildHome()
        {
            HomeContent.Children.Clear();
            var entries = LibraryEntries().ToList();
            int installed = Installed.Count();
            HomeSubtitle.Text = installed == 0
                ? "Nothing installed yet"
                : $"{installed} installed{(_patches.Count(p => p.IsPartial) is int n && n > 0 ? $"  ·  {n} incomplete" : "")}";

            if (installed == 0 && !_patches.Any(p => p.IsPartial))
            {
                HomeContent.Children.Add(UiFactory.EmptyState(Icons.Library, "Your library is empty",
                    "Pick a season to download. Installed seasons show up here with your playtime and quick launch.",
                    UiFactory.Button("Browse seasons", "Btn.Primary", (_, _) => Nav_Click(NavBrowse, new RoutedEventArgs()), Icons.Browse)));
                return;
            }

            bool searching = !string.IsNullOrWhiteSpace(_librarySearch);
            if (!searching && _libraryFilter == "all")
            {
                if (S.ShowHeroBanner && BuildHero() is { } hero) HomeContent.Children.Add(hero);
                if (S.ShowRecentlyPlayed && BuildRecentRow() is { } recent) HomeContent.Children.Add(recent);
            }

            HomeContent.Children.Add(BuildFilterBar(entries.Count));

            if (entries.Count == 0)
            {
                HomeContent.Children.Add(UiFactory.EmptyState(Icons.Search, "No matches", "Try another search or filter."));
                return;
            }

            if (S.LibraryView == "list")
            {
                var list = new StackPanel();
                foreach (var p in entries) list.Children.Add(BuildListRow(p));
                HomeContent.Children.Add(list);
            }
            else
            {
                var wrap = new WrapPanel { Margin = new Thickness(0, 0, -16, 0) };
                foreach (var p in entries) wrap.Children.Add(BuildLibraryCard(p));
                HomeContent.Children.Add(wrap);
            }
        }

        private UIElement BuildFilterBar(int count)
        {
            var host = new Grid { Margin = new Thickness(0, 8, 0, 14) };
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, label) in new[] { ("all", "All"), ("favorites", "Favorites"), ("hm", "Heated Metal"), ("incomplete", "Incomplete") })
            {
                var rb = new RadioButton { Content = label, GroupName = "LibFilter", IsChecked = _libraryFilter == key };
                rb.Styled("Segment");
                string k = key;
                rb.Click += (_, _) => { _libraryFilter = k; BuildHome(); };
                seg.Children.Add(rb);
            }
            var segHost = new Border { Child = seg };
            segHost.Styled("SegmentHost");
            host.Children.Add(segHost);
            host.Children.Add(UiFactory.Text($"{count} shown", 12, "Brush.Fg3").Col(1).Margin(14, 0, 0, 0));
            return host;
        }

        private UIElement? BuildHero()
        {
            var patch = _playtime.GetRecentlyPlayed(5).Select(FindPatch).FirstOrDefault(p => p is { IsInstalled: true })
                        ?? Installed.OrderByDescending(p => p.Rank).FirstOrDefault();
            if (patch == null) return null;

            var card = new Border { Height = 260, CornerRadius = new CornerRadius(12), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 22), Cursor = Cursors.Hand };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            var grid = new Grid();
            grid.Clip = new RectangleGeometry { RadiusX = 12, RadiusY = 12 };
            grid.SizeChanged += (_, e) => ((RectangleGeometry)grid.Clip).Rect = new Rect(e.NewSize);
            grid.Children.Add(ArtworkCache.Build(patch.ArtworkPath, 1080, card, zoom: 1.03));
            var fade = new Border();
            fade.SetResourceReference(Border.BackgroundProperty, "Brush.ArtFadeLeft");
            grid.Children.Add(fade);

            long last = _playtime.GetLastPlayedForPatch(patch.Id);
            long secs = _playtime.GetSecondsForPatch(patch.Id);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(32, 0, 32, 28) };
            info.Children.Add(new TextBlock { Text = last > 0 ? "CONTINUE PLAYING" : "READY TO PLAY", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 255, 255, 255)) });
            info.Children.Add(new TextBlock { Text = patch.IsHeatedMetal ? $"{patch.BaseName} (Heated Metal)" : patch.BaseName, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0) });
            string meta = $"{patch.Code}  ·  Patch {patch.PatchKey}";
            if (last > 0) meta += $"  ·  Last played {PlaytimeService.FormatRelative(last)}";
            if (secs > 0) meta += $"  ·  {PlaytimeService.Format(secs)} played";
            info.Children.Add(new TextBlock { Text = meta, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)), Margin = new Thickness(0, 6, 0, 16) });

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(PlayButton(patch, large: true));
            var details = UiFactory.Button("Details", "Btn.Secondary", (_, _) => OpenDetail(patch), Icons.Info);
            details.Height = 40;
            details.Margin = new Thickness(10, 0, 0, 0);
            actions.Children.Add(details);
            info.Children.Add(actions);
            grid.Children.Add(info);

            card.Child = grid;
            card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not DependencyObject d || !IsInButton(d)) OpenDetail(patch); };
            return card;
        }

        private UIElement? BuildRecentRow()
        {
            var recent = _playtime.GetRecentlyPlayed(8).Select(FindPatch).Where(p => p is { IsInstalled: true }).Cast<SeasonPatch>().Take(6).ToList();
            if (recent.Count < 2) return null;

            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            sp.Children.Add(UiFactory.Section("RECENTLY PLAYED").Margin(0, 0, 0, 10));
            var row = new WrapPanel();
            foreach (var p in recent)
            {
                var tile = new Border { Width = 200, Height = 72, Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                tile.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");
                tile.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var art = ArtworkCache.Build(p.ArtworkPath, 160, tile, zoom: 1.1);
                art.Clip = new RectangleGeometry(new Rect(0, 0, 70, 70), 7, 7);
                g.Children.Add(art);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) };
                text.Children.Add(UiFactory.Text(p.BaseName, 13, "Brush.Fg", FontWeights.SemiBold));
                text.Children.Add(UiFactory.Text(PlaytimeService.FormatRelative(_playtime.GetLastPlayedForPatch(p.Id)), 11, "Brush.Fg3").Margin(0, 2, 0, 0));
                g.Children.Add(text.Col(1));
                tile.Child = g;
                var local = p;
                tile.MouseLeftButtonUp += (_, _) => OpenDetail(local);
                tile.ToolTip = $"{p.ShortLabel}  ·  Click for details";
                row.Children.Add(tile);
            }
            sp.Children.Add(row);
            return sp;
        }

        // ── Cards ────────────────────────────────────────────────────────────

        private Border BuildLibraryCard(SeasonPatch p)
        {
            bool pinned = S.PinnedSeasons.Contains(p.Id);
            bool running = IsPatchRunning(p);

            var card = new Border
            {
                Width = 212, Margin = new Thickness(0, 0, 16, 16), CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ClipToBounds = true,
            };
            card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");
            UiFactory.AutomationName(card, p.ShortLabel);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var art = ArtworkCache.Build(p.ArtworkPath, 440, card);
            art.Clip = new RectangleGeometry(new Rect(0, 0, 212, 212), 9, 9);
            root.Children.Add(art);

            var badges = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            if (running) badges.Children.Add(OverlayChip("PLAYING", solid: true));
            else if (_downloads.IsActive(p)) badges.Children.Add(OverlayChip($"{_downloads.Percent:0}%", solid: true));
            else if (_downloads.IsQueued(p)) badges.Children.Add(OverlayChip("QUEUED"));
            else if (p.IsPartial) badges.Children.Add(OverlayChip("INCOMPLETE"));
            if (p.IsHeatedMetal) badges.Children.Add(OverlayChip("HEATED METAL").Margin(badges.Children.Count > 0 ? 6 : 0, 0, 0, 0));
            root.Children.Add(badges);

            if (pinned)
                root.Children.Add(new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                    Child = new TextBlock { Text = Icons.StarFilled, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = (FontFamily)FindResource("Font.Icons") },
                    ToolTip = "Pinned",
                });

            if (p.IsInstalled)
            {
                var play = PlayButton(p, large: false);
                play.HorizontalAlignment = HorizontalAlignment.Right;
                play.VerticalAlignment = VerticalAlignment.Bottom;
                play.Margin = new Thickness(0, 0, 10, 10);
                play.Opacity = running ? 1 : 0;
                card.MouseEnter += (_, _) => play.Opacity = 1;
                card.MouseLeave += (_, _) => play.Opacity = running ? 1 : 0;
                root.Children.Add(play);
            }

            var info = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            info.Children.Add(UiFactory.Text(p.IsHeatedMetal ? $"{p.BaseName} HM" : p.BaseName, 14, "Brush.Fg", FontWeights.SemiBold));
            var meta = UiFactory.Text("", 12, "Brush.Fg3").Margin(0, 3, 0, 0);
            info.Children.Add(meta);
            SetCardMeta(meta, p);
            root.Children.Add(info.Row(1));

            card.Child = root;
            card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInButton(d)) return; OpenDetail(p); };
            card.MouseRightButtonUp += (_, e) => { e.Handled = true; OpenSeasonMenu(card, p); };
            return card;
        }

        private void SetCardMeta(TextBlock meta, SeasonPatch p)
        {
            long secs = _playtime.GetSecondsForPatch(p.Id);
            string Build(long? bytes)
            {
                var parts = new List<string> { p.Code };
                if (secs > 0) parts.Add(PlaytimeService.Format(secs));
                if (bytes is long b && b > 0) parts.Add(UiFactory.FormatBytes(b));
                return string.Join("  ·  ", parts);
            }
            if (_sizeCache.TryGetValue(p.InstallDir, out long known)) { meta.Text = Build(known); return; }
            meta.Text = Build(null);
            RequestSize(p.InstallDir, size => meta.Text = Build(size));
        }

        private Border BuildListRow(SeasonPatch p)
        {
            bool running = IsPatchRunning(p);
            var row = new Border { Height = 64, Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Padding = new Thickness(8, 0, 12, 0) };
            row.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            row.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var art = ArtworkCache.Build(p.ArtworkPath, 120, row, zoom: 1.12);
            art.Width = 48; art.Height = 48;
            art.Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 6, 6);
            g.Children.Add(art);

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(UiFactory.Text(p.BaseName, 14, "Brush.Fg", FontWeights.SemiBold));
            if (p.IsHeatedMetal) name.Children.Add(UiFactory.Chip("HEATED METAL").Margin(8, 0, 0, 0));
            if (S.PinnedSeasons.Contains(p.Id)) name.Children.Add(UiFactory.Icon(Icons.StarFilled, 11, "Brush.Fg2").Margin(8, 0, 0, 0));
            if (running) name.Children.Add(UiFactory.Chip("PLAYING", "Brush.Accent", "Brush.OnAccent").Margin(8, 0, 0, 0));
            else if (p.IsPartial) name.Children.Add(UiFactory.Chip("INCOMPLETE").Margin(8, 0, 0, 0));
            title.Children.Add(name);
            title.Children.Add(UiFactory.Text($"{p.Code}  ·  Patch {p.PatchKey}", 12, "Brush.Fg3").Margin(0, 2, 0, 0));
            g.Children.Add(title.Col(1));

            long secs = _playtime.GetSecondsForPatch(p.Id);
            g.Children.Add(UiFactory.Text(secs > 0 ? PlaytimeService.Format(secs) : "Not played", 12, "Brush.Fg2").Col(2));
            var size = UiFactory.Text("", 12, "Brush.Fg3").Col(3);
            if (_sizeCache.TryGetValue(p.InstallDir, out long known)) size.Text = UiFactory.FormatBytes(known);
            else RequestSize(p.InstallDir, b => size.Text = UiFactory.FormatBytes(b));
            g.Children.Add(size);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (p.IsInstalled) actions.Children.Add(PlayButton(p, large: false));
            var more = UiFactory.IconButton(Icons.More, "More actions");
            more.Margin = new Thickness(6, 0, 0, 0);
            more.Click += (_, _) => OpenSeasonMenu(more, p);
            actions.Children.Add(more);
            g.Children.Add(actions.Col(4));

            row.Child = g;
            row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInButton(d)) return; OpenDetail(p); };
            row.MouseRightButtonUp += (_, e) => { e.Handled = true; OpenSeasonMenu(row, p); };
            return row;
        }

        private static Border OverlayChip(string text, bool solid = false) => new()
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 3),
            Background = solid ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            Child = new TextBlock
            {
                Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold,
                Foreground = solid ? Brushes.Black : Brushes.White,
            },
        };

        private Button PlayButton(SeasonPatch p, bool large)
        {
            bool running = IsPatchRunning(p);
            var btn = UiFactory.Button(running ? "Stop" : "Play", "Btn.Primary", null, running ? Icons.Stop : Icons.Play);
            btn.Height = large ? 40 : 32;
            btn.Padding = new Thickness(large ? 20 : 14, 0, large ? 20 : 14, 0);
            UiFactory.AutomationName(btn, $"{(running ? "Stop" : "Play")} {p.ShortLabel}");
            btn.Click += async (_, e) =>
            {
                e.Handled = true;
                if (IsPatchRunning(p)) StopGame();
                else await LaunchPatchAsync(p);
            };
            return btn;
        }

        private static bool IsInButton(DependencyObject d)
        {
            while (d != null)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // ── Season context menu ──────────────────────────────────────────────

        private void OpenSeasonMenu(UIElement target, SeasonPatch p)
        {
            bool pinned = S.PinnedSeasons.Contains(p.Id);
            var items = new List<object>();
            if (p.IsInstalled)
            {
                items.Add(IsPatchRunning(p)
                    ? UiFactory.MenuItem("Stop game", Icons.Stop, StopGame)
                    : UiFactory.MenuItem("Play", Icons.Play, () => _ = LaunchPatchAsync(p)));
            }
            items.Add(UiFactory.MenuItem("Details", Icons.Info, () => OpenDetail(p)));
            items.Add(UiFactory.MenuItem("Manage", Icons.Settings, () => OpenDetail(p, "manage"), p.IsInstalled || p.IsPartial));
            items.Add(new Separator());
            items.Add(UiFactory.MenuItem(pinned ? "Unpin" : "Pin to top", pinned ? Icons.Unpin : Icons.Pin, () => TogglePin(p)));
            if (p.IsInstalled || p.IsPartial)
            {
                items.Add(UiFactory.MenuItem("Open folder", Icons.Folder, () => OpenFolder(p.InstallDir)));
                items.Add(UiFactory.MenuItem("Verify files", Icons.Sync, () => QueueVerify(p), p.IsInstalled && !_downloads.IsBusyWith(p) && !IsPatchRunning(p)));
                if (p.IsPartial)
                    items.Add(UiFactory.MenuItem("Resume download", Icons.Download, () => StartDownload(p), !_downloads.IsBusyWith(p)));
                items.Add(new Separator());
                items.Add(UiFactory.MenuItem("Uninstall", Icons.Delete, () => Uninstall(p), !_downloads.IsBusyWith(p) && !IsPatchRunning(p)));
            }
            else
            {
                items.Add(UiFactory.MenuItem("Download", Icons.Download, () => StartDownload(p), !_downloads.IsBusyWith(p)));
            }
            UiFactory.OpenMenu(target, items.ToArray());
        }

        private void TogglePin(SeasonPatch p)
        {
            if (!S.PinnedSeasons.Remove(p.Id)) S.PinnedSeasons.Add(p.Id);
            SaveSettings();
            BuildHome();
        }

        private async Task PlayRandomAsync()
        {
            var installed = Installed.ToList();
            if (installed.Count == 0) return;
            var pick = installed[Random.Shared.Next(installed.Count)];
            if (ThemedDialog.Confirm(this, "Play a random season", $"Launch {pick.ShortLabel}?", "Play"))
                await LaunchPatchAsync(pick);
        }

        // ── Folder sizes (computed off the UI thread, cached) ────────────────

        private readonly Dictionary<string, List<Action<long>>> _sizeWaiters = new(StringComparer.OrdinalIgnoreCase);

        // Called on the UI thread only; the worker writes the cache and hands results back via the dispatcher.
        private void RequestSize(string dir, Action<long> apply)
        {
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return;
            if (!_sizeWaiters.TryGetValue(dir, out var waiters)) _sizeWaiters[dir] = waiters = new();
            waiters.Add(apply);
            if (!_sizePending.Add(dir)) return;
            Task.Run(() =>
            {
                long size = InstallLocator.DirectorySize(dir);
                _sizeCache[dir] = size;
                Dispatcher.InvokeAsync(() =>
                {
                    _sizePending.Remove(dir);
                    if (_sizeWaiters.Remove(dir, out var list))
                        foreach (var w in list) w(size);
                });
            });
        }

        private void InvalidateSizes(SeasonPatch? only)
        {
            if (only == null) _sizeCache.Clear();
            else _sizeCache.TryRemove(only.InstallDir, out _);
        }
    }
}
