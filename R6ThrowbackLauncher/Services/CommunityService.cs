using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    public sealed class PlayerProfile
    {
        public string SteamId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SteamName { get; set; } = "";
        public string Bio { get; set; } = "";
        public string FavouriteSeason { get; set; } = "";
        public long TotalPlaytimeSec { get; set; }
        public int SessionCount { get; set; }
        public List<string> Installed { get; set; } = new();
        public long MemberSince { get; set; }
        public long UpdatedAt { get; set; }
        public string LegacyUsername { get; set; } = "";
        public bool IsLegacy { get; set; }

        public string Key => IsLegacy ? "legacy:" + LegacyUsername : SteamId;
        public string Name => !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
            : !string.IsNullOrWhiteSpace(SteamName) ? SteamName
            : IsLegacy ? MaskLogin(LegacyUsername) : "Player";

        /// <summary>
        /// Pre-3.0 profiles are named after the Steam login name people typed in. Only part of it is shown,
        /// enough for the owner to recognise, since a login name is half of a Steam sign-in.
        /// </summary>
        public static string MaskLogin(string login)
        {
            string s = login.Trim();
            if (s.Length == 0) return "Player";
            if (s.Length <= 3) return s[..1] + "•••";
            int keep = s.Length >= 8 ? 3 : 2;
            return s[..keep] + "•••" + s[^1..];
        }
    }

    public sealed class Presence
    {
        public string State { get; set; } = "online";
        public string Season { get; set; } = "";
        public long LastSeenMs { get; set; }

        public bool IsOnline => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - LastSeenMs < CommunityService.OnlineWindowMs;
        public bool InGame => IsOnline && State == "in-game";
    }

    public sealed record FriendInfo(string SteamId, string Name, Presence? Presence)
    {
        public bool Online => Presence?.IsOnline == true;
    }

    public sealed record FriendRequestInfo(string SteamId, string Name, long Timestamp);

    public sealed record ChatMessage(string From, string Text, long Timestamp);

    /// <summary>
    /// Community data in the Firebase Realtime Database, keyed by verified SteamID64.
    /// Everything lives under <c>community/</c>; the pre-3.0 username-keyed nodes are only read for migration.
    /// </summary>
    public sealed class CommunityService : IDisposable
    {
        public const string DbUrl = "https://operationthrowback-47d1e-default-rtdb.firebaseio.com";
        public const long OnlineWindowMs = 150_000;

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

        public string SteamId { get; private set; } = "";
        public string DisplayName { get; private set; } = "";
        public bool IsSignedIn => SteamId.Length > 0;

        public void SetIdentity(string steamId, string displayName)
        {
            SteamId = steamId ?? "";
            DisplayName = displayName ?? "";
        }

        // ── Presence ─────────────────────────────────────────────────────────

        public Task HeartbeatAsync(string state, string season) => !IsSignedIn ? Task.CompletedTask :
            PutAsync($"community/presence/{SteamId}", new JsonObject
            {
                ["state"] = state,
                ["season"] = season,
                ["name"] = DisplayName,
                ["lastSeen"] = new JsonObject { [".sv"] = "timestamp" },
            });

        public Task SetOfflineAsync() => !IsSignedIn ? Task.CompletedTask : DeleteAsync($"community/presence/{SteamId}");

        public async Task<Dictionary<string, Presence>> GetPresenceAsync()
        {
            var result = new Dictionary<string, Presence>();
            var node = await GetAsync("community/presence") as JsonObject;
            if (node == null) return result;
            foreach (var (sid, value) in node)
                if (value is JsonObject o) result[sid] = ParsePresence(o);
            return result;
        }

        public async Task<int> GetOnlineCountAsync() => (await GetPresenceAsync()).Values.Count(p => p.IsOnline);

        private static Presence ParsePresence(JsonObject o) => new()
        {
            State = Str(o, "state", "online"),
            Season = Str(o, "season"),
            LastSeenMs = Long(o, "lastSeen"),
        };

        // ── Players ──────────────────────────────────────────────────────────

        public Task PublishProfileAsync(PlayerProfile p)
        {
            if (!IsSignedIn) return Task.CompletedTask;
            return PatchAsync($"community/players/{SteamId}", new JsonObject
            {
                ["displayName"] = p.DisplayName,
                ["steamName"] = p.SteamName,
                ["bio"] = p.Bio,
                ["favouriteSeason"] = p.FavouriteSeason,
                ["totalPlaytimeSec"] = p.TotalPlaytimeSec,
                ["sessionCount"] = p.SessionCount,
                ["installed"] = new JsonArray(p.Installed.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                ["memberSince"] = p.MemberSince,
                ["legacyUsername"] = p.LegacyUsername,
                ["updatedAt"] = new JsonObject { [".sv"] = "timestamp" },
            });
        }

        public async Task<PlayerProfile?> GetPlayerAsync(string steamId)
        {
            return await GetAsync($"community/players/{steamId}") is JsonObject o ? ParsePlayer(steamId, o) : null;
        }

        public async Task<List<PlayerProfile>> GetPlayersAsync()
        {
            var list = new List<PlayerProfile>();
            if (await GetAsync("community/players") is JsonObject node)
                foreach (var (sid, value) in node)
                    if (value is JsonObject o) list.Add(ParsePlayer(sid, o));
            return list;
        }

        private static PlayerProfile ParsePlayer(string sid, JsonObject o) => new()
        {
            SteamId = sid,
            DisplayName = Str(o, "displayName"),
            SteamName = Str(o, "steamName"),
            Bio = Str(o, "bio"),
            FavouriteSeason = Str(o, "favouriteSeason"),
            TotalPlaytimeSec = Long(o, "totalPlaytimeSec"),
            SessionCount = (int)Long(o, "sessionCount"),
            Installed = o["installed"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() : new(),
            MemberSince = Long(o, "memberSince"),
            UpdatedAt = Long(o, "updatedAt"),
            LegacyUsername = Str(o, "legacyUsername"),
        };

        // ── Legacy (pre-3.0) profiles ────────────────────────────────────────

        /// <summary>Image-free snapshot of the old username-keyed profiles.</summary>
        public async Task<List<PlayerProfile>> GetLegacyProfilesAsync()
        {
            var list = new List<PlayerProfile>();
            if (await GetAsync("community/legacy") is JsonObject node)
                foreach (var (_, value) in node)
                    if (value is JsonObject o) list.Add(ParseLegacy(o));
            return list;
        }

        /// <summary>
        /// The pre-3.0 profile for a username. Older launchers can still update the live node after the
        /// snapshot was taken, so the copy with more playtime wins. Images are never requested.
        /// </summary>
        public async Task<PlayerProfile?> GetLegacyProfileAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            string enc = Encode(username);
            var snapTask = GetAsync($"community/legacy/{enc}");
            var liveTask = Task.WhenAll(
                GetAsync($"userProfiles/{enc}/totalPlaytimeSec"),
                GetAsync($"userProfiles/{enc}/sessionCount"),
                GetAsync($"userProfiles/{enc}/favouriteSeason"),
                GetAsync($"userProfiles/{enc}/bio"),
                GetAsync($"userProfiles/{enc}/installedVersions"));
            var snap = await snapTask as JsonObject;
            var live = await liveTask;

            PlayerProfile? result = snap != null ? ParseLegacy(snap) : null;
            if (live.All(n => n == null)) return result;
            long livePlaytime = AsLong(live[0]);
            if (result == null || livePlaytime > result.TotalPlaytimeSec)
            {
                var o = new JsonObject
                {
                    ["username"] = username,
                    ["totalPlaytimeSec"] = livePlaytime,
                    ["sessionCount"] = live[1]?.DeepClone(),
                    ["favouriteSeason"] = live[2]?.DeepClone(),
                    ["bio"] = live[3]?.DeepClone(),
                    ["installedVersions"] = live[4]?.DeepClone(),
                };
                result = ParseLegacy(o);
            }
            return result;
        }

        private static PlayerProfile ParseLegacy(JsonObject o) => new()
        {
            IsLegacy = true,
            LegacyUsername = Str(o, "username"),
            Bio = Str(o, "bio"),
            FavouriteSeason = Str(o, "favouriteSeason"),
            TotalPlaytimeSec = Long(o, "totalPlaytimeSec"),
            SessionCount = (int)Long(o, "sessionCount"),
            Installed = Str(o, "installedVersions").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            UpdatedAt = Long(o, "lastUpdated") * 1000,
        };

        public async Task<Dictionary<string, string>> GetLegacyClaimsAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (await GetAsync("community/legacyClaims") is JsonObject node)
                foreach (var (enc, value) in node)
                    map[Decode(enc)] = value?.ToString() ?? "";
            return map;
        }

        public async Task<string?> GetLegacyClaimAsync(string username) =>
            (await GetAsync($"community/legacyClaims/{Encode(username)}"))?.ToString();

        public Task ClaimLegacyAsync(string username) =>
            PutAsync($"community/legacyClaims/{Encode(username)}", JsonValue.Create(SteamId)!);

        /// <summary>Old friend usernames of a legacy account.</summary>
        public async Task<List<string>> GetLegacyFriendsAsync(string username)
        {
            if (await GetAsync($"friends/{Encode(username)}") is JsonObject node)
                return node.Select(kv => Decode(kv.Key)).ToList();
            return new();
        }

        /// <summary>Remembers an old friendship until the other person links their Steam account.</summary>
        public Task AddPendingFriendAsync(string theirLegacyUsername) =>
            PutAsync($"community/pendingFriends/{Encode(theirLegacyUsername)}/{SteamId}", new JsonObject
            {
                ["name"] = DisplayName,
                ["ts"] = new JsonObject { [".sv"] = "timestamp" },
            });

        public async Task<List<string>> TakePendingFriendsAsync(string myLegacyUsername)
        {
            string path = $"community/pendingFriends/{Encode(myLegacyUsername)}";
            var ids = (await GetAsync(path) as JsonObject)?.Select(kv => kv.Key).ToList() ?? new();
            if (ids.Count > 0) await DeleteAsync(path);
            return ids;
        }

        // ── Friends ──────────────────────────────────────────────────────────

        public async Task<List<string>> GetFriendIdsAsync(string? of = null)
        {
            string who = of ?? SteamId;
            if (who.Length == 0) return new();
            return (await GetAsync($"community/friends/{who}") as JsonObject)?.Select(kv => kv.Key).ToList() ?? new();
        }

        public async Task<List<FriendInfo>> GetFriendsAsync()
        {
            var ids = await GetFriendIdsAsync();
            if (ids.Count == 0) return new();
            var presence = await GetPresenceAsync();
            var players = await Task.WhenAll(ids.Select(GetPlayerAsync));
            var result = new List<FriendInfo>();
            for (int i = 0; i < ids.Count; i++)
            {
                presence.TryGetValue(ids[i], out var p);
                result.Add(new FriendInfo(ids[i], players[i]?.Name ?? ids[i], p));
            }
            return result;
        }

        public Task AddFriendshipAsync(string other) => PatchAsync("", new JsonObject
        {
            [$"community/friends/{SteamId}/{other}"] = new JsonObject { ["since"] = new JsonObject { [".sv"] = "timestamp" } },
            [$"community/friends/{other}/{SteamId}"] = new JsonObject { ["since"] = new JsonObject { [".sv"] = "timestamp" } },
        });

        public Task SendFriendRequestAsync(string to) => PutAsync($"community/requests/{to}/{SteamId}", new JsonObject
        {
            ["name"] = DisplayName,
            ["ts"] = new JsonObject { [".sv"] = "timestamp" },
        });

        public async Task AcceptFriendRequestAsync(string from)
        {
            await AddFriendshipAsync(from);
            await DeleteAsync($"community/requests/{SteamId}/{from}");
        }

        public Task DeclineFriendRequestAsync(string from) => DeleteAsync($"community/requests/{SteamId}/{from}");

        public Task CancelFriendRequestAsync(string to) => DeleteAsync($"community/requests/{to}/{SteamId}");

        public Task RemoveFriendAsync(string other) => PatchAsync("", new JsonObject
        {
            [$"community/friends/{SteamId}/{other}"] = null,
            [$"community/friends/{other}/{SteamId}"] = null,
        });

        public async Task<List<FriendRequestInfo>> GetIncomingRequestsAsync()
        {
            var list = new List<FriendRequestInfo>();
            if (IsSignedIn && await GetAsync($"community/requests/{SteamId}") is JsonObject node)
                foreach (var (sid, value) in node)
                    if (value is JsonObject o) list.Add(new FriendRequestInfo(sid, Str(o, "name", sid), Long(o, "ts")));
            return list.OrderByDescending(r => r.Timestamp).ToList();
        }

        public async Task<bool> HasOutgoingRequestAsync(string to) =>
            await GetAsync($"community/requests/{to}/{SteamId}") != null;

        // ── Chat ─────────────────────────────────────────────────────────────

        private string ChatPath(string other) =>
            string.CompareOrdinal(SteamId, other) < 0 ? $"community/chats/{SteamId}_{other}" : $"community/chats/{other}_{SteamId}";

        public Task SendMessageAsync(string to, string text)
        {
            if (!IsSignedIn || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string trimmed = text.Trim();
            if (trimmed.Length > 1000) trimmed = trimmed[..1000];
            return PutAsync($"{ChatPath(to)}/{ts}_{SteamId[^4..]}", new JsonObject
            {
                ["from"] = SteamId,
                ["text"] = trimmed,
                ["ts"] = ts,
            });
        }

        public async Task<List<ChatMessage>> GetMessagesAsync(string with, int limit = 80)
        {
            var list = new List<ChatMessage>();
            if (!IsSignedIn) return list;
            // Keys start with the send time, so ordering by key returns the newest messages.
            if (await GetAsync(ChatPath(with), $"orderBy=%22%24key%22&limitToLast={limit}") is JsonObject node)
                foreach (var (_, value) in node)
                    if (value is JsonObject o) list.Add(new ChatMessage(Str(o, "from"), Str(o, "text"), Long(o, "ts")));
            list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return list;
        }

        public async Task<ChatMessage?> GetLastMessageAsync(string with) =>
            (await GetMessagesAsync(with, 1)).LastOrDefault();

        // ── REST helpers ─────────────────────────────────────────────────────

        private async Task<JsonNode?> GetAsync(string path, string? query = null)
        {
            try
            {
                using var resp = await _http.GetAsync($"{DbUrl}/{path}.json" + (query == null ? "" : "?" + query));
                if (!resp.IsSuccessStatusCode) return null;
                string body = await resp.Content.ReadAsStringAsync();
                return body == "null" ? null : JsonNode.Parse(body);
            }
            catch { return null; }
        }

        private Task PutAsync(string path, JsonNode data) => SendAsync(HttpMethod.Put, path, data);

        private Task PatchAsync(string path, JsonNode data) => SendAsync(HttpMethod.Patch, path, data);

        private async Task DeleteAsync(string path)
        {
            using var resp = await _http.DeleteAsync($"{DbUrl}/{path}.json");
            resp.EnsureSuccessStatusCode();
        }

        private async Task SendAsync(HttpMethod method, string path, JsonNode data)
        {
            string url = path.Length == 0 ? $"{DbUrl}/.json" : $"{DbUrl}/{path}.json";
            using var req = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(data.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        private static string Str(JsonObject o, string key, string fallback = "") =>
            o[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? fallback : o[key]?.ToString() ?? fallback;

        private static long Long(JsonObject o, string key) => AsLong(o[key]);

        private static long AsLong(JsonNode? node)
        {
            if (node is not JsonValue v) return 0;
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out double d)) return (long)d;
            return long.TryParse(v.ToString(), out long p) ? p : 0;
        }

        // Firebase keys can't contain . # $ [ ] /; matches the pre-3.0 encoding so old usernames resolve.
        public static string Encode(string s) =>
            s.Replace(".", "_dot_").Replace("#", "_hash_").Replace("$", "_dollar_")
             .Replace("[", "_lb_").Replace("]", "_rb_").Replace("/", "_sl_");

        public static string Decode(string s) =>
            s.Replace("_dot_", ".").Replace("_hash_", "#").Replace("_dollar_", "$")
             .Replace("_lb_", "[").Replace("_rb_", "]").Replace("_sl_", "/");

        public void Dispose() => _http.Dispose();
    }
}
