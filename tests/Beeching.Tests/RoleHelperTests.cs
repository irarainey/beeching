using Beeching.Commands;
using Beeching.Helpers;
using Beeching.Models;

namespace Beeching.Tests;

public class RoleHelperTests
{
    private static RoleDefinition CreateRoleDefinition(
        string roleName,
        List<string>? actions = null,
        List<string>? notActions = null)
    {
        return new RoleDefinition
        {
            Name = "role-guid",
            Id = "/providers/Microsoft.Authorization/roleDefinitions/role-guid",
            Type = "Microsoft.Authorization/roleDefinitions",
            Properties = new RoleDefinitionProperties
            {
                RoleName = roleName,
                Type = "BuiltInRole",
                Permissions =
                [
                    new RoleDefinitionPermission
                    {
                        Actions = actions ?? [],
                        NotActions = notActions ?? [],
                    }
                ],
            }
        };
    }

    [Fact]
    public void CreateEffectiveRole_Owner_Priority0()
    {
        var roleDef = CreateRoleDefinition("Owner", ["*"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.Equal("Owner", result.Name);
        Assert.Equal(0, result.Priority);
    }

    [Fact]
    public void CreateEffectiveRole_Contributor_Priority1()
    {
        var roleDef = CreateRoleDefinition("Contributor", ["*"], ["Microsoft.Authorization/*/Delete", "Microsoft.Authorization/*/Write"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.Equal("Contributor", result.Name);
        Assert.Equal(1, result.Priority);
    }

    [Fact]
    public void CreateEffectiveRole_CustomRole_Priority2()
    {
        var roleDef = CreateRoleDefinition("Custom Reader", ["Microsoft.Resources/*/read"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.Equal("Custom Reader", result.Name);
        Assert.Equal(2, result.Priority);
    }

    [Fact]
    public void CreateEffectiveRole_OwnerWithWildcard_CanManageLocks()
    {
        var roleDef = CreateRoleDefinition("Owner", ["*"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.True(result.CanManageLocks);
    }

    [Fact]
    public void CreateEffectiveRole_ContributorWithDeleteBlocked_CannotManageLocks()
    {
        var roleDef = CreateRoleDefinition("Contributor", ["*"], ["Microsoft.Authorization/*/Delete"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.False(result.CanManageLocks);
    }

    [Fact]
    public void CreateEffectiveRole_ContributorWithWriteBlocked_CannotManageLocks()
    {
        var roleDef = CreateRoleDefinition("Contributor", ["*"], ["Microsoft.Authorization/*/Write"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.False(result.CanManageLocks);
    }

    [Fact]
    public void CreateEffectiveRole_AllAuthBlocked_CannotManageLocks()
    {
        var roleDef = CreateRoleDefinition("Custom", ["*"], ["Microsoft.Authorization/*"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.False(result.CanManageLocks);
    }

    [Fact]
    public void CreateEffectiveRole_ExplicitAuthPermission_CanManageLocks()
    {
        var roleDef = CreateRoleDefinition("Custom Lock Manager", ["Microsoft.Authorization/*"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub/resourceGroups/rg", "resource group");

        Assert.True(result.CanManageLocks);
    }

    [Fact]
    public void CreateEffectiveRole_NoAuthPermission_CannotManageLocks()
    {
        var roleDef = CreateRoleDefinition("Reader", ["*/read"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub", "subscription");

        Assert.False(result.CanManageLocks);
    }

    [Fact]
    public void CreateEffectiveRole_SetsScope()
    {
        var roleDef = CreateRoleDefinition("Owner", ["*"]);
        var result = RoleHelper.CreateEffectiveRole(roleDef, "/subscriptions/sub/resourceGroups/rg", "resource group");

        Assert.Equal("/subscriptions/sub/resourceGroups/rg", result.Scope);
        Assert.Equal("resource group", result.ScopeType);
    }
}
