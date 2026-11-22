using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Toolkit.Generators.Tests;

/// <summary>Compiles a snippet of C# in memory and runs the generator on it, the way the compiler would.</summary>
internal static class GeneratorHarness
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    // Every assembly the test process can see, as compile references: enough for System, Text.Json, etc.
    private static readonly MetadataReference[] References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    /// <returns>The driver (what Verify snapshots) and the compilation with generated code added.</returns>
    public static (GeneratorDriver Driver, Compilation Output) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Ids.cs")],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new StronglyTypedIdGenerator().AsSourceGenerator()], parseOptions: ParseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver, output);
    }

    public static IReadOnlyList<string> GeneratorDiagnosticIds(string source) =>
        Run(source).Driver.GetRunResult().Diagnostics.Select(d => d.Id).ToList();

    public static IReadOnlyList<Diagnostic> CompileErrors(string source) =>
        Run(source).Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
}
