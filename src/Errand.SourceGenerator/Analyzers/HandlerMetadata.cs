using Microsoft.CodeAnalysis;

namespace Errand.SourceGenerator;

/// <summary>
/// Immutable data model representing a handler discovered during compilation.
/// Implements <see cref="IEquatable{T}"/> to support incremental generator caching:
/// only handler/request/response type names participate in equality so that
/// trivial source edits (e.g. adding blank lines) do not invalidate the
/// downstream code-generation step.
/// </summary>
internal sealed class HandlerMetadata : IEquatable<HandlerMetadata>
{
    /// <summary>Fully-qualified type name of the handler, e.g. <c>MyApp.CreateUserHandler</c>.</summary>
    public string HandlerFullName { get; }

    /// <summary>Simple (unqualified) type name of the handler, e.g. <c>CreateUserHandler</c>.</summary>
    public string HandlerTypeName { get; }

    /// <summary>Namespace that contains the handler, e.g. <c>MyApp</c>. Empty for the global namespace.</summary>
    public string HandlerNamespace { get; }

    /// <summary>Fully-qualified type name of the request, e.g. <c>MyApp.CreateUserRequest</c>.</summary>
    public string RequestFullName { get; }

    /// <summary>Simple (unqualified) type name of the request, e.g. <c>CreateUserRequest</c>.</summary>
    public string RequestTypeName { get; }

    /// <summary>Fully-qualified type name of the response, e.g. <c>System.String</c>.</summary>
    public string ResponseFullName { get; }

    /// <summary>Simple (unqualified) type name of the response, e.g. <c>String</c>.</summary>
    public string ResponseTypeName { get; }

    /// <summary>
    /// Source location of the handler class declaration.
    /// Used only when emitting diagnostics; excluded from equality comparisons.
    /// </summary>
    public Location? DiagnosticLocation { get; }

    public HandlerMetadata(
        string handlerFullName,
        string handlerTypeName,
        string handlerNamespace,
        string requestFullName,
        string requestTypeName,
        string responseFullName,
        string responseTypeName,
        Location? diagnosticLocation = null)
    {
        HandlerFullName = handlerFullName;
        HandlerTypeName = handlerTypeName;
        HandlerNamespace = handlerNamespace;
        RequestFullName = requestFullName;
        RequestTypeName = requestTypeName;
        ResponseFullName = responseFullName;
        ResponseTypeName = responseTypeName;
        DiagnosticLocation = diagnosticLocation;
    }

    /// <inheritdoc/>
    /// <remarks>DiagnosticLocation is intentionally excluded.</remarks>
    public bool Equals(HandlerMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return HandlerFullName == other.HandlerFullName
            && RequestFullName == other.RequestFullName
            && ResponseFullName == other.ResponseFullName;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as HandlerMetadata);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = HandlerFullName.GetHashCode();
            hash = (hash * 31) + RequestFullName.GetHashCode();
            hash = (hash * 31) + ResponseFullName.GetHashCode();
            return hash;
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{HandlerFullName} handles {RequestFullName} → {ResponseFullName}";
}
