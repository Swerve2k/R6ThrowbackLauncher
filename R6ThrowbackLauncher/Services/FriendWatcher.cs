using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>Polls friends' presence and chats and raises events for changes since the last poll.</summary>
    public sealed class FriendWatcher : IDisposable
    {
        private readonly CommunityService _community;

        private CancellationTokenSource? _cts;
        private readonly Dictionary<string, string> _lastStates = new();
        private readonly Dictionary<string, long> _lastMessageTs = new();
        private bool _disposed;

        public const int PollIntervalMs = 30_000;

        public event Action<string>? FriendCameOnline;
        public event Action<string>? FriendWentInGame;
        public event Action<string, string, string>? NewMessageReceived; // (steamId, name, preview)

        public FriendWatcher(CommunityService community)
        {
            _community = community;
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = PollLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            bool baseline = true;
            while (!ct.IsCancellationRequested)
            {
                try { await PollAsync(notify: !baseline); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FriendWatcher] {ex.Message}"); }
                baseline = false;
                try { await Task.Delay(PollIntervalMs, ct); } catch { break; }
            }
        }

        private async Task PollAsync(bool notify)
        {
            if (!_community.IsSignedIn) return;
            var friends = await _community.GetFriendsAsync();
            foreach (var f in friends)
            {
                string state = f.Presence is { IsOnline: true } p ? p.State : "offline";
                _lastStates.TryGetValue(f.SteamId, out string? old);
                if (notify && old != null && old != state)
                {
                    if (state == "in-game") FriendWentInGame?.Invoke(f.Name);
                    else if (old == "offline") FriendCameOnline?.Invoke(f.Name);
                }
                _lastStates[f.SteamId] = state;

                var last = await _community.GetLastMessageAsync(f.SteamId);
                long ts = last?.Timestamp ?? 0;
                bool seen = _lastMessageTs.TryGetValue(f.SteamId, out long known);
                if (notify && seen && ts > known && last!.From != _community.SteamId)
                    NewMessageReceived?.Invoke(f.SteamId, f.Name, last.Text);
                _lastMessageTs[f.SteamId] = Math.Max(known, ts);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
