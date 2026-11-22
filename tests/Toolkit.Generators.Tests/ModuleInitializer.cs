using System.Runtime.CompilerServices;

namespace Toolkit.Generators.Tests;

public static class ModuleInitializer
{
    /// <summary>Teaches Verify how to snapshot a GeneratorDriver: one file per generated source, plus diagnostics.</summary>
    [ModuleInitializer]
    public static void Initialize() => VerifySourceGenerators.Initialize();
}
