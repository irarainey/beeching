using Beeching.Commands;

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
    }
}
