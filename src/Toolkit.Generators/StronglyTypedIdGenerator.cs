using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Toolkit.Generators;

/// <summary>
/// Turns <c>[StronglyTypedId] partial record struct OrderId;</c> into a full id type at compile time.
/// </summary>
/// <remarks>
/// <para>
/// An incremental generator describes a pipeline instead of doing work directly. The compiler runs
/// it as you type, caches every step, and re-runs only what an edit affects, which is what keeps the
/// IDE responsive. The pipeline here:
/// </para>
/// <list type="number">
/// <item>Once per project: add the [StronglyTypedId] attribute source.</item>
/// <item><see cref="SyntaxValueProvider.ForAttributeWithMetadataName{T}"/>: find declarations carrying the
/// attribute. Roslyn indexes attributes, so this is far cheaper than scanning every syntax node.</item>
/// <item>Transform each into an <see cref="IdTarget"/>: plain data, so unchanged ids are cache hits.</item>
/// <item>For each target, add a source file, or report why it can't be generated.</item>
/// </list>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class StronglyTypedIdGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
        {
            output.AddEmbeddedAttributeDefinition(); // defines [Embedded] for the attribute source to use
            output.AddSource("StronglyTypedIdAttribute.g.cs", AttributeSource.Text);
        });

        var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeSource.AttributeFullName,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (attributeContext, _) => Inspect(attributeContext));

        context.RegisterSourceOutput(targets, static (output, target) =>
        {
            if (target.Problem is { } problem)
            {
                output.ReportDiagnostic(Diagnostics.Create(problem));
            }
            else if (target.Id is { } id)
            {
                output.AddSource(id.HintName, IdSourceRenderer.Render(id));
            }
        });
    }

    /// <summary>Checks the declaration and reduces it to plain data.</summary>
    private static IdTarget Inspect(GeneratorAttributeSyntaxContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.TargetNode;
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var location = LocationInfo.From(declaration.Identifier.GetLocation());

        IdTarget Problem(string id) => new(null, new ProblemInfo(id, location, symbol.Name));

        if (declaration is not RecordDeclarationSyntax record || !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword))
        {
            return Problem(Diagnostics.MustBeRecordStruct.Id);
        }

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return Problem(Diagnostics.MustBePartial.Id);
        }

        if (symbol.ContainingType is not null)
        {
            return Problem(Diagnostics.MustNotBeNested.Id);
        }

        if (record.ParameterList is not null)
        {
            return Problem(Diagnostics.MustNotHaveParameters.Id);
        }

        // A list pattern ("is [{ Value: int v }]") would be neater, but needs System.Index,
        // which netstandard2.0 doesn't have.
        var arguments = context.Attributes[0].ConstructorArguments;
        var backing = arguments.Length == 1 && arguments[0].Value is int value
            ? (BackingType)value
            : BackingType.Guid;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString();
        return new IdTarget(new IdModel(ns, symbol.Name, backing), null);
    }
}
