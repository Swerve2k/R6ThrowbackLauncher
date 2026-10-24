using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public sealed record LiberatorState(bool Attached, bool Unlocked, bool Unsupported);

    /// <summary>
    /// "Unlock all" through Liberator, a separate GPLv3 program by xeralin
    /// (https://github.com/xeralin/ThrowbackLauncher). It is downloaded from their release, never bundled.
    /// Once started with <c>--runtime {port}</c> it connects back over loopback, attaches to the running game
    /// and unlocks items itself, reporting progress as JSON lines.
    /// </summary>
    public static class LiberatorService
    {
        public const string Owner = "xeralin";
        public const string Repo = "ThrowbackLauncher";
        public const string ProjectUrl = "https://github.com/" + Owner + "/" + Repo;
        private const string ReleaseAsset = "Runtime.zip";
        private const string Member = "Liberator.exe";

        public static string Dir => Path.Combine(AppPaths.DataDir, "Liberator");
        public static string ExePath => Path.Combine(Dir, Member);
        private static string InfoPath => Path.Combine(Dir, "liberator.json");

        public static bool IsInstalled => File.Exists(ExePath);

        // Unlock all is only offered for Y1 to Y9. Builds from Y10 on are close to live Siege, where an
        // unlocker would be cheating, so the launcher never runs Liberator on them.
        public const int MaxYear = 9;

        // Builds Liberator recognises, Y1 to Y9 only (from its public build table).
        private static readonly HashSet<string> SupportedBuilds = new()
        {
            "8194013", "8519860", "9132097", "9654076", "9860556", "10211195", "10751226", "11216230", "11432634",
            "11493221", "11553121", "11580709", "11726982", "11938214", "11965022", "12213419", "12362767", "12512571",
            "12815133", "12863847", "13147883", "13632147", "13777760", "13924517", "14303219", "14664131", "15018155",
            "15241382", "15500403", "15680437", "15701375", "15772114", "32627923", "35130040", "37046065", "39785137",
            "43489433", "47165842", "50497889", "55217154", "58222837", "62486471", "67466841", "69866602", "72730050",
            "79429095", "82873685",
        };

        /// <summary>Null when unlock all can run on this season, otherwise the reason it can't.</summary>
        public static string? Unavailable(SeasonPatch patch, string? renderer)
        {
            if (patch.Year < 1 || patch.Year > MaxYear) return "Unlock all is only available for Y1 to Y9 seasons.";
            if (patch.IsHeatedMetal) return "Unlock all doesn't run on Heated Metal builds.";
            if (renderer == "vulkan") return "Unlock all doesn't work with the Vulkan renderer.";
            if (!SupportedBuilds.Contains(patch.BuildId)) return "Liberator doesn't support this build.";
            return null;
        }

        public sealed record Info(string Tag, string Sha256, DateTime Downloaded);

        public static Info? ReadInfo()
        {
            try { return JsonSerializer.Deserialize<Info>(File.ReadAllText(InfoPath)); }
            catch { return null; }
        }

        public static void Remove()
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
        }

        private static HttpClient NewClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"R6ThrowbackLauncher/{AppInfo.Version}");
            return http;
        }

        /// <summary>Latest release tag of Liberator's project, or null if GitHub can't be reached.</summary>
        public static async Task<string?> LatestTagAsync()
        {
            try
            {
                using var http = NewClient();
                using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest"));
                return doc.RootElement.GetProperty("tag_name").GetString();
            }
            catch { return null; }
        }

        /// <summary>
        /// Downloads only Liberator.exe out of the release's Runtime.zip using HTTP range requests,
        /// checks its CRC, and saves it with the release tag and SHA-256.
        /// </summary>
        public static async Task<Info> DownloadAsync(IProgress<string>? status, CancellationToken ct = default)
        {
            using var http = NewClient();
            status?.Report("Finding the latest Liberator release");
            using var rel = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct));
            string tag = rel.RootElement.GetProperty("tag_name").GetString() ?? "";
            var asset = rel.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == ReleaseAsset);
            if (asset.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"The latest {Repo} release has no {ReleaseAsset}.");
            string url = asset.GetProperty("browser_download_url").GetString()!;
            long size = asset.GetProperty("size").GetInt64();

            status?.Report("Reading the release archive");
            int tailLen = (int)Math.Min(size, 65_557);
            byte[] tail = await RangeAsync(http, url, size - tailLen, tailLen, ct);
            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
                if (BitConverter.ToUInt32(tail, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException("The release archive couldn't be read.");
            long cdSize = BitConverter.ToUInt32(tail, eocd + 12);
            long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
            if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF)
                throw new InvalidDataException("The release archive uses a format this launcher can't read.");

            byte[] cd = await RangeAsync(http, url, cdOffset, (int)cdSize, ct);
            (ushort method, uint crc, long comp, long uncomp, long local)? entry = null;
            for (int p = 0; p + 46 <= cd.Length && BitConverter.ToUInt32(cd, p) == 0x02014b50;)
            {
                ushort method = BitConverter.ToUInt16(cd, p + 10);
                uint crc = BitConverter.ToUInt32(cd, p + 16);
                uint comp = BitConverter.ToUInt32(cd, p + 20);
                uint uncomp = BitConverter.ToUInt32(cd, p + 24);
                int nameLen = BitConverter.ToUInt16(cd, p + 28);
                int extraLen = BitConverter.ToUInt16(cd, p + 30);
                int commentLen = BitConverter.ToUInt16(cd, p + 32);
                uint local = BitConverter.ToUInt32(cd, p + 42);
                string name = Encoding.UTF8.GetString(cd, p + 46, nameLen);
                if (name == Member) { entry = (method, crc, comp, uncomp, local); break; }
                p += 46 + nameLen + extraLen + commentLen;
            }
            if (entry is not { } e) throw new InvalidDataException($"{Member} isn't in the release archive.");
            if (e.method is not (0 or 8)) throw new InvalidDataException($"{Member} is compressed in a way this launcher can't read.");

            status?.Report($"Downloading Liberator {tag}");
            byte[] header = await RangeAsync(http, url, e.local, 30, ct);
            long dataStart = e.local + 30 + BitConverter.ToUInt16(header, 26) + BitConverter.ToUInt16(header, 28);
            byte[] packed = await RangeAsync(http, url, dataStart, (int)e.comp, ct);

            byte[] exe;
            if (e.method == 0) exe = packed;
            else
            {
                using var input = new MemoryStream(packed);
                using var inflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                await inflate.CopyToAsync(output, ct);
                exe = output.ToArray();
            }
            if (exe.Length != e.uncomp || Crc32(exe) != e.crc)
                throw new InvalidDataException("The download was damaged. Try again.");

            Directory.CreateDirectory(Dir);
            string part = ExePath + ".part";
            await File.WriteAllBytesAsync(part, exe, ct);
            File.Move(part, ExePath, overwrite: true);
            var info = new Info(tag, Convert.ToHexString(SHA256.HashData(exe)), DateTime.UtcNow);
            await File.WriteAllTextAsync(InfoPath, JsonSerializer.Serialize(info), ct);
            return info;
        }

        private static async Task<byte[]> RangeAsync(HttpClient http, string url, long offset, int length, CancellationToken ct)
        {
            if (length <= 0) return Array.Empty<byte>();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode != HttpStatusCode.PartialContent)
                throw new InvalidOperationException("GitHub didn't return part of the release file. Try again later.");
            byte[] data = await resp.Content.ReadAsByteArrayAsync(ct);
            if (data.Length != length) throw new InvalidDataException("The download was cut short. Try again.");
            return data;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }
    }

    /// <summary>Runs Liberator for one game session and relays its state. Restarts it with backoff if it drops out.</summary>
    public sealed class LiberatorSession : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private Process? _proc;

        public event Action<LiberatorState>? StateChanged;
        public LiberatorState State { get; private set; } = new(false, false, false);

        public void Start() => _ = RunAsync(_cts.Token);

        private async Task RunAsync(CancellationToken ct)
        {
            double delay = 1.5;
            for (int attempt = 0; attempt < 10 && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    if (await RunOnceAsync(ct)) delay = 1.5;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Debug.WriteLine($"[Liberator] {ex.Message}");
                }
                if (ct.IsCancellationRequested || State.Unsupported) break;
                try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch { break; }
                delay = Math.Min(delay * 2, 30);
            }
        }

        /// <summary>One Liberator run. True if it connected.</summary>
        private async Task<bool> RunOnceAsync(CancellationToken ct)
        {
            if (!LiberatorService.IsInstalled) return false;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _proc = Process.Start(new ProcessStartInfo
                {
                    FileName = LiberatorService.ExePath,
                    Arguments = $"--runtime {port}",
                    WorkingDirectory = LiberatorService.Dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                if (_proc == null) return false;

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(TimeSpan.FromSeconds(20));
                var acceptTask = listener.AcceptTcpClientAsync(wait.Token).AsTask();
                var exitTask = _proc.WaitForExitAsync(wait.Token);
                if (await Task.WhenAny(acceptTask, exitTask) != acceptTask || !acceptTask.IsCompletedSuccessfully) return false;

                using var client = acceptTask.Result;
                using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    if (ParseState(line) is { } state && state != State)
                    {
                        State = state;
                        StateChanged?.Invoke(state);
                    }
                }
                return true;
            }
            finally
            {
                listener.Stop();
                Kill();
                if (State.Attached || State.Unlocked)
                {
                    State = State with { Attached = false };
                    StateChanged?.Invoke(State);
                }
            }
        }

        private static LiberatorState? ParseState(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (!r.TryGetProperty("event", out var ev) || ev.GetString() != "state") return null;
                bool B(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
                return new LiberatorState(B("attached"), B("unlocked"), B("unsupported"));
            }
            catch { return null; }
        }

        private void Kill()
        {
            var p = _proc;
            _proc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            p.Dispose();
        }

        public void Dispose()
        {
            _cts.Cancel();
            Kill();
        }
    }
}
