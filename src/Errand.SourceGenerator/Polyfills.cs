// Polyfill required to use C# 9+ record types when targeting netstandard2.0.
// The compiler emits init-only setter calls that reference this type at the IL level.
// See: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/init
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
