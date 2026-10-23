using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public enum PromptKind { Password, GuardCode }

    public sealed class DownloadCallbacks
    {
        public Action<string> Output { get; init; } = _ => { };
        public Action<string> Status { get; init; } = _ => { };
        public Action<int, int, string> Step { get; init; } = (_, _, _) => { };
        public Func<PromptKind, string, Task<string?>> Prompt { get; init; } = (_, _) => Task.FromResult<string?>(null);
    }

    public sealed class DownloadFailedException : Exception
    {
        public bool LoginFailed { get; }
        public DownloadFailedException(string message, bool loginFailed = false) : base(message) => LoginFailed = loginFailed;
    }

    public sealed class DownloadService
    {
        public const string AppId = "359550";
        public const string RussianDepot = "377238";

        public string ResourcesDir => AppPaths.ResourcesDir;
        public string HMNewBloodZip => Path.Combine(ResourcesDir, "HeatedMetal_NewBlood.zip");
        public string ThrowbackLoaderSourceDir => Path.Combine(ResourcesDir, "ThrowbackLoader");
        public string BundledDepotDownloader => Path.Combine(ResourcesDir, "DepotDownloader.dll");
        public static string UpdatedDepotDownloaderDir => Path.Combine(AppPaths.BinDir, "DepotDownloader");
        public static string UpdatedDepotDownloaderExe => Path.Combine(UpdatedDepotDownloaderDir, "DepotDownloader.exe");

        public const string HmVersionUrl =
            "https://raw.githubusercontent.com/Swerve2k/R6ThrowbackLauncher/main/hm_version.json";

        public record HmUpdateInfo(string Version, string DownloadUrl, string Notes);

        private static string DepotLabel(string depotId) => depotId switch
        {
            "359551" => "Game content",
            "377237" => "Worldwide files",
            RussianDepot => "Russian files",
            _ => $"Depot {depotId}",
        };

        // ── Prerequisites (Plazas + Qualities, mirrors the batch downloader) ──
        private static string PrereqDir => AppPaths.PrerequisitesDir;

        public async Task EnsurePrerequisitesAsync(Action<string> onStatus, CancellationToken ct = default)
        {
            Directory.CreateDirectory(PrereqDir);

            string z7Path       = Path.Combine(PrereqDir, "7z.exe");
            string plazasDir    = Path.Combine(PrereqDir, "Plazas");
            string qualitiesDir = Path.Combine(PrereqDir, "Qualities");

            bool plazasOk    = Directory.Exists(plazasDir)    && Directory.GetDirectories(plazasDir).Length > 0;
            bool qualitiesOk = Directory.Exists(qualitiesDir) && Directory.GetFiles(qualitiesDir).Length    > 0;
            if (plazasOk && qualitiesOk && File.Exists(z7Path)) return;

            if (!File.Exists(z7Path))
            {
                onStatus("Downloading prerequisite: 7-Zip");
                try { await DownloadFileAsync("https://github.com/DataCluster0/R6TBBatchTool/raw/master/Requirements/7z.exe", z7Path, ct); }
                catch (OperationCanceledException) { throw; }
                catch { }
            }

            if (!plazasOk)
                await FetchArchiveAsync("https://github.com/SMGTD/R6SuperDownloader/raw/refs/heads/main/Plazas.zip",
                    plazasDir, "Plaza bypass files", z7Path, onStatus, ct);
            if (!qualitiesOk)
                await FetchArchiveAsync("https://github.com/SMGTD/R6SuperDownloader/raw/refs/heads/main/Qualities.zip",
                    qualitiesDir, "quality file lists", z7Path, onStatus, ct);
        }

        private static async Task FetchArchiveAsync(string url, string destDir, string label, string z7Path,
            Action<string> onStatus, CancellationToken ct)
        {
            onStatus($"Downloading prerequisite: {label}");
            Directory.CreateDirectory(destDir);
            string tmp = Path.Combine(PrereqDir, $"_{Guid.NewGuid():N}.zip");
            try
            {
                await DownloadFileAsync(url, tmp, ct);
                if (File.Exists(z7Path))
                    await RunSilentAsync(z7Path, $"x -y -o\"{destDir}\" \"{tmp}\" -aoa");
                else
                    ZipFile.ExtractToDirectory(tmp, destDir, overwriteFiles: true);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private static async Task DownloadFileAsync(string url, string dest, CancellationToken ct)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"R6ThrowbackLauncher/{AppInfo.Version}");
            var bytes = await http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(dest, bytes, ct);
        }

        private static async Task RunSilentAsync(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow  = true,
                UseShellExecute = false,
                WindowStyle     = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
        }

        // ── DepotDownloader ──────────────────────────────────────────────────

        public bool HasDepotDownloader => File.Exists(UpdatedDepotDownloaderExe) || File.Exists(BundledDepotDownloader);

        public string DepotDownloaderLabel => File.Exists(UpdatedDepotDownloaderExe)
            ? $"Updated copy ({UpdatedDepotDownloaderDir})"
            : "Bundled with the launcher";

        private (string FileName, string ArgPrefix, string WorkingDir) DepotDownloaderCommand()
        {
            if (File.Exists(UpdatedDepotDownloaderExe))
                return (UpdatedDepotDownloaderExe, "", UpdatedDepotDownloaderDir);
            return (FindDotnet(), $"\"{BundledDepotDownloader}\" ", ResourcesDir);
        }

        /// <summary>
        /// Downloads (or validates) every depot of a season into <paramref name="installDir"/>,
        /// running DepotDownloader once per depot, then applies the loader files.
        /// </summary>
        public async Task RunDownloadAsync(
            SeasonPatch patch, string installDir, string username, string? password,
            int maxDownloads, bool rememberLogin, string inGameName,
            DownloadCallbacks cb, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new DownloadFailedException("Add your Steam account name in Settings first.");
            if (!HasDepotDownloader)
                throw new DownloadFailedException("DepotDownloader is missing. Reinstall the launcher or download it from Updates.");
            if (patch.Depots.Count == 0)
                throw new DownloadFailedException($"No depot data found for {patch.Id}.");

            await EnsurePrerequisitesAsync(cb.Status, ct);

            Directory.CreateDirectory(installDir);
            var (fileName, prefix, workDir) = DepotDownloaderCommand();
            var depots = patch.Depots.OrderBy(d => d.DepotId == "359551" ? 1 : 0).ToList();

            for (int i = 0; i < depots.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (depotId, manifestId) = depots[i];
                cb.Step(i + 1, depots.Count, DepotLabel(depotId));

                var args = new StringBuilder(prefix)
                    .Append($"-app {AppId} -depot {depotId} -manifest {manifestId}")
                    .Append($" -username \"{EscapeArg(username)}\"");
                if (!string.IsNullOrEmpty(password)) args.Append($" -password \"{EscapeArg(password)}\"");
                args.Append($" -dir \"{EscapeArg(installDir)}\" -validate -max-downloads {Math.Clamp(maxDownloads, 1, 100)}");
                if (rememberLogin) args.Append(" -remember-password");

                cb.Output($"--- {DepotLabel(depotId)} (depot {depotId}) ---");
                var (exitCode, loginFailed) = await RunDepotDownloaderAsync(fileName, args.ToString(), workDir, username, cb, ct);

                if (exitCode == 0) continue;
                if (loginFailed)
                    throw new DownloadFailedException("Steam rejected the sign-in. Check your password and try again.", loginFailed: true);
                if (depotId == RussianDepot)
                {
                    cb.Output($"Russian files failed (exit {exitCode}); continuing without them.");
                    continue;
                }
                throw new DownloadFailedException($"{DepotLabel(depotId)} failed with exit code {exitCode}. See the log for details.");
            }

            cb.Status("Applying loader files");
            await PostProcessAsync(patch, inGameName, installDir);
        }

        private async Task<(int ExitCode, bool LoginFailed)> RunDepotDownloaderAsync(
            string fileName, string arguments, string workDir, string username, DownloadCallbacks cb, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = fileName,
                Arguments              = arguments,
                WorkingDirectory       = workDir,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var promptLock = new SemaphoreSlim(1, 1);
            bool loginFailed = false;

            void HandleLine(string line)
            {
                cb.Output(line);
                if (line.Contains("InvalidPassword", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Access token was rejected", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Failed to authenticate", StringComparison.OrdinalIgnoreCase))
                    loginFailed = true;

                if (line.Contains("Steam Mobile App", StringComparison.OrdinalIgnoreCase))
                    cb.Status("Approve the sign-in in the Steam Mobile app");

                string trimmed = line.TrimEnd();
                if (!trimmed.EndsWith(':')) return;
                string low = trimmed.ToLowerInvariant();
                PromptKind? kind = null;
                if (low.Contains("auth code") || low.Contains("2 factor") || low.Contains("two-factor") ||
                    low.Contains("two factor") || low.Contains("steam guard"))
                    kind = PromptKind.GuardCode;
                else if (low.Contains("password"))
                    kind = PromptKind.Password;
                if (kind == null) return;

                _ = Task.Run(async () =>
                {
                    await promptLock.WaitAsync();
                    try
                    {
                        string text = kind == PromptKind.GuardCode
                            ? (low.Contains("email") ? "Enter the Steam Guard code sent to your email" : "Enter your Steam Guard code")
                            : $"Enter the Steam password for \"{username}\"";
                        string? response = await cb.Prompt(kind.Value, text);
                        if (response != null)
                            await process.StandardInput.WriteLineAsync(response);
                        else
                            try { process.Kill(entireProcessTree: true); } catch { }
                    }
                    catch { }
                    finally { promptLock.Release(); }
                });
            }

            process.Start();
            using var reg = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });

            // Character-level reads: Steam Guard prompts are not newline-terminated.
            Task Pump(StreamReader reader) => Task.Run(() =>
            {
                var buf = new StringBuilder();
                int ch;
                while ((ch = reader.Read()) != -1)
                {
                    if (ch == '\n')
                    {
                        HandleLine(buf.ToString().TrimEnd('\r'));
                        buf.Clear();
                        continue;
                    }
                    buf.Append((char)ch);
                    if (buf.Length >= 2 && buf[^2] == ':' && (buf[^1] == ' ' || buf[^1] == '\t') && LooksLikePrompt(buf))
                    {
                        HandleLine(buf.ToString().TrimEnd());
                        buf.Clear();
                    }
                }
                if (buf.Length > 0) HandleLine(buf.ToString());
            });

            var stdout = Pump(process.StandardOutput);
            var stderr = Pump(process.StandardError);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            ct.ThrowIfCancellationRequested();
            return (process.ExitCode, loginFailed);
        }

        private static bool LooksLikePrompt(StringBuilder buf)
        {
            string s = buf.ToString();
            return s.Contains("password", StringComparison.OrdinalIgnoreCase)
                || s.Contains("code", StringComparison.OrdinalIgnoreCase)
                || s.Contains("steam guard", StringComparison.OrdinalIgnoreCase)
                || s.Contains("2 factor", StringComparison.OrdinalIgnoreCase);
        }

        // ── Post-processing (loader files) ───────────────────────────────────

        private async Task PostProcessAsync(SeasonPatch patch, string username, string installDir)
        {
            username = LoaderConfig.Sanitize(username);
            await Task.Run(() =>
            {
                if (patch.IsHeatedMetal && patch.Code == "Y9S2")
                {
                    if (File.Exists(HMNewBloodZip))
                        ExtractZip(HMNewBloodZip, installDir);

                    // The HM archive ships its own DefaultArgs.dll; only add the auth bypass loaders.
                    TryCopy(Path.Combine(ThrowbackLoaderSourceDir, "steam_api64.dll"),     installDir);
                    TryCopy(Path.Combine(ThrowbackLoaderSourceDir, "upc_r2_loader64.dll"), installDir);
                    TryCopy(Path.Combine(ThrowbackLoaderSourceDir, "LaunchR6.bat"),        installDir);
                    LoaderConfig.WriteToml(installDir, username);
                }
                else if (patch.IsHeatedMetal)
                {
                    string heliosDir = Path.Combine(ResourcesDir, "HeliosLoader");
                    if (Directory.Exists(heliosDir)) CopyDirectoryRecursive(heliosDir, installDir, skipExisting: "HeliosLoader.json");

                    string hmDir = Path.Combine(ResourcesDir, "HeatedMetal");
                    if (Directory.Exists(hmDir))
                    {
                        foreach (var f in Directory.GetFiles(hmDir))
                            File.Copy(f, Path.Combine(installDir, Path.GetFileName(f)), overwrite: true);
                        string hmSub = Path.Combine(hmDir, "HeatedMetal");
                        if (Directory.Exists(hmSub))
                            CopyDirectoryRecursive(hmSub, Path.Combine(installDir, "HeatedMetal"));
                    }

                    TryCopy(Path.Combine(ThrowbackLoaderSourceDir, "LaunchR6.bat"), installDir);
                    if (!File.Exists(Path.Combine(installDir, "ThrowbackLoader.toml")))
                        TryCopy(Path.Combine(ThrowbackLoaderSourceDir, "ThrowbackLoader.toml"), installDir);
                    LoaderConfig.WriteHelios(installDir, username);
                }
                else
                {
                    CopyDirectoryRecursive(ThrowbackLoaderSourceDir, installDir, skipExisting: "ThrowbackLoader.toml");
                    KeepCorrectUplayDll(patch, installDir);
                    LoaderConfig.WriteToml(installDir, username);
                }
            });
        }

        private static void ExtractZip(string zipPath, string destDir)
        {
            string root = Path.GetFullPath(destDir).TrimEnd('\\') + "\\";
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string dest = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }

        /// <summary>
        /// Keeps only the Uplay/UPC loader this era imports:
        /// up to Y6S2 uplay_r1, Y6S3 upc_r1, Y6S4 and later upc_r2.
        /// </summary>
        private static void KeepCorrectUplayDll(SeasonPatch patch, string installDir)
        {
            int year = patch.Year, season = patch.Season;
            if (year == 0) return;
            string needed = year < 6 || (year == 6 && season <= 2) ? "uplay_r1_loader64.dll"
                : year == 6 && season == 3 ? "upc_r1_loader64.dll"
                : "upc_r2_loader64.dll";
            foreach (var dll in new[] { "uplay_r1_loader64.dll", "upc_r1_loader64.dll", "upc_r2_loader64.dll" })
            {
                if (dll == needed) continue;
                string path = Path.Combine(installDir, dll);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void CopyDirectoryRecursive(string src, string dest, string? skipExisting = null)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(src))
            {
                string target = Path.Combine(dest, Path.GetFileName(f));
                if (skipExisting != null && File.Exists(target) &&
                    string.Equals(Path.GetFileName(f), skipExisting, StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(f, target, overwrite: true);
            }
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectoryRecursive(d, Path.Combine(dest, Path.GetFileName(d)), skipExisting);
        }

        private static void TryCopy(string src, string destDir)
        {
            if (File.Exists(src)) File.Copy(src, Path.Combine(destDir, Path.GetFileName(src)), overwrite: true);
        }

        private static string FindDotnet()
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            return File.Exists(p) ? p : "dotnet";
        }

        private static string EscapeArg(string s) => s.Replace("\"", "\\\"");

        // ── Heated Metal updates ─────────────────────────────────────────────

        public async Task<HmUpdateInfo?> CheckHmUpdateAsync(string installedVersion, CancellationToken ct = default)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"R6ThrowbackLauncher/{AppInfo.Version}");
                string json = await http.GetStringAsync(HmVersionUrl, ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                string remoteVer   = root.GetProperty("version").GetString() ?? "0.0.0";
                string downloadUrl = root.GetProperty("downloadUrl").GetString() ?? string.Empty;
                string notes       = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                if (AppInfo.IsNewer(remoteVer, installedVersion) && !string.IsNullOrWhiteSpace(downloadUrl))
                    return new HmUpdateInfo(remoteVer, downloadUrl, notes);
                return null;
            }
            catch { return null; }
        }

        public async Task ApplyHmUpdateAsync(string downloadUrl, string installDir, Action<string> onStatus, CancellationToken ct = default)
        {
            string tmpZip = Path.Combine(Path.GetTempPath(), $"HM_Update_{Guid.NewGuid():N}.zip");
            try
            {
                onStatus("Downloading Heated Metal update");
                await DownloadFileAsync(downloadUrl, tmpZip, ct);
                onStatus("Extracting files");
                await ApplyHmArchiveAsync(tmpZip, installDir);
            }
            finally
            {
                try { File.Delete(tmpZip); } catch { }
            }
        }

        public async Task ApplyCustomHmArchiveAsync(string archivePath, string installDir, Action<string> onStatus)
        {
            onStatus("Applying Heated Metal archive");
            await ApplyHmArchiveAsync(archivePath, installDir);
        }

        private static async Task ApplyHmArchiveAsync(string archivePath, string installDir)
        {
            string ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".zip")
            {
                await Task.Run(() => ExtractZip(archivePath, installDir));
            }
            else if (ext == ".7z")
            {
                string z7 = Path.Combine(PrereqDir, "7z.exe");
                if (!File.Exists(z7))
                    throw new FileNotFoundException("7z.exe is missing. Download any season once so prerequisites are installed, then try again.", z7);
                await RunSilentAsync(z7, $"x -y -o\"{installDir}\" \"{archivePath}\" -aoa");
            }
            else
            {
                throw new NotSupportedException($"Unsupported archive format '{ext}'. Use a .zip or .7z file.");
            }
        }
    }
}
