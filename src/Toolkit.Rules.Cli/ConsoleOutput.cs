namespace Toolkit.Rules.Cli;

/// <summary>
/// Console formatting. Uses ANSI colour codes when writing to a terminal, and plain text when
/// output is redirected to a file or another program, or when NO_COLOR is set (no-color.org).
/// </summary>
internal static class ConsoleOutput
{
    private static readonly bool UseColor =
        !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;

    public static void Line(string text) => Console.WriteLine(text);

    public static int Error(string message)
    {
        Console.Error.WriteLine(Paint(message, "31")); // red
        return 1;
    }

    public static string Dim(string text) => Paint(text, "2");

    private static string Paint(string text, string code) => UseColor ? $"\e[{code}m{text}\e[0m" : text;
}
