using System;
using System.Collections.Generic;

namespace R6ThrowbackLauncher.Models
{
    public sealed class SeasonPatch
    {
        public string Id { get; init; } = "";
        public string YearKey { get; init; } = "";
        public string SeasonKey { get; init; } = "";
        public string PatchKey { get; init; } = "";
        public string SeasonName { get; init; } = "";
        public string BaseName { get; init; } = "";
        public DateTime? Date { get; init; }
        public string DisplayName { get; init; } = "";
        public string ArtworkPath { get; init; } = "";
        public string Description { get; init; } = "";
        public string BuildId { get; init; } = "";
        public double SizeGb { get; init; }
        public bool IsHeatedMetal { get; init; }
        public IReadOnlyList<(string DepotId, string ManifestId)> Depots { get; init; } = Array.Empty<(string, string)>();

        public string InstallDir { get; set; } = "";
        public bool IsInstalled { get; set; }
        public bool IsPartial { get; set; }

        public string Code => $"{YearKey}{SeasonKey}";
        public int Year => int.TryParse(YearKey.TrimStart('Y'), out int y) ? y : 0;
        public int Season => int.TryParse(SeasonKey.TrimStart('S'), out int s) ? s : 0;
        public int Rank => Year * 10 + Season;
        public string ShortLabel => IsHeatedMetal ? $"{Code} {BaseName} (Heated Metal)" : $"{Code} {BaseName}";
    }

    public sealed class ManifestLoadResult
    {
        public IReadOnlyList<SeasonPatch> Patches { get; init; } = Array.Empty<SeasonPatch>();
    }

    public sealed record InfoOperator(string Name, string Gadget, string Summary);

    public sealed class SeasonInfo
    {
        public string Release { get; init; } = "";
        public IReadOnlyList<InfoOperator> Operators { get; init; } = Array.Empty<InfoOperator>();
        public IReadOnlyList<string> Maps { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Events { get; init; } = Array.Empty<string>();
    }
}
