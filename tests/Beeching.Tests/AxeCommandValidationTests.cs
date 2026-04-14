using Beeching.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Beeching.Tests;

public class AxeCommandValidationTests
{
    private readonly AxeCommandTestable _command = new();

    private static AxeSettings DefaultSettings() => new()
    {
        MaxRetries = 6,
        RetryPause = 10,
    };

    [Fact]
    public void Validate_NameOnly_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_TagOnly_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Tag = "env:dev";
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_BothNameAndTag_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.Tag = "env:dev";
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_NeitherNameNorTag_Fails()
    {
        var settings = DefaultSettings();
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_TagWithoutColon_Fails()
    {
        var settings = DefaultSettings();
        settings.Tag = "novalue";
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_TagWithEmptyKey_Fails()
    {
        var settings = DefaultSettings();
        settings.Tag = ":value";
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_TagWithEmptyValue_Fails()
    {
        var settings = DefaultSettings();
        settings.Tag = "key:";
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_ResourceTypesWithResourceGroups_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.ResourceTypes = "Microsoft.Compute/virtualMachines";
        settings.ResourceGroups = true;
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_ResourceTypesInvalidFormat_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.ResourceTypes = "InvalidType";
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_ResourceTypesValidFormat_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.ResourceTypes = "Microsoft.Compute/virtualMachines";
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_MaxRetriesTooLow_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.MaxRetries = 0;
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_MaxRetriesTooHigh_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.MaxRetries = 101;
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_MaxRetriesValidRange_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.MaxRetries = 50;
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_RetryPauseTooLow_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.RetryPause = 4;
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_RetryPauseTooHigh_Fails()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.RetryPause = 61;
        var result = _command.TestValidate(settings);
        Assert.False(result.Successful);
    }

    [Fact]
    public void Validate_RetryPauseValidRange_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.RetryPause = 30;
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_RetryPauseBoundaryLow_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.RetryPause = 5;
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_RetryPauseBoundaryHigh_Succeeds()
    {
        var settings = DefaultSettings();
        settings.Name = "my-resource";
        settings.RetryPause = 60;
        var result = _command.TestValidate(settings);
        Assert.True(result.Successful);
    }

    /// <summary>
    /// Test-friendly wrapper that exposes the protected Validate method.
    /// </summary>
    private class AxeCommandTestable : AxeCommand
    {
        public AxeCommandTestable() : base(null!) { }

        public ValidationResult TestValidate(AxeSettings settings)
        {
            return Validate(null!, settings);
        }
    }
}
