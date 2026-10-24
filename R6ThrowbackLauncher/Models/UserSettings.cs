using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace R6ThrowbackLauncher.Models
{
    public sealed class QueuedDownload
    {
        [JsonPropertyName("patchId")]     public string PatchId { get; set; } = "";
        [JsonPropertyName("libraryRoot")] public string LibraryRoot { get; set; } = "";
        [JsonPropertyName("verify")]      public bool Verify { get; set; }
        [JsonPropertyName("paused")]      public bool Paused { get; set; }
    }

    public sealed class UserSettings
    {
        // Steam login used by DepotDownloader for downloads. Before 3.0 this also doubled as the community username.
        [JsonPropertyName("steamUsername")]
        public string SteamUsername { get; set; } = string.Empty;

        // Legacy plaintext password. Read once, migrated to SteamPasswordProtected, never written again.
        [JsonPropertyName("steamPassword")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SteamPassword { get; set; }

        [JsonPropertyName("steamPasswordProtected")]
        public string SteamPasswordProtected { get; set; } = string.Empty;

        [JsonPropertyName("rememberSteamLogin")]
        public bool RememberSteamLogin { get; set; } = true;

        [JsonPropertyName("steamTokenUser")]
        public string SteamTokenUser { get; set; } = string.Empty;

        [JsonPropertyName("inGameUsername")]
        public string InGameUsername { get; set; } = string.Empty;

        // Community identity, verified through Steam sign-in
        [JsonPropertyName("steamId")]
        public string SteamId { get; set; } = string.Empty;

        [JsonPropertyName("steamPersona")]
        public string SteamPersona { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("profileBio")]
        public string ProfileBio { get; set; } = string.Empty;

        // Stats carried over from a pre-3.0 profile, added on top of local playtime.
        [JsonPropertyName("statsBaselinePlaytime")]
        public long StatsBaselinePlaytime { get; set; }

        [JsonPropertyName("statsBaselineSessions")]
        public int StatsBaselineSessions { get; set; }

        [JsonPropertyName("legacyProfileClaimed")]
        public string LegacyProfileClaimed { get; set; } = string.Empty;

        [JsonPropertyName("memberSince")]
        public long MemberSince { get; set; }

        // Appearance
        [JsonPropertyName("themeMode")]
        public string ThemeMode { get; set; } = string.Empty;

        [JsonPropertyName("lightMode")]
        public bool LightMode { get; set; }

        [JsonPropertyName("uiScale")]
        public int UiScale { get; set; } = 100;

        [JsonPropertyName("reduceMotion")]
        public bool ReduceMotion { get; set; }

        [JsonPropertyName("artworkColor")]
        public string ArtworkMode { get; set; } = "color";

        // Library
        [JsonPropertyName("pinnedSeasons")]
        public List<string> PinnedSeasons { get; set; } = new();

        [JsonPropertyName("librarySortMode")]
        public string LibrarySortMode { get; set; } = "season";

        [JsonPropertyName("libraryView")]
        public string LibraryView { get; set; } = "grid";

        [JsonPropertyName("seasonNotes")]
        public Dictionary<string, string> SeasonNotes { get; set; } = new();

        [JsonPropertyName("showHeroBanner")]
        public bool ShowHeroBanner { get; set; } = true;

        [JsonPropertyName("showRecentlyPlayed")]
        public bool ShowRecentlyPlayed { get; set; } = true;

        [JsonPropertyName("launchArgs")]
        public Dictionary<string, string> LaunchArgs { get; set; } = new();

        [JsonPropertyName("renderer")]
        public Dictionary<string, string> Renderer { get; set; } = new();

        // Downloads
        [JsonPropertyName("customDownloadPath")]
        public string CustomDownloadPath { get; set; } = string.Empty;

        [JsonPropertyName("libraryFolders")]
        public List<string> LibraryFolders { get; set; } = new();

        [JsonPropertyName("maxDownloads")]
        public int MaxDownloads { get; set; } = 16;

        [JsonPropertyName("downloadQueue")]
        public List<QueuedDownload> DownloadQueue { get; set; } = new();

        // Behavior
        [JsonPropertyName("showTrayNotifications")]
        public bool ShowTrayNotifications { get; set; } = true;

        [JsonPropertyName("closeToTray")]
        public bool CloseToTray { get; set; }

        [JsonPropertyName("discordPresence")]
        public bool DiscordPresence { get; set; } = true;

        [JsonPropertyName("gameStateReader")]
        public bool GameStateReader { get; set; } = true;

        [JsonPropertyName("inGameOverlay")]
        public bool InGameOverlay { get; set; } = true;

        // Unlock all through Liberator (Y1 to Y9 only)
        [JsonPropertyName("liberatorEnabled")]
        public bool LiberatorEnabled { get; set; }

        [JsonPropertyName("checkUpdatesOnStartup")]
        public bool CheckUpdatesOnStartup { get; set; } = true;

        // Updates and announcements
        [JsonPropertyName("lastRunVersion")]
        public string LastRunVersion { get; set; } = string.Empty;

        [JsonPropertyName("dismissedBroadcasts")]
        public List<string> DismissedBroadcasts { get; set; } = new();

        // Heated Metal update tracking
        [JsonPropertyName("hmVersion")]
        public string HmVersion { get; set; } = "1.0.0";

        [JsonPropertyName("hmSkipVersion")]
        public string HmSkipVersion { get; set; } = string.Empty;
    }
}
