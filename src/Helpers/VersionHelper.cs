using Beeching.Commands;
using System.Text.Json;

namespace Beeching.Helpers
{
    internal class VersionHelper
    {
        private static readonly HttpClient _httpClient = new();

        public static string GetVersion()
        {
            var version = typeof(AxeCommand).Assembly.GetName().Version;
            if (version != null)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            else
            {
                return "Unknown";
            }
        }

        public static async Task<string?> GetLatestVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(Constants.NuGetPackageUrl);
                using var json = JsonDocument.Parse(response);
                var versions = json.RootElement.GetProperty("versions");
                var lastVersion = versions.EnumerateArray().LastOrDefault();
                return lastVersion.ValueKind != JsonValueKind.Undefined ? lastVersion.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsUpdateAvailable(string installedVersion, string latestVersion)
        {
            string[] parts = installedVersion.Split('.');
            int major = int.Parse(parts[0]);
            int minor = int.Parse(parts[1]);
            int patch = int.Parse(parts[2]);
            int installedVersionNumber = major * 10000 + minor * 100 + patch;

            parts = latestVersion.Split('.');
            major = int.Parse(parts[0]);
            minor = int.Parse(parts[1]);
            patch = int.Parse(parts[2]);
            int latestVersionNumber = major * 10000 + minor * 100 + patch;

            return latestVersionNumber > installedVersionNumber;
        }
    }
}
