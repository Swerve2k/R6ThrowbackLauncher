using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public sealed class DownloadService
    {
        private readonly string _baseDir;
        private readonly string _dataDir;

        public DownloadService(string baseDir, string? dataDir = null)
        {
            _baseDir = baseDir;
            _dataDir = dataDir ?? baseDir;
        }

        public string ResourcesDir => Path.Combine(_baseDir, "Resources");
        public string BackendScriptPath => Path.Combine(ResourcesDir, "backend.ps1");
        public string ManifestPath => Path.Combine(ResourcesDir, "manifest.json");
        public string OutputDir => Path.Combine(_dataDir, "Downloads");
        public string ThrowbackLoaderSourceDir => Path.Combine(ResourcesDir, "ThrowbackLoader");

        public async Task<int> RunDownloadAsync(
            SeasonPatch patch,
            string username,
            int maxDownloads,
            Action<string>? logCallback = null)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username is required.", nameof(username));

            if (!File.Exists(BackendScriptPath))
                throw new FileNotFoundException("backend.ps1 not found", BackendScriptPath);

            Directory.CreateDirectory(OutputDir);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = _dataDir,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            // Patch.Id looks like "Y5.S4.2"
            string[] parts = patch.Id.Split('.');
            string year = parts[0];      // "Y5"
            string season = parts[1];    // "S4"
            string patchKey = parts[2];  // "2"

            // UseShellExecute=true: pass args as string so console shows DepotDownloader's password prompt
            string outputDirArg = "\"" + OutputDir.Replace("\"", "`\"") + "\"";
            psi.Arguments = string.Join(" ", new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                "\"" + BackendScriptPath.Replace("\"", "`\"") + "\"",
                "-Year", "\"" + year.Replace("\"", "`\"") + "\"",
                "-Season", "\"" + season.Replace("\"", "`\"") + "\"",
                "-Patch", "\"" + patchKey.Replace("\"", "`\"") + "\"",
                "-Username", "\"" + username.Replace("\"", "`\"") + "\"",
                "-MaxDownloads", maxDownloads.ToString(),
                "-ManifestPath", "\"" + ManifestPath.Replace("\"", "`\"") + "\"",
                "-OutputDir", outputDirArg
            });

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to start PowerShell. " + ex.Message, ex);
            }

            await Task.Run(() => process.WaitForExit());
            return process.ExitCode;
        }

        public SeasonPatch WithInstallDir(SeasonPatch patch)
        {
            // Mirror backend.ps1 folder naming rule
            var hmPatch = "Y5.S4.2";
            string folderName;
            if (string.Equals(patch.Id, hmPatch, StringComparison.OrdinalIgnoreCase))
            {
                folderName = $"{patch.YearKey}{patch.SeasonKey}_{patch.SeasonName}HM";
            }
            else
            {
                folderName = $"{patch.YearKey}{patch.SeasonKey}_{patch.SeasonName}";
            }

            folderName = folderName.Replace(" ", string.Empty);
            var installDir = Path.Combine(OutputDir, folderName);

            return new SeasonPatch
            {
                Id = patch.Id,
                YearKey = patch.YearKey,
                SeasonKey = patch.SeasonKey,
                PatchKey = patch.PatchKey,
                SeasonName = patch.SeasonName,
                Date = patch.Date,
                DisplayName = patch.DisplayName,
                ArtworkPath = patch.ArtworkPath,
                InstallDir = installDir,
                IsInstalled = Directory.Exists(installDir)
            };
        }

        public void CopyThrowbackLoaderIntoInstallDir(SeasonPatch patch)
        {
            if (string.IsNullOrWhiteSpace(patch.InstallDir))
            {
                return;
            }

            if (!Directory.Exists(patch.InstallDir))
            {
                return;
            }

            if (!Directory.Exists(ThrowbackLoaderSourceDir))
            {
                return;
            }

            CopyDirectoryRecursive(ThrowbackLoaderSourceDir, patch.InstallDir);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(directory);
                var destSubDir = Path.Combine(destDir, dirName);
                CopyDirectoryRecursive(directory, destSubDir);
            }
        }
    }
}