using Beeching.Models;

namespace Beeching.Helpers
{
    internal static class ResourceOrderingHelper
    {
        // Lower priority = deleted first. Types not listed default to 50.
        private static readonly Dictionary<string, int> TypePriority = new(StringComparer.OrdinalIgnoreCase)
        {
            // Tier 0 — top-level consumers that hold references to many other resources
            ["Microsoft.Compute/virtualMachines"] = 0,
            ["Microsoft.Compute/virtualMachineScaleSets"] = 0,
            ["Microsoft.ContainerService/managedClusters"] = 0,
            ["Microsoft.Databricks/workspaces"] = 0,
            ["Microsoft.ServiceFabric/clusters"] = 0,
            ["Microsoft.HDInsight/clusters"] = 0,
            ["Microsoft.MachineLearningServices/workspaces"] = 0,
            ["Microsoft.RecoveryServices/vaults"] = 0,

            // Tier 5 — high-level app/data services that attach to networking and storage
            ["Microsoft.App/containerApps"] = 5,
            ["Microsoft.ContainerInstance/containerGroups"] = 5,
            ["Microsoft.Synapse/workspaces"] = 5,
            ["Microsoft.Kusto/clusters"] = 5,
            ["Microsoft.SignalRService/signalR"] = 5,
            ["Microsoft.Web/sites"] = 5,
            ["Microsoft.Web/sites/slots"] = 5,
            ["Microsoft.Logic/workflows"] = 5,
            ["Microsoft.Insights/autoscaleSettings"] = 5,
            ["Microsoft.Network/networkWatchers/flowLogs"] = 5,

            // Tier 8 — environments/hosts that tier-5 resources depend on
            ["Microsoft.App/managedEnvironments"] = 8,

            // Tier 10 — services that depend on networking / platform primitives
            ["Microsoft.Sql/servers/databases"] = 10,
            ["Microsoft.Sql/servers/elasticPools"] = 10,
            ["Microsoft.DBforPostgreSQL/flexibleServers"] = 10,
            ["Microsoft.DBforMySQL/flexibleServers"] = 10,
            ["Microsoft.DocumentDB/databaseAccounts"] = 10,
            ["Microsoft.Cache/redis"] = 10,
            ["Microsoft.EventHub/namespaces"] = 10,
            ["Microsoft.ServiceBus/namespaces"] = 10,
            ["Microsoft.Network/applicationGateways"] = 10,
            ["Microsoft.Network/loadBalancers"] = 10,
            ["Microsoft.Network/privateEndpoints"] = 10,
            ["Microsoft.Network/bastionHosts"] = 10,
            ["Microsoft.Network/virtualNetworkGateways"] = 10,
            ["Microsoft.Network/localNetworkGateways"] = 10,
            ["Microsoft.Network/azureFirewalls"] = 10,
            ["Microsoft.Network/frontDoors"] = 10,
            ["Microsoft.Network/natGateways"] = 10,
            ["Microsoft.Cdn/profiles"] = 10,
            ["Microsoft.Network/trafficManagerProfiles"] = 10,
            ["Microsoft.ApiManagement/service"] = 10,
            ["Microsoft.CognitiveServices/accounts"] = 10,
            ["Microsoft.Search/searchServices"] = 10,
            ["Microsoft.Automation/automationAccounts"] = 10,
            ["Microsoft.Network/networkWatchers"] = 10,

            // Tier 15 — connection/peering resources that reference gateways or VNets
            ["Microsoft.Network/connections"] = 15,
            ["Microsoft.Network/virtualNetworkPeerings"] = 15,
            ["Microsoft.Network/privateDnsZones/virtualNetworkLinks"] = 15,
            ["Microsoft.Web/connections"] = 15,

            // Tier 20 — ancillary resources still referenced by tier-10 items
            ["Microsoft.Compute/disks"] = 20,
            ["Microsoft.Compute/snapshots"] = 20,
            ["Microsoft.Compute/images"] = 20,
            ["Microsoft.Compute/availabilitySets"] = 20,
            ["Microsoft.Compute/proximityPlacementGroups"] = 20,
            ["Microsoft.Network/networkInterfaces"] = 20,
            ["Microsoft.Network/publicIPAddresses"] = 20,
            ["Microsoft.Network/publicIPPrefixes"] = 20,
            ["Microsoft.Network/privateEndpoints/privateDnsZoneGroups"] = 20,
            ["Microsoft.ContainerRegistry/registries"] = 20,
            ["Microsoft.Storage/storageAccounts"] = 20,

            // Tier 30 — foundational networking and platform resources
            ["Microsoft.Network/networkSecurityGroups"] = 30,
            ["Microsoft.Network/applicationSecurityGroups"] = 30,
            ["Microsoft.Network/routeTables"] = 30,
            ["Microsoft.Network/virtualNetworks"] = 30,
            ["Microsoft.Network/privateDnsZones"] = 30,
            ["Microsoft.Network/dnsZones"] = 30,
            ["Microsoft.Network/firewallPolicies"] = 30,
            ["Microsoft.Network/ApplicationGatewayWebApplicationFirewallPolicies"] = 30,
            ["Microsoft.Network/FrontDoorWebApplicationFirewallPolicies"] = 30,
            ["Microsoft.Network/ipGroups"] = 30,
            ["Microsoft.Web/serverfarms"] = 30,
            ["Microsoft.Sql/servers"] = 30,
            ["Microsoft.DBforPostgreSQL/servers"] = 30,
            ["Microsoft.DBforMySQL/servers"] = 30,

            // Tier 35 — CMK intermediaries: disk encryption sets reference Key Vault keys,
            // and are referenced by disks/VMs, so must be deleted after disks but before vaults
            ["Microsoft.Compute/diskEncryptionSets"] = 35,

            // Tier 40 — identity, vaults, keys, monitoring — often referenced as CMK sources
            // by storage accounts, databases, and disk encryption sets (all deleted earlier)
            ["Microsoft.ManagedIdentity/userAssignedIdentities"] = 40,
            ["Microsoft.KeyVault/vaults"] = 40,
            ["Microsoft.KeyVault/managedHSMs"] = 40,
            ["Microsoft.KeyVault/vaults/keys"] = 40,
            ["Microsoft.KeyVault/vaults/secrets"] = 40,
            ["Microsoft.Insights/components"] = 40,
            ["Microsoft.OperationalInsights/workspaces"] = 40,
            ["Microsoft.Insights/actionGroups"] = 40,
            ["Microsoft.EventGrid/topics"] = 40,
            ["Microsoft.EventGrid/systemTopics"] = 40,
        };

        private const int DefaultTypePriority = 50;

        public static List<Resource> OrderForDeletion(List<Resource> resources)
        {
            return resources
                .OrderBy(r => GetTypePriority(r.Type))
                .ThenByDescending(r => GetDepth(r.Id))
                .ToList();
        }

        private static int GetTypePriority(string? resourceType)
        {
            if (string.IsNullOrEmpty(resourceType))
            {
                return DefaultTypePriority;
            }

            return TypePriority.TryGetValue(resourceType, out int priority) ? priority : DefaultTypePriority;
        }

        private static int GetDepth(string? resourceId)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                return 0;
            }

            return resourceId.Split('/').Length;
        }
    }
}
