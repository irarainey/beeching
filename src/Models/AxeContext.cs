using Beeching.Commands;

namespace Beeching.Models
{
    internal class AxeContext
    {
        public AxeSettings Settings { get; }
        public string UserId { get; set; } = string.Empty;
        public string SubscriptionRole { get; set; } = "None";
        public bool IsSubscriptionRolePrivileged { get; set; }
        public CancellationToken CancellationToken { get; set; }

        public AxeContext(AxeSettings settings)
        {
            Settings = settings;
        }
    }
}
