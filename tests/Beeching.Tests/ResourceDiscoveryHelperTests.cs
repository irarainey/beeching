using Beeching.Helpers;
using Beeching.Models;

namespace Beeching.Tests;

public class ResourceDiscoveryHelperTests
{
    [Fact]
    public void ParseDelimitedValues_SingleValue_ReturnsSingleItem()
    {
        var result = ResourceDiscoveryHelper.ParseDelimitedValues("value1");
        Assert.Single(result);
        Assert.Equal("value1", result[0]);
    }

    [Fact]
    public void ParseDelimitedValues_MultipleValues_SplitsByColon()
    {
        var result = ResourceDiscoveryHelper.ParseDelimitedValues("val1:val2:val3");
        Assert.Equal(3, result.Count);
        Assert.Equal("val1", result[0]);
        Assert.Equal("val2", result[1]);
        Assert.Equal("val3", result[2]);
    }

    [Fact]
    public void ParseDelimitedValues_EmptyString_ReturnsEmptyList()
    {
        var result = ResourceDiscoveryHelper.ParseDelimitedValues("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseDelimitedValues_Null_ReturnsEmptyList()
    {
        var result = ResourceDiscoveryHelper.ParseDelimitedValues(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void SanitizeODataValue_EscapesSingleQuotes()
    {
        var result = ResourceDiscoveryHelper.SanitizeODataValue("it's a test");
        Assert.Equal("it''s a test", result);
    }

    [Fact]
    public void SanitizeODataValue_MultipleSingleQuotes()
    {
        var result = ResourceDiscoveryHelper.SanitizeODataValue("it's Bob's test");
        Assert.Equal("it''s Bob''s test", result);
    }

    [Fact]
    public void SanitizeODataValue_NoQuotes_UnchangedString()
    {
        var result = ResourceDiscoveryHelper.SanitizeODataValue("clean-value");
        Assert.Equal("clean-value", result);
    }

    [Fact]
    public void SanitizeODataValue_EmptyString_ReturnsEmpty()
    {
        var result = ResourceDiscoveryHelper.SanitizeODataValue("");
        Assert.Equal("", result);
    }

    [Fact]
    public void Deduplicate_RemovesDuplicateIds()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/res1", Name = "res1-first" },
            new() { Id = "/sub/rg/res1", Name = "res1-dupe" },
            new() { Id = "/sub/rg/res2", Name = "res2" },
        };

        var result = ResourceDiscoveryHelper.Deduplicate(resources);

        Assert.Equal(2, result.Count);
        Assert.Equal("res1-first", result[0].Name);
        Assert.Equal("res2", result[1].Name);
    }

    [Fact]
    public void Deduplicate_NoDuplicates_ReturnsSameCount()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/res1", Name = "res1" },
            new() { Id = "/sub/rg/res2", Name = "res2" },
        };

        var result = ResourceDiscoveryHelper.Deduplicate(resources);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Deduplicate_EmptyList_ReturnsEmpty()
    {
        var result = ResourceDiscoveryHelper.Deduplicate(new List<Resource>());
        Assert.Empty(result);
    }

    [Fact]
    public void ApplyExclusions_ExcludesNamedResources()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/keep", Name = "keep" },
            new() { Id = "/sub/rg/remove", Name = "remove" },
            new() { Id = "/sub/rg/also-keep", Name = "also-keep" },
        };

        var result = ResourceDiscoveryHelper.ApplyExclusions(resources, "remove");

        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, r => r.Name == "remove");
    }

    [Fact]
    public void ApplyExclusions_MultipleExclusions()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/keep", Name = "keep" },
            new() { Id = "/sub/rg/rem1", Name = "rem1" },
            new() { Id = "/sub/rg/rem2", Name = "rem2" },
        };

        var result = ResourceDiscoveryHelper.ApplyExclusions(resources, "rem1:rem2");

        Assert.Single(result);
        Assert.Equal("keep", result[0].Name);
    }

    [Fact]
    public void ApplyExclusions_EmptyExclude_ReturnsAll()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/res1", Name = "res1" },
        };

        var result = ResourceDiscoveryHelper.ApplyExclusions(resources, "");
        Assert.Single(result);
    }

    [Fact]
    public void ApplyExclusions_NullExclude_ReturnsAll()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/res1", Name = "res1" },
        };

        var result = ResourceDiscoveryHelper.ApplyExclusions(resources, null!);
        Assert.Single(result);
    }

    [Fact]
    public void FilterByResourceTypes_FiltersToMatchingTypes()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/vm1", Name = "vm1", Type = "Microsoft.Compute/virtualMachines" },
            new() { Id = "/sub/rg/sa1", Name = "sa1", Type = "Microsoft.Storage/storageAccounts" },
            new() { Id = "/sub/rg/nic1", Name = "nic1", Type = "Microsoft.Network/networkInterfaces" },
        };

        var result = ResourceDiscoveryHelper.FilterByResourceTypes(resources, "Microsoft.Compute/virtualMachines");

        Assert.Single(result);
        Assert.Equal("vm1", result[0].Name);
    }

    [Fact]
    public void FilterByResourceTypes_MultipleTypes()
    {
        var resources = new List<Resource>
        {
            new() { Id = "/sub/rg/vm1", Name = "vm1", Type = "Microsoft.Compute/virtualMachines" },
            new() { Id = "/sub/rg/sa1", Name = "sa1", Type = "Microsoft.Storage/storageAccounts" },
            new() { Id = "/sub/rg/nic1", Name = "nic1", Type = "Microsoft.Network/networkInterfaces" },
        };

        var result = ResourceDiscoveryHelper.FilterByResourceTypes(resources, "Microsoft.Compute/virtualMachines:Microsoft.Storage/storageAccounts");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void PopulateResourceGroup_SetsResourceGroupFromId()
    {
        var resource = new Resource
        {
            Id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/my-rg/providers/Microsoft.Compute/virtualMachines/vm1"
        };
        var subscriptionId = Guid.Parse("00000000-0000-0000-0000-000000000000");

        ResourceDiscoveryHelper.PopulateResourceGroup(resource, subscriptionId);

        Assert.Equal("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/my-rg", resource.ResourceGroup);
    }

    [Fact]
    public void PopulateResourceGroup_ShortId_DoesNotSet()
    {
        var resource = new Resource { Id = "/subscriptions/sub1/foo" };

        ResourceDiscoveryHelper.PopulateResourceGroup(resource, Guid.Empty);

        Assert.Null(resource.ResourceGroup);
    }
}
