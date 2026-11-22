using System.CommandLine;

namespace TkCli.Tests;

/// <summary>Runs commands in memory, as if typed on the command line, and checks what they print.</summary>
public class CliTests
{
    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = Cli.Build().Parse(args).Invoke(new InvocationConfiguration { Output = output, Error = error });
        return (exitCode, output.ToString().Trim(), error.ToString());
    }

    [Fact]
    public void Greets_by_name()
    {
        var (exitCode, output, _) = Run("hello", "Ada");

        Assert.Equal(0, exitCode);
        Assert.Equal("Hello, Ada!", output);
    }

    [Fact]
    public void Greets_the_world_when_no_name_is_given() =>
        Assert.Equal("Hello, world!", Run("hello").Output);

    [Fact]
    public void Shouts_when_asked() =>
        Assert.Equal("HELLO, ADA!", Run("hello", "Ada", "--shout").Output);

    [Fact]
    public void Reports_an_unknown_command_with_a_failing_exit_code()
    {
        var (exitCode, _, error) = Run("goodbye");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("goodbye", error);
    }
}
