using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public sealed class SettingsService
    {
        private static readonly int[] ScaleLevels = { 80, 90, 100, 110, 125 };
        private readonly string _settingsPath;

        // Keys from earlier versions that are no longer stored (profile images, wallpaper, window outline).
        private static readonly string[] RemovedKeys =
        {
            "\"profileAvatarBase64\"", "\"profileBannerBase64\"", "\"profileBannerColor\"",
            "\"wallpaperPath\"", "\"wallpaperOpacity\"", "\"monochromeWallpaper\"", "\"showBorder\"", "\"artworkMode\"",
        };

        private bool _hadRemovedKeys;

        public UserSettings Settings { get; private set; } = new();
        public string? LoadWarning { get; private set; }

        /// <summary>True when a settings file from an earlier run was found.</summary>
        public bool LoadedExisting { get; private set; }

        public SettingsService()
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            _settingsPath = Path.Combine(AppPaths.DataDir, "settings.json");
        }

        public static int[] AllowedScales => ScaleLevels;

        public void Load()
        {
            if (File.Exists(_settingsPath))
            {
                LoadedExisting = true;
                try
                {
                    string text = File.ReadAllText(_settingsPath);
                    Settings = JsonSerializer.Deserialize<UserSettings>(text) ?? new UserSettings();
                    _hadRemovedKeys = RemovedKeys.Any(k => text.Contains(k, StringComparison.Ordinal));
                }
                catch (JsonException)
                {
                    string backup = _settingsPath + ".broken";
                    try { File.Copy(_settingsPath, backup, overwrite: true); } catch { }
                    LoadWarning = "Settings were unreadable and have been reset. A backup was saved as settings.json.broken.";
                    Settings = new UserSettings();
                }
                catch (IOException ex)
                {
                    LoadWarning = "Settings could not be read: " + ex.Message;
                    Settings = new UserSettings();
                }
            }

            if (Migrate()) Save();
        }

        private bool Migrate()
        {
            var s = Settings;
            bool changed = _hadRemovedKeys;

            if (string.IsNullOrEmpty(s.ThemeMode))
            {
                s.ThemeMode = s.LightMode ? "light" : "dark";
                changed = true;
            }

            if (s.SteamPassword != null)
            {
                if (s.SteamPassword.Length > 0 && string.IsNullOrEmpty(s.SteamPasswordProtected))
                {
                    try { s.SteamPasswordProtected = SecretProtector.Protect(s.SteamPassword); } catch { }
                }
                s.SteamPassword = null;
                changed = true;
            }

            s.LibraryFolders = s.LibraryFolders
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().TrimEnd('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (s.LibraryFolders.Count == 0)
            {
                s.LibraryFolders.Add(string.IsNullOrWhiteSpace(s.CustomDownloadPath)
                    ? AppPaths.DefaultLibrary
                    : s.CustomDownloadPath.TrimEnd('\\', '/'));
                changed = true;
            }

            if (!ScaleLevels.Contains(s.UiScale)) { s.UiScale = 100; changed = true; }
            if (s.MaxDownloads is < 1 or > 100) { s.MaxDownloads = 16; changed = true; }

            return changed;
        }

        public void Save()
        {
            string tmp = _settingsPath + ".tmp";
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, _settingsPath, overwrite: true);
        }
    }
}
