using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public sealed class LibraryService
    {
        private const string BaseArguments = "/belaunch /nologo";

        public async Task<Process?> LaunchSeasonAsync(SeasonPatch patch, string? renderer = null, string? extraArgs = null)
        {
            if (!Directory.Exists(patch.InstallDir))
                return null;

            string? exePath = InstallLocator.FindExecutable(patch.InstallDir, renderer);
            if (exePath == null)
                return null;

            string args = string.IsNullOrWhiteSpace(extraArgs) ? BaseArguments : $"{BaseArguments} {extraArgs.Trim()}";

            // UseShellExecute = true gives the game the same interactive desktop token as a
            // double-click, which DirectX initialisation and the loader hooks depend on.
            var psi = new ProcessStartInfo
            {
                FileName         = exePath,
                Arguments        = args,
                WorkingDirectory = patch.InstallDir,
                UseShellExecute  = true,
            };

            var launched = await Task.Run(() => Process.Start(psi));

            // Shell execution can hand back a short-lived helper; find the real game process.
            if (launched == null || launched.HasExited)
                launched = await FindGameProcessAsync(patch.InstallDir, timeoutMs: 8000);

            return launched;
        }

        private static async Task<Process?> FindGameProcessAsync(string installDir, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var running = GameProcessService.Running();
                foreach (var game in running)
                {
                    if (!GameProcessService.IsRunningFrom(installDir, new[] { game })) continue;
                    try { return Process.GetProcessById(game.Pid); } catch { }
                }
                await Task.Delay(500);
            }
            return null;
        }
    }
}
