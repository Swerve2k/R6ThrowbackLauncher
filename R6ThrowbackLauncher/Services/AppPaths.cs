using System;
using System.IO;

namespace R6ThrowbackLauncher.Services
{
    public static class AppPaths
    {
        public static string BaseDir { get; } = AppDomain.CurrentDomain.BaseDirectory;

        public static string ResourcesDir => Path.Combine(BaseDir, "Resources");

        public static string DataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OperationThrowback");

        public static string DefaultLibrary => Path.Combine(DataDir, "Downloads");
        public static string BinDir => Path.Combine(DataDir, "bin");
        public static string PrerequisitesDir => Path.Combine(DataDir, "Prerequisites");
        public static string DownloadHistoryFile => Path.Combine(DataDir, "download_history.json");

        public static string SiegeDocumentsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Rainbow Six - Siege");
    }
}
