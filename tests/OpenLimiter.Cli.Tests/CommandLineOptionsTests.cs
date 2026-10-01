using OpenLimiter.Cli;

namespace OpenLimiter.Cli.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void Parse_accepts_allowed_options_case_insensitively()
    {
        var options = CommandLineOptions.Parse(["--PATH", @"C:\Apps\sample.exe"], "path");

        Assert.True(options.TryGetValue("path", out var value));
        Assert.Equal(@"C:\Apps\sample.exe", value);
    }

    [Fact]
    public void Parse_rejects_unknown_option()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CommandLineOptions.Parse(["--blok", "both"], "block"));

        Assert.Contains("Unknown option", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_rejects_duplicate_option()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CommandLineOptions.Parse(["--path", "first.exe", "--PATH", "second.exe"], "path"));

        Assert.Contains("only be specified once", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("value")]
    [InlineData("--")]
    [InlineData("--path")]
    public void Parse_rejects_malformed_arguments(string argument)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse([argument], "path"));
    }

    [Fact]
    public void Parse_rejects_arguments_for_optionless_command()
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--unexpected", "value"]));
    }
}
