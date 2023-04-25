using Beeching.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Linq;

namespace Beeching.Helpers
{
    internal class VersionHelper
    {
        internal static string GetVersion()
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

        internal static async Task<string?> GetLatestVersionAsync()
        {
            ILogger logger = NullLogger.Instance;
            CancellationToken cancellationToken = CancellationToken.None;

            SourceCacheContext cache = new();
            SourceRepository repository = Repository.Factory.GetCoreV3 ("https://api.nuget.org/v3/index.json");
            FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource> ();

            IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync (
                "beeching",
                cache,
                logger,
                cancellationToken);

            return versions.LastOrDefault()?.ToString();
        }

        internal static bool IsUpdateAvailable(string installedVersion, string latestVersion)
        {
            string[] parts = installedVersion.Split ('.');
            int major = int.Parse (parts[0]);
            int minor = int.Parse (parts[1]);
            int patch = int.Parse (parts[2]);
            int installedVersionNumber = major * 10000 + minor * 100 + patch;

            parts = latestVersion.Split ('.');
            major = int.Parse (parts[0]);
            minor = int.Parse (parts[1]);
            patch = int.Parse (parts[2]);
            int latestVersionNumber = major * 10000 + minor * 100 + patch;

            return latestVersionNumber > installedVersionNumber;
        }
    }
}
