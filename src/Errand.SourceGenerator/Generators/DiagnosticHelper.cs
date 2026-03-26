using Microsoft.CodeAnalysis;
using Errand.SourceGenerator.Analyzers;

namespace Errand.SourceGenerator.Generators;

/// <summary>
/// Centralised definitions for all Errand compiler diagnostics.
/// Descriptors are static so they can be declared in the generator's
/// <c>SupportedDiagnostics</c> property without allocating on every run.
/// </summary>
internal static class DiagnosticHelper
{
    private const string Category = "Errand";

    // -------------------------------------------------------------------------
    // Error descriptors
    // -------------------------------------------------------------------------

    /// <summary>
    /// ERR001 — Reserved descriptor for a missing handler.
    /// Emitted when a request type has no registered handler.
    /// <para>Format args: {0} = request type name.</para>
    /// </summary>
    /// <remarks>
    /// Tracking all <c>IErrandRequest</c> usages across the compilation requires a
    /// dedicated scan of call-sites and is planned for a future release. The descriptor
    /// is defined here so it can be referenced by documentation and tests.
    /// </remarks>
    public static readonly DiagnosticDescriptor MissingHandler = new DiagnosticDescriptor(
        id: "ERR001",
        title: "Missing Handler",
        messageFormat: "No IErrandHandler<{0}, TResponse> implementation was found for request type '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Each IErrandRequest<TResponse> passed to IErrandSender.SendAsync must have " +
            "exactly one IErrandHandler<TRequest, TResponse> implementation in the compilation.");

    /// <summary>
    /// ERR002 — Emitted when more than one handler is registered for the same request type.
    /// <para>Format args: {0} = request full name, {1} = comma-separated handler names.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingHandlers = new DiagnosticDescriptor(
        id: "ERR002",
        title: "Conflicting Handler Registrations",
        messageFormat: "Multiple handlers found for request type '{0}': {1}. Only one IErrandHandler<TRequest, TResponse> per request type is allowed.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Each IErrandRequest<TResponse> must be handled by exactly one " +
            "IErrandHandler<TRequest, TResponse>. Remove or merge the duplicate handlers.");

    /// <summary>
    /// ERR003 — Emitted when a handler class is abstract and therefore cannot be instantiated
    /// by the generated dispatcher (<c>new THandler()</c>).
    /// <para>Format args: {0} = handler type name.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor AbstractHandler = new DiagnosticDescriptor(
        id: "ERR003",
        title: "Handler Must Not Be Abstract",
        messageFormat: "Handler type '{0}' is abstract and cannot be instantiated by the generated dispatcher. Remove the abstract modifier or provide a concrete subclass.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The Errand source generator instantiates handlers via 'new THandler()' in generated code. " +
            "Abstract classes cannot be constructed directly. Make the handler class concrete, " +
            "or move the implementation to a non-abstract derived class.");

    /// <summary>
    /// ERR004 — Emitted when the request type argument of an <c>IErrandHandler</c>
    /// does not implement <c>IErrandRequest&lt;TResponse&gt;</c>.
    /// <para>Format args: {0} = request type name, {1} = expected response type.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor RequestTypeMismatch = new DiagnosticDescriptor(
        id: "ERR004",
        title: "Request Type Does Not Implement IErrandRequest",
        messageFormat: "Request type '{0}' must implement IErrandRequest<{1}> to be dispatched by IErrandSender",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every TRequest used in IErrandHandler<TRequest, TResponse> must also implement " +
            "IErrandRequest<TResponse>. This constraint allows the compiler to route requests " +
            "to their handlers at call-sites of IErrandSender.SendAsync.");

    /// <summary>
    /// ERR005 — Emitted when a handler type is not public or internal.
    /// <para>Format args: {0} = handler type name.</para>
    /// </summary>
    public static readonly DiagnosticDescriptor HandlerNotAccessible = new DiagnosticDescriptor(
        id: "ERR005",
        title: "Handler Not Accessible",
        messageFormat: "Handler type '{0}' must be public or internal. Private and file-local types cannot be instantiated by the generated dispatcher.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The Errand source generator instantiates handlers in generated code. " +
            "Mark the handler class as public or internal.");

    // -------------------------------------------------------------------------
    // Factory methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an ERR002 <see cref="Diagnostic"/> for the given <see cref="HandlerConflict"/>.
    /// The location is taken from the first conflicting handler's declaration.
    /// </summary>
    public static Diagnostic CreateConflictDiagnostic(HandlerConflict conflict)
    {
        var location = conflict.ConflictingHandlers.Length > 0
            ? conflict.ConflictingHandlers[0].DiagnosticLocation ?? Location.None
            : Location.None;

        // Strip "global::" from display names so the message is human-readable.
        var requestName = StripGlobal(conflict.RequestFullName);
        var handlerNames = string.Join(", ", System.Array.ConvertAll(
            conflict.ConflictingHandlers, h => h.HandlerTypeName));

        return Diagnostic.Create(ConflictingHandlers, location, requestName, handlerNames);
    }

    /// <summary>
    /// Creates a handler-level <see cref="Diagnostic"/> (ERR003, ERR004, or ERR005)
    /// from a <see cref="HandlerErrorData"/> produced by the diagnostic pipeline.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown if <paramref name="error"/>'s <c>DiagnosticId</c> is not a recognised handler error.
    /// </exception>
    public static Diagnostic CreateHandlerErrorDiagnostic(HandlerErrorData error)
    {
        var descriptor = error.DiagnosticId switch
        {
            "ERR003" => AbstractHandler,
            "ERR004" => RequestTypeMismatch,
            "ERR005" => HandlerNotAccessible,
            _ => throw new System.ArgumentOutOfRangeException(
                nameof(error), error.DiagnosticId, "Unknown handler error diagnostic ID.")
        };

        var location = error.DiagnosticLocation ?? Location.None;
        return Diagnostic.Create(descriptor, location, (object[])error.MessageArgs);
    }

    /// <summary>Strips the <c>global::</c> qualifier prefix for use in diagnostic messages.</summary>
    private static string StripGlobal(string name) =>
        name.StartsWith("global::", System.StringComparison.Ordinal)
            ? name.Substring("global::".Length)
            : name;
}
