using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public enum TransferState { Idle, Preparing, Running, Paused }

    public sealed class TransferItem
    {
        public SeasonPatch Patch { get; init; } = null!;
        public string TargetDir { get; init; } = "";
        public bool Verify { get; init; }
    }

    public sealed record SteamCredentials(string Username, string? Password, bool Remember);

    public sealed record TransferResult(SeasonPatch Patch, bool Success, bool Verify, string Message);

    /// <summary>Runs downloads and verifications one at a time from a persistent queue.</summary>
    public sealed class DownloadManager
    {
        private const int LogLimit = 1500;
        private const long StagingHeadroomBytes = 2L * 1024 * 1024 * 1024;
        private static readonly Regex PercentRx = new(@"^\s*(\d{1,3}(?:\.\d+)?)%", RegexOptions.Compiled);

        private readonly DownloadService _service;
        private readonly SettingsService _settings;
        private readonly Func<string, SeasonPatch?> _resolve;
        private readonly Dispatcher _dispatcher;
        private readonly List<TransferItem> _queue = new();
        private readonly List<string> _log = new();
        private readonly List<string> _pendingLog = new();
        private readonly object _logLock = new();
        private readonly DispatcherTimer _ticker;
        private CancellationTokenSource? _cts;
        private bool _pauseRequested;
        private bool _cancelRequested;
        private long _lastRxBytes;
        private DateTime _lastRxTime;
        private double _pctRate;
        private double _lastPct;
        private DateTime _lastPctTime;

        public event Action? Changed;
        public event Action<IReadOnlyList<string>>? LogAppended;
        public event Action<TransferResult>? Finished;

        public Func<SeasonPatch, Task<SteamCredentials?>> CredentialsProvider { get; set; } = _ => Task.FromResult<SteamCredentials?>(null);
        public Func<PromptKind, string, Task<string?>> PromptHandler { get; set; } = (_, _) => Task.FromResult<string?>(null);
        public Func<string> InGameName { get; set; } = () => LoaderConfig.DefaultUsername;
        public Action<bool>? LoginOutcome { get; set; }

        public TransferItem? Active { get; private set; }
        public TransferState State { get; private set; } = TransferState.Idle;
        public bool QueuePaused { get; private set; }
        public double Percent { get; private set; }
        public int Step { get; private set; }
        public int Steps { get; private set; }
        public string StepLabel { get; private set; } = "";
        public string StatusText { get; private set; } = "";
        public string SpeedText { get; private set; } = "";
        public string EtaText { get; private set; } = "";
        public IReadOnlyList<TransferItem> Queue => _queue;
        public IReadOnlyList<string> LogLines { get { lock (_logLock) return _log.ToList(); } }
        public bool IsRunning => Active != null;

        public DownloadManager(DownloadService service, SettingsService settings, Func<string, SeasonPatch?> resolve, Dispatcher dispatcher)
        {
            _service = service;
            _settings = settings;
            _resolve = resolve;
            _dispatcher = dispatcher;
            _ticker = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
            _ticker.Tick += (_, _) => Tick();
        }

        // ── Queue management ─────────────────────────────────────────────────

        public bool IsBusyWith(SeasonPatch patch) =>
            (Active?.Patch.Id == patch.Id) || _queue.Any(q => q.Patch.Id == patch.Id);

        public bool IsActive(SeasonPatch patch) => Active?.Patch.Id == patch.Id;

        public bool IsQueued(SeasonPatch patch) => _queue.Any(q => q.Patch.Id == patch.Id);

        public bool IsBusyWithFolder(string dir) =>
            (Active != null && string.Equals(Active.TargetDir, dir, StringComparison.OrdinalIgnoreCase)) ||
            _queue.Any(q => string.Equals(q.TargetDir, dir, StringComparison.OrdinalIgnoreCase));

        /// <summary>Bytes still needed on disk to finish this download, or 0 if there is room.</summary>
        public static long DiskShortfall(SeasonPatch patch, string targetDir)
        {
            if (patch.SizeGb <= 0) return 0;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(targetDir)) ?? "C:\\";
                long free = new DriveInfo(root).AvailableFreeSpace;
                long present = Directory.Exists(targetDir) ? InstallLocator.DirectorySize(targetDir) : 0;
                long need = Math.Max(0, (long)(patch.SizeGb * 1024 * 1024 * 1024) - present) + StagingHeadroomBytes;
                return Math.Max(0, need - free);
            }
            catch { return 0; }
        }

        public void Enqueue(SeasonPatch patch, string targetDir, bool verify)
        {
            if (IsBusyWith(patch)) return;
            _queue.Add(new TransferItem { Patch = patch, TargetDir = targetDir, Verify = verify });
            Persist();
            RaiseChanged();
            if (!IsRunning && !QueuePaused) _ = StartNextAsync();
        }

        public void Remove(SeasonPatch patch)
        {
            _queue.RemoveAll(q => q.Patch.Id == patch.Id);
            Persist();
            RaiseChanged();
        }

        public void Move(SeasonPatch patch, int delta)
        {
            int i = _queue.FindIndex(q => q.Patch.Id == patch.Id);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _queue.Count) return;
            (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
            Persist();
            RaiseChanged();
        }

        public void Pause()
        {
            QueuePaused = true;
            if (Active != null)
            {
                _pauseRequested = true;
                _cts?.Cancel();
            }
            else
            {
                State = TransferState.Paused;
            }
            Persist();
            RaiseChanged();
        }

        public void Resume()
        {
            QueuePaused = false;
            if (State == TransferState.Paused) State = TransferState.Idle;
            Persist();
            RaiseChanged();
            if (!IsRunning) _ = StartNextAsync();
        }

        /// <summary>Stops the active transfer and drops it from the queue. Downloaded files stay on disk.</summary>
        public void CancelActive()
        {
            if (Active == null) return;
            _pauseRequested = false;
            _cancelRequested = true;
            _cts?.Cancel();
        }

        public void Restore()
        {
            foreach (var q in _settings.Settings.DownloadQueue)
            {
                var patch = _resolve(q.PatchId);
                if (patch == null || IsBusyWith(patch)) continue;
                string target = string.IsNullOrWhiteSpace(q.LibraryRoot)
                    ? patch.InstallDir
                    : Path.Combine(q.LibraryRoot, InstallLocator.PrimaryFolderName(patch));
                if (Directory.Exists(patch.InstallDir)) target = patch.InstallDir;
                _queue.Add(new TransferItem { Patch = patch, TargetDir = target, Verify = q.Verify });
            }
            if (_queue.Count > 0)
            {
                QueuePaused = true;
                State = TransferState.Paused;
                RaiseChanged();
            }
        }

        private void Persist()
        {
            var list = new List<QueuedDownload>();
            if (Active != null) list.Add(ToRecord(Active));
            list.AddRange(_queue.Select(ToRecord));
            _settings.Settings.DownloadQueue = list;
            try { _settings.Save(); } catch { }
        }

        private QueuedDownload ToRecord(TransferItem item) => new()
        {
            PatchId = item.Patch.Id,
            LibraryRoot = Path.GetDirectoryName(item.TargetDir) ?? "",
            Verify = item.Verify,
            Paused = QueuePaused,
        };

        // ── Run loop ─────────────────────────────────────────────────────────

        private async Task StartNextAsync()
        {
            if (IsRunning || QueuePaused || _queue.Count == 0) return;

            var item = _queue[0];
            _queue.RemoveAt(0);
            Active = item;
            State = TransferState.Preparing;
            Percent = 0; Step = 0; Steps = item.Patch.Depots.Count; StepLabel = "";
            StatusText = item.Verify ? "Preparing to verify" : "Preparing download";
            SpeedText = ""; EtaText = "";
            _pauseRequested = false;
            _cancelRequested = false;
            lock (_logLock) { _log.Clear(); _pendingLog.Clear(); }
            Persist();
            RaiseChanged();

            var creds = await CredentialsProvider(item.Patch);
            if (_cancelRequested)
            {
                _cancelRequested = false;
                Active = null;
                State = QueuePaused ? TransferState.Paused : TransferState.Idle;
                StatusText = "Cancelled";
                Persist();
                RaiseChanged();
                if (!QueuePaused && _queue.Count > 0) await StartNextAsync();
                return;
            }
            if (creds == null || _pauseRequested || QueuePaused)
            {
                _pauseRequested = false;
                _queue.Insert(0, item);
                Active = null;
                QueuePaused = true;
                State = TransferState.Paused;
                StatusText = creds == null ? "Waiting for Steam sign-in" : "Paused";
                Persist();
                RaiseChanged();
                return;
            }

            _cts = new CancellationTokenSource();
            State = TransferState.Running;
            ResetRates();
            _ticker.Start();
            RaiseChanged();

            if (!item.Verify) InstallLocator.MarkPartial(item.TargetDir);

            var callbacks = new DownloadCallbacks
            {
                Output = OnOutput,
                Status = s => _dispatcher.InvokeAsync(() => { StatusText = s; RaiseChanged(); }),
                Step = (i, n, label) => _dispatcher.InvokeAsync(() =>
                {
                    Step = i; Steps = n; StepLabel = label; Percent = 0;
                    StatusText = item.Verify ? $"Verifying {label.ToLowerInvariant()}" : $"Downloading {label.ToLowerInvariant()}";
                    ResetRates();
                    RaiseChanged();
                }),
                Prompt = (kind, text) => _dispatcher.InvokeAsync(() => PromptHandler(kind, text)).Task.Unwrap(),
            };

            TransferResult result;
            try
            {
                await _service.RunDownloadAsync(item.Patch, item.TargetDir, creds.Username, creds.Password,
                    _settings.Settings.MaxDownloads, creds.Remember, InGameName(), callbacks, _cts.Token);
                InstallLocator.ClearPartial(item.TargetDir);
                LoginOutcome?.Invoke(true);
                result = new TransferResult(item.Patch, true, item.Verify,
                    item.Verify ? $"{item.Patch.ShortLabel} verified" : $"{item.Patch.ShortLabel} is ready to play");
            }
            catch (OperationCanceledException)
            {
                result = new TransferResult(item.Patch, false, item.Verify, _pauseRequested ? "Paused" : "Cancelled");
            }
            catch (DownloadFailedException ex)
            {
                if (ex.LoginFailed) LoginOutcome?.Invoke(false);
                result = new TransferResult(item.Patch, false, item.Verify, ex.Message);
            }
            catch (Exception ex)
            {
                result = new TransferResult(item.Patch, false, item.Verify, ex.Message);
            }
            finally
            {
                _ticker.Stop();
                FlushLog();
                _cts?.Dispose();
                _cts = null;
            }

            bool paused = _pauseRequested;
            _pauseRequested = false;
            Active = null;
            if (paused)
            {
                _queue.Insert(0, item);
                State = TransferState.Paused;
                StatusText = "Paused";
            }
            else
            {
                State = QueuePaused ? TransferState.Paused : TransferState.Idle;
                StatusText = result.Message;
                if (result.Message != "Cancelled") SaveHistory(result);
            }
            SpeedText = ""; EtaText = "";
            Persist();
            RaiseChanged();
            if (!paused) Finished?.Invoke(result);

            if (!QueuePaused && _queue.Count > 0) await StartNextAsync();
        }

        // ── Progress, speed and log ──────────────────────────────────────────

        private void OnOutput(string line)
        {
            lock (_logLock) _pendingLog.Add(line);
            var m = PercentRx.Match(line);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            {
                pct = Math.Clamp(pct, 0, 100);
                _dispatcher.InvokeAsync(() => UpdatePercent(pct));
            }
        }

        private void UpdatePercent(double pct)
        {
            var now = DateTime.UtcNow;
            double dt = (now - _lastPctTime).TotalSeconds;
            if (dt >= 1 && pct > _lastPct)
            {
                double rate = (pct - _lastPct) / dt;
                _pctRate = _pctRate <= 0 ? rate : _pctRate * 0.7 + rate * 0.3;
                _lastPct = pct;
                _lastPctTime = now;
            }
            Percent = pct;
        }

        private void ResetRates()
        {
            _lastPct = 0;
            _lastPctTime = DateTime.UtcNow;
            _pctRate = 0;
            _lastRxBytes = TotalBytesReceived();
            _lastRxTime = DateTime.UtcNow;
        }

        private void Tick()
        {
            FlushLog();
            if (State != TransferState.Running) return;

            long rx = TotalBytesReceived();
            var now = DateTime.UtcNow;
            double dt = (now - _lastRxTime).TotalSeconds;
            if (dt >= 1)
            {
                double bps = Math.Max(0, rx - _lastRxBytes) / dt;
                SpeedText = bps > 1024 ? FormatSpeed(bps) : "";
                _lastRxBytes = rx;
                _lastRxTime = now;
            }
            EtaText = _pctRate > 0.001 && Percent < 100
                ? FormatEta(TimeSpan.FromSeconds((100 - Percent) / _pctRate))
                : "";
            RaiseChanged();
        }

        private void FlushLog()
        {
            List<string> batch;
            lock (_logLock)
            {
                if (_pendingLog.Count == 0) return;
                batch = _pendingLog.ToList();
                _pendingLog.Clear();
                _log.AddRange(batch);
                if (_log.Count > LogLimit) _log.RemoveRange(0, _log.Count - LogLimit);
            }
            LogAppended?.Invoke(batch);
        }

        private static long TotalBytesReceived()
        {
            long total = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    total += ni.GetIPStatistics().BytesReceived;
                }
            }
            catch { }
            return total;
        }

        public static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
        {
            >= 1024 * 1024 * 1024 => $"{bytesPerSecond / (1024.0 * 1024 * 1024):0.0} GB/s",
            >= 1024 * 1024        => $"{bytesPerSecond / (1024.0 * 1024):0.0} MB/s",
            _                     => $"{bytesPerSecond / 1024.0:0} KB/s",
        };

        private static string FormatEta(TimeSpan t)
        {
            if (t.TotalHours >= 48) return "";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m left";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds:00}s left";
            return $"{Math.Max(1, t.Seconds)}s left";
        }

        private void RaiseChanged() => Changed?.Invoke();

        // ── History ──────────────────────────────────────────────────────────

        private static void SaveHistory(TransferResult r)
        {
            try
            {
                string path = AppPaths.DownloadHistoryFile;
                var history = new List<Dictionary<string, object>>();
                if (File.Exists(path))
                {
                    try { history = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(File.ReadAllText(path)) ?? new(); }
                    catch { history = new(); }
                }
                history.Insert(0, new Dictionary<string, object>
                {
                    ["id"] = r.Patch.Id,
                    ["name"] = r.Patch.SeasonName,
                    ["display"] = r.Patch.DisplayName,
                    ["success"] = r.Success,
                    ["verify"] = r.Verify,
                    ["time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });
                if (history.Count > 50) history.RemoveRange(50, history.Count - 50);
                File.WriteAllText(path, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static List<(string Name, bool Success, bool Verify, long Time)> LoadHistory(int count)
        {
            var result = new List<(string, bool, bool, long)>();
            try
            {
                if (!File.Exists(AppPaths.DownloadHistoryFile)) return result;
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.DownloadHistoryFile));
                foreach (var e in doc.RootElement.EnumerateArray().Take(count))
                {
                    string name = e.TryGetProperty("display", out var d) ? d.GetString() ?? "" : "";
                    bool ok = e.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                    bool verify = e.TryGetProperty("verify", out var v) && v.ValueKind == JsonValueKind.True;
                    long time = e.TryGetProperty("time", out var t) && t.TryGetInt64(out long tv) ? tv : 0;
                    result.Add((name, ok, verify, time));
                }
            }
            catch { }
            return result;
        }

        public string LogText()
        {
            var sb = new StringBuilder();
            foreach (var l in LogLines) sb.AppendLine(l);
            return sb.ToString();
        }
    }
}
