using System.Reflection;
using CloudSmith.Cli.Commands;
using CloudSmith.Cli.Services;
using Xunit;

namespace CloudSmith.Cli.Tests;

/// <summary>
/// AB#1522 — Verifies that the core scaffold wiring compiles and key types exist.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void CommandBase_OutputOption_HasExpectedDefaults()
    {
        Assert.Equal("--output", CommandBase.OutputOption.Aliases.First(a => a.StartsWith("--")));
        Assert.Equal("table", CommandBase.OutputOption.GetDefaultValue()?.ToString());
    }

    [Fact]
    public void CommandBase_ServerOption_IsNullableString()
    {
        // Server option should accept null (not set)
        Assert.True(CommandBase.ServerOption.ValueType == typeof(string));
    }

    [Fact]
    public void ConfigService_SupportedKeys_RoundTrip()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            ConfigService svc = new(tempFile);

            svc.Set("server", "https://example.com");
            svc.Set("org", "my-org");
            svc.Set("output", "json");

            Assert.Equal("https://example.com", svc.Get("server"));
            Assert.Equal("my-org",              svc.Get("org"));
            Assert.Equal("json",                svc.Get("output"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Version_IsExpected()
    {
        // Read the version from the assembly's informational version attribute
        Assembly asm = typeof(CommandBase).Assembly;
        string? version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]; // strip commit hash suffix

        Assert.Equal("0.1.0", version);
    }
}
