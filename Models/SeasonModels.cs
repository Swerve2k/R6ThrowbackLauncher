using System;
using System.Collections.Generic;

namespace R6ThrowbackLauncher.Models
{
    public sealed class SeasonPatch
    {
        public string Id { get; init; } = "";          // e.g. "Y5.S4.2"
        public string YearKey { get; init; } = "";     // "Y5"
        public string SeasonKey { get; init; } = "";   // "S4"
        public string PatchKey { get; init; } = "";    // "2"
        public string SeasonName { get; init; } = "";  // "Neon Dawn"
        public DateTime? Date { get; init; }
        public string DisplayName { get; init; } = ""; // for combo box display
        public string ArtworkPath { get; init; } = ""; // optional image path
        public string InstallDir { get; init; } = "";  // where it was downloaded
        public bool IsInstalled { get; set; }
    }

    public sealed class ManifestLoadResult
    {
        public IReadOnlyList<SeasonPatch> Patches { get; init; } = Array.Empty<SeasonPatch>();
    }
}