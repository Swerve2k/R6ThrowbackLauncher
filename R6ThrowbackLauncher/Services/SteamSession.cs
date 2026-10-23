using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace R6ThrowbackLauncher.Services
{
    /// <summary>DepotDownloader keeps its "remember password" login token in .NET isolated storage.</summary>
    public static class SteamSession
    {
        private static string IsolatedStorageRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IsolatedStorage");

        private static IEnumerable<string> AccountFiles() =>
            !Directory.Exists(IsolatedStorageRoot) ? Enumerable.Empty<string>() :
            Directory.EnumerateFiles(IsolatedStorageRoot, "account.config", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            }).Where(f => f.Contains($"{Path.DirectorySeparatorChar}AssemFiles{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        /// <summary>Deletes stored DepotDownloader login tokens. Returns how many were removed.</summary>
        public static int SignOut()
        {
            int removed = 0;
            foreach (var f in AccountFiles().ToList())
            {
                try { File.Delete(f); removed++; } catch { }
            }
            return removed;
        }

        /// <summary>
        /// True when DepotDownloader on this PC holds a saved login for the account, which means that
        /// account signed in to Steam here. The store is deflated protobuf; names are length-prefixed strings.
        /// </summary>
        public static bool HasSavedLogin(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName)) return false;
            byte[] name = Encoding.UTF8.GetBytes(accountName.Trim().ToLowerInvariant());
            if (name.Length is 0 or > 127) return false;
            foreach (var file in AccountFiles())
            {
                try
                {
                    using var fs = File.OpenRead(file);
                    using var deflate = new DeflateStream(fs, CompressionMode.Decompress);
                    using var ms = new MemoryStream();
                    deflate.CopyTo(ms);
                    byte[] data = ms.ToArray();
                    for (int i = 1; i + name.Length <= data.Length; i++)
                    {
                        if (data[i - 1] != name.Length) continue;
                        int j = 0;
                        while (j < name.Length && AsciiLower(data[i + j]) == name[j]) j++;
                        if (j == name.Length) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static byte AsciiLower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 32) : b;
    }
}
