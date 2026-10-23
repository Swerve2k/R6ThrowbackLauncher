using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace R6ThrowbackLauncher.Services
{
    public static class GameProcessService
    {
        public static readonly string[] ProcessNames =
        {
            "RainbowSix", "RainbowSixGame", "RainbowSix_DX11", "RainbowSix_DX12", "RainbowSix_Vulkan", "RainbowSix_BE",
        };

        public sealed record RunningGame(int Pid, string Folder);

        /// <summary>Running Siege processes with the folder their executable lives in (empty if inaccessible).</summary>
        public static List<RunningGame> Running()
        {
            var result = new List<RunningGame>();
            foreach (var name in ProcessNames)
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(name); } catch { continue; }
                foreach (var p in procs)
                {
                    string folder = "";
                    try
                    {
                        string? exe = p.MainModule?.FileName;
                        if (exe != null) folder = Path.GetDirectoryName(Path.GetFullPath(exe)) ?? "";
                    }
                    catch { }
                    result.Add(new RunningGame(p.Id, folder));
                    p.Dispose();
                }
            }
            return result;
        }

        public static bool IsRunningFrom(string installDir, IReadOnlyList<RunningGame> running)
        {
            if (string.IsNullOrEmpty(installDir)) return false;
            string full = Path.GetFullPath(installDir).TrimEnd('\\');
            string resolved = ResolveJunction(full);
            foreach (var g in running)
            {
                if (g.Folder.Length == 0) continue;
                if (string.Equals(g.Folder, full, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(g.Folder, resolved, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ResolveJunction(string dir)
        {
            try
            {
                var info = new DirectoryInfo(dir);
                if (info.LinkTarget is string target) return Path.GetFullPath(target).TrimEnd('\\');
            }
            catch { }
            return dir;
        }

        /// <summary>Kills every known Siege process (STOP GAME behavior).</summary>
        public static void StopAll(Process? tracked)
        {
            try { tracked?.Kill(entireProcessTree: true); } catch { }
            foreach (var name in ProcessNames)
            {
                Process[] procs;
                try { procs = Process.GetProcessesByName(name); } catch { continue; }
                foreach (var p in procs)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    p.Dispose();
                }
            }
        }
    }
}
