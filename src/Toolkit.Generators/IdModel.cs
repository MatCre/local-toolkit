using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Toolkit.Generators;

/// <summary>Mirrors IdType in <see cref="AttributeSource"/>: the numbers must match.</summary>
internal enum BackingType
{
    Guid = 0,
    Int = 1,
    Long = 2,
    String = 3,
}

/// <summary>Everything needed to write one id's source: plain, comparable data only.</summary>
/// <remarks>
/// Incremental generators cache each pipeline step and skip the rest when a step's output equals
/// last time's. That only works if outputs compare by value, so the models are records holding
/// strings and enums: never syntax nodes, symbols or a Compilation, which differ on every keystroke.
/// </remarks>
internal sealed record IdModel(string? Namespace, string Name, BackingType Backing)
{
    /// <summary>File name for the generated source; unique per type.</summary>
    public string HintName => Namespace is null ? $"{Name}.g.cs" : $"{Namespace}.{Name}.g.cs";
}

/// <summary>Where in the source a problem is, in a form that compares by value (unlike <see cref="Location"/>).</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location location) =>
        location.SourceTree is null ? null : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
}

/// <summary>A problem to report instead of generating code.</summary>
internal sealed record ProblemInfo(string DiagnosticId, LocationInfo? Location, string TypeName);

/// <summary>The result of looking at one [StronglyTypedId] declaration: an id to generate, or a problem.</summary>
internal sealed record IdTarget(IdModel? Id, ProblemInfo? Problem);
