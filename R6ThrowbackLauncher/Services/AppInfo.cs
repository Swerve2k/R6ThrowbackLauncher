using System;

namespace R6ThrowbackLauncher.Services
{
    public static class AppInfo
    {
        public const string Version     = "3.0.0";
        public const string Name        = "Operation Throwback";
        public const string GitHubOwner = "Swerve2k";
        public const string GitHubRepo  = "R6ThrowbackLauncher";
        public const string RepoUrl     = "https://github.com/" + GitHubOwner + "/" + GitHubRepo;

        public static bool IsNewer(string candidate, string current)
        {
            string a = candidate.Trim().TrimStart('v', 'V', '.'), b = current.Trim().TrimStart('v', 'V', '.');
            if (System.Version.TryParse(a, out var va) && System.Version.TryParse(b, out var vb)) return va > vb;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
