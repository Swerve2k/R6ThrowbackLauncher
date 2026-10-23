using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using R6ThrowbackLauncher.Services;

namespace R6ThrowbackLauncher
{
    public partial class OverlayWindow : Window
    {
        // ── Dependencies ───────────────────────────────────────────────────────
        private readonly CommunityService _community;
        private readonly PlaytimeService  _playtime;

        // ── Session state ──────────────────────────────────────────────────────
        private string?  _currentVersion;
        private string?  _currentMap;
        private string?  _currentGamemode;
        private string?  _currentOperator;
        private DateTime _gameStartTime;

        // ── Internal state ─────────────────────────────────────────────────────
        private string           _activeTab = "friends";
        private bool             _isVisible = false;
        private DispatcherTimer? _sessionTimer;

        // ── Chat state ─────────────────────────────────────────────────────────
        private string?          _chatPartner;
        private string?          _chatPartnerDisplayName;
        private DispatcherTimer? _chatPollTimer;

        // ── Win32 P/Invoke for fullscreen overlay ────────────────────────────
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly IntPtr HWND_TOPMOST   = new IntPtr(-1);
        private const uint SWP_NOMOVE     = 0x0002;
        private const uint SWP_NOSIZE     = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int  GWL_EXSTYLE    = -20;
        private const int  WS_EX_NOACTIVATE = 0x08000000;
        private const int  WS_EX_TOOLWINDOW = 0x00000080;

        // ── Avatar color palette ───────────────────────────────────────────────
        private static readonly Color[] AvatarColors =
        {
            Color.FromRgb(0x3A, 0x3A, 0x3A),
            Color.FromRgb(0x46, 0x46, 0x46),
            Color.FromRgb(0x52, 0x52, 0x52),
            Color.FromRgb(0x2E, 0x2E, 0x2E),
            Color.FromRgb(0x5E, 0x5E, 0x5E),
            Color.FromRgb(0x40, 0x40, 0x40),
        };

        // ── Constructor ───────────────────────────────────────────────────────
        public OverlayWindow(CommunityService community, PlaytimeService playtime)
        {
            _community = community;
            _playtime = playtime;

            InitializeComponent();

            // Apply WS_EX_NOACTIVATE so the overlay never steals focus from the game
            // (prevents exclusive fullscreen from minimizing when overlay opens)
            SourceInitialized += (_, _) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };

            SnapOffScreen();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ══════════════════════════════════════════════════════════════════════

        public void SetSessionInfo(string version, DateTime gameStart)
        {
            _currentVersion = version;
            _gameStartTime  = gameStart;
            _currentMap = _currentGamemode = _currentOperator = null;
            if (_isVisible) Dispatcher.Invoke(() =>
            {
                RefreshHeader();
                RefreshSessionTab();
                RefreshNowPlayingBanner();
            });
        }

        public void UpdateGameState(string? map, string? gamemode, string? op)
        {
            _currentMap      = map;
            _currentGamemode = gamemode;
            _currentOperator = op;
            if (_isVisible) Dispatcher.Invoke(() =>
            {
                RefreshNowPlayingBanner();
                if (_activeTab == "session") RefreshSessionTab();
            });
        }

        public void Toggle()
        {
            if (_isVisible) SlideOut();
            else            SlideIn();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SLIDE ANIMATION  (LEFT side)
        // ══════════════════════════════════════════════════════════════════════

        private (double hidden, double visible) SlidePositions()
        {
            // Use Bounds (not WorkingArea) so the overlay covers fullscreen games too
            var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                         ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

            // Convert physical pixels → WPF device-independent pixels
            var source = PresentationSource.FromVisual(this);
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

            Height = screen.Height / dpiY;
            Top    = screen.Top    / dpiY;
            double leftEdge = screen.Left / dpiX;
            return (leftEdge - (Width + 20), leftEdge);
        }

        private void SnapOffScreen()
        {
            var (hidden, _) = SlidePositions();
            Left = hidden;
        }

        private void SlideIn()
        {
            var (hidden, visible) = SlidePositions();
            _isVisible = true;
            Left       = hidden;
            Show();

            // Force above fullscreen games
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            var anim = new DoubleAnimation(hidden, visible,
                new Duration(TimeSpan.FromMilliseconds(300)))
            { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(LeftProperty, anim);

            RefreshHeader();
            RefreshNowPlayingBanner();
            SelectTab(_activeTab, forceRefresh: true);
            StartSessionTimer();
        }

        private void SlideOut()
        {
            var (hidden, visible) = SlidePositions();
            _isVisible = false;
            StopSessionTimer();
            StopChatPoll();

            var anim = new DoubleAnimation(visible, hidden,
                new Duration(TimeSpan.FromMilliseconds(240)))
            { EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (_, _) => Hide();
            BeginAnimation(LeftProperty, anim);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  HEADER
        // ══════════════════════════════════════════════════════════════════════

        private void RefreshHeader()
        {
            string username = string.IsNullOrWhiteSpace(_community.DisplayName) ? "Player" : _community.DisplayName;
            HeaderUsername.Text     = username;
            HeaderInitials.Text     = GetInitials(username);
            HeaderAvatar.Background = new SolidColorBrush(GetAvatarColor(username));
            HeaderVersion.Text      = _currentVersion ?? "Swerve2k Downloader";

            // IN GAME badge visibility
            HeaderInGameBadge.Visibility = _currentVersion != null
                ? Visibility.Visible : Visibility.Collapsed;

            // Playtime stats line
            long totalSec = _playtime.GetTotalSeconds();
            int  sessions = _playtime.GetTotalSessionCount();
            if (totalSec > 0 || sessions > 0)
                HeaderStats.Text = $"{PlaytimeService.Format(totalSec)}  ·  {sessions} sessions";
            else
                HeaderStats.Text = "";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  NOW PLAYING BANNER
        // ══════════════════════════════════════════════════════════════════════

        private void RefreshNowPlayingBanner()
        {
            if (_currentVersion == null)
            {
                NowPlayingBanner.Visibility = Visibility.Collapsed;
                return;
            }

            NowPlayingBanner.Visibility = Visibility.Visible;
            NowPlayingVersion.Text      = _currentVersion;

            if (!string.IsNullOrEmpty(_currentMap))
                NowPlayingDetail.Text = _currentOperator != null
                    ? $"{_currentMap}  ·  {_currentOperator}"
                    : _currentMap;
            else
                NowPlayingDetail.Text = "In game";

            UpdateNowPlayingClock();
        }

        private void UpdateNowPlayingClock()
        {
            if (_gameStartTime == default || _currentVersion == null) return;
            var t = DateTime.UtcNow - _gameStartTime;
            NowPlayingTimer.Text = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TAB SWITCHING
        // ══════════════════════════════════════════════════════════════════════

        private void TabBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Border btn && btn.Tag is string tag)
                SelectTab(tag);
        }

        private void SelectTab(string tabName, bool forceRefresh = false)
        {
            _activeTab = tabName;
            if (tabName != "friends") HideChatView();

            var tabs = new (Border Btn, TextBlock Txt, UIElement Panel, string Tag)[]
            {
                (TabBtnFriends, TabTxtFriends, PanelFriends,  "friends"),
                (TabBtnStats,   TabTxtStats,   PanelStats,    "stats"),
                (TabBtnSession, TabTxtSession, PanelSession,  "session"),
            };

            foreach (var (btn, txt, panel, tag) in tabs)
            {
                bool active = tag == tabName;
                // Use BorderBrush for active state so XAML hover trigger on Background still works
                btn.BorderBrush  = active
                    ? new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2))
                    : new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1D));
                txt.Foreground   = active
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0x5D, 0x5D, 0x5D));
                panel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }

            switch (tabName)
            {
                case "friends": _ = RefreshFriendsAsync();  break;
                case "stats":   _ = RefreshStatsTabAsync(); break;
                case "session": RefreshSessionTab();        break;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  FRIENDS TAB
        // ══════════════════════════════════════════════════════════════════════

        private async System.Threading.Tasks.Task RefreshFriendsAsync()
        {
            FriendsActiveList.Children.Clear();
            FriendsOfflineList.Children.Clear();
            FriendsActiveList.Children.Add(MakeEmptyHint("Loading…", "#3D3D3D"));

            List<FriendInfo> friends;
            try   { friends = await _community.GetFriendsAsync(); }
            catch { friends = new(); }

            Dispatcher.Invoke(() =>
            {
                FriendsActiveList.Children.Clear();
                FriendsOfflineList.Children.Clear();

                var online  = friends.Where(f => f.Online).OrderBy(f => f.Name).ToList();
                var offline = friends.Where(f => !f.Online).OrderBy(f => f.Name).ToList();

                FriendsOnlineStatText.Text = online.Count.ToString();
                FriendsTotalStatText.Text  = friends.Count.ToString();
                OnlineCountText.Text       = online.Count.ToString();
                OfflineCountText.Text      = offline.Count.ToString();

                if (!_community.IsSignedIn)
                    FriendsActiveList.Children.Add(MakeEmptyHint("Sign in through Steam in the launcher to see friends", "#5D5D5D"));
                else if (friends.Count == 0)
                    FriendsActiveList.Children.Add(MakeEmptyHint("No friends added yet", "#3D3D3D"));
                else if (online.Count == 0)
                    FriendsActiveList.Children.Add(MakeEmptyHint("No one is online right now", "#3D3D3D"));

                foreach (var f in online)
                    FriendsActiveList.Children.Add(BuildFriendRow(f, dim: false));

                foreach (var f in offline)
                    FriendsOfflineList.Children.Add(BuildFriendRow(f, dim: true));
            });

        }



        private UIElement BuildFriendRow(FriendInfo friend, bool dim)
        {
            string display  = friend.Name;
            bool   inGame   = friend.Presence?.InGame == true;
            bool   online   = friend.Online && !inGame;
            string subLine  = inGame  ? (string.IsNullOrEmpty(friend.Presence!.Season)
                                             ? "In Game" : friend.Presence.Season)
                              : online ? "Online"
                              :          "Offline";

            Color  avatarColor = dim ? Color.FromRgb(0x29, 0x29, 0x29) : GetAvatarColor(display);
            double rowOpacity  = dim ? 0.45 : 1.0;

            var row = new Border
            {
                Margin        = new Thickness(0, 1, 0, 1),
                CornerRadius  = new CornerRadius(8),
                Padding       = new Thickness(10, 9, 10, 9),
                Background    = Brushes.Transparent,
                Opacity       = rowOpacity,
                Cursor        = System.Windows.Input.Cursors.Hand
            };
            row.MouseEnter += (_, _) =>
                row.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16));
            row.MouseLeave += (_, _) =>
                row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, _) => ShowChatView(friend);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var avatar = new Border
            {
                Width             = 36,
                Height            = 36,
                CornerRadius      = new CornerRadius(18),
                Background        = new SolidColorBrush(avatarColor),
                VerticalAlignment = VerticalAlignment.Center
            };
            avatar.Child = new TextBlock
            {
                Text                = GetInitials(display),
                Foreground          = dim ? new SolidColorBrush(Color.FromRgb(0x5B, 0x5B, 0x5B))
                                          : Brushes.White,
                FontSize            = 12,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(avatar, 0);

            var nameStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(6, 0, 8, 0)
            };
            nameStack.Children.Add(new TextBlock
            {
                Text         = display,
                Foreground   = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
                FontSize     = 13,
                FontWeight   = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            nameStack.Children.Add(new TextBlock
            {
                Text         = subLine,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x5B, 0x5B, 0x5B)),
                FontSize     = 11,
                Margin       = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(nameStack, 1);

            if (!dim)
            {
                Color pillBg    = inGame ? Color.FromRgb(0x0A, 0x0A, 0x0A) : Color.FromRgb(0x1D, 0x1D, 0x1D);
                Color pillBdr   = inGame ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0x2A, 0x2A, 0x2A);
                Color pillText  = inGame ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0xB8, 0xB8, 0xB8);
                string pillLabel = inGame ? "IN GAME" : "ONLINE";

                var pill = new Border
                {
                    Background        = new SolidColorBrush(pillBg),
                    BorderBrush       = new SolidColorBrush(pillBdr),
                    BorderThickness   = new Thickness(1),
                    CornerRadius      = new CornerRadius(5),
                    Padding           = new Thickness(7, 3, 7, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };
                pill.Child = new TextBlock
                {
                    Text       = pillLabel,
                    Foreground = new SolidColorBrush(pillText),
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold
                };
                Grid.SetColumn(pill, 2);
                grid.Children.Add(pill);
            }

            grid.Children.Add(avatar);
            grid.Children.Add(nameStack);
            row.Child = grid;
            return row;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  CHAT VIEW
        // ══════════════════════════════════════════════════════════════════════

        private void ShowChatView(FriendInfo friend)
        {
            string displayName      = friend.Name;
            _chatPartner            = friend.SteamId;
            _chatPartnerDisplayName = displayName;

            ChatPartnerInitials.Text     = GetInitials(displayName);
            ChatPartnerAvatar.Background = new SolidColorBrush(GetAvatarColor(displayName));
            ChatPartnerName.Text         = displayName;

            bool inGame = friend.Presence?.InGame == true;
            bool online = friend.Online && !inGame;

            if (inGame)
            {
                ChatPartnerStatusText.Text        = string.IsNullOrEmpty(friend.Presence!.Season)
                                                        ? "In Game" : friend.Presence.Season;
                ChatPartnerStatusPill.Background  = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A));
                ChatPartnerStatusLabel.Text       = "IN GAME";
                ChatPartnerStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
            }
            else if (online)
            {
                ChatPartnerStatusText.Text        = "Online";
                ChatPartnerStatusPill.Background  = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
                ChatPartnerStatusLabel.Text       = "ONLINE";
                ChatPartnerStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8));
            }
            else
            {
                ChatPartnerStatusText.Text        = "Offline";
                ChatPartnerStatusPill.Background  = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17));
                ChatPartnerStatusLabel.Text       = "OFFLINE";
                ChatPartnerStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x5D, 0x5D, 0x5D));
            }

            FriendsScrollViewer.Visibility = Visibility.Collapsed;
            ChatView.Visibility            = Visibility.Visible;

            _ = LoadChatMessagesAsync(friend.SteamId);
            StartChatPoll(friend.SteamId);
        }

        private void HideChatView()
        {
            StopChatPoll();
            _chatPartner            = null;
            _chatPartnerDisplayName = null;

            ChatView.Visibility            = Visibility.Collapsed;
            FriendsScrollViewer.Visibility = Visibility.Visible;
        }

        private async System.Threading.Tasks.Task LoadChatMessagesAsync(string withUsername)
        {
            List<ChatMessage> msgs;
            try   { msgs = await _community.GetMessagesAsync(withUsername); }
            catch { msgs = new(); }

            Dispatcher.Invoke(() =>
            {
                ChatMessagesList.Children.Clear();
                if (msgs.Count == 0)
                {
                    ChatMessagesList.Children.Add(new TextBlock
                    {
                        Text                = "No messages yet. Say hi!",
                        Foreground          = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                        FontSize            = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin              = new Thickness(0, 20, 0, 0)
                    });
                    return;
                }

                foreach (var msg in msgs)
                    ChatMessagesList.Children.Add(BuildChatBubble(msg));

                ChatScrollViewer.UpdateLayout();
                ChatScrollViewer.ScrollToBottom();
            });
        }

        private UIElement BuildChatBubble(ChatMessage msg)
        {
            bool mine = msg.From == _community.SteamId;
            var bubble = new Border
            {
                CornerRadius    = mine
                    ? new CornerRadius(14, 14, 4, 14)
                    : new CornerRadius(14, 14, 14, 4),
                Background      = mine
                    ? new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10))
                    : new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)),
                BorderBrush     = mine
                    ? new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21))
                    : new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x23)),
                BorderThickness = new Thickness(1),
                Padding         = new Thickness(11, 8, 11, 8),
                MaxWidth        = 280,
                Margin          = mine
                    ? new Thickness(40, 2, 0, 2)
                    : new Thickness(0, 2, 40, 2),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left
            };
            bubble.Child = new TextBlock
            {
                Text         = msg.Text,
                Foreground   = mine
                    ? new SolidColorBrush(Color.FromRgb(0xDC, 0xDC, 0xDC))
                    : new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                FontSize     = 13,
                TextWrapping = TextWrapping.Wrap
            };
            return bubble;
        }

        private void StartChatPoll(string withUsername)
        {
            StopChatPoll();
            _chatPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _chatPollTimer.Tick += (_, _) =>
            {
                if (_chatPartner != null) _ = LoadChatMessagesAsync(_chatPartner);
            };
            _chatPollTimer.Start();
        }

        private void StopChatPoll()
        {
            _chatPollTimer?.Stop();
            _chatPollTimer = null;
        }

        private async System.Threading.Tasks.Task SendChatMessage()
        {
            string text = ChatInput.Text.Trim();
            if (string.IsNullOrEmpty(text) || _chatPartner == null) return;
            ChatInput.Text = "";
            try
            {
                await _community.SendMessageAsync(_chatPartner, text);
                await LoadChatMessagesAsync(_chatPartner);
            }
            catch { }
        }

        private void ChatBack_Click(object sender, RoutedEventArgs e) => HideChatView();
        private void ChatSend_Click(object sender, RoutedEventArgs e) => _ = SendChatMessage();
        private void ChatInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                _ = SendChatMessage();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  STATS TAB
        // ══════════════════════════════════════════════════════════════════════

        private async System.Threading.Tasks.Task RefreshStatsTabAsync()
        {
            long   totalSec = _playtime.GetTotalSeconds();
            int    sessions = _playtime.GetTotalSessionCount();
            long   firstTs  = _playtime.GetFirstLaunch();
            string favVer   = "—";

            var all = _playtime.GetAllEntriesSorted();
            if (all.Count > 0) favVer = all[0].PatchId;

            StatHours.Text      = PlaytimeService.Format(totalSec);
            StatSessions.Text   = sessions.ToString();
            StatFavVersion.Text = favVer;
            StatSince.Text      = firstTs > 0
                ? DateTimeOffset.FromUnixTimeSeconds(firstTs).ToString("MMM yyyy")
                : "—";

            RecentVersionsList.Children.Clear();
            foreach (var (patchId, entry) in all.Take(6))
            {
                double hours = entry.TotalSeconds / 3600.0;
                var pill = new Border
                {
                    Background      = new SolidColorBrush(Color.FromRgb(0x09, 0x09, 0x09)),
                    BorderBrush     = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(6),
                    Padding         = new Thickness(9, 5, 9, 5),
                    Margin          = new Thickness(0, 0, 6, 6)
                };
                var stack = new StackPanel { Orientation = Orientation.Horizontal };
                stack.Children.Add(new TextBlock
                {
                    Text       = patchId,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
                    FontSize   = 10,
                    FontWeight = FontWeights.Bold
                });
                stack.Children.Add(new TextBlock
                {
                    Text       = $"  {hours:F0}h",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0x5B, 0x5B)),
                    FontSize   = 10
                });
                pill.Child = stack;
                RecentVersionsList.Children.Add(pill);
            }

            if (!all.Any())
                RecentVersionsList.Children.Add(MakeEmptyHint("No sessions recorded yet", "#3D3D3D"));

            // Community stats (async)
            try
            {
                var presence = await _community.GetPresenceAsync();
                CommunityOnlineText.Text  = presence.Values.Count(p => p.IsOnline).ToString();
                CommunityInGameText.Text  = presence.Values.Count(p => p.InGame).ToString();
            }
            catch
            {
                CommunityOnlineText.Text = "—";
                CommunityInGameText.Text = "—";
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SESSION TAB
        // ══════════════════════════════════════════════════════════════════════

        private void RefreshSessionTab()
        {
            SessVersion.Text  = _currentVersion  ?? "—";
            SessMap.Text      = _currentMap       ?? "—";
            SessMode.Text     = _currentGamemode  ?? "—";
            SessOperator.Text = _currentOperator  ?? "—";
            UpdateSessionClock();
        }

        private void StartSessionTimer()
        {
            StopSessionTimer();
            _sessionTimer       = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _sessionTimer.Tick += (_, _) =>
            {
                UpdateSessionClock();
                UpdateNowPlayingClock();
            };
            _sessionTimer.Start();
        }

        private void StopSessionTimer()
        {
            _sessionTimer?.Stop();
            _sessionTimer = null;
        }

        private void UpdateSessionClock()
        {
            if (_gameStartTime == default) { BigTimerText.Text = "00:00:00"; return; }
            var t = DateTime.UtcNow - _gameStartTime;
            BigTimerText.Text = t.TotalHours >= 1
                ? $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"00:{t.Minutes:D2}:{t.Seconds:D2}";
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PARTY TAB
        // ══════════════════════════════════════════════════════════════════════



        // ══════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private static UIElement MakeEmptyHint(string text, string hexColor)
        {
            return new TextBlock
            {
                Text       = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)),
                FontSize   = 12,
                Margin     = new Thickness(12, 6, 12, 6)
            };
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "??";
            var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            return name.Length >= 2
                ? name.Substring(0, 2).ToUpper()
                : name.ToUpper().PadRight(2, '?');
        }

        private static Color GetAvatarColor(string name)
        {
            if (string.IsNullOrEmpty(name)) return AvatarColors[0];
            int hash = name.Aggregate(0, (h, c) => h * 31 + c);
            return AvatarColors[Math.Abs(hash) % AvatarColors.Length];
        }

        // ══════════════════════════════════════════════════════════════════════
        //  WINDOW EVENTS
        // ══════════════════════════════════════════════════════════════════════

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => SlideOut();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            SlideOut();
        }
    }
}
