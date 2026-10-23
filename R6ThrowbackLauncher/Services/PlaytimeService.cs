using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace R6ThrowbackLauncher.Services
{
    public sealed class PlaytimeEntry
    {
        [JsonPropertyName("totalSeconds")]
        public long TotalSeconds { get; set; } = 0;

        [JsonPropertyName("lastPlayed")]
        public long LastPlayed { get; set; } = 0;

        [JsonPropertyName("sessionCount")]
        public int SessionCount { get; set; } = 0;
    }

    public sealed class PlaytimeData
    {
        [JsonPropertyName("perVersion")]
        public Dictionary<string, PlaytimeEntry> PerVersion { get; set; } = new();

        [JsonPropertyName("totalSeconds")]
        public long TotalSeconds { get; set; } = 0;

        [JsonPropertyName("firstLaunch")]
        public long FirstLaunch { get; set; } = 0;
    }

    public sealed class PlaytimeService
    {
        private readonly string _filePath;
        private PlaytimeData   _data = new();
        private string?        _activeSessionId;
        private DateTime       _sessionStart;

        public PlaytimeService(string dataDir)
        {
            _filePath = Path.Combine(dataDir, "playtime.json");
            Load();
        }

        // -- session management ------------------------------------------------

        public void StartSession(string patchId)
        {
            _activeSessionId = patchId;
            _sessionStart    = DateTime.UtcNow;

            if (_data.FirstLaunch == 0)
            {
                _data.FirstLaunch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Save();
            }
        }

        public void EndSession()
        {
            if (_activeSessionId == null) return;

            long elapsed = (long)(DateTime.UtcNow - _sessionStart).TotalSeconds;
            if (elapsed < 5) { _activeSessionId = null; return; }

            if (!_data.PerVersion.TryGetValue(_activeSessionId, out var entry))
            {
                entry = new PlaytimeEntry();
                _data.PerVersion[_activeSessionId] = entry;
            }

            entry.TotalSeconds += elapsed;
            entry.LastPlayed    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            entry.SessionCount += 1;
            _data.TotalSeconds += elapsed;

            _activeSessionId = null;
            Save();
        }

        // -- queries -----------------------------------------------------------

        public long GetSecondsForPatch(string patchId) =>
            _data.PerVersion.TryGetValue(patchId, out var e) ? e.TotalSeconds : 0;

        public long GetLastPlayedForPatch(string patchId) =>
            _data.PerVersion.TryGetValue(patchId, out var e) ? e.LastPlayed : 0;

        public long GetTotalSeconds() => _data.TotalSeconds;

        public long GetFirstLaunch() => _data.FirstLaunch;

        public int GetTotalSessionCount() => _data.PerVersion.Values.Sum(e => e.SessionCount);

        public int GetSessionCountForPatch(string patchId) =>
            _data.PerVersion.TryGetValue(patchId, out var e) ? e.SessionCount : 0;

        /// <summary>Returns all patch entries sorted by total playtime descending.</summary>
        public IReadOnlyList<(string PatchId, PlaytimeEntry Entry)> GetAllEntriesSorted() =>
            _data.PerVersion
                .OrderByDescending(kv => kv.Value.TotalSeconds)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        /// <summary>Returns the patch IDs sorted by most recently played.</summary>
        public IReadOnlyList<string> GetRecentlyPlayed(int count = 5) =>
            _data.PerVersion
                .Where(kv => kv.Value.LastPlayed > 0)
                .OrderByDescending(kv => kv.Value.LastPlayed)
                .Take(count)
                .Select(kv => kv.Key)
                .ToList();

        public static string Format(long seconds)
        {
            if (seconds < 60) return "< 1 min";
            long days  = seconds / 86400;
            long hours = (seconds % 86400) / 3600;
            long mins  = (seconds % 3600) / 60;

            if (days > 0)  return $"{days}d {hours}h {mins}m";
            if (hours > 0) return $"{hours}h {mins}m";
            return $"{mins}m";
        }

        public static string FormatRelative(long unixTimestamp)
        {
            if (unixTimestamp == 0) return "Never";
            var dt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            var ago = DateTimeOffset.UtcNow - dt;
            if (ago.TotalMinutes < 1)  return "Just now";
            if (ago.TotalHours < 1)    return $"{(int)ago.TotalMinutes}m ago";
            if (ago.TotalDays < 1)     return $"{(int)ago.TotalHours}h ago";
            if (ago.TotalDays < 30)    return $"{(int)ago.TotalDays}d ago";
            return dt.LocalDateTime.ToString("MMM yyyy");
        }

        // -- persistence -------------------------------------------------------

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                    _data = JsonSerializer.Deserialize<PlaytimeData>(File.ReadAllText(_filePath)) ?? new();
            }
            catch { _data = new(); }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
