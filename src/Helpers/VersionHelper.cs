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
            if (Version.TryParse(installedVersion, out var installed) && Version.TryParse(latestVersion, out var latest))
            {
                return latest > installed;
            }
            return false;
        }
    }
}
