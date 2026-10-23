using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using R6ThrowbackLauncher.Models;

namespace R6ThrowbackLauncher.Services
{
    public static class SeasonCatalog
    {
        public static string ManifestPath => Path.Combine(AppPaths.ResourcesDir, "manifest.json");

        public static List<SeasonPatch> Load()
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException("manifest.json not found", ManifestPath);

            using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath));
            var patches = new List<SeasonPatch>();

            foreach (var yearProp in doc.RootElement.EnumerateObject())
            {
                string yearKey = yearProp.Name;
                foreach (var seasonProp in yearProp.Value.EnumerateObject())
                {
                    string seasonKey = seasonProp.Name;
                    var seasonObj = seasonProp.Value;
                    if (!seasonObj.TryGetProperty("name", out var nameEl)) continue;
                    string baseName = nameEl.GetString() ?? "Unknown";
                    string description = seasonObj.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";

                    foreach (var patchProp in seasonObj.EnumerateObject())
                    {
                        if (patchProp.Name is "name" or "description") continue;
                        var p = patchProp.Value;
                        string patchKey = patchProp.Name;

                        DateTime? date = null;
                        if (p.TryGetProperty("date", out var dateEl) &&
                            DateTime.TryParse(dateEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                            date = parsed;

                        bool isHm = p.TryGetProperty("isHeatedMetal", out var hmEl) && hmEl.ValueKind == JsonValueKind.True;
                        string build = p.TryGetProperty("build", out var bEl) ? bEl.ToString() : "";
                        double size = p.TryGetProperty("sizeGb", out var sEl) && sEl.ValueKind == JsonValueKind.Number ? sEl.GetDouble() : 0;

                        var depots = new List<(string, string)>();
                        foreach (var prop in p.EnumerateObject())
                        {
                            if (!long.TryParse(prop.Name, out _)) continue;
                            string manifestId = prop.Value.ValueKind == JsonValueKind.Number
                                ? prop.Value.GetRawText()
                                : prop.Value.GetString() ?? "";
                            depots.Add((prop.Name, manifestId));
                        }

                        string seasonName = isHm ? $"{baseName} - Heated Metal" : baseName;
                        string display = isHm
                            ? $"{yearKey}{seasonKey} {baseName} (Heated Metal)"
                            : $"{yearKey}{seasonKey} {baseName}, patch {patchKey}";

                        patches.Add(new SeasonPatch
                        {
                            Id = $"{yearKey}.{seasonKey}.{patchKey}",
                            YearKey = yearKey,
                            SeasonKey = seasonKey,
                            PatchKey = patchKey,
                            SeasonName = seasonName,
                            BaseName = baseName,
                            Date = date,
                            DisplayName = display,
                            ArtworkPath = FindArtwork(yearKey, seasonKey, baseName),
                            Description = description,
                            BuildId = build,
                            SizeGb = size,
                            IsHeatedMetal = isHm,
                            Depots = depots,
                        });
                    }
                }
            }

            return patches
                .OrderBy(p => p.Year).ThenBy(p => p.Season).ThenBy(p => p.IsHeatedMetal).ThenBy(p => p.PatchKey)
                .ToList();
        }

        private static string FindArtwork(string yearKey, string seasonKey, string baseName)
        {
            string dir = Path.Combine(AppPaths.ResourcesDir, "Artwork");
            string[] candidates =
            {
                $"{yearKey}_{seasonKey}_{baseName.Replace(' ', '_')}.png",
                $"{yearKey}_{seasonKey}_{baseName.Replace(" ", "")}.png",
            };
            foreach (var c in candidates)
            {
                string path = Path.Combine(dir, c);
                if (File.Exists(path)) return path;
            }
            try
            {
                return Directory.EnumerateFiles(dir, $"{yearKey}_{seasonKey}_*.png").FirstOrDefault() ?? "";
            }
            catch { return ""; }
        }

        /// <summary>Seasons whose event playlists ship as removable "*events*" forge files.</summary>
        public static string? EventFilePattern(SeasonPatch patch)
        {
            if (patch.Code == "Y3S1") return "fury";
            return patch.Rank >= 41 ? "events" : null;
        }
    }
}
