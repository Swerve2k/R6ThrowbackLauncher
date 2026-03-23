using System.Text.Json.Serialization;

namespace R6ThrowbackLauncher.Models
{
    public sealed class UserSettings
    {
        [JsonPropertyName("steamUsername")]
        public string SteamUsername { get; set; } = string.Empty;

        [JsonPropertyName("accentR")]
        public byte AccentR { get; set; } = 0xDC;

        [JsonPropertyName("accentG")]
        public byte AccentG { get; set; } = 0x14;

        [JsonPropertyName("accentB")]
        public byte AccentB { get; set; } = 0x3C;

        [JsonPropertyName("showBorder")]
        public bool ShowBorder { get; set; } = false;
    }
}

