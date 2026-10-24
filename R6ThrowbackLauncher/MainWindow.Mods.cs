using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    /// <summary>Modifications page: unlock all, playlist editor and game mods, all through Liberator.</summary>
    public partial class MainWindow
    {
        // Playlist editing is offered for Y1 to Y4, where Liberator can rewrite the game-mode list.
        private const int MaxPlaylistYear = 4;

        private static readonly (string Title, (string Key, string Label)[] Mods)[] ModGroups =
        {
            ("Players", new[]
            {
                ("deathless", "Players and hostage can't die"),
                ("unlimitedEquip", "Unlimited equipment"),
                ("unlimitedAmmo", "Unlimited ammo"),
            }),
            ("Match", new[]
            {
                ("infiniteTime", "Infinite match time"),
                ("disableAI", "Brain-dead AI"),
                ("displayBuild", "Show build number"),
            }),
            ("Loadout", new[]
            {
                ("disablePrimary", "No primary weapon"),
                ("disableSecondary", "No secondary weapon"),
                ("disablePrimaryGadget", "No primary gadget"),
                ("disableSecondaryGadget", "No secondary gadget"),
            }),
        };

        private LiberatorSession? _liberator;
        private SeasonPatch? _liberatorPatch;
        private string? _liberatorRefusal;
        private readonly Dictionary<string, bool> _modValues = new();
        private readonly List<int> _playlistPath = new();
        private string _playlistPicked = "";
        private string _playlistPickedLabel = "";
        private bool _liberatorBusy;
        private string _liberatorStatus = "";

        // ══ Session ══════════════════════════════════════════════════════════

        private void StartLiberator(SeasonPatch patch, string? renderer)
        {
            StopLiberator();
            _liberatorPatch = patch;
            _liberatorRefusal = null;
            if (!S.LiberatorEnabled) { RefreshModsView(); return; }
            if (!LiberatorService.IsInstalled)
            {
                _liberatorRefusal = "Liberator isn't downloaded.";
                ShowToast("Unlock all is on, but Liberator isn't downloaded. Get it on the Modifications page.", Icons.Warning);
                RefreshModsView();
                return;
            }
            if (LiberatorService.Unavailable(patch, renderer) is string reason)
            {
                _liberatorRefusal = reason;
                ShowToast(reason, Icons.Info);
                RefreshModsView();
                return;
            }

            var session = new LiberatorSession();
            bool announced = false, warned = false;
            session.StateChanged += state => Dispatcher.InvokeAsync(() =>
            {
                if (_liberator != session) return;
                if (state.Unsupported && !warned) { warned = true; ShowToast("Liberator doesn't support this build, so nothing was unlocked.", Icons.Info); }
                else if (state.Error != null && !warned) { warned = true; ShowToast(state.Error, Icons.Warning); }
                else if (state.Unlocked && !announced)
                {
                    announced = true;
                    ShowToast("Unlock all is running. Every operator and item is unlocked.", Icons.Lock);
                }
                UpdateNowPlaying();
                RefreshModsView();
            });
            session.TreeChanged += _ => Dispatcher.InvokeAsync(() => { if (_liberator == session) RefreshModsView(); });
            _liberator = session;
            session.Start();
            RefreshModsView();
        }

        private void StopLiberator()
        {
            _liberator?.Dispose();
            _liberator = null;
            _liberatorPatch = null;
            _liberatorRefusal = null;
            _modValues.Clear();
            _playlistPath.Clear();
            _playlistPicked = "";
            _playlistPickedLabel = "";
            RefreshModsView();
        }

        private void RefreshModsView()
        {
            if (_currentView == ModsView) BuildModsView();
        }

        private enum LibStatus { NotDownloaded, Off, Waiting, Starting, Running, NotSupported, Error }

        private (LibStatus Status, string Label, string Detail) LiberatorStatusInfo()
        {
            if (!LiberatorService.IsInstalled) return (LibStatus.NotDownloaded, "Not downloaded", "Download Liberator to use unlock all and mods.");
            if (!S.LiberatorEnabled) return (LibStatus.Off, "Off", "Turn on unlock all to use it the next time you play.");
            if (_liberator == null)
                return _liberatorRefusal != null && _liberatorPatch != null
                    ? (LibStatus.NotSupported, "Not supported", $"{_liberatorPatch.ShortLabel}: {_liberatorRefusal}")
                    : (LibStatus.Waiting, "Waiting for game", "Starts when you launch a supported season from your library.");
            var st = _liberator.State;
            string season = _liberatorPatch?.ShortLabel ?? "this season";
            if (st.Unsupported) return (LibStatus.NotSupported, "Not supported", $"Liberator doesn't support the build of {season}.");
            if (st.Error != null) return (LibStatus.Error, "Error", st.Error);
            if (st.Unlocked) return (LibStatus.Running, "Running", $"Everything is unlocked in {season}.");
            return (LibStatus.Starting, "Starting", st.Attached ? $"Attached to {season}. Unlocking once the game finishes loading." : $"Waiting for {season} to start.");
        }

        private static Border StatusChip(LibStatus status, string label)
        {
            bool strong = status is LibStatus.Running or LibStatus.Error;
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, strong ? "Brush.OnAccent" : "Brush.Fg2");
            var text = UiFactory.Text(label.ToUpperInvariant(), 11, strong ? "Brush.OnAccent" : "Brush.Fg", FontWeights.SemiBold);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4), VerticalAlignment = VerticalAlignment.Center,
                Child = UiFactory.HStack(0, status == LibStatus.Error ? UiFactory.Icon(Icons.Warning, 11, "Brush.OnAccent").Margin(0, 0, 7, 0) : dot, text),
            };
            return chip.Bg(strong ? "Brush.Accent" : "Brush.Surface3");
        }

        // ══ Page ═════════════════════════════════════════════════════════════

        private void BuildModsView()
        {
            double offset = ModsScroll.VerticalOffset;
            ModsContent.Children.Clear();
            var (status, label, _) = LiberatorStatusInfo();
            string subtitle = _liberatorPatch != null && _liberator != null
                ? $"Playing {_liberatorPatch.ShortLabel}. Changes apply to this game and reset when it closes."
                : "Unlock all, game modes and mods for older seasons. Changes reset when the game closes.";
            ModsContent.Children.Add(PageHeader("Modifications", subtitle, StatusChip(status, label)));

            ModsContent.Children.Add(UnlockAllCard().Margin(0, 0, 0, 16));

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            cols.Children.Add(PlaylistCard());
            cols.Children.Add(GameModsCard().Col(2));
            ModsContent.Children.Add(cols);
            ModsScroll.ScrollToVerticalOffset(offset);
        }

        private static StackPanel CardTitle(string title, string hint)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            sp.Children.Add(UiFactory.Text(title, 15, "Brush.Fg", FontWeights.SemiBold));
            if (hint.Length > 0) sp.Children.Add(UiFactory.Text(hint, 12, "Brush.Fg3", wrap: true).Margin(0, 3, 0, 0));
            return sp;
        }

        // ── Unlock all ───────────────────────────────────────────────────────

        private Border UnlockAllCard()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(CardTitle("Unlock all", $"Unlocks every operator and item while you play Y1 to Y{LiberatorService.MaxYear} seasons. Uses Liberator by xeralin (GPLv3), downloaded from their GitHub release. It changes the game's memory while it runs; no game files are modified. Not available for Y10 and later, Heated Metal or Vulkan."));

            var (status, label, detail) = LiberatorStatusInfo();
            var statusRow = UiFactory.HStack(10, StatusChip(status, label), UiFactory.Text(detail, 13, status == LibStatus.Error ? "Brush.Fg" : "Brush.Fg2", wrap: true));
            ((FrameworkElement)statusRow.Children[1]).MaxWidth = 620;
            left.Children.Add(statusRow);

            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            if (!LiberatorService.IsInstalled)
            {
                var get = UiFactory.Button(_liberatorBusy ? "Downloading" : "Download Liberator", "Btn.Primary", async (_, _) => await DownloadLiberatorAsync(), Icons.Download);
                get.IsEnabled = !_liberatorBusy;
                buttons.Children.Add(get.Margin(0, 0, 8, 8));
            }
            else
            {
                var update = UiFactory.Button(_liberatorBusy ? "Checking" : "Check for update", "Btn.Secondary", async (_, _) => await DownloadLiberatorAsync(onlyIfNewer: true), Icons.Refresh);
                update.IsEnabled = !_liberatorBusy;
                buttons.Children.Add(update.Margin(0, 0, 8, 8));
                buttons.Children.Add(UiFactory.Button("Remove", "Btn.Ghost", (_, _) =>
                {
                    if (_liberator != null) { ShowToast("Close the game first.", Icons.Warning); return; }
                    try { LiberatorService.Remove(); } catch (Exception ex) { ShowToast(ex.Message, Icons.Warning); return; }
                    S.LiberatorEnabled = false;
                    SaveSettings();
                    BuildModsView();
                }).Margin(0, 0, 8, 8));
            }
            buttons.Children.Add(UiFactory.Button("Liberator on GitHub", "Btn.Ghost", (_, _) => OpenUrl(LiberatorService.ProjectUrl), Icons.OpenExternal).Margin(0, 0, 8, 8));
            left.Children.Add(buttons);

            var info = LiberatorService.IsInstalled ? LiberatorService.ReadInfo() : null;
            string version = _liberatorStatus.Length > 0 ? _liberatorStatus
                : info != null ? $"Liberator {info.Tag}, downloaded {info.Downloaded.ToLocalTime():MMM d, yyyy}."
                : "";
            if (version.Length > 0) left.Children.Add(UiFactory.Text(version, 12, "Brush.Fg3", wrap: true));
            g.Children.Add(left);

            var toggle = UiFactory.Switch(S.LiberatorEnabled, on =>
            {
                S.LiberatorEnabled = on;
                SaveSettings();
                if (!on && _liberator != null) ShowToast("Unlock all turns off when this game closes.", Icons.Info);
                BuildModsView();
            });
            toggle.IsEnabled = LiberatorService.IsInstalled;
            toggle.VerticalAlignment = VerticalAlignment.Top;
            toggle.ToolTip = LiberatorService.IsInstalled ? "Unlock all when I play" : "Download Liberator first";
            g.Children.Add(toggle.Col(1));
            return UiFactory.Card(g);
        }

        private async Task DownloadLiberatorAsync(bool onlyIfNewer = false)
        {
            if (_liberatorBusy) return;
            if (_liberator != null) { ShowToast("Close the game first.", Icons.Warning); return; }
            _liberatorBusy = true;
            _liberatorStatus = "";
            RefreshModsView();
            try
            {
                if (onlyIfNewer)
                {
                    string? latest = await LiberatorService.LatestTagAsync();
                    if (latest == null) throw new InvalidOperationException("GitHub couldn't be reached.");
                    if (latest == LiberatorService.ReadInfo()?.Tag)
                    {
                        _liberatorStatus = $"You have the latest Liberator ({latest}).";
                        return;
                    }
                }
                var progress = new Progress<string>(m => { _liberatorStatus = m; RefreshModsView(); });
                var info = await LiberatorService.DownloadAsync(progress);
                _liberatorStatus = "";
                if (!onlyIfNewer) { S.LiberatorEnabled = true; SaveSettings(); }
                ShowToast($"Liberator {info.Tag} is ready", Icons.Check);
            }
            catch (Exception ex)
            {
                _liberatorStatus = "Download failed: " + ex.Message;
            }
            finally
            {
                _liberatorBusy = false;
                RefreshModsView();
            }
        }

        // ── Playlist editor ──────────────────────────────────────────────────

        private Border PlaylistCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(CardTitle("Playlist editor", $"Change the game mode, map list and difficulty for Y1 to Y{MaxPlaylistYear} seasons. Pick a mode, then start the match in game."));

            var st = _liberator?.State;
            string? blocked =
                _liberator == null || _liberatorPatch == null ? $"Launch a Y1 to Y{MaxPlaylistYear} season with unlock all on to edit its playlists."
                : _liberatorPatch.Year > MaxPlaylistYear ? $"The playlist editor works on Y1 to Y{MaxPlaylistYear} seasons. {_liberatorPatch.ShortLabel} isn't one of them."
                : st!.Unsupported || st.Error != null ? "Liberator isn't running, so playlists can't be changed."
                : !st.Unlocked ? "Game modes appear here once the game has loaded."
                : !st.FullFeature || _liberator.Tree == null ? "This build has no editable playlists."
                : null;
            if (blocked != null)
            {
                sp.Children.Add(UiFactory.EmptyState(Icons.List, "No playlists yet", blocked));
                return UiFactory.Card(sp);
            }

            var roots = _liberator!.Tree!;
            // Columns: the roots, then the children of each selected branch.
            var columns = new List<(IReadOnlyList<PlaylistNode> Nodes, int Selected)>();
            IReadOnlyList<PlaylistNode> level = roots;
            int depth = 0;
            while (true)
            {
                int sel = depth < _playlistPath.Count ? _playlistPath[depth] : -1;
                if (sel >= level.Count) { _playlistPath.RemoveRange(depth, _playlistPath.Count - depth); sel = -1; }
                columns.Add((level, sel));
                if (sel < 0 || level[sel].Children.Count == 0) break;
                level = level[sel].Children;
                depth++;
            }

            var grid = new Grid { Height = 360 };
            for (int c = 0; c < columns.Count; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 150 });
                var list = new StackPanel { Margin = new Thickness(6) };
                var (nodes, selected) = columns[c];
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    int col = c, index = i;
                    bool branch = node.Children.Count > 0;
                    bool active = branch ? selected == i : node.Id == _playlistPicked;
                    var row = new Grid();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(UiFactory.Text(node.Text, 13, active ? "Brush.Fg" : "Brush.Fg2", active ? FontWeights.SemiBold : FontWeights.Normal));
                    if (branch) row.Children.Add(UiFactory.Icon(Icons.Forward, 10, "Brush.Fg3").Col(1));
                    else if (active) row.Children.Add(UiFactory.Icon(Icons.Check, 12, "Brush.Fg").Col(1));
                    var item = new Border { Child = row, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7, 8, 7), Margin = new Thickness(0, 0, 0, 2), Cursor = UiFactory.Hand };
                    UiFactory.Hover(item, active ? "Brush.Selected" : "Brush.Surface", active ? "Brush.Selected" : "Brush.Surface2");
                    item.MouseLeftButtonUp += (_, _) =>
                    {
                        if (_playlistPath.Count > col) _playlistPath.RemoveRange(col, _playlistPath.Count - col);
                        if (branch)
                        {
                            _playlistPath.Add(index);
                        }
                        else
                        {
                            _playlistPicked = node.Id;
                            _playlistPickedLabel = string.Join("  >  ", columns.Take(col).Select(x => x.Nodes[x.Selected].Text).Append(node.Text));
                            _liberator?.SetPlaylist(node.Id);
                            ShowToast($"Game mode set to {node.Text}", Icons.Check);
                        }
                        BuildModsView();
                    };
                    list.Children.Add(item);
                }
                var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                grid.Children.Add(scroll.Col(c));
                if (c > 0)
                {
                    var rule = new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left }.Bg("Brush.Stroke");
                    grid.Children.Add(rule.Col(c));
                }
            }
            var frame = new Border { Child = grid, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true };
            frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");
            sp.Children.Add(new ScrollViewer { Content = frame, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            sp.Children.Add(UiFactory.Text(_playlistPicked.Length > 0 ? $"Selected: {_playlistPickedLabel}" : "Nothing picked yet. The game's own playlist is used.", 12, "Brush.Fg3", wrap: true).Margin(0, 10, 0, 0));
            return UiFactory.Card(sp);
        }

        // ── Game mods ────────────────────────────────────────────────────────

        private Border GameModsCard()
        {
            var sp = new StackPanel();
            var st = _liberator?.State ?? LiberatorState.Idle;
            bool any = ModGroups.SelectMany(gr => gr.Mods).Any(m => st.Can(m.Key)) || st.Can("endRound") || st.Can("endMatch");
            sp.Children.Add(CardTitle("Game mods", any
                ? "Toggle these while you play. Options this build doesn't support are greyed out."
                : $"Available while unlock all is running on a supported season, mostly Y1 to Y{MaxPlaylistYear}."));

            foreach (var (title, mods) in ModGroups)
            {
                sp.Children.Add(UiFactory.Section(title.ToUpperInvariant()).Margin(0, sp.Children.Count > 1 ? 10 : 0, 0, 2));
                foreach (var (key, label) in mods)
                {
                    string k = key;
                    var sw = UiFactory.Switch(_modValues.GetValueOrDefault(k), on =>
                    {
                        _modValues[k] = on;
                        _liberator?.SetMod(k, on);
                    });
                    sw.IsEnabled = st.Can(k);
                    var row = UiFactory.SettingRow(label, null, sw);
                    row.Margin = new Thickness(0, 2, 0, 2);
                    row.MinHeight = 30;
                    if (!sw.IsEnabled) row.Opacity = 0.55;
                    sp.Children.Add(row);
                }
            }

            var endRound = UiFactory.Button("End round", "Btn.Secondary", (_, _) => { _liberator?.EndRound(); ShowToast("Round ended", Icons.Check); });
            endRound.IsEnabled = st.Can("endRound");
            var endMatch = UiFactory.Button("End match", "Btn.Secondary", (_, _) => { _liberator?.EndMatch(); ShowToast("Match ended", Icons.Check); });
            endMatch.IsEnabled = st.Can("endMatch");
            var actions = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
            actions.Children.Add(endRound.Margin(0, 0, 4, 0));
            actions.Children.Add(endMatch.Margin(4, 0, 0, 0));
            sp.Children.Add(actions);
            return UiFactory.Card(sp);
        }
    }
}
