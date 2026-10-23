using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace R6ThrowbackLauncher.Services
{
    public sealed record TextureTier(int Level, string Quality, long Bytes);

    public sealed class ShearsScan
    {
        public long VideoBytes { get; init; }
        public long EventBytes { get; init; }
        public IReadOnlyList<TextureTier> Tiers { get; init; } = Array.Empty<TextureTier>();
        public bool HasAnything => VideoBytes > 0 || EventBytes > 0 || Tiers.Count > 1;
    }

    /// <summary>
    /// Reclaims disk space from an install by removing intro videos, event playlist data
    /// and texture tiers above a chosen quality. Verify restores anything removed.
    /// </summary>
    public static class ShearsService
    {
        public static readonly string[] Qualities = { "Low", "Medium", "High", "Very High", "Ultra" };
        private static readonly Regex TextureRx = new(@"_textures(\d)", RegexOptions.IgnoreCase);

        public static ShearsScan Scan(string dir, string? eventPattern)
        {
            return new ShearsScan
            {
                VideoBytes = SizeOf(VideoFiles(dir)),
                EventBytes = eventPattern == null ? 0 : SizeOf(EventFiles(dir, eventPattern)),
                Tiers = TextureForges(dir)
                    .GroupBy(t => t.Level)
                    .OrderBy(g => g.Key)
                    .Select(g => new TextureTier(g.Key, Qualities[g.Key], g.Sum(t => t.Bytes)))
                    .ToList(),
            };
        }

        public static long RemoveVideos(string dir) => Delete(VideoFiles(dir));

        public static long RemoveEvents(string dir, string eventPattern) => Delete(EventFiles(dir, eventPattern));

        public static long RemoveTexturesAbove(string dir, int keepLevel) =>
            Delete(TextureForges(dir).Where(t => t.Level > keepLevel).Select(t => t.Path).ToList());

        private static IEnumerable<(string Path, int Level, long Bytes)> TextureForges(string dir)
        {
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.EnumerateFiles(dir, "*.forge"))
            {
                var m = TextureRx.Match(Path.GetFileNameWithoutExtension(f));
                if (!m.Success) continue;
                int level = m.Groups[1].Value[0] - '0';
                if (level < 0 || level >= Qualities.Length) continue;
                long size;
                try { size = new FileInfo(f).Length; } catch { continue; }
                yield return (f, level, size);
            }
        }

        private static List<string> VideoFiles(string dir)
        {
            string videos = Path.Combine(dir, "videos");
            if (!Directory.Exists(videos)) return new();
            try { return Directory.GetFiles(videos, "*", SearchOption.AllDirectories).ToList(); }
            catch { return new(); }
        }

        private static List<string> EventFiles(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) return new();
            return Directory.EnumerateFiles(dir)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    return (ext == ".forge" || ext == ".depgraphbin")
                           && Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(pattern);
                })
                .ToList();
        }

        private static long SizeOf(IEnumerable<string> files)
        {
            long total = 0;
            foreach (var f in files)
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }

        private static long Delete(IReadOnlyCollection<string> files)
        {
            long freed = 0;
            int failed = 0;
            foreach (var f in files)
            {
                try
                {
                    long len = new FileInfo(f).Length;
                    File.Delete(f);
                    freed += len;
                }
                catch { failed++; }
            }
            if (failed > 0) throw new IOException($"{failed} file(s) could not be removed. Close the game and try again.");
            return freed;
        }
    }
}
