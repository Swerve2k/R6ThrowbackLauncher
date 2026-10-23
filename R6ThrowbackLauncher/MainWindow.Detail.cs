using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private string _detailTab = "overview";
        private StackPanel? _detailActions;
        private TextBlock? _detailProgressLabel;
        private TextBlock? _detailProgressMeta;
        private ProgressBar? _detailProgressBar;
        private TextBox? _detailLogBox;
        private string _detailSignature = "";
        private string _detailLibraryChoice = "";

        private string DetailSignature(SeasonPatch p) =>
            $"{p.IsInstalled}|{p.IsPartial}|{IsPatchRunning(p)}|{_downloads.IsActive(p)}|{_downloads.IsQueued(p)}|{_downloads.State}|{_downloads.QueuePaused}|{_downloads.Queue.ToList().FindIndex(q => q.Patch.Id == p.Id)}";

        private void BuildDetail(SeasonPatch p)
        {
            DetailContent.Children.Clear();
            _detailLogBox = null;
            if (_detailTab == "manage" && !(p.IsInstalled || p.IsPartial)) _detailTab = "overview";
            if (_detailTab == "log" && !HasDetailLog(p)) _detailTab = "overview";

            DetailContent.Children.Add(BuildDetailHero(p));

            var body = new StackPanel { Margin = new Thickness(36, 0, 26, 36) };
            body.Children.Add(BuildDetailTabs(p));
            body.Children.Add(_detailTab switch
            {
                "manage" => BuildManageTab(p),
                "log"    => BuildLogTab(),
                _        => BuildOverviewTab(p),
            });
            DetailContent.Children.Add(body);
            _detailSignature = DetailSignature(p);
        }

        private bool HasDetailLog(SeasonPatch p) => _downloads.IsActive(p) || (_lastTransferPatchId == p.Id && _downloads.LogLines.Count > 0);

        /// <summary>Called on queue/progress changes while the detail page is open.</summary>
        private void UpdateDetailLive()
        {
            if (_currentView != DetailView || _detailPatch == null) return;
            var p = _detailPatch;
            string sig = DetailSignature(p);
            if (sig != _detailSignature)
            {
                _detailSignature = sig;
                RebuildDetailActions(p);
                return;
            }
            if (_downloads.IsActive(p) && _detailProgressBar != null)
            {
                _detailProgressBar.Value = _downloads.Percent;
                _detailProgressLabel!.Text = _downloads.StatusText;
                _detailProgressMeta!.Text = TransferMeta();
            }
        }

        private string TransferMeta()
        {
            var parts = new List<string>();
            if (_downloads.Steps > 0 && _downloads.Step > 0) parts.Add($"Step {_downloads.Step} of {_downloads.Steps}");
            if (_downloads.State == TransferState.Running) parts.Add($"{_downloads.Percent:0.0}%");
            if (_downloads.SpeedText.Length > 0) parts.Add(_downloads.SpeedText);
            if (_downloads.EtaText.Length > 0) parts.Add(_downloads.EtaText);
            return string.Join("  ·  ", parts);
        }

        // ── Hero ─────────────────────────────────────────────────────────────

        private UIElement BuildDetailHero(SeasonPatch p)
        {
            var hero = new Border { Height = 330, Margin = new Thickness(36, 8, 36, 20), CornerRadius = new CornerRadius(14), ClipToBounds = true };
            hero.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            var g = new Grid();
            g.Clip = new RectangleGeometry { RadiusX = 14, RadiusY = 14 };
            g.SizeChanged += (_, e) => ((RectangleGeometry)g.Clip).Rect = new Rect(e.NewSize);

            var art = ArtworkCache.Build(p.ArtworkPath, 1080, hero, zoom: 1);
            g.Children.Add(art);
            var fadeL = new Border();
            fadeL.SetResourceReference(Border.BackgroundProperty, "Brush.ArtFadeLeft");
            g.Children.Add(fadeL);
            var fadeB = new Border();
            fadeB.SetResourceReference(Border.BackgroundProperty, "Brush.ArtFadeBottom");
            fadeB.Opacity = 0.7;
            g.Children.Add(fadeB);

            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(36, 0, 36, 30) };
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            chips.Children.Add(OverlayChip(p.Code));
            chips.Children.Add(OverlayChip($"PATCH {p.PatchKey}").Margin(6, 0, 0, 0));
            if (p.IsHeatedMetal) chips.Children.Add(OverlayChip("HEATED METAL", solid: true).Margin(6, 0, 0, 0));
            content.Children.Add(chips);

            content.Children.Add(new TextBlock
            {
                Text = p.BaseName, FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var info = SeasonInfoData.For(p.Code);
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(info?.Release)) meta.Add($"Released {info!.Release}");
            else if (p.Date.HasValue) meta.Add($"Patch date {p.Date:MMM d, yyyy}");
            if (p.SizeGb > 0) meta.Add($"{p.SizeGb:0.#} GB download");
            if (!string.IsNullOrEmpty(p.BuildId)) meta.Add($"Build {p.BuildId}");
            content.Children.Add(new TextBlock
            {
                Text = string.Join("  ·  ", meta), FontSize = 13, Margin = new Thickness(0, 6, 0, 18),
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)),
            });

            _detailActions = new StackPanel();
            content.Children.Add(_detailActions);
            FillDetailActions(p);

            g.Children.Add(content);
            hero.Child = g;
            return hero;
        }

        private void RebuildDetailActions(SeasonPatch p)
        {
            // State changes can add or remove tabs (Manage, Log), so rebuild the page.
            double offset = DetailScroll.VerticalOffset;
            BuildDetail(p);
            DetailScroll.ScrollToVerticalOffset(offset);
        }

        private void FillDetailActions(SeasonPatch p)
        {
            var host = _detailActions!;
            host.Children.Clear();
            _detailProgressBar = null;
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            if (IsPatchRunning(p))
            {
                row.Children.Add(PlayButton(p, large: true));
                row.Children.Add(HeroNote("Running now"));
            }
            else if (_downloads.IsActive(p))
            {
                var panel = new StackPanel { Width = 520, HorizontalAlignment = HorizontalAlignment.Left };
                _detailProgressLabel = new TextBlock { Text = _downloads.StatusText, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
                _detailProgressBar = UiFactory.Progress(6);
                _detailProgressBar.Margin = new Thickness(0, 10, 0, 8);
                _detailProgressBar.Value = _downloads.Percent;
                _detailProgressBar.IsIndeterminate = false;
                _detailProgressMeta = new TextBlock { Text = TransferMeta(), FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255)) };
                panel.Children.Add(_detailProgressLabel);
                panel.Children.Add(_detailProgressBar);
                panel.Children.Add(_detailProgressMeta);
                host.Children.Add(panel);

                row.Margin = new Thickness(0, 14, 0, 0);
                row.Children.Add(HeroButton("Pause", Icons.Pause, primary: false, (_, _) => _downloads.Pause()));
                row.Children.Add(HeroButton("Cancel", Icons.Close, primary: false, (_, _) => CancelTransfer()));
            }
            else if (_downloads.IsQueued(p))
            {
                int pos = _downloads.Queue.ToList().FindIndex(q => q.Patch.Id == p.Id) + 1;
                if (_downloads.QueuePaused)
                    row.Children.Add(HeroButton("Resume downloads", Icons.Play, primary: true, (_, _) => _downloads.Resume()));
                row.Children.Add(HeroButton("Remove from queue", Icons.Close, primary: !_downloads.QueuePaused, (_, _) => _downloads.Remove(p)));
                row.Children.Add(HeroNote(_downloads.QueuePaused ? $"Paused  ·  position {pos} in queue" : $"Queued  ·  position {pos}"));
            }
            else if (p.IsInstalled)
            {
                row.Children.Add(PlayWithRenderer(p));
                row.Children.Add(HeroButton("Verify", Icons.Sync, primary: false, (_, _) => QueueVerify(p)));
                var folder = HeroIconButton(Icons.Folder, "Open folder", (_, _) => OpenFolder(p.InstallDir));
                row.Children.Add(folder);
                var more = HeroIconButton(Icons.More, "More actions", null);
                more.Click += (_, _) => OpenSeasonMenu(more, p);
                row.Children.Add(more);
            }
            else if (p.IsPartial)
            {
                row.Children.Add(HeroButton("Resume download", Icons.Download, primary: true, (_, _) => StartDownload(p)));
                row.Children.Add(HeroButton("Remove files", Icons.Delete, primary: false, (_, _) => Uninstall(p)));
                row.Children.Add(HeroNote("Incomplete download"));
            }
            else
            {
                string label = p.SizeGb > 0 ? $"Download  ·  {p.SizeGb:0.#} GB" : "Download";
                row.Children.Add(HeroButton(label, Icons.Download, primary: true, (_, _) => StartDownload(p, _detailLibraryChoice)));
                if (S.LibraryFolders.Count > 1) row.Children.Add(LibraryPicker());
                row.Children.Add(HeroNote(FreeSpaceText(LibraryForNewDownload())));
            }

            host.Children.Add(row);
        }

        private string LibraryForNewDownload() =>
            S.LibraryFolders.Contains(_detailLibraryChoice, StringComparer.OrdinalIgnoreCase) ? _detailLibraryChoice : _locator.DefaultLibrary;

        private UIElement LibraryPicker()
        {
            var combo = new ComboBox { Width = 230, Height = 40, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Library folder" };
            combo.Styled("Combo");
            foreach (var lib in S.LibraryFolders)
                combo.Items.Add(new ComboBoxItem { Content = ShortPath(lib), Tag = lib, ToolTip = lib });
            string current = LibraryForNewDownload();
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, current, StringComparison.OrdinalIgnoreCase));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: string lib }) _detailLibraryChoice = lib;
                if (_detailPatch != null) FillDetailActions(_detailPatch);
            };
            return combo;
        }

        private static string ShortPath(string path)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
        }

        private static string FreeSpaceText(string library)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(library)) ?? "C:\\";
                var drive = new DriveInfo(root);
                return $"{UiFactory.FormatBytes(drive.AvailableFreeSpace)} free on {root.TrimEnd('\\')}";
            }
            catch { return ""; }
        }

        private UIElement PlayWithRenderer(SeasonPatch p)
        {
            var options = InstallLocator.RendererOptions(p.InstallDir);
            var play = PlayButton(p, large: true);
            if (options.Count == 0) return play;

            S.Renderer.TryGetValue(p.Id, out string? current);
            current ??= "";
            string label = options.FirstOrDefault(o => o.Key == current).Label ?? options[0].Label;
            play.ToolTip = $"Launch with {label}";
            play.Content = UiFactory.Content($"Play  ·  {label}", Icons.Play);

            var chevron = new Button { Content = Icons.ChevronDown, Width = 34, Height = 40, Margin = new Thickness(2, 0, 0, 0), ToolTip = "Choose renderer" };
            chevron.Styled("Btn.Primary");
            chevron.FontFamily = (FontFamily)FindResource("Font.Icons");
            chevron.FontSize = 11;
            chevron.Padding = new Thickness(0);
            chevron.Click += (_, _) =>
            {
                var items = options.Select(o => (object)UiFactory.MenuItem(o.Label, o.Key == current ? Icons.Check : null, () =>
                {
                    if (o.Key.Length == 0) S.Renderer.Remove(p.Id); else S.Renderer[p.Id] = o.Key;
                    SaveSettings();
                    FillDetailActions(p);
                })).ToArray();
                UiFactory.OpenMenu(chevron, items);
            };
            return UiFactory.HStack(0, play, chevron);
        }

        private static Button HeroButton(string text, string icon, bool primary, RoutedEventHandler? click)
        {
            var b = UiFactory.Button(text, primary ? "Btn.Primary" : "Btn.Secondary", click, icon);
            b.Height = 40;
            b.Padding = new Thickness(18, 0, 18, 0);
            b.Margin = new Thickness(0, 0, 10, 0);
            return b;
        }

        private static Button HeroIconButton(string glyph, string tip, RoutedEventHandler? click)
        {
            var b = UiFactory.Button("", "Btn.Secondary", click, glyph);
            b.Width = 40; b.Height = 40; b.Padding = new Thickness(0);
            b.Margin = new Thickness(0, 0, 10, 0);
            b.ToolTip = tip;
            UiFactory.AutomationName(b, tip);
            return b;
        }

        private static TextBlock HeroNote(string text) => new()
        {
            Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0xB8, 255, 255, 255)),
        };

        // ── Tabs ─────────────────────────────────────────────────────────────

        private UIElement BuildDetailTabs(SeasonPatch p)
        {
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            void Add(string key, string label)
            {
                var rb = new RadioButton { Content = label, GroupName = "DetailTab", IsChecked = _detailTab == key };
                rb.Styled("Segment");
                rb.Click += (_, _) => { _detailTab = key; BuildDetail(p); };
                seg.Children.Add(rb);
            }
            Add("overview", "Overview");
            if (p.IsInstalled || p.IsPartial) Add("manage", "Manage");
            if (HasDetailLog(p)) Add("log", "Log");
            var host = new Border { Child = seg, Margin = new Thickness(0, 0, 0, 20) };
            host.Styled("SegmentHost");
            return host;
        }

        // ── Overview ─────────────────────────────────────────────────────────

        private UIElement BuildOverviewTab(SeasonPatch p)
        {
            var info = SeasonInfoData.For(p.Code) ?? new SeasonInfo();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });

            var left = new StackPanel();
            if (!string.IsNullOrWhiteSpace(p.Description))
            {
                left.Children.Add(UiFactory.Section("ABOUT").Margin(0, 0, 0, 8));
                left.Children.Add(UiFactory.Text(p.Description, 14, "Brush.Fg2", wrap: true).Margin(0, 0, 0, 24));
                ((TextBlock)left.Children[^1]).LineHeight = 22;
            }

            if (p.IsHeatedMetal)
            {
                left.Children.Add(UiFactory.Section("HEATED METAL").Margin(0, 0, 0, 8));
                left.Children.Add(BulletList(SeasonInfoData.HeatedMetal.Highlights));
            }

            if (info.Operators.Count > 0)
            {
                left.Children.Add(UiFactory.Section(info.Operators.Count == 1 ? "OPERATOR" : "OPERATORS").Margin(0, 0, 0, 10));
                var ops = new UniformGrid { Columns = info.Operators.Count == 1 ? 1 : 2, Margin = new Thickness(0, 0, -12, 14) };
                foreach (var op in info.Operators) ops.Children.Add(OperatorCard(op));
                left.Children.Add(ops);
            }

            if (info.Maps.Count > 0)
            {
                left.Children.Add(UiFactory.Section(info.Maps.Count == 1 ? "MAP" : "MAPS").Margin(0, 0, 0, 10));
                var maps = new WrapPanel { Margin = new Thickness(0, 0, 0, 24) };
                foreach (var m in info.Maps)
                {
                    var chip = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 0, 8, 8) };
                    chip.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
                    chip.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");
                    chip.Child = UiFactory.HStack(10, UiFactory.Icon(Icons.Flag, 13, "Brush.Fg3"), UiFactory.Text(m, 13, "Brush.Fg", FontWeights.SemiBold));
                    maps.Children.Add(chip);
                }
                left.Children.Add(maps);
            }

            if (info.Highlights.Count > 0)
            {
                left.Children.Add(UiFactory.Section("HIGHLIGHTS").Margin(0, 0, 0, 8));
                left.Children.Add(BulletList(info.Highlights));
            }
            if (left.Children.Count > 0 && left.Children[^1] is FrameworkElement last)
                last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
            grid.Children.Add(left.Children.Count > 0 ? UiFactory.Card(left, 24) : left);

            var right = new StackPanel();
            var details = new StackPanel();
            details.Children.Add(UiFactory.Text("Details", 14, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 10));
            if (!string.IsNullOrEmpty(info.Release)) details.Children.Add(KeyValue("Released", info.Release));
            details.Children.Add(KeyValue("Patch", p.PatchKey + (p.Date.HasValue ? $"  ({p.Date:MMM d, yyyy})" : "")));
            if (!string.IsNullOrEmpty(p.BuildId)) details.Children.Add(KeyValue("Build", p.BuildId));
            if (p.SizeGb > 0) details.Children.Add(KeyValue("Download", $"{p.SizeGb:0.#} GB"));
            details.Children.Add(KeyValue("Depots", p.Depots.Count.ToString()));
            if (p.IsInstalled || p.IsPartial)
            {
                var sizeValue = KeyValue("On disk", "Calculating");
                details.Children.Add(sizeValue);
                var valueText = (TextBlock)sizeValue.Children[1];
                if (_sizeCache.TryGetValue(p.InstallDir, out long known)) valueText.Text = UiFactory.FormatBytes(known);
                else RequestSize(p.InstallDir, b => valueText.Text = UiFactory.FormatBytes(b));
            }
            right.Children.Add(UiFactory.Card(details, 18).Margin(0, 0, 0, 14));

            if (info.Events.Count > 0)
            {
                var ev = new StackPanel();
                ev.Children.Add(UiFactory.Text("Events in this build", 14, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 10));
                var wrap = new WrapPanel();
                foreach (var e in info.Events) wrap.Children.Add(UiFactory.Chip(e, "Brush.Surface3", "Brush.Fg").Margin(0, 0, 6, 6));
                ev.Children.Add(wrap);
                right.Children.Add(UiFactory.Card(ev, 18).Margin(0, 0, 0, 14));
            }

            var req = new StackPanel();
            req.Children.Add(UiFactory.Text("Before you play", 14, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 10));
            req.Children.Add(BulletList(SeasonInfoData.Requirements(p), compact: true));
            right.Children.Add(UiFactory.Card(req, 18).Margin(0, 0, 0, 14));

            long secs = _playtime.GetSecondsForPatch(p.Id);
            if (secs > 0)
            {
                var act = new StackPanel();
                act.Children.Add(UiFactory.Text("Your activity", 14, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 10));
                act.Children.Add(KeyValue("Played", PlaytimeService.Format(secs)));
                act.Children.Add(KeyValue("Sessions", _playtime.GetSessionCountForPatch(p.Id).ToString()));
                act.Children.Add(KeyValue("Last played", PlaytimeService.FormatRelative(_playtime.GetLastPlayedForPatch(p.Id))));
                right.Children.Add(UiFactory.Card(act, 18));
            }
            grid.Children.Add(right.Col(2));
            return grid;
        }

        private static Border OperatorCard(InfoOperator op)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text(op.Name, 15, "Brush.Fg", FontWeights.SemiBold));
            sp.Children.Add(UiFactory.Text(op.Gadget, 12, "Brush.Fg2", FontWeights.SemiBold).Margin(0, 3, 0, 4));
            sp.Children.Add(UiFactory.Text(op.Summary, 12, "Brush.Fg3", wrap: true));
            var card = UiFactory.Card(sp, 16);
            card.Margin = new Thickness(0, 0, 12, 12);
            card.Bg("Brush.Surface2");
            return card;
        }

        private static StackPanel BulletList(IEnumerable<string> items, bool compact = false)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, compact ? 0 : 24) };
            foreach (var item in items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, compact ? 6 : 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dot = new Border { Width = 4, Height = 4, Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                dot.SetResourceReference(Border.BackgroundProperty, "Brush.Fg3");
                row.Children.Add(dot);
                row.Children.Add(UiFactory.Text(item, compact ? 12 : 13, "Brush.Fg2", wrap: true).Col(1));
                sp.Children.Add(row);
            }
            return sp;
        }

        private static Grid KeyValue(string key, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(UiFactory.Text(key, 12, "Brush.Fg3"));
            var v = UiFactory.Text(value, 12, "Brush.Fg", wrap: true);
            v.TextWrapping = TextWrapping.Wrap;
            g.Children.Add(v.Col(1));
            return g;
        }

        // ── Manage ───────────────────────────────────────────────────────────

        private UIElement BuildManageTab(SeasonPatch p)
        {
            bool busy = _downloads.IsBusyWith(p) || IsPatchRunning(p);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            if (p.IsInstalled) left.Children.Add(BuildLaunchCard(p));
            left.Children.Add(BuildNotesCard(p));
            grid.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(BuildFilesCard(p, busy));
            if (p.IsInstalled) right.Children.Add(BuildShearsCard(p, busy));
            if (p.IsHeatedMetal && p.IsInstalled) right.Children.Add(BuildHeatedMetalCard(p, busy));
            grid.Children.Add(right.Col(2));
            return grid;
        }

        private Border BuildLaunchCard(SeasonPatch p)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text("Launch", 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 8));

            var options = InstallLocator.RendererOptions(p.InstallDir);
            if (options.Count > 0)
            {
                var combo = new ComboBox { Width = 170 };
                combo.Styled("Combo");
                S.Renderer.TryGetValue(p.Id, out string? current);
                foreach (var (key, label) in options)
                {
                    var item = new ComboBoxItem { Content = label, Tag = key };
                    combo.Items.Add(item);
                    if (key == (current ?? "")) combo.SelectedItem = item;
                }
                if (combo.SelectedItem == null) combo.SelectedIndex = 0;
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem { Tag: string key }) return;
                    if (key.Length == 0) S.Renderer.Remove(p.Id); else S.Renderer[p.Id] = key;
                    SaveSettings();
                    FillDetailActions(p);
                };
                sp.Children.Add(UiFactory.SettingRow("Renderer", "Which executable to start.", combo));
            }

            S.LaunchArgs.TryGetValue(p.Id, out string? args);
            var argsBox = UiFactory.Input(args ?? "", "Extra launch options");
            argsBox.Margin = new Thickness(0, 6, 0, 0);
            var argsSave = UiFactory.Button("Save", "Btn.Secondary");
            argsSave.Margin = new Thickness(8, 6, 0, 0);
            argsSave.Click += (_, _) =>
            {
                string v = argsBox.Text.Trim();
                if (v.Count(c => c == '"') % 2 != 0) { ShowToast("Launch options have an unclosed quote.", Icons.Warning); return; }
                if (v.Length == 0) S.LaunchArgs.Remove(p.Id); else S.LaunchArgs[p.Id] = v;
                SaveSettings();
                ShowToast("Launch options saved", Icons.Check);
            };
            sp.Children.Add(UiFactory.Text("Launch options", 13, "Brush.Fg").Margin(0, 10, 0, 0));
            sp.Children.Add(UiFactory.Text("Added after /belaunch /nologo when this season starts.", 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 0));
            sp.Children.Add(InputWithButton(argsBox, argsSave));

            string currentName = LoaderConfig.Read(p) ?? EffectiveInGameName();
            var nameBox = UiFactory.Input(currentName, "In-game name");
            nameBox.MaxLength = LoaderConfig.MaxUsernameLength;
            nameBox.Margin = new Thickness(0, 6, 0, 0);
            var nameSave = UiFactory.Button("Save", "Btn.Secondary");
            nameSave.Margin = new Thickness(8, 6, 0, 0);
            nameSave.Click += (_, _) =>
            {
                string name = nameBox.Text.Trim();
                if (LoaderConfig.Validate(name) is string problem) { ShowToast(problem, Icons.Warning); return; }
                try { LoaderConfig.Write(p, name); ShowToast($"In-game name set to {name}", Icons.Check); }
                catch (Exception ex) { ShowToast("Could not save: " + ex.Message, Icons.Warning); }
            };
            sp.Children.Add(UiFactory.Text("In-game name", 13, "Brush.Fg").Margin(0, 16, 0, 0));
            sp.Children.Add(UiFactory.Text($"Only for this install. Up to {LoaderConfig.MaxUsernameLength} characters, no quotes or backslashes.", 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 0));
            sp.Children.Add(InputWithButton(nameBox, nameSave));

            var config = UiFactory.Button("Edit game settings", "Btn.Secondary", (_, _) => EditGameConfig(p), Icons.Document);
            config.HorizontalAlignment = HorizontalAlignment.Left;
            config.Margin = new Thickness(0, 18, 0, 0);
            sp.Children.Add(config);

            return UiFactory.Card(sp).Margin(0, 0, 0, 16);
        }

        private static Grid InputWithButton(TextBox box, Button button)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(box);
            g.Children.Add(button.Col(1));
            return g;
        }

        private Border BuildNotesCard(SeasonPatch p)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text("Notes", 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 8));
            S.SeasonNotes.TryGetValue(p.Id, out string? note);
            var box = new TextBox { Text = note ?? "", Tag = "Loadouts, settings, anything worth remembering", Height = 110 };
            box.Styled("Input.Multiline");
            sp.Children.Add(box);
            var save = UiFactory.Button("Save notes", "Btn.Secondary", (_, _) =>
            {
                string v = box.Text.Trim();
                if (v.Length == 0) S.SeasonNotes.Remove(p.Id); else S.SeasonNotes[p.Id] = v;
                SaveSettings();
                ShowToast("Notes saved", Icons.Check);
            });
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(save);
            return UiFactory.Card(sp).Margin(0, 0, 0, 16);
        }

        private Border BuildFilesCard(SeasonPatch p, bool busy)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text("Files", 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 6));
            var path = UiFactory.Text(p.InstallDir, 12, "Brush.Fg3", wrap: true);
            path.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            sp.Children.Add(path);

            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(UiFactory.Button("Open folder", "Btn.Secondary", (_, _) => OpenFolder(p.InstallDir), Icons.Folder).Margin(0, 0, 8, 8));
            if (p.IsInstalled)
            {
                var verify = UiFactory.Button("Verify files", "Btn.Secondary", (_, _) => QueueVerify(p), Icons.Sync).Margin(0, 0, 8, 8);
                verify.IsEnabled = !busy;
                verify.ToolTip = "Re-checks every file with Steam and redownloads anything missing or damaged.";
                buttons.Children.Add(verify);
            }
            else
            {
                var resume = UiFactory.Button("Resume download", "Btn.Secondary", (_, _) => StartDownload(p), Icons.Download).Margin(0, 0, 8, 8);
                resume.IsEnabled = !busy;
                buttons.Children.Add(resume);
            }
            var uninstall = UiFactory.Button(p.IsInstalled ? "Uninstall" : "Remove files", "Btn.Secondary", (_, _) => Uninstall(p), Icons.Delete).Margin(0, 0, 8, 8);
            uninstall.IsEnabled = !busy;
            uninstall.ToolTip = "Moves the folder to the Recycle Bin.";
            buttons.Children.Add(uninstall);
            sp.Children.Add(buttons);
            return UiFactory.Card(sp).Margin(0, 0, 0, 16);
        }

        private Border BuildShearsCard(SeasonPatch p, bool busy)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text("Free up space", 15, "Brush.Fg", FontWeights.SemiBold));
            sp.Children.Add(UiFactory.Text("Remove optional files from this install. Verify files brings everything back.", 12, "Brush.Fg3", wrap: true).Margin(0, 3, 0, 12));
            var results = new StackPanel();
            results.Children.Add(UiFactory.Text("Scanning", 12, "Brush.Fg3"));
            sp.Children.Add(results);

            string? pattern = SeasonCatalog.EventFilePattern(p);
            Task.Run(() => ShearsService.Scan(p.InstallDir, pattern)).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                results.Children.Clear();
                if (t.IsFaulted) { results.Children.Add(UiFactory.Text("Could not scan this folder.", 12, "Brush.Fg3")); return; }
                var scan = t.Result;
                if (!scan.HasAnything)
                {
                    results.Children.Add(UiFactory.Text("Nothing left to remove.", 12, "Brush.Fg3"));
                    return;
                }
                if (scan.VideoBytes > 0)
                    results.Children.Add(ShearsRow("Intro and menu videos", scan.VideoBytes, "Remove", busy,
                        () => RunShears(p, "videos", () => ShearsService.RemoveVideos(p.InstallDir))));
                if (scan.EventBytes > 0 && pattern != null)
                    results.Children.Add(ShearsRow("Event playlist data", scan.EventBytes, "Remove", busy,
                        () => RunShears(p, "event files", () => ShearsService.RemoveEvents(p.InstallDir, pattern))));
                if (scan.Tiers.Count > 1)
                    results.Children.Add(TextureRow(p, scan, busy));
            }));
            return UiFactory.Card(sp).Margin(0, 0, 0, 16);
        }

        private Grid ShearsRow(string label, long bytes, string action, bool busy, Action run)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(UiFactory.Text(label, 13, "Brush.Fg"));
            g.Children.Add(UiFactory.Text(UiFactory.FormatBytes(bytes), 12, "Brush.Fg2").Col(1).Margin(0, 0, 12, 0));
            var btn = UiFactory.Button(action, "Btn.Secondary", (_, _) => run());
            btn.Height = 30;
            btn.IsEnabled = !busy;
            g.Children.Add(btn.Col(2));
            return g;
        }

        private UIElement TextureRow(SeasonPatch p, ShearsScan scan, bool busy)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            sp.Children.Add(UiFactory.Text("Texture quality tiers", 13, "Brush.Fg"));
            var tiers = string.Join("   ", scan.Tiers.Select(t => $"{t.Quality} {UiFactory.FormatBytes(t.Bytes)}"));
            sp.Children.Add(UiFactory.Text(tiers, 12, "Brush.Fg3", wrap: true).Margin(0, 2, 0, 8));

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var combo = new ComboBox { Width = 150 };
            combo.Styled("Combo");
            foreach (var t in scan.Tiers.Take(scan.Tiers.Count - 1))
                combo.Items.Add(new ComboBoxItem { Content = $"Keep up to {t.Quality}", Tag = t.Level });
            combo.SelectedIndex = combo.Items.Count - 1;
            var freed = UiFactory.Text("", 12, "Brush.Fg2").Col(1).Margin(12, 0, 12, 0);
            void UpdateFreed()
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: int keep })
                    freed.Text = $"frees {UiFactory.FormatBytes(scan.Tiers.Where(t => t.Level > keep).Sum(t => t.Bytes))}";
            }
            combo.SelectionChanged += (_, _) => UpdateFreed();
            UpdateFreed();
            var trim = UiFactory.Button("Trim", "Btn.Secondary", (_, _) =>
            {
                if (combo.SelectedItem is not ComboBoxItem { Tag: int keep }) return;
                string quality = ShearsService.Qualities[keep];
                if (!ThemedDialog.Confirm(this, "Remove texture tiers",
                        $"Textures above {quality} will be deleted. Set the in-game texture quality to {quality} or lower, or the game may fail to load.",
                        "Remove"))
                    return;
                RunShears(p, "texture tiers", () => ShearsService.RemoveTexturesAbove(p.InstallDir, keep), confirm: false);
            });
            trim.Height = 30;
            trim.IsEnabled = !busy;
            g.Children.Add(combo);
            g.Children.Add(freed);
            g.Children.Add(trim.Col(2));
            sp.Children.Add(g);
            return sp;
        }

        private void RunShears(SeasonPatch p, string what, Func<long> action, bool confirm = true)
        {
            if (IsPatchRunning(p) || _downloads.IsBusyWith(p)) { ShowToast("Stop the game and any downloads for this season first.", Icons.Warning); return; }
            if (confirm && !ThemedDialog.Confirm(this, $"Remove {what}", $"Delete the {what} from {p.ShortLabel}? Verify files restores them later.", "Remove"))
                return;
            Task.Run(action).ContinueWith(t => Dispatcher.Invoke(() =>
            {
                if (t.IsFaulted) ShowToast(t.Exception?.InnerException?.Message ?? "Could not remove files.", Icons.Warning);
                else ShowToast($"Freed {UiFactory.FormatBytes(t.Result)}", Icons.Check);
                InvalidateSizes(p);
                if (_detailPatch == p) BuildDetail(p);
            }));
        }

        private Border BuildHeatedMetalCard(SeasonPatch p, bool busy)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text("Heated Metal", 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 6));
            string dll = Path.Combine(p.InstallDir, "HeatedMetal", "HeatedMetal.dll");
            if (File.Exists(dll))
                sp.Children.Add(UiFactory.Text($"Mod files dated {File.GetLastWriteTime(dll):MMM d, yyyy}", 12, "Brush.Fg3"));
            var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            var replace = UiFactory.Button("Replace from archive", "Btn.Secondary", async (_, _) => await ReplaceHmArchiveAsync(p), Icons.Archive).Margin(0, 0, 8, 8);
            replace.IsEnabled = !busy;
            buttons.Children.Add(replace);
            string crashDir = Path.Combine(p.InstallDir, "HeatedMetal", "CrashLogs");
            if (System.IO.Directory.Exists(crashDir) && System.IO.Directory.EnumerateFiles(crashDir).Any())
                buttons.Children.Add(UiFactory.Button("Crash logs", "Btn.Secondary", (_, _) => OpenFolder(crashDir), Icons.Document).Margin(0, 0, 8, 8));
            sp.Children.Add(buttons);
            return UiFactory.Card(sp).Margin(0, 0, 0, 16);
        }

        // ── Log ──────────────────────────────────────────────────────────────

        private UIElement BuildLogTab()
        {
            var sp = new StackPanel();
            var box = new TextBox
            {
                IsReadOnly = true, Height = 460, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Text = _downloads.LogText(),
            };
            box.Styled("Input.Multiline");
            box.SetResourceReference(TextBox.FontFamilyProperty, "Font.Mono");
            box.ScrollToEnd();
            _detailLogBox = box;
            sp.Children.Add(box);
            var copy = UiFactory.Button("Copy log", "Btn.Secondary", (_, _) =>
            {
                try { Clipboard.SetText(_downloads.LogText()); ShowToast("Log copied", Icons.Copy); } catch { }
            }, Icons.Copy);
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            copy.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(copy);
            return sp;
        }

        private void EditGameConfig(SeasonPatch p)
        {
            var dlg = new GameConfigDialog(LoaderConfig.GameSettingsPath(p), p.BaseName) { Owner = this };
            dlg.ShowDialog();
        }
    }
}
