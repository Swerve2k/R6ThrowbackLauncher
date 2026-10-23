using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Themes;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private bool _exitRequested;
        private bool _trayHintShown;

        // ── Window chrome ────────────────────────────────────────────────────

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OnWindowStateChanged()
        {
            bool max = WindowState == WindowState.Maximized;
            MaximizeButton.Content = max ? Icons.ChromeRestore : Icons.ChromeMaximize;
            MaximizeButton.ToolTip = max ? "Restore" : "Maximize";
            // A maximized WindowChrome window overhangs the screen by the resize frame.
            RootGrid.Margin = max ? MaximizedInset() : new Thickness(0);
        }

        private Thickness MaximizedInset()
        {
            var source = PresentationSource.FromVisual(this);
            double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            int frameX = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            int frameY = GetSystemMetrics(SM_CYSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
            return new Thickness(frameX / scale, frameY / scale, frameX / scale, frameY / scale);
        }

        private const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private void EnableRoundedCorners()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
                int dark = ThemeManager.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        private void ApplyScale()
        {
            double scale = S.UiScale / 100.0;
            ScaleRoot.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
        }

        public void SetScrim(bool visible) => Scrim.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // ── Toasts ───────────────────────────────────────────────────────────

        private void ShowToast(string message, string? icon = null)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(UiFactory.Icon(icon ?? Icons.Info, 14, "Brush.Fg").Margin(0, 1, 12, 0));
            row.Children.Add(UiFactory.Text(message, 13, "Brush.Fg", wrap: true).Col(1));

            var toast = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 8, 0, 0),
                Child = row,
                Opacity = 0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black },
            };
            toast.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
            toast.SetResourceReference(Border.BorderBrushProperty, "Brush.StrokeStrong");
            toast.MouseLeftButtonUp += (_, _) => ToastHost.Children.Remove(toast);
            ToastHost.Children.Add(toast);
            while (ToastHost.Children.Count > 4) ToastHost.Children.RemoveAt(0);

            toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ThemeManager.ReduceMotion ? 1 : 180)));
            _ = Task.Delay(4500).ContinueWith(_ => Dispatcher.Invoke(() =>
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fade.Completed += (_, _) => ToastHost.Children.Remove(toast);
                toast.BeginAnimation(OpacityProperty, fade);
            }));
        }

        // ── Tray ─────────────────────────────────────────────────────────────

        private void InitTrayIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppPaths.ResourcesDir, "icon.ico");
                var menu = new System.Windows.Forms.ContextMenuStrip();
                var open = new System.Windows.Forms.ToolStripMenuItem("Open");
                open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
                open.Click += (_, _) => Dispatcher.Invoke(RestoreFromTray);
                var exit = new System.Windows.Forms.ToolStripMenuItem("Exit");
                exit.Click += (_, _) => Dispatcher.Invoke(ExitApplication);
                menu.Items.Add(open);
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                menu.Items.Add(exit);

                _trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Text = AppInfo.Name,
                    Visible = true,
                    Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Application,
                    ContextMenuStrip = menu,
                };
                _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        private void ShowTrayNotification(string title, string message)
        {
            if (!S.ShowTrayNotifications || _trayIcon == null) return;
            try { _trayIcon.ShowBalloonTip(4000, title, message, System.Windows.Forms.ToolTipIcon.None); } catch { }
        }

        private void ExitApplication()
        {
            _exitRequested = true;
            Close();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_exitRequested && S.CloseToTray && _trayIcon != null)
            {
                e.Cancel = true;
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    ShowTrayNotification(AppInfo.Name, "Still running in the tray. Right-click the icon to exit.");
                }
                return;
            }

            if (_downloads != null && _downloads.IsRunning && !_exitRequested)
            {
                if (!ThemedDialog.Confirm(this, "Download in progress",
                        "Closing stops the current download. It can be resumed from Downloads next time.", "Close anyway"))
                {
                    e.Cancel = true;
                    return;
                }
            }

            _downloads?.Pause();
            _presenceTimer?.Stop();
            // Run off the UI thread so the request can finish while the window closes.
            try { Task.Run(() => _community.SetOfflineAsync()).Wait(1500); } catch { }
            _discord.Dispose();
            _community.Dispose();
            _trayIcon?.Dispose();
            _trayIcon = null;
            _sessionTimer?.Stop();
            _runningPoll?.Stop();
            _hotkeyService?.Dispose();
            _friendWatcher?.Dispose();
            _overlayWindow?.Close();
            Application.Current.Shutdown();
        }

        // ── Keyboard ─────────────────────────────────────────────────────────

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (UpdateLayer.Visibility == Visibility.Visible) return;
            if (e.Key == Key.Escape)
            {
                if (PopupLayer.Visibility == Visibility.Visible) { ClosePopup(null); e.Handled = true; return; }
                if (_currentView != null && IsSubPage(_currentView) && Keyboard.FocusedElement is not TextBox) { GoBack(); e.Handled = true; return; }
            }
            if (PopupLayer.Visibility == Visibility.Visible) return;

            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            switch (e.Key)
            {
                case Key.D1: Nav_Click(NavHome, e); e.Handled = true; break;
                case Key.D2: Nav_Click(NavBrowse, e); e.Handled = true; break;
                case Key.D3: Nav_Click(NavDownloads, e); e.Handled = true; break;
                case Key.D4: Nav_Click(NavStats, e); e.Handled = true; break;
                case Key.D5: Nav_Click(NavStorage, e); e.Handled = true; break;
                case Key.OemComma: Nav_Click(NavSettings, e); e.Handled = true; break;
                case Key.F:
                    if (_currentView == BrowseView) { BrowseSearch.Focus(); BrowseSearch.SelectAll(); }
                    else
                    {
                        if (_currentView != HomeView) Nav_Click(NavHome, e);
                        LibrarySearch.Focus();
                        LibrarySearch.SelectAll();
                    }
                    e.Handled = true;
                    break;
            }
        }

        // ── Profile area ─────────────────────────────────────────────────────

        /// <summary>The name shown to other players and in the sidebar. Never the Steam login name.</summary>
        private string MyDisplayName =>
            !string.IsNullOrWhiteSpace(S.DisplayName) ? S.DisplayName.Trim()
            : !string.IsNullOrWhiteSpace(S.SteamPersona) ? S.SteamPersona.Trim()
            : "Player";

        private void RefreshProfileArea()
        {
            SidebarUsername.Text = MyDisplayName;
            RefreshSidebarStatus();
        }

        private void RefreshSidebarStatus()
        {
            bool online = _community.IsSignedIn;
            SidebarStatus.Text = AnyGameRunning ? "In game" : online ? "Online" : "Not signed in to Steam";
            SidebarPresenceDot.Visibility = online ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_community.IsSignedIn)
            {
                Nav_Click(NavSettings, e);
                ShowToast("Sign in through Steam to set up your profile.", Icons.Contact);
                return;
            }
            _ = OpenProfileAsync(_community.SteamId);
        }

        private static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch { }
        }

        private static void OpenFolder(string path)
        {
            if (!Directory.Exists(path)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\""); } catch { }
        }
    }
}
