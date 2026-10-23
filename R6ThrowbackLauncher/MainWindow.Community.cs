using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using R6ThrowbackLauncher.Services;
using R6ThrowbackLauncher.Themes;
using R6ThrowbackLauncher.Ui;

namespace R6ThrowbackLauncher
{
    public partial class MainWindow
    {
        private const int MaxDisplayName = 24;
        private const int MaxBio = 160;

        private FriendWatcher? _friendWatcher;
        private DispatcherTimer? _presenceTimer;
        private CancellationTokenSource? _steamSignInCts;
        private Dictionary<string, Presence> _presence = new();
        private List<FriendRequestInfo> _requests = new();
        private bool _requestsLoaded;
        private string _legacyNote = "";
        private string _verifiedSteamLogin = "";   // account that signed in through DepotDownloader this session

        // ══ Identity ═════════════════════════════════════════════════════════

        private void StartCommunity()
        {
            if (!string.IsNullOrEmpty(S.SteamId))
            {
                _community.SetIdentity(S.SteamId, MyDisplayName);
                OnSignedIn();
            }
            RefreshProfileArea();
            StartPresenceLoop();
        }

        private void OnSignedIn()
        {
            if (S.MemberSince == 0)
            {
                S.MemberSince = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SaveSettings();
            }
            _ = SendHeartbeatAsync();
            _ = PublishProfileAsync();
            _ = RefreshPersonaAsync();
            _ = PollRequestsAsync();
            _ = TryLinkLegacyProfileAsync();

            _friendWatcher?.Dispose();
            _friendWatcher = new FriendWatcher(_community);
            _friendWatcher.FriendCameOnline += name => Dispatcher.Invoke(() => NotifyFriend($"{name} is online", Icons.People, () => ToastManager.ShowFriendOnline(name)));
            _friendWatcher.FriendWentInGame += name => Dispatcher.Invoke(() => NotifyFriend($"{name} started playing", Icons.Game, () => ToastManager.ShowFriendInGame(name)));
            _friendWatcher.NewMessageReceived += (sid, name, text) => Dispatcher.Invoke(() =>
            {
                if (_currentView == FriendsView && _chatWith == sid) { _ = LoadChatAsync(); return; }
                NotifyFriend($"{name}: {text}", Icons.Chat, () => ToastManager.ShowNewMessage(name, text));
            });
            _friendWatcher.Start();
        }

        /// <summary>In-app toast normally; the always-on-top game toast while a season is running.</summary>
        private void NotifyFriend(string message, string icon, Action gameToast)
        {
            if (AnyGameRunning) gameToast();
            else ShowToast(message, icon);
        }

        /// <summary>Call after the display name or Steam identity changes.</summary>
        private void OnIdentityChanged()
        {
            if (!_community.IsSignedIn) return;
            _community.SetIdentity(S.SteamId, MyDisplayName);
            _ = SendHeartbeatAsync();
            _ = PublishProfileAsync();
        }

        private async Task RefreshPersonaAsync()
        {
            string? persona = await SteamAuthService.GetPersonaNameAsync(S.SteamId);
            if (string.IsNullOrWhiteSpace(persona) || persona == S.SteamPersona) return;
            S.SteamPersona = persona;
            SaveSettings();
            RefreshProfileArea();
            OnIdentityChanged();
        }

        private async Task SignInThroughSteamAsync()
        {
            if (_steamSignInCts != null) return;
            _steamSignInCts = new CancellationTokenSource();
            if (_currentView == SettingsView) BuildSettingsView();
            try
            {
                var id = await SteamAuthService.SignInAsync(_steamSignInCts.Token);
                if (S.SteamId != id.SteamId)
                {
                    // Carried-over stats belong to one Steam account; a different account starts clean.
                    S.StatsBaselinePlaytime = 0;
                    S.StatsBaselineSessions = 0;
                    S.LegacyProfileClaimed = "";
                }
                S.SteamId = id.SteamId;
                if (!string.IsNullOrWhiteSpace(id.PersonaName)) S.SteamPersona = id.PersonaName;
                SaveSettings();
                _community.SetIdentity(S.SteamId, MyDisplayName);
                OnSignedIn();
                RefreshProfileArea();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                ShowToast($"Signed in through Steam as {MyDisplayName}", Icons.Check);
            }
            catch (OperationCanceledException)
            {
                ShowToast("Steam sign-in was cancelled.", Icons.Info);
            }
            catch (Exception ex)
            {
                ThemedDialog.Alert(this, "Steam sign-in failed", ex.Message);
            }
            finally
            {
                _steamSignInCts.Dispose();
                _steamSignInCts = null;
                if (_currentView == SettingsView) BuildSettingsView();
                else if (_currentView == FriendsView) BuildFriendsView();
            }
        }

        private void SignOutOfCommunity()
        {
            if (!ThemedDialog.Confirm(this, "Sign out of Steam",
                    "You'll go offline for friends and stop updating your leaderboard stats until you sign in again. Your stats stay saved on your Steam account.", "Sign out"))
                return;
            _ = Task.Run(() => _community.SetOfflineAsync());
            _friendWatcher?.Dispose();
            _friendWatcher = null;
            S.SteamId = "";
            S.SteamPersona = "";
            S.StatsBaselinePlaytime = 0;
            S.StatsBaselineSessions = 0;
            S.LegacyProfileClaimed = "";
            SaveSettings();
            _community.SetIdentity("", "");
            _requests.Clear();
            _chatWith = null;
            UpdateFriendsBadge();
            RefreshProfileArea();
            BuildSettingsView();
            ShowToast("Signed out of Steam", Icons.Check);
        }

        private static string? ValidateDisplayName(string name)
        {
            if (name.Length == 0) return null;
            if (name.Length < 2) return "Use at least 2 characters.";
            if (name.Length > MaxDisplayName) return $"Use {MaxDisplayName} characters or fewer.";
            if (name.Any(char.IsControl)) return "That name has characters that can't be shown.";
            return null;
        }

        // ══ Presence and player count ════════════════════════════════════════

        private void StartPresenceLoop()
        {
            _presenceTimer?.Stop();
            _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _presenceTimer.Tick += async (_, _) =>
            {
                await SendHeartbeatAsync();
                await UpdateOnlineCountAsync();
                await PollRequestsAsync();
            };
            _presenceTimer.Start();
            _ = UpdateOnlineCountAsync();
            StartOnlinePulse();
        }

        private async Task SendHeartbeatAsync()
        {
            if (!_community.IsSignedIn) return;
            string season = _sessionPatch?.ShortLabel
                ?? _patches.FirstOrDefault(p => p.IsInstalled && GameProcessService.IsRunningFrom(p.InstallDir, _runningGames))?.ShortLabel
                ?? "";
            try { await _community.HeartbeatAsync(AnyGameRunning ? "in-game" : "online", season); } catch { }
        }

        private async Task UpdateOnlineCountAsync()
        {
            try
            {
                _presence = await _community.GetPresenceAsync();
                int count = _presence.Values.Count(p => p.IsOnline);
                if (_community.IsSignedIn && !_presence.ContainsKey(_community.SteamId)) count++;
                OnlineText.Text = count == 1 ? "1 player online" : $"{count:N0} players online";
                OnlineChip.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        private void StartOnlinePulse()
        {
            if (ThemeManager.ReduceMotion) return;
            var dur = TimeSpan.FromSeconds(1.8);
            var grow = new DoubleAnimation(1, 2.6, dur) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            OnlinePulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            OnlinePulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            OnlinePulse.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 0, dur) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private async Task PollRequestsAsync()
        {
            if (!_community.IsSignedIn) return;
            try
            {
                var list = await _community.GetIncomingRequestsAsync();
                if (_requestsLoaded)
                    foreach (var r in list.Where(r => _requests.All(o => o.SteamId != r.SteamId)))
                        ShowToast($"{r.Name} sent you a friend request", Icons.People);
                _requests = list;
                _requestsLoaded = true;
                UpdateFriendsBadge();
                if (_currentView == FriendsView && _friendsTab == "requests") RenderFriendsLeft();
            }
            catch { }
        }

        private void UpdateFriendsBadge()
        {
            FriendsBadge.Visibility = _requests.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            FriendsBadgeText.Text = _requests.Count.ToString();
        }

        // ══ Profile publishing and pre-3.0 stats ═════════════════════════════

        private async Task PublishProfileAsync()
        {
            if (!_community.IsSignedIn) return;
            string fav = _playtime.GetAllEntriesSorted().Select(e => FindPatch(e.PatchId)).FirstOrDefault(p => p != null)?.BaseName ?? "";
            var profile = new PlayerProfile
            {
                DisplayName      = MyDisplayName,
                SteamName        = S.SteamPersona,
                Bio              = S.ProfileBio,
                FavouriteSeason  = fav,
                TotalPlaytimeSec = _playtime.GetTotalSeconds() + S.StatsBaselinePlaytime,
                SessionCount     = _playtime.GetTotalSessionCount() + S.StatsBaselineSessions,
                Installed        = Installed.Select(p => p.IsHeatedMetal ? $"{p.BaseName} HM" : p.BaseName).Distinct().ToList(),
                MemberSince      = S.MemberSince,
                LegacyUsername   = S.LegacyProfileClaimed,
            };
            try { await _community.PublishProfileAsync(profile); } catch { }
        }

        /// <summary>
        /// Moves a pre-3.0 profile (keyed by the Steam login name typed into the launcher) onto the signed-in
        /// Steam account. The login name must have signed in to Steam on this PC, and each old profile can be
        /// linked to one Steam account only, so typing someone else's name no longer gets you their stats.
        /// </summary>
        private async Task TryLinkLegacyProfileAsync()
        {
            if (!_community.IsSignedIn) return;
            try
            {
                if (S.LegacyProfileClaimed.Length > 0)
                {
                    await RestoreLegacyFriendsAsync(S.LegacyProfileClaimed, pendingOnly: true);
                    _legacyNote = $"Linked. Playtime and sessions from your old \"{S.LegacyProfileClaimed}\" profile count toward your Steam account.";
                    return;
                }

                string account = S.SteamUsername.Trim();
                if (account.Length == 0)
                {
                    _legacyNote = "If you used the launcher before 3.0, enter the Steam login name you used then under \"Steam login for downloads\" and your old stats will be linked.";
                    return;
                }

                var legacy = await _community.GetLegacyProfileAsync(account);
                if (legacy == null)
                {
                    _legacyNote = "No profile from before 3.0 was found for your Steam login name. Nothing to link.";
                    return;
                }

                string? owner = await _community.GetLegacyClaimAsync(account);
                if (!string.IsNullOrEmpty(owner) && owner != _community.SteamId)
                {
                    _legacyNote = $"The old \"{account}\" profile is already linked to a different Steam account.";
                    return;
                }

                string verifiedNow = _verifiedSteamLogin;
                if (!await Task.Run(() => string.Equals(verifiedNow, account, StringComparison.OrdinalIgnoreCase) || SteamSession.HasSavedLogin(account)))
                {
                    _legacyNote = $"Found your old \"{account}\" profile ({PlaytimeService.Format(legacy.TotalPlaytimeSec)}). It links automatically after your next Steam login for a download or verify, which proves the account is yours.";
                    return;
                }

                if (string.IsNullOrEmpty(owner))
                {
                    await _community.ClaimLegacyAsync(account);
                    if (await _community.GetLegacyClaimAsync(account) != _community.SteamId) return;
                }

                S.StatsBaselinePlaytime = Math.Max(0, legacy.TotalPlaytimeSec - _playtime.GetTotalSeconds());
                S.StatsBaselineSessions = Math.Max(0, legacy.SessionCount - _playtime.GetTotalSessionCount());
                if (string.IsNullOrWhiteSpace(S.ProfileBio)) S.ProfileBio = legacy.Bio;
                S.LegacyProfileClaimed = account;
                SaveSettings();
                _legacyNote = $"Linked. Playtime and sessions from your old \"{account}\" profile count toward your Steam account.";

                await RestoreLegacyFriendsAsync(account, pendingOnly: false);
                await PublishProfileAsync();
                ShowToast($"Your stats from before 3.0 ({PlaytimeService.Format(legacy.TotalPlaytimeSec)}) are now linked to your Steam account", Icons.Check);
                if (_currentView == SettingsView) BuildSettingsView();
            }
            catch { }
        }

        /// <summary>Re-adds old friendships once both people have linked their Steam accounts.</summary>
        private async Task RestoreLegacyFriendsAsync(string myLegacyName, bool pendingOnly)
        {
            var current = new HashSet<string>(await _community.GetFriendIdsAsync());
            foreach (var sid in await _community.TakePendingFriendsAsync(myLegacyName))
                if (sid != _community.SteamId && current.Add(sid)) await _community.AddFriendshipAsync(sid);
            if (pendingOnly) return;

            var claims = await _community.GetLegacyClaimsAsync();
            foreach (var oldFriend in await _community.GetLegacyFriendsAsync(myLegacyName))
            {
                if (claims.TryGetValue(oldFriend, out var sid) && sid.Length > 0)
                {
                    if (sid != _community.SteamId && current.Add(sid)) await _community.AddFriendshipAsync(sid);
                }
                else await _community.AddPendingFriendAsync(oldFriend);
            }
        }

        private string LegacyStatusText() => _legacyNote.Length > 0 ? _legacyNote : "Checking for a profile from before 3.0.";

        // ══ Shared pieces ════════════════════════════════════════════════════

        /// <summary>The default person icon. Profile pictures were removed in 3.0.</summary>
        private static Grid Avatar(double size, string? state = null)
        {
            var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            var circle = new Ellipse();
            circle.SetResourceReference(Shape.FillProperty, "Brush.Surface3");
            g.Children.Add(circle);
            g.Children.Add(UiFactory.Icon(Icons.Contact, Math.Round(size * 0.46), "Brush.Fg2"));
            if (state != null)
            {
                double d = Math.Clamp(size * 0.24, 10, 18);
                var dot = new Ellipse { Width = d, Height = d, StrokeThickness = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
                dot.SetResourceReference(Shape.StrokeProperty, "Brush.Surface");
                dot.SetResourceReference(Shape.FillProperty, state == "offline" ? "Brush.Fg3" : "Brush.Fg");
                if (state == "offline") dot.Opacity = 0.6;
                g.Children.Add(dot);
            }
            return g;
        }

        private Presence? PresenceOf(string steamId) =>
            _presence.TryGetValue(steamId, out var p) && p.IsOnline ? p : null;

        private static string PresenceState(Presence? p) => p == null ? "offline" : p.InGame ? "in-game" : "online";

        private static string PresenceText(Presence? p) =>
            p == null ? "Offline" : p.InGame ? (string.IsNullOrEmpty(p.Season) ? "In game" : $"Playing {p.Season}") : "Online";

        private static Border PersonRow(string name, string subtitle, string? state, Action? onClick, params UIElement[] actions)
        {
            var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 8, 8), Margin = new Thickness(0, 0, 0, 2) };
            UiFactory.Hover(row, "Brush.Surface", "Brush.Surface2");
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(Avatar(36, state));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
            text.Children.Add(UiFactory.Text(name, 13, state == "offline" ? "Brush.Fg2" : "Brush.Fg", FontWeights.SemiBold));
            if (subtitle.Length > 0) text.Children.Add(UiFactory.Text(subtitle, 11, "Brush.Fg3").Margin(0, 2, 0, 0));
            g.Children.Add(text.Col(1));
            var act = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var a in actions)
            {
                if (a is FrameworkElement fe && act.Children.Count > 0) fe.Margin = new Thickness(4, 0, 0, 0);
                act.Children.Add(a);
            }
            g.Children.Add(act.Col(2));
            row.Child = g;
            if (onClick != null)
            {
                row.Cursor = Cursors.Hand;
                row.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && IsInButton(d)) return; onClick(); };
            }
            return row;
        }

        private UIElement SignInPrompt(string title, string message)
        {
            var signIn = UiFactory.Button(_steamSignInCts != null ? "Waiting for Steam" : "Sign in through Steam", "Btn.Primary",
                async (_, _) => await SignInThroughSteamAsync(), Icons.Contact);
            signIn.IsEnabled = _steamSignInCts == null;
            return UiFactory.EmptyState(Icons.People, title, message, signIn);
        }

        // ══ Friends page ═════════════════════════════════════════════════════

        private string _friendsTab = "friends";
        private List<FriendInfo> _friends = new();
        private bool _friendsLoaded;
        private string? _chatWith;
        private string _chatWithName = "";
        private DispatcherTimer? _chatTimer;
        private StackPanel? _friendsLeft;
        private Border? _chatHost;
        private StackPanel? _chatMessages;
        private ScrollViewer? _chatScroll;
        private TextBlock? _friendsSubtitle;
        private string _playerQuery = "";
        private List<PlayerProfile>? _playersCache;
        private DateTime _playersCacheAt;

        private void BuildFriendsView()
        {
            FriendsContent.Children.Clear();
            if (!_community.IsSignedIn)
            {
                FriendsContent.Children.Add(PageHeader("Friends", ""));
                FriendsContent.Children.Add(UiFactory.Card(SignInPrompt("Sign in through Steam to add friends",
                    "Friends are linked to Steam accounts, so the person you add is really them. Steam only shares your public Steam ID with the launcher.")));
                return;
            }

            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, label) in new[] { ("friends", "Friends"), ("requests", _requests.Count > 0 ? $"Requests ({_requests.Count})" : "Requests"), ("find", "Find players") })
            {
                var rb = new RadioButton { Content = label, GroupName = "FriendsTab", IsChecked = _friendsTab == key };
                rb.Styled("Segment");
                string k = key;
                rb.Click += (_, _) => { _friendsTab = k; BuildFriendsView(); };
                seg.Children.Add(rb);
            }
            var segHost = new Border { Child = seg };
            segHost.Styled("SegmentHost");
            var refresh = UiFactory.IconButton(Icons.Refresh, "Refresh", async (_, _) => { await LoadFriendsAsync(); await PollRequestsAsync(); });
            var header = PageHeader("Friends", "", UiFactory.HStack(8, segHost, refresh));
            _friendsSubtitle = UiFactory.Text(FriendsSubtitle(), 12, "Brush.Fg3").Margin(0, 4, 0, 0);
            ((StackPanel)header.Children[0]).Children.Add(_friendsSubtitle);
            FriendsContent.Children.Add(header);

            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _friendsLeft = new StackPanel();
            cols.Children.Add(UiFactory.Card(_friendsLeft, 12));
            _chatHost = new Border();
            cols.Children.Add(_chatHost.Col(2));
            FriendsContent.Children.Add(cols);

            RenderFriendsLeft();
            RenderChat();
            if (!_friendsLoaded) _ = LoadFriendsAsync();
        }

        private string FriendsSubtitle()
        {
            if (!_friendsLoaded) return "Loading";
            int online = _friends.Count(f => f.Online);
            return _friends.Count == 0 ? "No friends yet" : $"{online} online  ·  {_friends.Count} friend{(_friends.Count == 1 ? "" : "s")}";
        }

        private async Task LoadFriendsAsync()
        {
            try
            {
                _presence = await _community.GetPresenceAsync();
                _friends = await _community.GetFriendsAsync();
            }
            catch { }
            _friendsLoaded = true;
            if (_currentView != FriendsView) return;
            if (_friendsSubtitle != null) _friendsSubtitle.Text = FriendsSubtitle();
            RenderFriendsLeft();
            RenderChat();
        }

        private void RenderFriendsLeft()
        {
            if (_friendsLeft == null) return;
            _friendsLeft.Children.Clear();
            switch (_friendsTab)
            {
                case "requests": RenderRequests(); break;
                case "find": RenderFindPlayers(); break;
                default: RenderFriendList(); break;
            }
        }

        private void RenderFriendList()
        {
            if (!_friendsLoaded) { _friendsLeft!.Children.Add(UiFactory.Text("Loading friends", 12, "Brush.Fg3").Margin(8, 8, 8, 8)); return; }
            if (_friends.Count == 0)
            {
                var find = UiFactory.Button("Find players", "Btn.Secondary", (_, _) => { _friendsTab = "find"; BuildFriendsView(); }, Icons.Search);
                _friendsLeft!.Children.Add(UiFactory.EmptyState(Icons.People, "No friends yet", "Find players by name or Steam profile link and send a request.", find));
                return;
            }
            void Group(string title, IEnumerable<FriendInfo> items)
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                _friendsLeft!.Children.Add(UiFactory.Section($"{title}  {list.Count}").Margin(10, _friendsLeft.Children.Count == 0 ? 4 : 14, 0, 6));
                foreach (var f in list)
                {
                    var p = f.Presence is { IsOnline: true } pr ? pr : null;
                    var friend = f;
                    var row = PersonRow(f.Name, PresenceText(p), PresenceState(p), () => OpenChat(friend.SteamId, friend.Name),
                        UiFactory.IconButton(Icons.Contact, "View profile", (_, _) => _ = OpenProfileAsync(friend.SteamId)));
                    if (_chatWith == f.SteamId)
                    {
                        row.Bg("Brush.Selected");
                        row.MouseLeave += (_, _) => row.Bg("Brush.Selected");
                    }
                    _friendsLeft.Children.Add(row);
                }
            }
            Group("IN GAME", _friends.Where(f => f.Presence?.InGame == true).OrderBy(f => f.Name));
            Group("ONLINE", _friends.Where(f => f.Online && f.Presence?.InGame != true).OrderBy(f => f.Name));
            Group("OFFLINE", _friends.Where(f => !f.Online).OrderBy(f => f.Name));
        }

        private void RenderRequests()
        {
            if (_requests.Count == 0)
            {
                _friendsLeft!.Children.Add(UiFactory.EmptyState(Icons.People, "No friend requests", "Requests other players send you show up here."));
                return;
            }
            foreach (var r in _requests)
            {
                var req = r;
                var accept = UiFactory.Button("Accept", "Btn.Primary", async (_, _) =>
                {
                    try
                    {
                        await _community.AcceptFriendRequestAsync(req.SteamId);
                        _requests.RemoveAll(x => x.SteamId == req.SteamId);
                        UpdateFriendsBadge();
                        ShowToast($"You and {req.Name} are now friends", Icons.Check);
                        await LoadFriendsAsync();
                        BuildFriendsView();
                    }
                    catch (Exception ex) { ShowToast("Could not accept: " + ex.Message, Icons.Warning); }
                });
                accept.Height = 30;
                var decline = UiFactory.IconButton(Icons.Close, "Decline", async (_, _) =>
                {
                    try { await _community.DeclineFriendRequestAsync(req.SteamId); } catch { }
                    _requests.RemoveAll(x => x.SteamId == req.SteamId);
                    UpdateFriendsBadge();
                    BuildFriendsView();
                });
                string when = req.Timestamp > 0 ? PlaytimeService.FormatRelative(req.Timestamp / 1000) : "";
                _friendsLeft!.Children.Add(PersonRow(req.Name, when.Length > 0 ? $"Sent {when}" : "Wants to be friends", null,
                    () => _ = OpenProfileAsync(req.SteamId), accept, decline));
            }
        }

        private void RenderFindPlayers()
        {
            var box = UiFactory.Input(_playerQuery, "Name, Steam ID or profile link");
            box.Margin = new Thickness(4, 4, 4, 10);
            var results = new StackPanel();
            _friendsLeft!.Children.Add(box);
            _friendsLeft.Children.Add(results);

            async void Search()
            {
                _playerQuery = box.Text.Trim();
                results.Children.Clear();
                if (_playerQuery.Length < 2)
                {
                    results.Children.Add(UiFactory.Text("Type at least 2 characters. You can also paste a steamcommunity.com/profiles/ link.", 12, "Brush.Fg3", wrap: true).Margin(8, 0, 8, 8));
                    return;
                }
                string query = _playerQuery;
                if (_playersCache == null || DateTime.UtcNow - _playersCacheAt > TimeSpan.FromMinutes(2))
                {
                    results.Children.Add(UiFactory.Text("Searching", 12, "Brush.Fg3").Margin(8, 0, 8, 8));
                    try { _playersCache = await _community.GetPlayersAsync(); _playersCacheAt = DateTime.UtcNow; }
                    catch { _playersCache = new(); }
                    if (query != _playerQuery) return;
                    results.Children.Clear();
                }

                string? sid = SteamAuthService.ParseSteamId(query);
                var matches = _playersCache!
                    .Where(p => p.SteamId != _community.SteamId)
                    .Where(p => sid != null ? p.SteamId == sid
                        : p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.SteamName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(p => p.TotalPlaytimeSec)
                    .Take(25).ToList();
                if (matches.Count == 0)
                {
                    results.Children.Add(UiFactory.Text(sid != null ? "That Steam account hasn't signed in to the launcher yet." : "No players found. Players appear here after they sign in through Steam.", 12, "Brush.Fg3", wrap: true).Margin(8, 0, 8, 8));
                    return;
                }
                foreach (var m in matches)
                {
                    var player = m;
                    var presence = PresenceOf(m.SteamId);
                    bool isFriend = _friends.Any(f => f.SteamId == m.SteamId);
                    UIElement action = isFriend
                        ? UiFactory.Chip("FRIEND")
                        : UiFactory.IconButton(Icons.AddFriend, "Send friend request", async (s, _) =>
                        {
                            if (s is Button b) b.IsEnabled = false;
                            await SendFriendRequestAsync(player.SteamId, player.Name);
                        });
                    results.Children.Add(PersonRow(m.Name, $"{PresenceText(presence)}  ·  {PlaytimeService.Format(m.TotalPlaytimeSec)} played", PresenceState(presence),
                        () => _ = OpenProfileAsync(player.SteamId, player), action));
                }
            }

            var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            debounce.Tick += (_, _) => { debounce.Stop(); Search(); };
            box.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
            box.Loaded += (_, _) => box.Focus();
            Search();
        }

        private async Task SendFriendRequestAsync(string steamId, string name)
        {
            if (!_community.IsSignedIn || steamId == _community.SteamId) return;
            try
            {
                if (_requests.Any(r => r.SteamId == steamId))
                {
                    await _community.AcceptFriendRequestAsync(steamId);
                    _requests.RemoveAll(r => r.SteamId == steamId);
                    UpdateFriendsBadge();
                    ShowToast($"You and {name} are now friends", Icons.Check);
                    await LoadFriendsAsync();
                    return;
                }
                await _community.SendFriendRequestAsync(steamId);
                ShowToast($"Friend request sent to {name}", Icons.Check);
            }
            catch (Exception ex) { ShowToast("Could not send the request: " + ex.Message, Icons.Warning); }
        }

        // ── Chat ─────────────────────────────────────────────────────────────

        private void OpenChat(string steamId, string name)
        {
            if (_currentView != FriendsView)
            {
                _friendsTab = "friends";
                ShowView(FriendsView, "Friends", NavFriends);
                BuildFriendsView();
            }
            _chatWith = steamId;
            _chatWithName = name;
            RenderFriendsLeft();
            RenderChat();
        }

        private void RenderChat()
        {
            if (_chatHost == null) return;
            if (_chatWith == null || _friends.All(f => f.SteamId != _chatWith))
            {
                _chatHost.Child = UiFactory.Card(UiFactory.EmptyState(Icons.Chat, "Pick a friend to chat",
                    "Messages are only between friends. Select someone on the left."));
                StopChatPolling();
                return;
            }

            var friend = _friends.First(f => f.SteamId == _chatWith);
            var presence = friend.Presence is { IsOnline: true } pr ? pr : null;
            var root = new Grid { Height = 560 };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid { Margin = new Thickness(18, 14, 12, 12) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Avatar(40, PresenceState(presence)));
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            who.Children.Add(UiFactory.Text(friend.Name, 15, "Brush.Fg", FontWeights.SemiBold));
            who.Children.Add(UiFactory.Text(PresenceText(presence), 12, "Brush.Fg3").Margin(0, 2, 0, 0));
            head.Children.Add(who.Col(1));
            var sid = friend.SteamId;
            var headActions = UiFactory.HStack(4,
                UiFactory.IconButton(Icons.Contact, "View profile", (_, _) => _ = OpenProfileAsync(sid)),
                UiFactory.IconButton(Icons.More, "More", (s, e) => UiFactory.OpenMenu((UIElement)s,
                    UiFactory.MenuItem("Steam profile", Icons.OpenExternal, () => OpenUrl(SteamAuthService.ProfileUrl(sid))),
                    UiFactory.MenuItem("Remove friend", Icons.Remove, () => _ = RemoveFriendAsync(sid, friend.Name)))));
            headActions.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(headActions.Col(2));
            root.Children.Add(head);
            var line = UiFactory.Divider(0, 0);
            line.VerticalAlignment = VerticalAlignment.Bottom;
            root.Children.Add(line);

            _chatMessages = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            _chatScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _chatMessages };
            root.Children.Add(_chatScroll.Row(1));
            _chatMessages.Children.Add(UiFactory.Text("Loading messages", 12, "Brush.Fg3"));

            var inputRow = new Grid { Margin = new Thickness(14, 10, 14, 14) };
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var input = UiFactory.Input("", $"Message {friend.Name}");
            input.MaxLength = 1000;
            var send = UiFactory.Button("", "Btn.Primary", null, Icons.Send);
            send.Margin = new Thickness(8, 0, 0, 0);
            send.Width = 44;
            UiFactory.AutomationName(send, "Send");
            async Task Send()
            {
                string text = input.Text.Trim();
                if (text.Length == 0) return;
                input.Text = "";
                try { await _community.SendMessageAsync(sid, text); }
                catch (Exception ex) { ShowToast("Message not sent: " + ex.Message, Icons.Warning); input.Text = text; }
                await LoadChatAsync();
            }
            send.Click += async (_, _) => await Send();
            input.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Send(); } };
            inputRow.Children.Add(input);
            inputRow.Children.Add(send.Col(1));
            root.Children.Add(inputRow.Row(2));
            var topLine = UiFactory.Divider(0, 0);
            topLine.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(topLine.Row(2));

            var card = UiFactory.Card(root, 0);
            _chatHost.Child = card;
            input.Loaded += (_, _) => input.Focus();

            _ = LoadChatAsync();
            StopChatPolling();
            _chatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _chatTimer.Tick += async (_, _) => await LoadChatAsync();
            _chatTimer.Start();
        }

        private string _chatSignature = "";

        private async Task LoadChatAsync()
        {
            string? with = _chatWith;
            if (with == null || _chatMessages == null) return;
            List<ChatMessage> messages;
            try { messages = await _community.GetMessagesAsync(with); }
            catch { return; }
            if (with != _chatWith || _chatMessages == null) return;

            string sig = with + ":" + messages.Count + ":" + (messages.LastOrDefault()?.Timestamp ?? 0);
            if (sig == _chatSignature && _chatMessages.Children.Count > 0 && _chatMessages.Children[0] is not TextBlock) return;
            _chatSignature = sig;

            _chatMessages.Children.Clear();
            if (messages.Count == 0)
            {
                _chatMessages.Children.Add(UiFactory.Text($"No messages yet. Say hi to {_chatWithName}.", 12, "Brush.Fg3"));
                return;
            }
            DateTime? lastDay = null;
            foreach (var m in messages)
            {
                var local = DateTimeOffset.FromUnixTimeMilliseconds(m.Timestamp).LocalDateTime;
                if (lastDay != local.Date)
                {
                    lastDay = local.Date;
                    var day = UiFactory.Text(local.Date == DateTime.Today ? "Today" : local.ToString("MMM d"), 11, "Brush.Fg3");
                    day.HorizontalAlignment = HorizontalAlignment.Center;
                    day.Margin = new Thickness(0, 8, 0, 6);
                    _chatMessages.Children.Add(day);
                }
                bool mine = m.From == _community.SteamId;
                var bubble = new Border
                {
                    CornerRadius = mine ? new CornerRadius(12, 12, 4, 12) : new CornerRadius(12, 12, 12, 4),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = mine ? new Thickness(60, 2, 0, 2) : new Thickness(0, 2, 60, 2),
                    HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    MaxWidth = 460,
                    ToolTip = local.ToString("g"),
                };
                bubble.Bg(mine ? "Brush.Accent" : "Brush.Surface3");
                bubble.Child = UiFactory.Text(m.Text, 13, mine ? "Brush.OnAccent" : "Brush.Fg", wrap: true);
                _chatMessages.Children.Add(bubble);
            }
            _chatScroll?.ScrollToBottom();
        }

        private void StopChatPolling()
        {
            _chatTimer?.Stop();
            _chatTimer = null;
        }

        private async Task RemoveFriendAsync(string steamId, string name)
        {
            if (!ThemedDialog.Confirm(this, "Remove friend", $"Remove {name} from your friends? You can send a new request later.", "Remove")) return;
            try
            {
                await _community.RemoveFriendAsync(steamId);
                _friends.RemoveAll(f => f.SteamId == steamId);
                if (_chatWith == steamId) _chatWith = null;
                ShowToast($"Removed {name}", Icons.Check);
            }
            catch (Exception ex) { ShowToast("Could not remove: " + ex.Message, Icons.Warning); }
            if (_currentView == FriendsView) BuildFriendsView();
            else if (_currentView == ProfileView) _ = OpenProfileAsync(steamId);
        }

        // ══ Profile page ═════════════════════════════════════════════════════

        private string? _profileKey;

        /// <summary>Opens a player's profile. <paramref name="key"/> is a SteamID64 or "legacy:{username}".</summary>
        private async Task OpenProfileAsync(string key, PlayerProfile? known = null)
        {
            _profileKey = key;
            ShowView(ProfileView, "Profile", null);
            ProfileScroll.ScrollToTop();
            ProfileContent.Children.Clear();
            if (known != null) RenderProfile(known, null, loading: true);
            else ProfileContent.Children.Add(UiFactory.Text("Loading profile", 13, "Brush.Fg3"));

            bool legacy = key.StartsWith("legacy:", StringComparison.Ordinal);
            PlayerProfile? profile;
            FriendState? state = null;
            try
            {
                if (legacy)
                {
                    profile = await _community.GetLegacyProfileAsync(key["legacy:".Length..]);
                }
                else
                {
                    var profileTask = _community.GetPlayerAsync(key);
                    var presenceTask = _community.GetPresenceAsync();
                    var rankTask = _leaderboard.Count == 0 ? FetchLeaderboardAsync() : Task.CompletedTask;
                    profile = await profileTask;
                    _presence = await presenceTask;
                    await rankTask;
                    if (_community.IsSignedIn && key != _community.SteamId)
                    {
                        var friendIds = await _community.GetFriendIdsAsync();
                        state = friendIds.Contains(key) ? FriendState.Friends
                            : _requests.Any(r => r.SteamId == key) ? FriendState.Incoming
                            : await _community.HasOutgoingRequestAsync(key) ? FriendState.Outgoing
                            : FriendState.None;
                    }
                }
            }
            catch { profile = known; }

            if (_profileKey != key || _currentView != ProfileView) return;
            if (profile == null && !legacy && key == _community.SteamId)
                profile = new PlayerProfile { SteamId = key, DisplayName = MyDisplayName, SteamName = S.SteamPersona, Bio = S.ProfileBio, MemberSince = S.MemberSince };
            ProfileContent.Children.Clear();
            if (profile == null)
            {
                ProfileContent.Children.Add(UiFactory.EmptyState(Icons.Contact, "Profile not found", "This player hasn't published a profile yet."));
                return;
            }
            if (legacy) profile.IsLegacy = true;
            RenderProfile(profile, state, loading: false);
        }

        private enum FriendState { None, Outgoing, Incoming, Friends }

        private void RenderProfile(PlayerProfile p, FriendState? state, bool loading)
        {
            ProfileContent.Children.Clear();
            bool me = !p.IsLegacy && p.SteamId == _community.SteamId && _community.IsSignedIn;
            var presence = p.IsLegacy ? null : me ? (_community.IsSignedIn ? new Presence { State = AnyGameRunning ? "in-game" : "online", Season = _sessionPatch?.ShortLabel ?? "", LastSeenMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } : null) : PresenceOf(p.SteamId);
            PageTitle.Text = p.Name;

            // Header card
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Avatar(96, p.IsLegacy ? null : PresenceState(presence)));

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 16, 0) };
            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(UiFactory.Text(p.Name, 26, "Brush.Fg", FontWeights.Bold));
            if (me) nameRow.Children.Add(UiFactory.Chip("YOU").Margin(10, 0, 0, 0));
            if (state == FriendState.Friends) nameRow.Children.Add(UiFactory.Chip("FRIEND").Margin(10, 0, 0, 0));
            if (p.IsLegacy) nameRow.Children.Add(UiFactory.Chip("LEGACY PROFILE").Margin(10, 0, 0, 0));
            info.Children.Add(nameRow);

            var lines = new List<string>();
            if (!p.IsLegacy) lines.Add(PresenceText(presence));
            if (!p.IsLegacy && p.SteamName.Length > 0 && p.SteamName != p.Name) lines.Add($"Steam: {p.SteamName}");
            if (p.MemberSince > 0) lines.Add($"Member since {DateTimeOffset.FromUnixTimeMilliseconds(p.MemberSince).LocalDateTime:MMM yyyy}");
            if (p.IsLegacy && p.UpdatedAt > 0) lines.Add($"Last active {DateTimeOffset.FromUnixTimeMilliseconds(p.UpdatedAt).LocalDateTime:MMM d, yyyy}");
            info.Children.Add(UiFactory.Text(string.Join("  ·  ", lines), 13, "Brush.Fg2").Margin(0, 6, 0, 0));
            head.Children.Add(info.Col(1));

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (me)
            {
                actions.Children.Add(UiFactory.Button("Edit profile", "Btn.Secondary", (_, e) => Nav_Click(NavSettings, e), Icons.Edit));
            }
            else if (!p.IsLegacy && _community.IsSignedIn && !loading && state != null)
            {
                string sid = p.SteamId, name = p.Name;
                switch (state)
                {
                    case FriendState.Friends:
                        actions.Children.Add(UiFactory.Button("Message", "Btn.Primary", (_, _) => OpenChat(sid, name), Icons.Chat));
                        actions.Children.Add(UiFactory.Button("Remove", "Btn.Ghost", async (_, _) => await RemoveFriendAsync(sid, name)).Margin(8, 0, 0, 0));
                        break;
                    case FriendState.Incoming:
                        actions.Children.Add(UiFactory.Button("Accept request", "Btn.Primary", async (_, _) => { await SendFriendRequestAsync(sid, name); _ = OpenProfileAsync(sid); }, Icons.AddFriend));
                        break;
                    case FriendState.Outgoing:
                        actions.Children.Add(UiFactory.Button("Cancel request", "Btn.Secondary", async (_, _) =>
                        {
                            try { await _community.CancelFriendRequestAsync(sid); } catch { }
                            _ = OpenProfileAsync(sid);
                        }));
                        break;
                    default:
                        actions.Children.Add(UiFactory.Button("Add friend", "Btn.Primary", async (_, _) => { await SendFriendRequestAsync(sid, name); _ = OpenProfileAsync(sid); }, Icons.AddFriend));
                        break;
                }
            }
            if (!p.IsLegacy && p.SteamId.Length > 0)
                actions.Children.Add(UiFactory.IconButton(Icons.OpenExternal, "Steam profile", (_, _) => OpenUrl(SteamAuthService.ProfileUrl(p.SteamId))).Margin(8, 0, 0, 0));
            head.Children.Add(actions.Col(2));
            ProfileContent.Children.Add(UiFactory.Card(head, 28).Margin(0, 0, 0, 16));

            // Stats
            int rank = LeaderboardRank(p);
            var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -14, 2) };
            tiles.Children.Add(StatTile("Playtime", p.TotalPlaytimeSec > 0 ? PlaytimeService.Format(p.TotalPlaytimeSec) : "None yet"));
            tiles.Children.Add(StatTile("Sessions", p.SessionCount.ToString("N0")));
            tiles.Children.Add(StatTile("Seasons installed", p.Installed.Count.ToString()));
            tiles.Children.Add(StatTile("Leaderboard", rank > 0 ? $"#{rank}" : "Unranked"));
            ProfileContent.Children.Add(tiles);

            // About and seasons
            var cols = new Grid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var about = new StackPanel();
            about.Children.Add(UiFactory.Text("About", 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 10));
            about.Children.Add(UiFactory.Text(string.IsNullOrWhiteSpace(p.Bio) ? (me ? "You haven't written a bio yet. Add one in Settings." : "No bio yet.") : p.Bio,
                13, string.IsNullOrWhiteSpace(p.Bio) ? "Brush.Fg3" : "Brush.Fg2", wrap: true));
            if (p.FavouriteSeason.Length > 0)
            {
                about.Children.Add(UiFactory.Text("Most played", 12, "Brush.Fg3").Margin(0, 16, 0, 2));
                about.Children.Add(UiFactory.Text(p.FavouriteSeason, 14, "Brush.Fg", FontWeights.SemiBold));
            }
            if (p.IsLegacy)
                about.Children.Add(UiFactory.Text("This profile was made before Steam sign-in. Its owner can link it to their Steam account by signing in through Steam in Settings; their stats carry over.", 12, "Brush.Fg3", wrap: true).Margin(0, 16, 0, 0));
            cols.Children.Add(UiFactory.Card(about));

            var seasons = new StackPanel();
            seasons.Children.Add(UiFactory.Text("Installed seasons", 15, "Brush.Fg", FontWeights.SemiBold).Margin(0, 0, 0, 10));
            if (p.Installed.Count == 0) seasons.Children.Add(UiFactory.Text("None shared.", 13, "Brush.Fg3"));
            var wrap = new WrapPanel();
            foreach (var s in p.Installed.OrderBy(s => s))
                wrap.Children.Add(UiFactory.Chip(s, "Brush.Surface3", "Brush.Fg").Margin(0, 0, 6, 6));
            seasons.Children.Add(wrap);
            cols.Children.Add(UiFactory.Card(seasons).Col(2));
            ProfileContent.Children.Add(cols);
        }

        private int LeaderboardRank(PlayerProfile p)
        {
            if (_leaderboard.Count == 0 || p.TotalPlaytimeSec <= 0) return 0;
            int index = _leaderboard.OrderByDescending(x => x.TotalPlaytimeSec).ToList().FindIndex(x => x.Key == p.Key);
            return index < 0 ? 0 : index + 1;
        }

        // ══ Leaderboard ══════════════════════════════════════════════════════

        private string _leaderboardSort = "playtime";
        private string _leaderboardQuery = "";
        private int _leaderboardShown = 100;
        private List<PlayerProfile> _leaderboard = new();
        private bool _leaderboardLoading;

        private async Task LoadLeaderboardAsync()
        {
            _leaderboardLoading = true;
            RenderLeaderboard();
            await FetchLeaderboardAsync();
            _leaderboardLoading = false;
            if (_currentView == LeaderboardView) RenderLeaderboard();
        }

        /// <summary>Steam-linked players plus pre-3.0 profiles nobody has linked yet.</summary>
        private async Task FetchLeaderboardAsync()
        {
            try
            {
                var playersTask = _community.GetPlayersAsync();
                var legacyTask = _community.GetLegacyProfilesAsync();
                var claimsTask = _community.GetLegacyClaimsAsync();
                var presenceTask = _community.GetPresenceAsync();
                var players = await playersTask;
                var claimed = new HashSet<string>((await claimsTask).Keys, StringComparer.OrdinalIgnoreCase);
                foreach (var p in players.Where(p => p.LegacyUsername.Length > 0)) claimed.Add(p.LegacyUsername);
                _leaderboard = players.Concat((await legacyTask).Where(l => l.LegacyUsername.Length > 0 && !claimed.Contains(l.LegacyUsername))).ToList();
                _presence = await presenceTask;
            }
            catch { }
        }

        private void RenderLeaderboard()
        {
            LeaderboardContent.Children.Clear();
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (key, label) in new[] { ("playtime", "Playtime"), ("sessions", "Sessions"), ("versions", "Seasons installed") })
            {
                var rb = new RadioButton { Content = label, GroupName = "Lb", IsChecked = _leaderboardSort == key };
                rb.Styled("Segment");
                string k = key;
                rb.Click += (_, _) => { _leaderboardSort = k; _leaderboardShown = 100; RenderLeaderboard(); };
                seg.Children.Add(rb);
            }
            var segHost = new Border { Child = seg };
            segHost.Styled("SegmentHost");
            var search = new Grid { Width = 200 };
            var searchBox = UiFactory.Input(_leaderboardQuery, "Find a player");
            searchBox.Padding = new Thickness(32, 0, 10, 0);
            search.Children.Add(searchBox);
            var glyph = UiFactory.Icon(Icons.Search, 12, "Brush.Fg3");
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            glyph.Margin = new Thickness(11, 0, 0, 0);
            glyph.IsHitTestVisible = false;
            search.Children.Add(glyph);
            var right = UiFactory.HStack(8, search, segHost, UiFactory.IconButton(Icons.Refresh, "Refresh", (_, _) => _ = LoadLeaderboardAsync()));
            int legacyCount = _leaderboard.Count(p => p.IsLegacy);
            string subtitle = _leaderboardLoading && _leaderboard.Count == 0 ? "Loading"
                : $"{_leaderboard.Count - legacyCount:N0} Steam-linked players  ·  {legacyCount:N0} legacy profiles waiting to be linked";
            LeaderboardContent.Children.Add(PageHeader("Leaderboard", subtitle, right));

            var list = new StackPanel();
            LeaderboardContent.Children.Add(list);
            void Fill()
            {
                list.Children.Clear();
                if (_leaderboard.Count == 0)
                {
                    if (!_leaderboardLoading)
                        list.Children.Add(UiFactory.EmptyState(Icons.Trophy, "No rankings yet", "Players appear here after they sign in through Steam."));
                    return;
                }
                RenderLeaderboardRows(list);
            }
            var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            debounce.Tick += (_, _) => { debounce.Stop(); _leaderboardQuery = searchBox.Text.Trim(); _leaderboardShown = 100; Fill(); };
            searchBox.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
            Fill();
        }

        private static long SortValue(PlayerProfile p, string sort) => sort switch
        {
            "sessions" => p.SessionCount,
            "versions" => p.Installed.Count,
            _          => p.TotalPlaytimeSec,
        };

        private void RenderLeaderboardRows(StackPanel list)
        {
            var ranked = _leaderboard.OrderByDescending(p => SortValue(p, _leaderboardSort)).ThenBy(p => p.Name).ToList();
            double max = Math.Max(1, ranked.Count == 0 ? 1 : SortValue(ranked[0], _leaderboardSort));
            var rows = ranked.Select((p, i) => (p, rank: i + 1))
                .Where(x => _leaderboardQuery.Length == 0 || x.p.Name.Contains(_leaderboardQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rows.Count == 0)
            {
                list.Children.Add(UiFactory.Text("No players match that name.", 13, "Brush.Fg3").Margin(4, 8, 0, 0));
                return;
            }

            foreach (var (p, rank) in rows.Take(_leaderboardShown))
            {
                bool me = !p.IsLegacy && _community.IsSignedIn && p.SteamId == _community.SteamId;
                var presence = p.IsLegacy ? null : PresenceOf(p.SteamId);
                string valueText = _leaderboardSort switch
                {
                    "sessions" => $"{p.SessionCount:N0} sessions",
                    "versions" => $"{p.Installed.Count} seasons",
                    _          => PlaytimeService.Format(p.TotalPlaytimeSec),
                };

                var row = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand };
                row.SetResourceReference(Border.BorderBrushProperty, me ? "Brush.StrokeStrong" : "Brush.Stroke");
                UiFactory.Hover(row, me ? "Brush.Surface2" : "Brush.Surface", "Brush.Surface2");
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                g.Children.Add(UiFactory.Text($"{rank}", rank <= 3 ? 18 : 14, rank <= 3 ? "Brush.Fg" : "Brush.Fg3", FontWeights.Bold));
                g.Children.Add(Avatar(34, p.IsLegacy ? null : PresenceState(presence)).Col(1));

                var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
                var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
                var nameText = UiFactory.Text(me ? $"{p.Name} (you)" : p.Name, 14, "Brush.Fg", FontWeights.SemiBold);
                nameText.ToolTip = "View profile";
                nameLine.Children.Add(nameText);
                if (p.IsLegacy) nameLine.Children.Add(UiFactory.Chip("LEGACY").Margin(8, 0, 0, 0));
                nameCol.Children.Add(nameLine);
                string sub = p.IsLegacy ? "Not linked to Steam yet" : PresenceText(presence);
                if (p.FavouriteSeason.Length > 0) sub += $"  ·  Most played {p.FavouriteSeason}";
                nameCol.Children.Add(UiFactory.Text(sub, 12, "Brush.Fg3").Margin(0, 2, 0, 0));
                g.Children.Add(nameCol.Col(2));

                var bar = new ProgressBar { Maximum = max, Value = SortValue(p, _leaderboardSort), Height = 4, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
                bar.Styled("Progress.Plain");
                g.Children.Add(bar.Col(3));
                var v = UiFactory.Text(valueText, 14, "Brush.Fg", FontWeights.SemiBold);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                g.Children.Add(v.Col(4));
                row.Child = g;

                row.MouseEnter += (_, _) => nameText.TextDecorations = TextDecorations.Underline;
                row.MouseLeave += (_, _) => nameText.TextDecorations = null;
                var player = p;
                row.MouseLeftButtonUp += (_, _) => _ = OpenProfileAsync(player.Key, player);
                UiFactory.AutomationName(row, $"Rank {rank}, {p.Name}, {valueText}");
                list.Children.Add(row);
            }

            if (rows.Count > _leaderboardShown)
            {
                var more = UiFactory.Button($"Show all {rows.Count:N0}", "Btn.Secondary", (_, _) => { _leaderboardShown = int.MaxValue; list.Children.Clear(); RenderLeaderboardRows(list); });
                more.HorizontalAlignment = HorizontalAlignment.Center;
                more.Margin = new Thickness(0, 10, 0, 0);
                list.Children.Add(more);
            }
        }
    }
}
