using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>
    /// Connects to the local Discord client via its named-pipe IPC gateway
    /// and updates Rich Presence without any native DLL dependency.
    /// </summary>
    public sealed class DiscordRpcService : IDisposable
    {
        // ── constants ───────────────────────────────────────────────────────
        private const string AppId       = "1487291459374678046";
        private const int    HandshakeOp = 0;
        private const int    FrameOp     = 1;
        private const int    CloseOp     = 2;

        // ── state ────────────────────────────────────────────────────────────
        private NamedPipeClientStream? _pipe;
        private int  _nonce          = 1;
        private bool _connected       = false;
        private bool _disposed        = false;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private string  _currentTab     = "Library";
        private DateTimeOffset _launcherStart = DateTimeOffset.UtcNow;
        private DateTimeOffset? _gameStart    = null;

        // In-game state (map / gamemode / operator)
        private string? _mapName;
        private string? _gamemodeName;
        private string? _operatorName;

        // Season info stored so game state updates can refresh presence
        private string  _seasonName   = "";
        private string  _yearSeason   = "";
        private string  _patchKey     = "";
        private string  _username     = "";

        // Event playlist picked on the Modifications page (Outbreak, Rainbow is Magic and so on)
        private string? _eventName;
        private string? _modeName;
        private bool    _unlockAll;

        // ── public API ───────────────────────────────────────────────────────

        public async Task ConnectAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_connected) return;

                for (int i = 0; i < 10; i++)
                {
                    string pipeName = $"discord-ipc-{i}";
                    try
                    {
                        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await pipe.ConnectAsync(2000);
                        _pipe      = pipe;
                        _connected = true;

                        // Handshake
                        await WriteAsync(HandshakeOp, JsonSerializer.Serialize(new { v = 1, client_id = AppId }));
                        await ReadFrameAsync(); // consume READY response
                        return;
                    }
                    catch { }
                }
            }
            finally { _lock.Release(); }
        }

        /// <summary>Call when the user switches tabs in the launcher.</summary>
        public async Task SetLauncherTabAsync(string tabName)
        {
            _currentTab = tabName;
            await UpdateLauncherPresenceAsync();
        }

        /// <summary>Call when a game session starts.</summary>
        public async Task SetInGameAsync(string seasonName, string yearSeasonKey, string patchKey, string username)
        {
            _seasonName  = seasonName;
            _yearSeason  = yearSeasonKey;
            _patchKey    = patchKey;
            _username    = username;
            _mapName     = null;
            _gamemodeName = null;
            _operatorName = null;
            _eventName   = null;
            _modeName    = null;
            _unlockAll   = false;
            _gameStart   = DateTimeOffset.UtcNow;
            await UpdateGamePresenceAsync();
        }

        /// <summary>
        /// Call when a playlist is picked. <paramref name="eventName"/> is shown instead of the in-game name
        /// when the playlist is an event; <paramref name="modeName"/> appears in the image tooltip.
        /// </summary>
        public async Task SetPlaylistAsync(string? eventName, string? modeName)
        {
            _eventName = string.IsNullOrWhiteSpace(eventName) ? null : eventName.Trim();
            _modeName  = string.IsNullOrWhiteSpace(modeName) ? null : modeName.Trim();
            if (_gameStart != null)
                await UpdateGamePresenceAsync();
        }

        /// <summary>Call when unlock all starts or stops for the current game.</summary>
        public async Task SetUnlockAllAsync(bool on)
        {
            if (_unlockAll == on) return;
            _unlockAll = on;
            if (_gameStart != null)
                await UpdateGamePresenceAsync();
        }

        /// <summary>
        /// Call whenever the memory reader detects a map/gamemode/operator change.
        /// Safe to call from any thread.
        /// </summary>
        public async Task SetGameStateAsync(string? mapName, string? gamemodeName, string? operatorName)
        {
            _mapName      = mapName;
            _gamemodeName = gamemodeName;
            _operatorName = operatorName;
            if (_gameStart != null)
                await UpdateGamePresenceAsync();
        }

        /// <summary>Call when the game process exits.</summary>
        public async Task SetGameEndedAsync()
        {
            _gameStart    = null;
            _mapName      = null;
            _gamemodeName = null;
            _operatorName = null;
            _eventName    = null;
            _modeName     = null;
            _unlockAll    = false;
            await UpdateLauncherPresenceAsync();
        }

        /// <summary>Removes the activity from the user's Discord profile.</summary>
        public Task ClearAsync() => SendPresenceAsync(null);

        // ── internal presence builders ────────────────────────────────────────

        private async Task UpdateLauncherPresenceAsync()
        {
            var payload = BuildPresence(
                details  : $"In the launcher, {_currentTab}",
                state    : "Browsing seasons",
                startTime: _launcherStart,
                imageText: $"Swerve2k Downloader {AppInfo.Version}");
            await SendPresenceAsync(payload);
        }

        private async Task UpdateGamePresenceAsync()
        {
            // details: the season, or map and mode when the game-state reader knows them
            string details = (_mapName, _gamemodeName) switch
            {
                (string m, string g) => $"{m}, {g}",
                (string m, null    ) => $"{m}, {_yearSeason}",
                _                   => $"{_yearSeason} {_seasonName}"
            };

            // state: the event being played, otherwise the operator, otherwise the in-game name
            string state = _eventName != null ? _eventName
                : _operatorName != null ? $"Playing as {_operatorName}"
                : string.IsNullOrWhiteSpace(_username) ? "In game"
                : _username;

            // Image tooltip: picked mode and unlock all
            var tip = new List<string> { $"{_seasonName} ({_yearSeason}, patch {_patchKey})" };
            if (_modeName != null && _modeName != _eventName) tip.Add(_modeName);
            if (_unlockAll) tip.Add("Unlock all");

            var payload = BuildPresence(
                details  : details,
                state    : state,
                startTime: _gameStart ?? DateTimeOffset.UtcNow,
                imageText: string.Join("  ·  ", tip));
            await SendPresenceAsync(payload);
        }

        private Dictionary<string, object?> BuildPresence(string details, string? state, DateTimeOffset startTime, string imageText)
        {
            var timestamps = new Dictionary<string, long>
            {
                ["start"] = startTime.ToUnixTimeSeconds()
            };

            var assets = new Dictionary<string, string>
            {
                ["large_image"] = "operation_throwback_logo",
                ["large_text"]  = Trim(imageText, 128)
            };

            var activity = new Dictionary<string, object?>
            {
                ["details"]    = Trim(details, 128),
                ["timestamps"] = timestamps,
                ["assets"]     = assets,
                // Lets friends grab the launcher from your profile.
                ["buttons"]    = new[] { new Dictionary<string, string> { ["label"] = "Get Swerve2k Downloader", ["url"] = AppInfo.RepoUrl + "/releases/latest" } },
            };

            if (state != null)
                activity["state"] = Trim(state, 128);

            return activity;
        }

        // Discord rejects presence fields longer than 128 characters.
        private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "\u2026";

        private async Task SendPresenceAsync(Dictionary<string, object?>? activity)
        {
            if (!_connected || _pipe == null || !_pipe.IsConnected) return;

            await _lock.WaitAsync();
            try
            {
                var data = new Dictionary<string, object?>
                {
                    ["cmd"]   = "SET_ACTIVITY",
                    ["nonce"] = (_nonce++).ToString(),
                    ["args"]  = new Dictionary<string, object?>
                    {
                        ["pid"]      = Process.GetCurrentProcess().Id,
                        ["activity"] = activity
                    }
                };

                await WriteAsync(FrameOp, JsonSerializer.Serialize(data));
                await ReadFrameAsync(); // consume ACK
            }
            catch { _connected = false; }
            finally { _lock.Release(); }
        }

        // ── low-level pipe I/O ────────────────────────────────────────────────

        private async Task WriteAsync(int opcode, string json)
        {
            if (_pipe == null) return;
            byte[] data    = Encoding.UTF8.GetBytes(json);
            byte[] header  = new byte[8];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), opcode);
            BitConverter.TryWriteBytes(header.AsSpan(4, 4), data.Length);
            await _pipe.WriteAsync(header);
            await _pipe.WriteAsync(data);
            await _pipe.FlushAsync();
        }

        private async Task<string?> ReadFrameAsync()
        {
            if (_pipe == null) return null;
            try
            {
                byte[] header = new byte[8];
                int read = 0;
                while (read < 8)
                {
                    int n = await _pipe.ReadAsync(header.AsMemory(read, 8 - read));
                    if (n == 0) return null;
                    read += n;
                }
                int length = BitConverter.ToInt32(header, 4);
                if (length <= 0 || length > 65536) return null;

                byte[] body = new byte[length];
                read = 0;
                while (read < length)
                {
                    int n = await _pipe.ReadAsync(body.AsMemory(read, length - read));
                    if (n == 0) break;
                    read += n;
                }
                return Encoding.UTF8.GetString(body, 0, read);
            }
            catch { return null; }
        }

        // ── IDisposable ────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_pipe?.IsConnected == true)
                    _ = WriteAsync(CloseOp, "{}");
                _pipe?.Dispose();
            }
            catch { }
        }
    }
}
