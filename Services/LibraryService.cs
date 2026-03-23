using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public sealed class LibraryService
    {
        public async Task<bool> LaunchSeasonAsync(SeasonPatch patch)
        {
            if (!Directory.Exists(patch.InstallDir))
                return false;

            string launchBat = Path.Combine(patch.InstallDir, "LaunchR6.bat");
            string exe = Path.Combine(patch.InstallDir, "RainbowSix.exe");

            string? fileToRun = null;
            string arguments = string.Empty;

            if (File.Exists(launchBat))
            {
                fileToRun = "cmd.exe";
                arguments = $"/C \"\"{launchBat}\"\"";
            }
            else if (File.Exists(exe))
            {
                fileToRun = exe;
            }

            if (fileToRun == null)
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = fileToRun,
                Arguments = arguments,
                WorkingDirectory = patch.InstallDir,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            await Task.Run(() => Process.Start(psi));
            return true;
        }
    }
}