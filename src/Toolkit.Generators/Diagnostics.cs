using Microsoft.CodeAnalysis;

namespace Toolkit.Generators;

/// <summary>
/// Compile errors the generator reports when [StronglyTypedId] is used where it can't work. They show
/// up in the IDE as red squiggles on the type name, like any other compiler error.
/// </summary>
internal static class Diagnostics
{
    private const string Category = "Toolkit.Generators";

    public static readonly DiagnosticDescriptor MustBeRecordStruct = new(
        "TKG001", "[StronglyTypedId] needs a record struct",
        "'{0}' must be declared as 'partial record struct' to use [StronglyTypedId]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustBePartial = new(
        "TKG002", "[StronglyTypedId] needs a partial type",
        "'{0}' must be 'partial' so the generator can add its members",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustNotBeNested = new(
        "TKG003", "[StronglyTypedId] can't be used on a nested type",
        "'{0}' is declared inside another type; declare it directly in a namespace",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustNotHaveParameters = new(
        "TKG004", "[StronglyTypedId] adds the parameter list itself",
        "'{0}' must not declare a parameter list, because the generator adds one with a Value parameter",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static Diagnostic Create(ProblemInfo problem)
    {
        var descriptor = problem.DiagnosticId switch
        {
            "TKG001" => MustBeRecordStruct,
            "TKG002" => MustBePartial,
            "TKG003" => MustNotBeNested,
            _ => MustNotHaveParameters,
        };
        return Diagnostic.Create(descriptor, problem.Location?.ToLocation(), problem.TypeName);
    }
}
