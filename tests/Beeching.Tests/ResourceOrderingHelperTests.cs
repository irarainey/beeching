using Beeching.Helpers;
using Beeching.Models;

namespace Beeching.Tests;

public class ResourceOrderingHelperTests
{
    [Fact]
    public void OrderForDeletion_VmsDeletedBeforeNics()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Network/networkInterfaces/nic1", Type = "Microsoft.Network/networkInterfaces" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1", Type = "Microsoft.Compute/virtualMachines" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Compute/virtualMachines", ordered[0].Type);
        Assert.Equal("Microsoft.Network/networkInterfaces", ordered[1].Type);
    }

    [Fact]
    public void OrderForDeletion_NicsDeletedBeforeVnets()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet1", Type = "Microsoft.Network/virtualNetworks" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Network/networkInterfaces/nic1", Type = "Microsoft.Network/networkInterfaces" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Network/networkInterfaces", ordered[0].Type);
        Assert.Equal("Microsoft.Network/virtualNetworks", ordered[1].Type);
    }

    [Fact]
    public void OrderForDeletion_KeyVaultDeletedLast()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/kv1", Type = "Microsoft.KeyVault/vaults" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/disks/disk1", Type = "Microsoft.Compute/disks" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1", Type = "Microsoft.Compute/virtualMachines" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Compute/virtualMachines", ordered[0].Type);
        Assert.Equal("Microsoft.Compute/disks", ordered[1].Type);
        Assert.Equal("Microsoft.KeyVault/vaults", ordered[2].Type);
    }

    [Fact]
    public void OrderForDeletion_DiskEncryptionSetBetweenDiskAndKeyVault()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/kv1", Type = "Microsoft.KeyVault/vaults" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/diskEncryptionSets/des1", Type = "Microsoft.Compute/diskEncryptionSets" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/disks/disk1", Type = "Microsoft.Compute/disks" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Compute/disks", ordered[0].Type);
        Assert.Equal("Microsoft.Compute/diskEncryptionSets", ordered[1].Type);
        Assert.Equal("Microsoft.KeyVault/vaults", ordered[2].Type);
    }

    [Fact]
    public void OrderForDeletion_DeeperResourcesFirstWithinSameTier()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv1", Type = "Microsoft.Sql/servers" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv1/databases/db1", Type = "Microsoft.Sql/servers/databases" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Sql/servers/databases", ordered[0].Type);
        Assert.Equal("Microsoft.Sql/servers", ordered[1].Type);
    }

    [Fact]
    public void OrderForDeletion_UnknownTypesGetDefaultPriority()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Custom/widgets/w1", Type = "Microsoft.Custom/widgets" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1", Type = "Microsoft.Compute/virtualMachines" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/kv1", Type = "Microsoft.KeyVault/vaults" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Compute/virtualMachines", ordered[0].Type);
        Assert.Equal("Microsoft.KeyVault/vaults", ordered[1].Type);
        Assert.Equal("Microsoft.Custom/widgets", ordered[2].Type);
    }

    [Fact]
    public void OrderForDeletion_EmptyList_ReturnsEmptyList()
    {
        var ordered = ResourceOrderingHelper.OrderForDeletion(new List<Resource>());
        Assert.Empty(ordered);
    }

    [Fact]
    public void OrderForDeletion_ContainerAppsBeforeEnvironment()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/env1", Type = "Microsoft.App/managedEnvironments" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.App/containerApps/app1", Type = "Microsoft.App/containerApps" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.App/containerApps", ordered[0].Type);
        Assert.Equal("Microsoft.App/managedEnvironments", ordered[1].Type);
    }

    [Fact]
    public void OrderForDeletion_NatGatewayBeforePublicIp()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Network/publicIPAddresses/pip1", Type = "Microsoft.Network/publicIPAddresses" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Network/natGateways/nat1", Type = "Microsoft.Network/natGateways" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Network/natGateways", ordered[0].Type);
        Assert.Equal("Microsoft.Network/publicIPAddresses", ordered[1].Type);
    }

    [Fact]
    public void OrderForDeletion_TypeComparisonIsCaseInsensitive()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/kv1", Type = "MICROSOFT.KEYVAULT/VAULTS" },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1", Type = "microsoft.compute/virtualmachines" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("microsoft.compute/virtualmachines", ordered[0].Type);
        Assert.Equal("MICROSOFT.KEYVAULT/VAULTS", ordered[1].Type);
    }

    [Fact]
    public void OrderForDeletion_NullTypeAndId_DoNotThrow()
    {
        var resources = new List<Resource>
        {
            new() { Id = null!, Type = null! },
            new() { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1", Type = "Microsoft.Compute/virtualMachines" },
        };

        var ordered = ResourceOrderingHelper.OrderForDeletion(resources);

        Assert.Equal("Microsoft.Compute/virtualMachines", ordered[0].Type);
        Assert.Null(ordered[1].Type);
    }
}
