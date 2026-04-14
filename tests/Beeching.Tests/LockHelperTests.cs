using Beeching.Commands;
using Beeching.Helpers;
using Beeching.Models;

namespace Beeching.Tests;

public class LockHelperTests
{
    private static AxeContext CreateContext(string subscriptionRole = "None", bool force = false, bool resourceGroups = false)
    {
        var settings = new AxeSettings { Force = force, ResourceGroups = resourceGroups };
        return new AxeContext(settings)
        {
            SubscriptionRole = subscriptionRole,
            IsSubscriptionRolePrivileged = subscriptionRole is "Owner" or "Contributor"
        };
    }

    private static Resource CreateLockedResource(params (string scope, bool canManageLocks)[] lockAndRoles)
    {
        var resource = new Resource
        {
            Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1",
            Name = "vm1",
            IsLocked = true,
        };

        foreach (var (scope, _) in lockAndRoles)
        {
            resource.ResourceLocks.Add(new ResourceLock { Scope = scope, Name = $"lock-{scope}", Id = $"/locks/{scope}" });
        }

        foreach (var (scope, canManageLocks) in lockAndRoles)
        {
            if (canManageLocks)
            {
                resource.Roles.Add(new EffectiveRole
                {
                    Name = "Owner",
                    ScopeType = scope,
                    CanManageLocks = true,
                    Priority = 0,
                });
            }
        }

        return resource;
    }

    [Fact]
    public void ShouldSkipIfLocked_OwnerSubscriptionRole_DoesNotSkip()
    {
        var context = CreateContext(subscriptionRole: "Owner");
        var resource = CreateLockedResource(("resource", false));

        Assert.False(LockHelper.ShouldSkipIfLocked(context, resource));
    }

    [Fact]
    public void ShouldSkipIfLocked_NoPermissions_Skips()
    {
        var context = CreateContext(subscriptionRole: "Reader");
        var resource = CreateLockedResource(("resource", false));

        Assert.True(LockHelper.ShouldSkipIfLocked(context, resource));
    }

    [Fact]
    public void ShouldSkipIfLocked_ResourceRoleCanManageLocks_DoesNotSkip()
    {
        var context = CreateContext(subscriptionRole: "Reader");
        var resource = CreateLockedResource(("resource", true));

        Assert.False(LockHelper.ShouldSkipIfLocked(context, resource));
    }

    [Fact]
    public void ShouldSkipIfLocked_SubscriptionLock_NoOwnerSubscriptionRole_Skips()
    {
        var context = CreateContext(subscriptionRole: "Contributor");
        var resource = CreateLockedResource(("subscription", false));
        resource.Roles.Add(new EffectiveRole { Name = "Contributor", ScopeType = "resource", CanManageLocks = true, Priority = 1 });

        Assert.True(LockHelper.ShouldSkipIfLocked(context, resource));
    }

    [Fact]
    public void ShouldSkipIfLocked_ResourceGroupLock_WithResourceGroupOwnerRole_DoesNotSkip()
    {
        var context = CreateContext(subscriptionRole: "Reader");
        var resource = new Resource
        {
            Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1",
            Name = "vm1",
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource group", Name = "rg-lock", Id = "/locks/rg" });
        resource.Roles.Add(new EffectiveRole { Name = "Owner", ScopeType = "resource group", CanManageLocks = true, Priority = 0 });

        Assert.False(LockHelper.ShouldSkipIfLocked(context, resource));
    }

    [Fact]
    public void ShouldSkipIfLocked_ResourceGroupLock_WithoutOwnerRole_Skips()
    {
        var context = CreateContext(subscriptionRole: "Reader");
        var resource = new Resource
        {
            Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm1",
            Name = "vm1",
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource group", Name = "rg-lock", Id = "/locks/rg" });
        resource.Roles.Add(new EffectiveRole { Name = "Contributor", ScopeType = "resource group", CanManageLocks = true, Priority = 1 });

        Assert.True(LockHelper.ShouldSkipIfLocked(context, resource));
    }

    [Fact]
    public void ShouldSkipIfLocked_ResourceGroupsMode_WithLockPowers_DoesNotSkip()
    {
        var context = CreateContext(subscriptionRole: "Reader", resourceGroups: true);
        var resource = new Resource
        {
            Id = "/subscriptions/sub/resourceGroups/rg",
            Name = "rg",
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource group", Name = "rg-lock", Id = "/locks/rg" });
        resource.Roles.Add(new EffectiveRole { Name = "Contributor", ScopeType = "resource group", CanManageLocks = true, Priority = 1 });

        Assert.False(LockHelper.ShouldSkipIfLocked(context, resource));
    }
}
