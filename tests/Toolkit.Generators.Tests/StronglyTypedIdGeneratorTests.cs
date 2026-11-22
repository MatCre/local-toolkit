using static VerifyXunit.Verifier;

namespace Toolkit.Generators.Tests;

public class StronglyTypedIdGeneratorTests
{
    // Snapshot tests: the generated source is saved to Snapshots/*.verified.cs. If the generator's
    // output changes, the test fails and shows the difference; approve it by replacing the .verified
    // file with the new .received one. That makes every change to generated code a reviewed diff.

    [Fact]
    public Task Generates_a_guid_id() => Verify(GeneratorHarness.Run("""
        using Toolkit.Generators;

        namespace Shop;

        [StronglyTypedId]
        public partial record struct OrderId;
        """).Driver).UseDirectory("Snapshots");

    [Fact]
    public Task Generates_an_int_id() => Verify(GeneratorHarness.Run("""
        using Toolkit.Generators;

        namespace Shop;

        [StronglyTypedId(IdType.Int)]
        public partial record struct CustomerNumber;
        """).Driver).UseDirectory("Snapshots");

    [Fact]
    public Task Generates_a_string_id() => Verify(GeneratorHarness.Run("""
        using Toolkit.Generators;

        namespace Shop;

        [StronglyTypedId(IdType.String)]
        public partial record struct Sku;
        """).Driver).UseDirectory("Snapshots");

    [Theory]
    [InlineData("IdType.Guid")]
    [InlineData("IdType.Int")]
    [InlineData("IdType.Long")]
    [InlineData("IdType.String")]
    public void The_generated_code_compiles(string idType)
    {
        var errors = GeneratorHarness.CompileErrors($$"""
            using Toolkit.Generators;

            namespace Shop;

            [StronglyTypedId({{idType}})]
            public partial record struct SomeId;

            public static class Uses
            {
                public static int Sorted(SomeId a, SomeId b) => a.CompareTo(b);
                public static SomeId Parsed(string s) => SomeId.Parse(s);
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void Works_in_the_global_namespace()
    {
        Assert.Empty(GeneratorHarness.CompileErrors("""
            [Toolkit.Generators.StronglyTypedId]
            public partial record struct TopLevelId;
            """));
    }

    [Theory]
    [InlineData("TKG001", "[StronglyTypedId] public partial struct OrderId { }")]
    [InlineData("TKG002", "[StronglyTypedId] public record struct OrderId;")]
    [InlineData("TKG003", "public static class Outer { [StronglyTypedId] public partial record struct OrderId; }")]
    [InlineData("TKG004", "[StronglyTypedId] public partial record struct OrderId(System.Guid Id);")]
    public void Reports_a_clear_error_for_misuse(string expectedId, string declaration)
    {
        var ids = GeneratorHarness.GeneratorDiagnosticIds($"using Toolkit.Generators;\n{declaration}");

        Assert.Equal([expectedId], ids);
    }
}
