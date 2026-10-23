using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>An announcement published in <c>broadcast.json</c> on the repository's main branch.</summary>
    public sealed class Broadcast
    {
        public string Id { get; init; } = "";
        public string Type { get; init; } = "info";        // info | update | warning
        public string Title { get; init; } = "";
        public string Message { get; init; } = "";
        public string ButtonText { get; init; } = "";
        public string Url { get; init; } = "";
        public string MinVersion { get; init; } = "";
        public string MaxVersion { get; init; } = "";
        public DateTime? Expires { get; init; }
        public bool ShowEveryLaunch { get; init; }
    }

    public static class BroadcastService
    {
        public const string FileUrl =
            "https://raw.githubusercontent.com/" + AppInfo.GitHubOwner + "/" + AppInfo.GitHubRepo + "/main/broadcast.json";

        /// <summary>Downloads the announcement list. Returns an empty list when the file is missing or invalid.</summary>
        public static async Task<List<Broadcast>> FetchAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"R6ThrowbackLauncher/{AppInfo.Version}");
                using var resp = await http.GetAsync($"{FileUrl}?t={DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute}");
                if (!resp.IsSuccessStatusCode) return new();
                return Parse(await resp.Content.ReadAsStringAsync());
            }
            catch { return new(); }
        }

        /// <summary>Accepts <c>{ "messages": [...] }</c>, a bare array, or a single message object.</summary>
        public static List<Broadcast> Parse(string json)
        {
            var list = new List<Broadcast>();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            IEnumerable<JsonElement> items = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray(),
                JsonValueKind.Object when root.TryGetProperty("messages", out var m) && m.ValueKind == JsonValueKind.Array => m.EnumerateArray(),
                JsonValueKind.Object => new[] { root },
                _ => Array.Empty<JsonElement>(),
            };
            foreach (var e in items)
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var b = new Broadcast
                {
                    Id = Str("id"),
                    Type = Str("type").ToLowerInvariant() is "update" or "warning" ? Str("type").ToLowerInvariant() : "info",
                    Title = Str("title"),
                    Message = Str("message"),
                    ButtonText = Str("buttonText"),
                    Url = Str("url"),
                    MinVersion = Str("minVersion"),
                    MaxVersion = Str("maxVersion"),
                    Expires = DateTime.TryParse(Str("expires"), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var exp) ? exp : null,
                    ShowEveryLaunch = e.TryGetProperty("showEveryLaunch", out var every) && every.ValueKind == JsonValueKind.True,
                };
                if (b.Id.Length > 0 && (b.Title.Length > 0 || b.Message.Length > 0)) list.Add(b);
            }
            return list;
        }

        public static bool Applies(Broadcast b, string version, DateTime utcNow)
        {
            if (b.Expires is DateTime exp && utcNow > exp) return false;
            if (b.MinVersion.Length > 0 && AppInfo.IsNewer(b.MinVersion, version)) return false;
            if (b.MaxVersion.Length > 0 && AppInfo.IsNewer(version, b.MaxVersion)) return false;
            return true;
        }

        /// <summary>Only https links are opened from announcements.</summary>
        public static bool IsSafeUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
    }
}
