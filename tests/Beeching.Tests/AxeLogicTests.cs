using Beeching.Commands;
using Beeching.Helpers;
using Beeching.Models;

namespace Beeching.Tests;

public class AxeLogicTests
{
    private static AxeContext CreateContext(
        string subscriptionRole = "Owner",
        bool force = false,
        bool resourceGroups = false)
    {
        var settings = new AxeSettings { Force = force, ResourceGroups = resourceGroups };
        return new AxeContext(settings)
        {
            SubscriptionRole = subscriptionRole,
            IsSubscriptionRolePrivileged = subscriptionRole is "Owner" or "Contributor",
        };
    }

    [Fact]
    public void EvaluateSkipStatus_UnlockedResource_NotSkipped()
    {
        var context = CreateContext();
        var resource = new Resource { IsLocked = false };

        Axe.EvaluateSkipStatus(context, resource);

        Assert.False(resource.Skip);
    }

    [Fact]
    public void EvaluateSkipStatus_LockedResource_WithoutForce_Skipped()
    {
        var context = CreateContext(force: false);
        var resource = new Resource
        {
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource", Name = "lock1", Id = "/lock1" });

        Axe.EvaluateSkipStatus(context, resource);

        Assert.True(resource.Skip);
    }

    [Fact]
    public void EvaluateSkipStatus_LockedResource_WithForce_OwnerRole_NotSkipped()
    {
        var context = CreateContext(subscriptionRole: "Owner", force: true);
        var resource = new Resource
        {
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource", Name = "lock1", Id = "/lock1" });

        Axe.EvaluateSkipStatus(context, resource);

        Assert.False(resource.Skip);
    }

    [Fact]
    public void EvaluateSkipStatus_LockedResource_WithForce_NoPermission_Skipped()
    {
        var context = CreateContext(subscriptionRole: "Reader", force: true);
        var resource = new Resource
        {
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource", Name = "lock1", Id = "/lock1" });

        Axe.EvaluateSkipStatus(context, resource);

        Assert.True(resource.Skip);
    }

    [Fact]
    public void EvaluateSkipStatus_LockedResource_WithForce_ResourceRoleCanManage_NotSkipped()
    {
        var context = CreateContext(subscriptionRole: "Reader", force: true);
        var resource = new Resource
        {
            IsLocked = true,
        };
        resource.ResourceLocks.Add(new ResourceLock { Scope = "resource", Name = "lock1", Id = "/lock1" });
        resource.Roles.Add(new EffectiveRole
        {
            Name = "Owner",
            ScopeType = "resource",
            CanManageLocks = true,
            Priority = 0,
        });

        Axe.EvaluateSkipStatus(context, resource);

        Assert.False(resource.Skip);
    }
}
