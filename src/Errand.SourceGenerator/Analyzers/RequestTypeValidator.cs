using Microsoft.CodeAnalysis;

namespace Errand.SourceGenerator.Analyzers;

/// <summary>
/// Symbol-level validation helpers used by <see cref="HandlerScanner"/> during the
/// semantic transform step, while the Roslyn semantic model is still available.
/// </summary>
internal static class RequestTypeValidator
{
    private const string RequestInterfaceName = "IErrandRequest";
    private const string AbstractionsNamespace = "Errand.Abstractions";

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="requestType"/> implements
    /// <c>Errand.Abstractions.IErrandRequest&lt;TResponse&gt;</c> where the type
    /// argument matches <paramref name="responseType"/>.
    /// </summary>
    /// <param name="requestType">The candidate request type to validate.</param>
    /// <param name="responseType">
    /// The response type that must appear as the type argument of
    /// <c>IErrandRequest&lt;TResponse&gt;</c>.
    /// </param>
    public static bool ImplementsIErrandRequest(
        INamedTypeSymbol requestType,
        ITypeSymbol responseType)
    {
        foreach (var iface in requestType.AllInterfaces)
        {
            if (iface.Name == RequestInterfaceName
                && iface.ContainingNamespace?.ToDisplayString() == AbstractionsNamespace
                && iface.TypeArguments.Length == 1
                && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], responseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="handlerType"/> has
    /// <c>public</c> or <c>internal</c> accessibility.
    /// </summary>
    /// <remarks>
    /// Private and file-local handler types cannot be referenced by the generated
    /// dispatcher, which lives in a separate (generated) compilation unit.
    /// </remarks>
    /// <param name="handlerType">The handler type symbol to check.</param>
    public static bool IsHandlerAccessible(INamedTypeSymbol handlerType)
    {
        return handlerType.DeclaredAccessibility
            is Accessibility.Public
            or Accessibility.Internal;
    }
}
