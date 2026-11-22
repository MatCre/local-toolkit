using System.CommandLine;

namespace TkCli;

/// <summary>
/// The command tree. System.CommandLine parses args into the declared arguments and options,
/// generates --help, and reports bad input with a non-zero exit code. SetAction is each command's handler.
/// </summary>
/// <remarks>
/// Commands write to <c>parse.InvocationConfiguration.Output</c> rather than Console, so tests can
/// capture what they print. It is the console unless a caller supplies another writer.
/// </remarks>
public static class Cli
{
    public static RootCommand Build()
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Who to greet.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "world",
        };
        var shoutOption = new Option<bool>("--shout", "-s") { Description = "Greet in capitals." };

        var hello = new Command("hello", "Print a greeting. Replace this with your first real command.") { nameArgument, shoutOption };
        hello.SetAction(parse =>
        {
            var greeting = $"Hello, {parse.GetValue(nameArgument)}!";
            var output = parse.InvocationConfiguration.Output;
            output.WriteLine(Style.Bold(parse.GetValue(shoutOption) ? greeting.ToUpperInvariant() : greeting, output));
            return 0;
        });

        return new RootCommand("TkCli: say what this tool does here.") { hello };
    }
}

/// <summary>
/// ANSI colour for a real terminal only: plain text when writing to anything else (a file, a pipe,
/// a test's StringWriter) or when NO_COLOR is set (see no-color.org).
/// </summary>
internal static class Style
{
    public static string Bold(string text, TextWriter output) => Paint(text, "1", output);

    public static string Paint(string text, string ansiCode, TextWriter output) =>
        UseColor(output) ? $"\e[{ansiCode}m{text}\e[0m" : text;

    private static bool UseColor(TextWriter output) =>
        ReferenceEquals(output, Console.Out)
        && !Console.IsOutputRedirected
        && Environment.GetEnvironmentVariable("NO_COLOR") is null;
}
