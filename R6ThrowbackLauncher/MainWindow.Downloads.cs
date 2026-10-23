using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using R6ThrowbackLauncher.Models;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private string _lastTransferPatchId = "";
        private string _transferSignature = "";
        private string _downloadsViewSignature = "";
        private ProgressBar? _dlBar;
        private TextBlock? _dlStatus;
        private TextBlock? _dlMeta;
        private TextBox? _dlLog;
        private bool _dlLogOpen;

        private string TransferSignature() =>
            $"{_downloads.Active?.Patch.Id}|{_downloads.State}|{_downloads.QueuePaused}|{string.Join(",", _downloads.Queue.Select(q => q.Patch.Id))}";

        // ── Actions ──────────────────────────────────────────────────────────

        private void StartDownload(SeasonPatch p, string library = "")
        {
            if (_downloads.IsBusyWith(p)) { ShowToast($"{p.ShortLabel} is already in the queue."); return; }
            if (IsPatchRunning(p)) { ShowToast("Stop the game first.", Icons.Warning); return; }

            string target;
            if (System.IO.Directory.Exists(p.InstallDir) && (p.IsPartial || p.IsInstalled))
                target = p.InstallDir;
            else
            {
                string root = S.LibraryFolders.Contains(library, StringComparer.OrdinalIgnoreCase) ? library : _locator.DefaultLibrary;
                try { System.IO.Directory.CreateDirectory(root); }
                catch (Exception ex) { ThemedDialog.Alert(this, "Library unavailable", $"{root}\n\n{ex.Message}"); return; }
                target = Path.Combine(root, InstallLocator.PrimaryFolderName(p));
            }

            long shortfall = DownloadManager.DiskShortfall(p, target);
            if (shortfall > 0 && !ThemedDialog.Confirm(this, "Not enough disk space",
                    $"{p.ShortLabel} needs about {UiFactory.FormatBytes(shortfall)} more free space on this drive than is available. The download will fail when the drive fills up.",
                    "Download anyway"))
                return;

            bool queued = _downloads.IsRunning;
            _downloads.Enqueue(p, target, verify: false);
            if (_downloads.QueuePaused)
                ShowToast($"{p.ShortLabel} added. Downloads are paused; resume them from Downloads.", Icons.Download);
            else
                ShowToast(queued ? $"{p.ShortLabel} added to the queue" : $"Downloading {p.ShortLabel}", Icons.Download);
        }

        private void QueueVerify(SeasonPatch p)
        {
            if (IsPatchRunning(p)) { ShowToast("Stop the game before verifying.", Icons.Warning); return; }
            if (_downloads.IsBusyWith(p)) { ShowToast($"{p.ShortLabel} is already in the queue."); return; }
            _downloads.Enqueue(p, p.InstallDir, verify: true);
            ShowToast(_downloads.Queue.Count > 0 ? $"Verify queued for {p.ShortLabel}" : $"Verifying {p.ShortLabel}", Icons.Sync);
        }

        private void CancelTransfer()
        {
            var active = _downloads.Active;
            if (active == null) return;
            string message = active.Verify
                ? "Stop verifying? The install is left as it is."
                : "Stop this download? Files downloaded so far stay on disk, so you can resume later.";
            if (ThemedDialog.Confirm(this, active.Verify ? "Stop verify" : "Cancel download", message, "Stop", "Keep going"))
                _downloads.CancelActive();
        }

        private void Uninstall(SeasonPatch p)
        {
            if (IsPatchRunning(p) || _downloads.IsBusyWith(p)) { ShowToast("Stop the game and any downloads for this season first.", Icons.Warning); return; }
            string dir = p.InstallDir;
            if (!System.IO.Directory.Exists(dir)) { RefreshInstallState(p); RefreshAllViews(); return; }

            bool isLink = new DirectoryInfo(dir).LinkTarget != null;
            string message = isLink
                ? $"This season was imported from another folder. Only the link in your library is removed; the original files are not touched.\n\n{dir}"
                : $"The folder is moved to the Recycle Bin. If it is too large for the Recycle Bin, Windows asks before deleting it permanently.\n\n{dir}";
            if (!ThemedDialog.Confirm(this, $"Uninstall {p.ShortLabel}?", message, "Uninstall")) return;

            var done = new TaskCompletionSource<Exception?>();
            var thread = new Thread(() =>
            {
                try
                {
                    if (isLink) System.IO.Directory.Delete(dir, recursive: false);
                    else Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir,
                        Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                        Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing);
                    done.SetResult(null);
                }
                catch (Exception ex) { done.SetResult(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            done.Task.ContinueWith(t => Dispatcher.Invoke(() =>
            {
                RefreshInstallState(p);
                RefreshAllViews();
                if (t.Result != null) ShowToast("Uninstall failed: " + t.Result.Message, Icons.Warning);
                else if (!System.IO.Directory.Exists(dir)) ShowToast($"{p.ShortLabel} uninstalled", Icons.Delete);
                _ = PublishProfileAsync();
            }));
        }

        private async Task ReplaceHmArchiveAsync(SeasonPatch p)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a Heated Metal archive",
                Filter = "Archives (*.zip;*.7z)|*.zip;*.7z",
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                await _downloadService.ApplyCustomHmArchiveAsync(dlg.FileName, p.InstallDir, msg => Dispatcher.Invoke(() => ShowToast(msg, Icons.Archive)));
                ShowToast("Heated Metal files replaced", Icons.Check);
                if (_detailPatch == p) BuildDetail(p);
            }
            catch (Exception ex)
            {
                ThemedDialog.Alert(this, "Could not apply archive", ex.Message);
            }
        }

        // ── Steam sign-in ────────────────────────────────────────────────────

        private string EffectiveInGameName()
        {
            if (LoaderConfig.Validate(S.InGameUsername) == null) return S.InGameUsername;
            if (LoaderConfig.Validate(S.SteamUsername) == null) return S.SteamUsername;
            return LoaderConfig.DefaultUsername;
        }

        private Task<SteamCredentials?> RequestSteamCredentialsAsync(SeasonPatch patch)
        {
            if (string.IsNullOrWhiteSpace(S.SteamUsername))
            {
                string? account = ThemedDialog.Prompt(this, "Steam account",
                    "Downloads come straight from Steam, so the account must own Rainbow Six Siege. Enter the account name you sign in with (not your display name).",
                    "", "Steam account name", v => string.IsNullOrWhiteSpace(v) ? "Enter your account name." : null, "Continue");
                if (account == null) return Task.FromResult<SteamCredentials?>(null);
                S.SteamUsername = account.Trim();
                SaveSettings();
            }

            string user = S.SteamUsername;
            string? saved = SecretProtector.Unprotect(S.SteamPasswordProtected);
            if (saved != null) return Task.FromResult<SteamCredentials?>(new SteamCredentials(user, saved, S.RememberSteamLogin));
            if (S.RememberSteamLogin && string.Equals(S.SteamTokenUser, user, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<SteamCredentials?>(new SteamCredentials(user, null, true));

            var body = new StackPanel();
            body.Children.Add(ThemedDialog.Message($"Signing in as {user}. Your password goes directly to Steam."));
            var pw = new PasswordBox { Margin = new Thickness(0, 14, 0, 0) };
            pw.Styled("Input.Password");
            body.Children.Add(pw);
            var save = new CheckBox { Content = "Save password on this PC (encrypted with Windows)", Margin = new Thickness(0, 12, 0, 0) };
            save.Styled("Check");
            body.Children.Add(save);
            var remember = new CheckBox { Content = "Stay signed in with Steam", IsChecked = S.RememberSteamLogin, Margin = new Thickness(0, 8, 0, 0) };
            remember.Styled("Check");
            body.Children.Add(remember);
            body.Loaded += (_, _) => pw.Focus();

            string? result = ThemedDialog.Show(this, "Steam sign-in", body, new[]
            {
                new DialogButton("Cancel", "cancel", IsCancel: true),
                new DialogButton("Sign in", "ok", Primary: true),
            }, 440);
            if (result != "ok" || pw.Password.Length == 0) return Task.FromResult<SteamCredentials?>(null);

            S.RememberSteamLogin = remember.IsChecked == true;
            if (save.IsChecked == true)
            {
                try { S.SteamPasswordProtected = SecretProtector.Protect(pw.Password); } catch { }
            }
            SaveSettings();
            return Task.FromResult<SteamCredentials?>(new SteamCredentials(user, pw.Password, S.RememberSteamLogin));
        }

        private Task<string?> PromptForSteamInputAsync(PromptKind kind, string text)
        {
            if (WindowState == WindowState.Minimized || !IsVisible) RestoreFromTray();
            string? value = kind == PromptKind.GuardCode
                ? ThemedDialog.Prompt(this, "Steam Guard", text, "", "Code", v => string.IsNullOrWhiteSpace(v) ? "Enter the code." : null, "Submit")
                : ThemedDialog.Prompt(this, "Steam password", text, "", "", v => v.Length == 0 ? "Enter your password." : null, "Sign in", password: true);
            return Task.FromResult(value);
        }

        private void OnLoginOutcome(bool success)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (success)
                {
                    if (S.RememberSteamLogin) S.SteamTokenUser = S.SteamUsername;
                    _verifiedSteamLogin = S.SteamUsername;
                    SaveSettings();
                    _ = TryLinkLegacyProfileAsync();
                    return;
                }
                S.SteamTokenUser = "";
                S.SteamPasswordProtected = "";
                SaveSettings();
            });
        }

        // ── Manager events ───────────────────────────────────────────────────

        private void OnDownloadsChanged()
        {
            if (_downloads.Active != null) _lastTransferPatchId = _downloads.Active.Patch.Id;
            UpdateStatusTransfer();
            UpdateDownloadsBadge();
            if (_currentView == DownloadsView) BuildDownloadsView();
            UpdateDetailLive();

            string sig = TransferSignature();
            if (sig == _transferSignature) return;
            _transferSignature = sig;
            if (_currentView == HomeView) BuildHome();
            if (_currentView == BrowseView) BuildBrowse();
        }

        private void OnTransferFinished(TransferResult r)
        {
            RefreshInstallState(r.Patch);
            RefreshAllViews();
            if (r.Success)
            {
                ShowToast(r.Message, Icons.Check);
                ShowTrayNotification(r.Verify ? "Verify complete" : "Download complete", r.Message);
                if (!r.Verify) _ = PublishProfileAsync();
            }
            else if (r.Message != "Cancelled")
            {
                ShowToast($"{r.Patch.ShortLabel}: {r.Message}", Icons.Warning);
                ShowTrayNotification(r.Verify ? "Verify failed" : "Download failed", r.Message);
            }
        }

        private void OnTransferLog(IReadOnlyList<string> lines)
        {
            string text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
            foreach (var box in new[] { _detailLogBox, _dlLog })
            {
                if (box == null || !box.IsLoaded) continue;
                if (box.Text.Length > 200_000) box.Text = _downloads.LogText();
                else box.AppendText(text);
                box.ScrollToEnd();
            }
        }

        private void UpdateDownloadsBadge()
        {
            int count = _downloads.Queue.Count + (_downloads.IsRunning ? 1 : 0);
            DownloadsBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DownloadsBadgeText.Text = count.ToString();
        }

        private void UpdateStatusTransfer()
        {
            var active = _downloads.Active;
            if (active == null && !(_downloads.QueuePaused && _downloads.Queue.Count > 0))
            {
                StatusTransfer.Visibility = Visibility.Collapsed;
                return;
            }
            StatusTransfer.Visibility = Visibility.Visible;
            if (active == null)
            {
                StatusTransferText.Text = $"Downloads paused  ·  {_downloads.Queue.Count} waiting";
                StatusTransferBar.Visibility = Visibility.Collapsed;
                StatusTransferPct.Text = "";
                return;
            }
            StatusTransferBar.Visibility = Visibility.Visible;
            StatusTransferText.Text = $"{active.Patch.ShortLabel}  ·  {_downloads.StatusText}";
            StatusTransferBar.Value = _downloads.Percent;
            StatusTransferPct.Text = _downloads.State == TransferState.Running ? $"{_downloads.Percent:0}%" : "";
        }

        private void StatusTransfer_Click(object sender, RoutedEventArgs e) => Nav_Click(NavDownloads, e);

        // ── Downloads page ───────────────────────────────────────────────────

        private void BuildDownloadsView(bool force = false)
        {
            string sig = TransferSignature() + "|" + _dlLogOpen;
            if (!force && sig == _downloadsViewSignature)
            {
                if (_dlBar != null) _dlBar.Value = _downloads.Percent;
                if (_dlStatus != null) _dlStatus.Text = _downloads.StatusText;
                if (_dlMeta != null) _dlMeta.Text = TransferMeta();
                return;
            }
            _downloadsViewSignature = sig;
            _dlBar = null; _dlStatus = null; _dlMeta = null; _dlLog = null;
            DownloadsContent.Children.Clear();

            var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new StackPanel();
            title.Children.Add(new TextBlock { Text = "Downloads" }.Styled("Text.Title"));
            int waiting = _downloads.Queue.Count;
            title.Children.Add(UiFactory.Text(
                _downloads.IsRunning ? $"1 active{(waiting > 0 ? $", {waiting} queued" : "")}" :
                waiting > 0 ? $"{waiting} queued" : "Nothing in progress", 12, "Brush.Fg3").Margin(0, 4, 0, 0));
            header.Children.Add(title);
            if (_downloads.IsRunning || waiting > 0)
            {
                var toggle = _downloads.QueuePaused
                    ? UiFactory.Button("Resume all", "Btn.Primary", (_, _) => _downloads.Resume(), Icons.Play)
                    : UiFactory.Button("Pause all", "Btn.Secondary", (_, _) => _downloads.Pause(), Icons.Pause);
                toggle.VerticalAlignment = VerticalAlignment.Bottom;
                header.Children.Add(toggle.Col(1));
            }
            DownloadsContent.Children.Add(header);

            if (!_downloads.IsRunning && waiting == 0)
            {
                DownloadsContent.Children.Add(UiFactory.EmptyState(Icons.Download, "No downloads",
                    "Downloads and file checks you start appear here. You can pause, reorder and resume them at any time.",
                    UiFactory.Button("Browse seasons", "Btn.Primary", (_, _) => Nav_Click(NavBrowse, new RoutedEventArgs()), Icons.Browse)));
            }

            if (_downloads.Active is { } active) DownloadsContent.Children.Add(BuildActiveTransferCard(active));
            else if (_downloads.QueuePaused && waiting > 0)
            {
                var paused = UiFactory.Card(UiFactory.VStack(
                    UiFactory.Text("Downloads are paused", 14, "Brush.Fg", FontWeights.SemiBold),
                    UiFactory.Text(string.IsNullOrEmpty(_downloads.StatusText) || _downloads.StatusText == "Paused"
                        ? "Resume to continue where you left off. Steam keeps the parts that already finished."
                        : _downloads.StatusText, 12, "Brush.Fg3", wrap: true).Margin(0, 4, 0, 0)), 18);
                DownloadsContent.Children.Add(paused.Margin(0, 0, 0, 20));
            }

            if (waiting > 0)
            {
                DownloadsContent.Children.Add(UiFactory.Section("UP NEXT").Margin(0, 8, 0, 10));
                for (int i = 0; i < _downloads.Queue.Count; i++)
                    DownloadsContent.Children.Add(BuildQueueRow(_downloads.Queue[i], i, _downloads.Queue.Count));
            }

            var history = DownloadManager.LoadHistory(12);
            if (history.Count > 0)
            {
                DownloadsContent.Children.Add(UiFactory.Section("HISTORY").Margin(0, 24, 0, 10));
                var list = new StackPanel();
                foreach (var (name, ok, verify, time) in history)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(UiFactory.Icon(ok ? Icons.Check : Icons.Close, 12, ok ? "Brush.Fg" : "Brush.Fg3"));
                    string what = (verify, ok) switch { (true, true) => "Verified", (true, false) => "Verify failed:", (false, true) => "Downloaded", _ => "Download failed:" };
                    row.Children.Add(UiFactory.Text($"{what}  {name}", 13, ok ? "Brush.Fg2" : "Brush.Fg3").Col(1));
                    row.Children.Add(UiFactory.Text(PlaytimeService.FormatRelative(time), 12, "Brush.Fg3").Col(2));
                    list.Children.Add(row);
                }
                DownloadsContent.Children.Add(UiFactory.Card(list, 18));
            }
        }

        private Border BuildActiveTransferCard(TransferItem item)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var art = ArtworkCache.Build(item.Patch.ArtworkPath, 200, null, forceColor: true);
            art.Width = 96; art.Height = 96;
            art.Clip = new RectangleGeometry(new Rect(0, 0, 96, 96), 8, 8);
            art.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(art);

            var sp = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            titles.Children.Add(UiFactory.Text(item.Patch.ShortLabel, 16, "Brush.Fg", FontWeights.SemiBold));
            _dlStatus = UiFactory.Text(_downloads.StatusText, 13, "Brush.Fg2").Margin(0, 3, 0, 0);
            titles.Children.Add(_dlStatus);
            top.Children.Add(titles);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            buttons.Children.Add(UiFactory.Button("Pause", "Btn.Secondary", (_, _) => _downloads.Pause(), Icons.Pause));
            buttons.Children.Add(UiFactory.Button("Cancel", "Btn.Secondary", (_, _) => CancelTransfer(), Icons.Close).Margin(8, 0, 0, 0));
            buttons.Children.Add(UiFactory.IconButton(Icons.Info, "Open season", (_, _) => OpenDetail(item.Patch)).Margin(8, 1, 0, 0));
            top.Children.Add(buttons.Col(1));
            sp.Children.Add(top);

            _dlBar = UiFactory.Progress(8);
            _dlBar.Value = _downloads.Percent;
            _dlBar.Margin = new Thickness(0, 14, 0, 8);
            sp.Children.Add(_dlBar);
            _dlMeta = UiFactory.Text(TransferMeta(), 12, "Brush.Fg3");
            sp.Children.Add(_dlMeta);
            sp.Children.Add(UiFactory.Text($"To {item.TargetDir}", 12, "Brush.Fg3").Margin(0, 4, 0, 0));

            var logToggle = UiFactory.Button(_dlLogOpen ? "Hide log" : "Show log", "Btn.Ghost", (_, _) =>
            {
                _dlLogOpen = !_dlLogOpen;
                BuildDownloadsView(force: true);
            }, Icons.Document);
            logToggle.HorizontalAlignment = HorizontalAlignment.Left;
            logToggle.Margin = new Thickness(-10, 10, 0, 0);
            sp.Children.Add(logToggle);
            if (_dlLogOpen)
            {
                _dlLog = new TextBox { IsReadOnly = true, Height = 260, FontSize = 12, Text = _downloads.LogText(), Margin = new Thickness(0, 8, 0, 0) };
                _dlLog.Styled("Input.Multiline");
                _dlLog.SetResourceReference(TextBox.FontFamilyProperty, "Font.Mono");
                _dlLog.Loaded += (_, _) => _dlLog?.ScrollToEnd();
                sp.Children.Add(_dlLog);
            }

            g.Children.Add(sp.Col(1));
            return UiFactory.Card(g).Margin(0, 0, 0, 20);
        }

        private Border BuildQueueRow(TransferItem item, int index, int count)
        {
            var row = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 10, 10, 10), Margin = new Thickness(0, 0, 0, 6) };
            row.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
            row.SetResourceReference(Border.BorderBrushProperty, "Brush.Stroke");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(UiFactory.Text($"{index + 1}", 13, "Brush.Fg3"));
            var art = ArtworkCache.Build(item.Patch.ArtworkPath, 100, row);
            art.Width = 36; art.Height = 36;
            art.Clip = new RectangleGeometry(new Rect(0, 0, 36, 36), 5, 5);
            art.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(art.Col(1));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(UiFactory.Text(item.Patch.ShortLabel, 13, "Brush.Fg", FontWeights.SemiBold));
            string kind = item.Verify ? "Verify files" : item.Patch.SizeGb > 0 ? $"Download  ·  {item.Patch.SizeGb:0.#} GB" : "Download";
            text.Children.Add(UiFactory.Text($"{kind}  ·  {ShortPath(Path.GetDirectoryName(item.TargetDir) ?? "")}", 12, "Brush.Fg3"));
            g.Children.Add(text.Col(2));
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var up = UiFactory.IconButton(Icons.ChevronUp, "Move up", (_, _) => _downloads.Move(item.Patch, -1));
            up.IsEnabled = index > 0;
            var down = UiFactory.IconButton(Icons.ChevronDown, "Move down", (_, _) => _downloads.Move(item.Patch, 1));
            down.IsEnabled = index < count - 1;
            actions.Children.Add(up);
            actions.Children.Add(down);
            actions.Children.Add(UiFactory.IconButton(Icons.Close, "Remove from queue", (_, _) => _downloads.Remove(item.Patch)));
            g.Children.Add(actions.Col(3));
            row.Child = g;
            return row;
        }

        // ── Import existing folders ──────────────────────────────────────────

        private static readonly Regex CodeRx = new(@"Y(\d{1,2})S(\d)", RegexOptions.IgnoreCase);

        private SeasonPatch? GuessSeason(string folder)
        {
            string name = Path.GetFileName(folder.TrimEnd('\\'));
            var m = CodeRx.Match(name);
            if (!m.Success) return null;
            string code = $"Y{int.Parse(m.Groups[1].Value)}S{m.Groups[2].Value}";
            bool hm = name.Contains("heated", StringComparison.OrdinalIgnoreCase) || name.EndsWith("HM", StringComparison.OrdinalIgnoreCase);
            return _patches.FirstOrDefault(p => p.Code == code && p.IsHeatedMetal == hm)
                ?? _patches.FirstOrDefault(p => p.Code == code && !p.IsHeatedMetal);
        }

        private void ImportFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose a Rainbow Six Siege game folder",
                UseDescriptionForTitle = true,
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            if (InstallLocator.FindExecutable(dlg.SelectedPath) == null)
            {
                ThemedDialog.Alert(this, "Not a game folder", "No RainbowSix executable was found in that folder.");
                return;
            }
            var patch = GuessSeason(dlg.SelectedPath) ?? PickSeasonForFolder(dlg.SelectedPath);
            if (patch == null) return;
            if (ImportFolderAs(dlg.SelectedPath, patch))
            {
                RefreshInstallState();
                RefreshAllViews();
                ShowToast($"Imported as {patch.ShortLabel}", Icons.Check);
            }
        }

        private SeasonPatch? PickSeasonForFolder(string folder)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 14, 0, 0), MaxDropDownHeight = 320 };
            combo.Styled("Combo");
            foreach (var p in _patches) combo.Items.Add(new ComboBoxItem { Content = p.ShortLabel, Tag = p });
            combo.SelectedIndex = 0;
            var body = UiFactory.VStack(ThemedDialog.Message($"Which season is this?\n{folder}"), combo);
            string? r = ThemedDialog.Show(this, "Import game folder", body, new[]
            {
                new DialogButton("Cancel", "cancel", IsCancel: true),
                new DialogButton("Import", "ok", Primary: true),
            });
            return r == "ok" && combo.SelectedItem is ComboBoxItem { Tag: SeasonPatch picked } ? picked : null;
        }

        private bool ImportFolderAs(string folder, SeasonPatch patch)
        {
            string root = _locator.DefaultLibrary;
            string link = Path.Combine(root, InstallLocator.PrimaryFolderName(patch));
            if (System.IO.Directory.Exists(link))
            {
                ThemedDialog.Alert(this, "Already in your library", $"{patch.ShortLabel} already has a folder at\n{link}");
                return false;
            }
            try
            {
                System.IO.Directory.CreateDirectory(root);
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/C mklink /J \"{link}\" \"{folder}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit();
                return System.IO.Directory.Exists(link);
            }
            catch (Exception ex)
            {
                ThemedDialog.Alert(this, "Import failed", ex.Message);
                return false;
            }
        }

        private async Task ScanForExistingInstallsAsync()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[]
            {
                Path.Combine(home, "Downloads", "R6Legacy"),
                Path.Combine(home, "Downloads", "OperationThrowback"),
                Path.Combine(home, "Documents", "R6Legacy"),
                @"C:\R6Legacy", @"C:\OperationThrowback",
            };
            var known = new HashSet<string>(_patches.Where(p => p.IsInstalled || p.IsPartial).Select(p => Path.GetFullPath(p.InstallDir)), StringComparer.OrdinalIgnoreCase);
            var found = await Task.Run(() =>
            {
                var list = new List<string>();
                foreach (var root in roots)
                {
                    if (!System.IO.Directory.Exists(root)) continue;
                    try
                    {
                        foreach (var sub in System.IO.Directory.GetDirectories(root))
                            if (InstallLocator.FindExecutable(sub) != null && !known.Contains(Path.GetFullPath(sub)) && !_locator.IsInLibrary(sub))
                                list.Add(sub);
                    }
                    catch { }
                }
                return list;
            });
            if (found.Count == 0) return;

            var dlg = new DetectedInstallsDialog(found) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            int imported = 0;
            foreach (var folder in dlg.SelectedFolders)
            {
                var patch = GuessSeason(folder) ?? PickSeasonForFolder(folder);
                if (patch != null && !patch.IsInstalled && ImportFolderAs(folder, patch)) imported++;
            }
            RefreshInstallState();
            RefreshAllViews();
            if (imported > 0) ShowToast($"Imported {imported} folder{(imported == 1 ? "" : "s")}", Icons.Check);
        }
    }
}
