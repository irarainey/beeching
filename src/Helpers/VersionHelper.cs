using Beeching.Commands;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Beeching.Helpers
{
    internal class VersionHelper
    {
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
            SourceCacheContext cache = new();
            SourceRepository repository = Repository.Factory.GetCoreV3(Constants.NuGetBaseUrl);
            FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();
            IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(
                Constants.Beeching,
                cache,
                NullLogger.Instance,
                CancellationToken.None
            );

            return versions.LastOrDefault()?.ToString();
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
