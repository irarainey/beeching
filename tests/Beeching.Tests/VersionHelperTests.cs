using Beeching.Helpers;

namespace Beeching.Tests;

public class VersionHelperTests
{
    [Fact]
    public void IsUpdateAvailable_NewerVersion_ReturnsTrue()
    {
        Assert.True(VersionHelper.IsUpdateAvailable("1.0.0", "1.0.1"));
    }

    [Fact]
    public void IsUpdateAvailable_SameVersion_ReturnsFalse()
    {
        Assert.False(VersionHelper.IsUpdateAvailable("1.0.0", "1.0.0"));
    }

    [Fact]
    public void IsUpdateAvailable_OlderVersion_ReturnsFalse()
    {
        Assert.False(VersionHelper.IsUpdateAvailable("2.0.0", "1.0.0"));
    }

    [Fact]
    public void IsUpdateAvailable_MajorVersionBump_ReturnsTrue()
    {
        Assert.True(VersionHelper.IsUpdateAvailable("1.0.0", "2.0.0"));
    }

    [Fact]
    public void IsUpdateAvailable_MinorVersionBump_ReturnsTrue()
    {
        Assert.True(VersionHelper.IsUpdateAvailable("1.0.0", "1.1.0"));
    }

    [Fact]
    public void IsUpdateAvailable_HighMinorVersion_ReturnsTrue()
    {
        Assert.True(VersionHelper.IsUpdateAvailable("1.99.0", "1.100.0"));
    }

    [Fact]
    public void IsUpdateAvailable_HighPatchVersion_ReturnsTrue()
    {
        Assert.True(VersionHelper.IsUpdateAvailable("1.0.99", "1.0.100"));
    }

    [Fact]
    public void IsUpdateAvailable_InvalidInstalled_ReturnsFalse()
    {
        Assert.False(VersionHelper.IsUpdateAvailable("not-a-version", "1.0.0"));
    }

    [Fact]
    public void IsUpdateAvailable_InvalidLatest_ReturnsFalse()
    {
        Assert.False(VersionHelper.IsUpdateAvailable("1.0.0", "not-a-version"));
    }

    [Fact]
    public void IsUpdateAvailable_BothInvalid_ReturnsFalse()
    {
        Assert.False(VersionHelper.IsUpdateAvailable("abc", "def"));
    }

    [Fact]
    public void IsUpdateAvailable_EmptyStrings_ReturnsFalse()
    {
        Assert.False(VersionHelper.IsUpdateAvailable("", ""));
    }

    [Fact]
    public void GetVersion_ReturnsNonEmptyString()
    {
        string version = VersionHelper.GetVersion();
        Assert.NotNull(version);
        Assert.NotEmpty(version);
    }
}
