// netstandard2.0 predates C# 9, so it lacks this marker type that records' init-only setters need.
// Declaring it here lets the generator use records anyway. The compiler only checks the name.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
