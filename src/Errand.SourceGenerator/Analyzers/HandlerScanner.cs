using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Errand.SourceGenerator.Analyzers;

/// <summary>
/// Provides an <see cref="IncrementalValuesProvider{T}"/> that emits one
/// <see cref="HandlerMetadata"/> for each valid Errand handler found in the compilation.
/// </summary>
/// <remarks>
/// The scan runs in two stages:
/// <list type="number">
///   <item><description>
///     <b>Syntax pre-filter</b> (<see cref="IsPotentialHandler"/>): cheaply discards any syntax
///     node that is not a class declaration with a base list mentioning "IErrandHandler".
///   </description></item>
///   <item><description>
///     <b>Semantic transform</b> (<see cref="ExtractHandlerMetadata"/>): uses the semantic model
///     to confirm that the class truly implements
///     <c>Errand.Abstractions.IErrandHandler&lt;TRequest, TResponse&gt;</c>, validates the
///     request/response types, and produces the final <see cref="HandlerMetadata"/>.
///   </description></item>
/// </list>
/// </remarks>
internal static class HandlerScanner
{
    private const string HandlerInterfaceName = "IErrandHandler";
    private const string AbstractionsNamespace = "Errand.Abstractions";

    /// <summary>
    /// Creates and returns the incremental values provider. Wire this up in the main
    /// generator's <c>Initialize</c> method.
    /// </summary>
    public static IncrementalValuesProvider<HandlerMetadata> CreateHandlerProvider(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsPotentialHandler(node),
                transform: static (ctx, ct) => ExtractHandlerMetadata(ctx, ct))
            .Where(static metadata => metadata is not null)
            .Select(static (metadata, _) => metadata!);
    }

    /// <summary>
    /// Cheap syntactic pre-filter.
    /// Returns <see langword="true"/> for class declarations whose base-type list
    /// contains a type whose name contains "IErrandHandler".
    /// </summary>
    private static bool IsPotentialHandler(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDecl || classDecl.BaseList is null)
        {
            return false;
        }

        foreach (var baseType in classDecl.BaseList.Types)
        {
            if (baseType.Type.ToString().Contains(HandlerInterfaceName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Uses the semantic model to validate the handler candidate and build
    /// a <see cref="HandlerMetadata"/> instance. Returns <see langword="null"/>
    /// if the class is not a valid, accessible Errand handler.
    /// </summary>
    private static HandlerMetadata? ExtractHandlerMetadata(
        GeneratorSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var classDecl = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // Find IErrandHandler<TRequest, TResponse> among all implemented interfaces
        INamedTypeSymbol? handlerInterface = null;
        foreach (var iface in symbol.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            if (iface.Name == HandlerInterfaceName
                && iface.ContainingNamespace?.ToDisplayString() == AbstractionsNamespace
                && iface.TypeArguments.Length == 2)
            {
                handlerInterface = iface;
                break;
            }
        }

        if (handlerInterface is null)
        {
            return null;
        }

        // Both type arguments must be concrete named types, not open generic parameters
        if (handlerInterface.TypeArguments[0] is not INamedTypeSymbol requestType)
        {
            return null;
        }

        var responseType = handlerInterface.TypeArguments[1];

        // Validate: handler must be accessible so the generated dispatcher can instantiate it.
        if (!RequestTypeValidator.IsHandlerAccessible(symbol))
        {
            return null;
        }

        // Validate: abstract handlers cannot be instantiated via "new THandler()".
        if (symbol.IsAbstract)
        {
            return null;
        }

        // Validate: TRequest must implement IErrandRequest<TResponse>.
        if (!RequestTypeValidator.ImplementsIErrandRequest(requestType, responseType))
        {
            return null;
        }

        var handlerNamespace = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : string.Empty;

        // FullyQualifiedFormat produces "global::MyApp.Foo" names safe for use in generated code.
        // Simple .Name properties are kept for diagnostics and method name generation.
        return new HandlerMetadata(
            handlerFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            handlerTypeName: symbol.Name,
            handlerNamespace: handlerNamespace,
            requestFullName: requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            requestTypeName: requestType.Name,
            responseFullName: responseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            responseTypeName: responseType.Name,
            diagnosticLocation: classDecl.GetLocation());
    }

    // =========================================================================
    // Diagnostic pipeline
    // =========================================================================

    /// <summary>
    /// Creates an incremental values provider that emits one <see cref="HandlerErrorData"/>
    /// for each handler candidate that fails validation (ERR003, ERR004, or ERR005).
    /// Valid handlers produce no output from this provider.
    /// </summary>
    /// <remarks>
    /// This pipeline runs independently of <see cref="CreateHandlerProvider"/> so that
    /// diagnostics are reported even when the code-generation step is suppressed due to
    /// build errors. Both providers share the same syntactic pre-filter.
    /// </remarks>
    public static IncrementalValuesProvider<HandlerErrorData> CreateDiagnosticProvider(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsPotentialHandler(node),
                transform: static (ctx, ct) => ExtractHandlerError(ctx, ct))
            .Where(static error => error is not null)
            .Select(static (error, _) => error!);
    }

    /// <summary>
    /// Returns a <see cref="HandlerErrorData"/> for a handler candidate that fails
    /// validation, or <see langword="null"/> if the candidate is valid or is not an
    /// Errand handler at all.
    /// </summary>
    private static HandlerErrorData? ExtractHandlerError(
        GeneratorSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var classDecl = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // Find IErrandHandler<TRequest, TResponse> among implemented interfaces.
        INamedTypeSymbol? handlerInterface = null;
        foreach (var iface in symbol.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            if (iface.Name == HandlerInterfaceName
                && iface.ContainingNamespace?.ToDisplayString() == AbstractionsNamespace
                && iface.TypeArguments.Length == 2)
            {
                handlerInterface = iface;
                break;
            }
        }

        // Not an Errand handler — no diagnostic from this provider.
        if (handlerInterface is null) return null;

        // Open generic type parameters are intentional base classes — skip silently.
        if (handlerInterface.TypeArguments[0] is not INamedTypeSymbol requestType) return null;

        var responseType = handlerInterface.TypeArguments[1];
        var location = classDecl.GetLocation();

        // ERR005 — handler is inaccessible (private, protected, file-local).
        if (!RequestTypeValidator.IsHandlerAccessible(symbol))
        {
            return new HandlerErrorData(
                diagnosticId: "ERR005",
                messageArgs: new[] { symbol.Name },
                diagnosticLocation: location);
        }

        // ERR003 — abstract handler cannot be instantiated by the generated dispatcher.
        if (symbol.IsAbstract)
        {
            return new HandlerErrorData(
                diagnosticId: "ERR003",
                messageArgs: new[] { symbol.Name },
                diagnosticLocation: location);
        }

        // ERR004 — TRequest does not implement IErrandRequest<TResponse>.
        if (!RequestTypeValidator.ImplementsIErrandRequest(requestType, responseType))
        {
            return new HandlerErrorData(
                diagnosticId: "ERR004",
                messageArgs: new[] { requestType.Name, responseType.ToDisplayString() },
                diagnosticLocation: location);
        }

        // Valid handler — no diagnostic needed.
        return null;
    }
}
