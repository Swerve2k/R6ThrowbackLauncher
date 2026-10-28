using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>Resolves where each season lives across the configured library folders.</summary>
    public sealed class InstallLocator
    {
        public const string PartialMarker = ".throwback_partial";

        // In older builds (Y1S4 and nearby) RainbowSix.exe is a small Steam launcher stub and the game is
        // RainbowSixGame.exe, so that one goes first, like LaunchR6.bat.
        private static readonly string[] GameExecutables =
        {
            "RainbowSixGame.exe", "RainbowSix.exe", "RainbowSix_DX11.exe", "RainbowSix_Vulkan.exe",
        };

        private readonly Func<IReadOnlyList<string>> _libraries;

        public InstallLocator(Func<IReadOnlyList<string>> libraries) => _libraries = libraries;

        public IReadOnlyList<string> Libraries => _libraries();
        public string DefaultLibrary => Libraries.Count > 0 ? Libraries[0] : AppPaths.DefaultLibrary;

        public static string PrimaryFolderName(SeasonPatch patch)
        {
            string name = Compact(patch.BaseName);
            return patch.IsHeatedMetal ? $"{patch.Code}_{name}_HeatedMetal" : $"{patch.Code}_{name}";
        }

        public static IEnumerable<string> FolderNames(SeasonPatch patch)
        {
            yield return PrimaryFolderName(patch);
            if (!patch.IsHeatedMetal) yield break;
            string name = Compact(patch.BaseName);
            yield return $"{patch.Code}_{name}HeatedMetalHM";
            yield return $"{patch.Code}_{name}HeatedMetal";
            yield return $"{patch.Code}_{name}HM";
        }

        private static string Compact(string s) =>
            s.Replace(" ", "").Replace("—", "").Replace("-", "").Replace("'", "").Trim();

        public void Refresh(SeasonPatch patch)
        {
            foreach (var root in Libraries)
            {
                foreach (var name in FolderNames(patch))
                {
                    string dir = Path.Combine(root, name);
                    if (!Directory.Exists(dir)) continue;
                    patch.InstallDir = dir;
                    bool hasExe = FindExecutable(dir) != null;
                    bool marked = File.Exists(Path.Combine(dir, PartialMarker));
                    patch.IsInstalled = hasExe && !marked;
                    patch.IsPartial = !patch.IsInstalled;
                    return;
                }
            }
            patch.InstallDir = Path.Combine(DefaultLibrary, PrimaryFolderName(patch));
            patch.IsInstalled = false;
            patch.IsPartial = false;
        }

        public void RefreshAll(IEnumerable<SeasonPatch> patches)
        {
            foreach (var p in patches) Refresh(p);
        }

        public static string? FindExecutable(string dir, string? renderer = null)
        {
            if (renderer == "vulkan" && File.Exists(Path.Combine(dir, "RainbowSix_Vulkan.exe")))
                return Path.Combine(dir, "RainbowSix_Vulkan.exe");
            if (renderer == "dx11" && File.Exists(Path.Combine(dir, "RainbowSix_DX11.exe")))
                return Path.Combine(dir, "RainbowSix_DX11.exe");
            foreach (var exe in GameExecutables)
            {
                string path = Path.Combine(dir, exe);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Renderer choices that exist for an install, with "" as the default executable.</summary>
        public static IReadOnlyList<(string Key, string Label)> RendererOptions(string dir)
        {
            if (File.Exists(Path.Combine(dir, "RainbowSix_Vulkan.exe")))
                return new[] { ("", "DirectX 11"), ("vulkan", "Vulkan") };
            if (File.Exists(Path.Combine(dir, "RainbowSix_DX11.exe")))
                return new[] { ("", "DirectX 12"), ("dx11", "DirectX 11") };
            return Array.Empty<(string, string)>();
        }

        public bool IsInLibrary(string dir) =>
            Libraries.Any(root => dir.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

        /// <summary>Folders in library roots that no known season maps to.</summary>
        public List<string> UnrecognizedFolders(IEnumerable<SeasonPatch> patches)
        {
            var known = new HashSet<string>(patches.Where(p => !string.IsNullOrEmpty(p.InstallDir))
                .Select(p => Path.GetFullPath(p.InstallDir)), StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var root in Libraries)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                        if (!known.Contains(Path.GetFullPath(dir))) result.Add(dir);
                }
                catch { }
            }
            return result;
        }

        public static long DirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }).Sum(f => f.Length);
            }
            catch { return 0; }
        }

        public static void MarkPartial(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, PartialMarker), DateTime.UtcNow.ToString("o"));
        }

        public static void ClearPartial(string dir)
        {
            try { File.Delete(Path.Combine(dir, PartialMarker)); } catch { }
        }
    }
}
