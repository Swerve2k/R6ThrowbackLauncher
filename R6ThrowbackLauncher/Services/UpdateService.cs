using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace R6ThrowbackLauncher.Services
{
    public sealed record ReleaseAsset(string Name, string Url, long Size);

    public sealed record ReleaseInfo(string Tag, string Name, string Body, DateTime? Published, bool Prerelease,
        string HtmlUrl, IReadOnlyList<ReleaseAsset> Assets)
    {
        public string Version => Tag.TrimStart('v', 'V', '.');
    }

    public static class UpdateService
    {
        private const string DepotDownloaderApi = "https://api.github.com/repos/SteamRE/DepotDownloader/releases/latest";
        private const string DepotDownloaderAsset = "DepotDownloader-windows-x64.zip";

        private static HttpClient NewClient(int timeoutSeconds = 20)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"R6ThrowbackLauncher/{AppInfo.Version}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        public static async Task<List<ReleaseInfo>> FetchLauncherReleasesAsync(CancellationToken ct = default)
        {
            using var http = NewClient();
            string json = await http.GetStringAsync(
                $"https://api.github.com/repos/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases?per_page=30", ct);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(ParseRelease)
                .OrderByDescending(r => r.Published ?? DateTime.MinValue)
                .ToList();
        }

        private static ReleaseInfo ParseRelease(JsonElement r)
        {
            string Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            DateTime? published = DateTime.TryParse(Str("published_at"), out var d) ? d : null;
            var assets = new List<ReleaseAsset>();
            if (r.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                    assets.Add(new ReleaseAsset(
                        a.GetProperty("name").GetString() ?? "",
                        a.GetProperty("browser_download_url").GetString() ?? "",
                        a.TryGetProperty("size", out var s) ? s.GetInt64() : 0));
            bool pre = r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True;
            string tag = Str("tag_name");
            return new ReleaseInfo(tag, string.IsNullOrWhiteSpace(Str("name")) ? tag : Str("name"), Str("body"),
                published, pre, Str("html_url"), assets);
        }

        public static ReleaseInfo? Latest(IEnumerable<ReleaseInfo> releases) =>
            releases.Where(r => !r.Prerelease).OrderByDescending(r => r.Published ?? DateTime.MinValue).FirstOrDefault();

        /// <summary>Prefers an installer, then the launcher executable, then any .exe asset.</summary>
        public static ReleaseAsset? PickAsset(ReleaseInfo release)
        {
            var exes = release.Assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
            return exes.FirstOrDefault(a => a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                ?? exes.FirstOrDefault(a => a.Name.Equals("R6ThrowbackLauncher.exe", StringComparison.OrdinalIgnoreCase))
                ?? exes.FirstOrDefault();
        }

        public static async Task<string> DownloadAsync(string url, string fileName, IProgress<double>? progress, CancellationToken ct = default,
            IProgress<(long Done, long Total)>? bytes = null)
        {
            string dest = Path.Combine(Path.GetTempPath(), fileName);
            try
            {
                using var http = NewClient(600);
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? -1;
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(dest);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(done / (double)total);
                    bytes?.Report((done, total));
                }
            }
            catch
            {
                try { File.Delete(dest); } catch { }
                throw;
            }
            return dest;
        }

        public static bool IsInstaller(string exePath)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                string text = $"{info.FileDescription} {info.ProductName} {info.Comments} {info.InternalName}";
                return text.Contains("setup", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("installer", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Inno", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Starts the downloaded update. The caller must exit the app afterwards.</summary>
        public static void StartUpdate(string downloadedExe)
        {
            if (IsInstaller(downloadedExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = downloadedExe,
                    Arguments = "/SILENT /SP- /NOCANCEL /CLOSEAPPLICATIONS",
                    UseShellExecute = true,
                });
                return;
            }

            string currentExe = Environment.ProcessPath ?? Path.Combine(AppPaths.BaseDir, "R6ThrowbackLauncher.exe");
            int pid = Environment.ProcessId;
            string script = Path.Combine(Path.GetTempPath(), "throwback_update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                ":wait\r\n" +
                $"tasklist /fi \"pid eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
                "if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )\r\n" +
                "set N=0\r\n" +
                ":copy\r\n" +
                $"copy /y \"{downloadedExe}\" \"{currentExe}\" >nul\r\n" +
                "if not errorlevel 1 goto run\r\n" +
                "set /a N+=1\r\n" +
                "if %N% lss 5 ( timeout /t 2 /nobreak >nul & goto copy )\r\n" +
                "exit /b 1\r\n" +
                ":run\r\n" +
                $"start \"\" \"{currentExe}\"\r\n" +
                "del \"%~f0\"\r\n");

            bool writable = CanWrite(Path.GetDirectoryName(currentExe)!);
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{script}\"",
                UseShellExecute = !writable,
                Verb = writable ? "" : "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, $".write_{Guid.NewGuid():N}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // ── DepotDownloader ──────────────────────────────────────────────────

        public static string? InstalledDepotDownloaderVersion(DownloadService service)
        {
            string path = File.Exists(DownloadService.UpdatedDepotDownloaderExe)
                ? DownloadService.UpdatedDepotDownloaderExe
                : service.BundledDepotDownloader;
            if (!File.Exists(path)) return null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                string v = info.ProductVersion ?? info.FileVersion ?? "";
                int plus = v.IndexOf('+');
                return plus > 0 ? v[..plus] : v;
            }
            catch { return null; }
        }

        public static async Task<(string Version, ReleaseAsset? Asset, string Notes)> FetchDepotDownloaderLatestAsync(CancellationToken ct = default)
        {
            using var http = NewClient();
            using var doc = JsonDocument.Parse(await http.GetStringAsync(DepotDownloaderApi, ct));
            var release = ParseRelease(doc.RootElement);
            string version = release.Tag.Replace("DepotDownloader_", "").TrimStart('v');
            var asset = release.Assets.FirstOrDefault(a => a.Name.Equals(DepotDownloaderAsset, StringComparison.OrdinalIgnoreCase));
            return (version, asset, release.Body);
        }

        public static async Task InstallDepotDownloaderAsync(ReleaseAsset asset, IProgress<double>? progress, CancellationToken ct = default)
        {
            string zip = await DownloadAsync(asset.Url, $"dd_{Guid.NewGuid():N}.zip", progress, ct);
            string staging = DownloadService.UpdatedDepotDownloaderDir + ".new";
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                ZipFile.ExtractToDirectory(zip, staging);
                if (!File.Exists(Path.Combine(staging, "DepotDownloader.exe")))
                    throw new InvalidDataException("The DepotDownloader download did not contain DepotDownloader.exe.");
                if (Directory.Exists(DownloadService.UpdatedDepotDownloaderDir))
                    Directory.Delete(DownloadService.UpdatedDepotDownloaderDir, true);
                Directory.Move(staging, DownloadService.UpdatedDepotDownloaderDir);
            }
            finally
            {
                try { File.Delete(zip); } catch { }
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        public static void RemoveUpdatedDepotDownloader()
        {
            if (Directory.Exists(DownloadService.UpdatedDepotDownloaderDir))
                Directory.Delete(DownloadService.UpdatedDepotDownloaderDir, true);
        }
    }
}
