using System;
using System.IO;
using System.Text.Json;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public sealed class SettingsService
    {
        private readonly string _settingsPath;
        public UserSettings Settings { get; private set; } = new();

        public SettingsService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OperationThrowback");
            Directory.CreateDirectory(appData);
            _settingsPath = Path.Combine(appData, "settings.json");
        }

        public void Load()
        {
            if (!File.Exists(_settingsPath)) return;
            try
            {
                var json = File.ReadAllText(_settingsPath);
                Settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
            }
            catch { Settings = new UserSettings(); }
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
    }
}
