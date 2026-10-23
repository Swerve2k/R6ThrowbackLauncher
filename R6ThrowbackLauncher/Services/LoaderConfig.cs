using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>Reads and writes the in-game username for ThrowbackLoader and HeliosLoader installs.</summary>
    public static class LoaderConfig
    {
        public const int MaxUsernameLength = 16;
        public const string DefaultUsername = "ThrowbackUser";
        // Quotes, backslashes and control characters would break the TOML/JSON loader files.
        private static readonly Regex Forbidden = new(@"['""\\\x00-\x1F\x7F]");
        private static readonly Regex TomlUsername = new(@"username\s*=\s*'[^']*'");

        public static string? Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
            if (name.Length > MaxUsernameLength) return $"Use {MaxUsernameLength} characters or fewer.";
            if (name != name.Trim()) return "Remove spaces at the start or end.";
            if (Forbidden.IsMatch(name)) return "Quotes and backslashes aren't allowed.";
            return null;
        }

        public static string Sanitize(string name)
        {
            var cleaned = Forbidden.Replace(name ?? "", "").Trim();
            if (cleaned.Length > MaxUsernameLength) cleaned = cleaned[..MaxUsernameLength].Trim();
            return cleaned.Length == 0 ? DefaultUsername : cleaned;
        }

        private static string TomlPath(string dir) => Path.Combine(dir, "ThrowbackLoader.toml");
        private static string HeliosPath(string dir) => Path.Combine(dir, "HeliosLoader.json");

        public static string? Read(SeasonPatch patch)
        {
            try
            {
                if (File.Exists(HeliosPath(patch.InstallDir)))
                {
                    var node = JsonNode.Parse(File.ReadAllText(HeliosPath(patch.InstallDir)));
                    return node?["Username"]?.GetValue<string>();
                }
                if (File.Exists(TomlPath(patch.InstallDir)))
                {
                    var m = Regex.Match(File.ReadAllText(TomlPath(patch.InstallDir)), @"username\s*=\s*'([^']*)'");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            catch { }
            return null;
        }

        public static void Write(SeasonPatch patch, string username)
        {
            username = Sanitize(username);
            string dir = patch.InstallDir;
            bool helios = File.Exists(HeliosPath(dir)) || (patch.IsHeatedMetal && patch.Code != "Y9S2");
            if (helios) WriteHelios(dir, username);
            else WriteToml(dir, username);
        }

        public static void WriteToml(string dir, string username)
        {
            string path = TomlPath(dir);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, $"[general]\r\nusername = '{username}'\r\n\r\n[advanced]\r\ncustom_user_id = ''\r\n");
                return;
            }
            string text = File.ReadAllText(path);
            text = TomlUsername.IsMatch(text)
                ? TomlUsername.Replace(text, $"username = '{username}'", 1)
                : $"[general]\r\nusername = '{username}'\r\n\r\n" + text;
            File.WriteAllText(path, text);
        }

        public static void WriteHelios(string dir, string username)
        {
            string path = HeliosPath(dir);
            JsonObject obj;
            try { obj = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject(); }
            catch { obj = new JsonObject(); }
            obj["Username"] = username;
            obj["ProfileID"] ??= "00000000-0000-0000-0000-000000000000";
            obj["Email"] ??= "ConfigMan@ubisoft.com";
            obj["SavePath"] ??= "SAVE_GAMES";
            obj["Language"] ??= "en-US";
            obj["Offline"] ??= true;
            obj["UseGameProductID"] ??= true;
            obj["ProductID"] ??= 1337;
            File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Finds the GameSettings.ini folder Siege creates for a ThrowbackLoader user id.</summary>
        public static string GameSettingsPath(SeasonPatch patch)
        {
            string siege = AppPaths.SiegeDocumentsDir;
            string compact = patch.BaseName.Replace(" ", "");
            string[] names =
            {
                $"R6Throwback_{patch.Code}_{compact}",
                $"R6Throwback_{patch.Code}_{patch.BaseName.Replace(' ', '_')}",
            };
            foreach (var n in names)
            {
                string dir = Path.Combine(siege, n);
                if (Directory.Exists(dir)) return Path.Combine(dir, "GameSettings.ini");
            }
            try
            {
                if (Directory.Exists(siege))
                    foreach (var d in Directory.GetDirectories(siege, $"R6Throwback_{patch.Code}_*"))
                        return Path.Combine(d, "GameSettings.ini");
            }
            catch { }
            return Path.Combine(siege, names[0], "GameSettings.ini");
        }
    }
}
