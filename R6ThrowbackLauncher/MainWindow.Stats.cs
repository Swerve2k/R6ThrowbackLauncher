using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private static Grid PageHeader(string title, string subtitle, UIElement? right = null)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title }.Styled("Text.Title"));
            if (!string.IsNullOrEmpty(subtitle)) sp.Children.Add(UiFactory.Text(subtitle, 12, "Brush.Fg3").Margin(0, 4, 0, 0));
            header.Children.Add(sp);
            if (right is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Bottom;
                header.Children.Add(fe.Col(1));
            }
            return header;
        }

        private static Border StatTile(string label, string value)
        {
            var sp = new StackPanel();
            sp.Children.Add(UiFactory.Text(label, 12, "Brush.Fg3"));
            sp.Children.Add(UiFactory.Text(value, 26, "Brush.Fg", FontWeights.SemiBold).Margin(0, 6, 0, 0));
            var card = UiFactory.Card(sp, 20);
            card.Margin = new Thickness(0, 0, 14, 14);
            return card;
        }

        // ── Stats ────────────────────────────────────────────────────────────

        private void BuildStatsView()
        {
            StatsContent.Children.Clear();
            StatsContent.Children.Add(PageHeader("Stats", "Your playtime across every season you have launched from here."));

            long first = _playtime.GetFirstLaunch();
            var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 10) };
            tiles.Children.Add(StatTile("Total playtime", _playtime.GetTotalSeconds() > 0 ? PlaytimeService.Format(_playtime.GetTotalSeconds()) : "None yet"));
            tiles.Children.Add(StatTile("Sessions", _playtime.GetTotalSessionCount().ToString("N0")));
            tiles.Children.Add(StatTile("Installed", Installed.Count().ToString()));
            tiles.Children.Add(StatTile("Playing since", first > 0 ? DateTimeOffset.FromUnixTimeSeconds(first).LocalDateTime.ToString("MMM d, yyyy") : "Not yet"));
            StatsContent.Children.Add(tiles);

            var entries = _playtime.GetAllEntriesSorted().Where(e => e.Entry.TotalSeconds > 0).Take(12).ToList();
            if (entries.Count == 0)
            {
                StatsContent.Children.Add(UiFactory.EmptyState(Icons.Chart, "No playtime yet", "Launch a season and your sessions are tracked automatically."));
                return;
            }

            StatsContent.Children.Add(UiFactory.Section("MOST PLAYED").Margin(0, 10, 0, 12));
            long max = entries.Max(e => e.Entry.TotalSeconds);
            var list = new StackPanel();
            int rank = 1;
            foreach (var (id, entry) in entries)
            {
                var patch = FindPatch(id);
                string name = patch?.ShortLabel ?? id;
                var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.Children.Add(UiFactory.Text($"{rank}", 14, rank <= 3 ? "Brush.Fg" : "Brush.Fg3", FontWeights.Bold));
                g.Children.Add(UiFactory.Text(name, 13, "Brush.Fg", FontWeights.SemiBold).Col(1));
                var bar = new ProgressBar { Maximum = max, Value = entry.TotalSeconds, Height = 6, Margin = new Thickness(0, 0, 20, 0) };
                bar.Styled("Progress.Plain");
                g.Children.Add(bar.Col(2));
                g.Children.Add(UiFactory.Text(entry.SessionCount == 1 ? "1 session" : $"{entry.SessionCount} sessions", 12, "Brush.Fg3").Col(3));
                var t = UiFactory.Text(PlaytimeService.Format(entry.TotalSeconds), 13, "Brush.Fg", FontWeights.SemiBold);
                t.HorizontalAlignment = HorizontalAlignment.Right;
                g.Children.Add(t.Col(4));
                if (patch != null)
                {
                    g.Cursor = System.Windows.Input.Cursors.Hand;
                    g.Background = System.Windows.Media.Brushes.Transparent;
                    g.MouseLeftButtonUp += (_, _) => OpenDetail(patch);
                }
                list.Children.Add(g);
                rank++;
            }
            StatsContent.Children.Add(UiFactory.Card(list));

            var byYear = entries.Concat(_playtime.GetAllEntriesSorted().Skip(12))
                .Select(e => (Patch: FindPatch(e.PatchId), e.Entry.TotalSeconds))
                .Where(e => e.Patch != null)
                .GroupBy(e => e.Patch!.Year)
                .Select(g => (Year: g.Key, Seconds: g.Sum(x => x.TotalSeconds)))
                .OrderBy(g => g.Year).ToList();
            if (byYear.Count > 1)
            {
                StatsContent.Children.Add(UiFactory.Section("BY YEAR").Margin(0, 24, 0, 12));
                var years = new StackPanel();
                long ymax = byYear.Max(y => y.Seconds);
                foreach (var (year, secs) in byYear)
                {
                    var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                    g.Children.Add(UiFactory.Text($"Year {year}", 13, "Brush.Fg2"));
                    var bar = new ProgressBar { Maximum = ymax, Value = secs, Height = 6, Margin = new Thickness(0, 0, 20, 0) };
                    bar.Styled("Progress.Plain");
                    g.Children.Add(bar.Col(1));
                    var t = UiFactory.Text(PlaytimeService.Format(secs), 12, "Brush.Fg2");
                    t.HorizontalAlignment = HorizontalAlignment.Right;
                    g.Children.Add(t.Col(2));
                    years.Children.Add(g);
                }
                StatsContent.Children.Add(UiFactory.Card(years));
            }
        }

        // ── Storage ──────────────────────────────────────────────────────────

        private int _storageBuild;

        private void BuildStorageView()
        {
            int build = ++_storageBuild;
            StorageContent.Children.Clear();
            var refresh = UiFactory.Button("Rescan", "Btn.Secondary", (_, _) => { RefreshInstallState(); RefreshAllViews(); BuildStorageView(); }, Icons.Refresh);
            StorageContent.Children.Add(PageHeader("Storage", "Disk usage across your library folders.", refresh));

            var libs = new UniformGrid { Columns = Math.Clamp(S.LibraryFolders.Count, 1, 3), Margin = new Thickness(0, 0, -14, 10) };
            foreach (var lib in S.LibraryFolders) libs.Children.Add(LibraryUsageCard(lib));
            StorageContent.Children.Add(libs);

            var installs = _patches.Where(p => p.IsInstalled || p.IsPartial).ToList();
            StorageContent.Children.Add(UiFactory.Section("SEASONS").Margin(0, 10, 0, 12));
            var list = new StackPanel();
            if (installs.Count == 0) list.Children.Add(UiFactory.Text("No seasons installed.", 13, "Brush.Fg3"));
            var sizeLabels = new System.Collections.Generic.Dictionary<string, TextBlock>();
            foreach (var p in installs)
            {
                var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(UiFactory.Text(p.ShortLabel + (p.IsPartial ? "  (incomplete)" : ""), 13, "Brush.Fg", FontWeights.SemiBold));
                name.Children.Add(UiFactory.Text(p.InstallDir, 11, "Brush.Fg3"));
                g.Children.Add(name);
                var size = UiFactory.Text("…", 13, "Brush.Fg2");
                size.HorizontalAlignment = HorizontalAlignment.Right;
                size.Margin = new Thickness(0, 0, 16, 0);
                g.Children.Add(size.Col(1));
                if (_sizeCache.TryGetValue(p.InstallDir, out long known)) size.Text = UiFactory.FormatBytes(known);
                else RequestSize(p.InstallDir, b => size.Text = UiFactory.FormatBytes(b));
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(UiFactory.IconButton(Icons.Folder, "Open folder", (_, _) => OpenFolder(p.InstallDir)));
                actions.Children.Add(UiFactory.IconButton(Icons.Sliders, "Free up space", (_, _) => OpenDetail(p, "manage")));
                actions.Children.Add(UiFactory.IconButton(Icons.Delete, "Uninstall", (_, _) => Uninstall(p)));
                g.Children.Add(actions.Col(2));
                list.Children.Add(g);
                list.Children.Add(UiFactory.Divider(4, 4));
            }
            if (list.Children.Count > 0 && list.Children[^1] is Border) list.Children.RemoveAt(list.Children.Count - 1);
            StorageContent.Children.Add(UiFactory.Card(list, 18));

            var unknown = _locator.UnrecognizedFolders(_patches);
            if (unknown.Count > 0)
            {
                StorageContent.Children.Add(UiFactory.Section("OTHER FOLDERS IN YOUR LIBRARIES").Margin(0, 24, 0, 6));
                StorageContent.Children.Add(UiFactory.Text("These folders don't match any season. They may be leftovers from older launcher versions or other tools.", 12, "Brush.Fg3", wrap: true).Margin(0, 0, 0, 12));
                var other = new StackPanel();
                foreach (var dir in unknown)
                {
                    var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var name = new StackPanel();
                    name.Children.Add(UiFactory.Text(Path.GetFileName(dir), 13, "Brush.Fg", FontWeights.SemiBold));
                    name.Children.Add(UiFactory.Text(dir, 11, "Brush.Fg3"));
                    g.Children.Add(name);
                    var size = UiFactory.Text("…", 13, "Brush.Fg2");
                    size.HorizontalAlignment = HorizontalAlignment.Right;
                    size.Margin = new Thickness(0, 0, 16, 0);
                    g.Children.Add(size.Col(1));
                    RequestSize(dir, b => size.Text = UiFactory.FormatBytes(b));
                    var actions = new StackPanel { Orientation = Orientation.Horizontal };
                    actions.Children.Add(UiFactory.IconButton(Icons.Folder, "Open folder", (_, _) => OpenFolder(dir)));
                    string local = dir;
                    actions.Children.Add(UiFactory.IconButton(Icons.Link, "Assign to a season", (_, _) => AssignFolder(local)));
                    actions.Children.Add(UiFactory.IconButton(Icons.Delete, "Move to Recycle Bin", (_, _) => RecycleFolder(local)));
                    g.Children.Add(actions.Col(2));
                    other.Children.Add(g);
                }
                StorageContent.Children.Add(UiFactory.Card(other, 18));
            }
        }

        private Border LibraryUsageCard(string lib)
        {
            var sp = new StackPanel();
            bool isDefault = string.Equals(lib, _locator.DefaultLibrary, StringComparison.OrdinalIgnoreCase);
            var title = UiFactory.HStack(8, UiFactory.Text(ShortPath(lib), 13, "Brush.Fg", FontWeights.SemiBold));
            if (isDefault) title.Children.Add(UiFactory.Chip("DEFAULT").Margin(8, 0, 0, 0));
            sp.Children.Add(title);
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(lib)) ?? "C:\\";
                var drive = new DriveInfo(root);
                long used = drive.TotalSize - drive.AvailableFreeSpace;
                var bar = new ProgressBar { Maximum = drive.TotalSize, Value = used, Height = 6, Margin = new Thickness(0, 12, 0, 8) };
                bar.Styled("Progress.Plain");
                sp.Children.Add(bar);
                sp.Children.Add(UiFactory.Text($"{UiFactory.FormatBytes(drive.AvailableFreeSpace)} free of {UiFactory.FormatBytes(drive.TotalSize)} on {root.TrimEnd('\\')}", 12, "Brush.Fg3"));
            }
            catch
            {
                sp.Children.Add(UiFactory.Text("Drive not available", 12, "Brush.Fg3").Margin(0, 8, 0, 0));
            }
            var card = UiFactory.Card(sp, 18);
            card.Margin = new Thickness(0, 0, 14, 14);
            return card;
        }

        private void AssignFolder(string dir)
        {
            var patch = PickSeasonForFolder(dir);
            if (patch == null) return;
            string target = Path.Combine(Path.GetDirectoryName(dir)!, InstallLocator.PrimaryFolderName(patch));
            if (System.IO.Directory.Exists(target))
            {
                ThemedDialog.Alert(this, "Folder already exists", $"{patch.ShortLabel} already uses\n{target}");
                return;
            }
            if (!ThemedDialog.Confirm(this, "Rename folder", $"Rename\n{dir}\nto\n{target}\nso it shows up as {patch.ShortLabel}?", "Rename")) return;
            try
            {
                System.IO.Directory.Move(dir, target);
                RefreshInstallState();
                RefreshAllViews();
                BuildStorageView();
                ShowToast($"Linked to {patch.ShortLabel}", Icons.Check);
            }
            catch (Exception ex) { ThemedDialog.Alert(this, "Rename failed", ex.Message); }
        }

        private void RecycleFolder(string dir)
        {
            if (!ThemedDialog.Confirm(this, "Remove folder", $"Move this folder to the Recycle Bin?\n\n{dir}", "Remove")) return;
            var done = new TaskCompletionSource<Exception?>();
            var thread = new Thread(() =>
            {
                try
                {
                    if (new DirectoryInfo(dir).LinkTarget != null) System.IO.Directory.Delete(dir, false);
                    else Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin, Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                    done.SetResult(null);
                }
                catch (Exception ex) { done.SetResult(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            done.Task.ContinueWith(t => Dispatcher.Invoke(() =>
            {
                if (t.Result != null) ShowToast("Could not remove: " + t.Result.Message, Icons.Warning);
                BuildStorageView();
            }));
        }
    }
}
