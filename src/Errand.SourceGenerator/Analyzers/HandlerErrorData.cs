using Microsoft.CodeAnalysis;

namespace Errand.SourceGenerator.Analyzers;

/// <summary>
/// Carries the information needed to emit a handler-level compiler diagnostic
/// from within the incremental generator pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Instances travel through the incremental pipeline as values, so the type
/// implements <see cref="IEquatable{T}"/> with <see cref="DiagnosticLocation"/>
/// excluded from equality. This prevents trivial source edits (whitespace,
/// comments) from invalidating the diagnostic-reporting step unnecessarily.
/// </para>
/// </remarks>
internal sealed class HandlerErrorData : IEquatable<HandlerErrorData>
{
    /// <summary>Gets the diagnostic descriptor ID (e.g., <c>"ERR003"</c>).</summary>
    public string DiagnosticId { get; }

    /// <summary>
    /// Gets the ordered message arguments that are substituted into the
    /// descriptor's <c>messageFormat</c> string.
    /// </summary>
    public string[] MessageArgs { get; }

    /// <summary>
    /// Gets the source location to attach to the diagnostic.
    /// Excluded from equality and hash-code computation.
    /// </summary>
    public Location? DiagnosticLocation { get; }

    /// <summary>Initializes a new instance of <see cref="HandlerErrorData"/>.</summary>
    public HandlerErrorData(string diagnosticId, string[] messageArgs, Location? diagnosticLocation)
    {
        DiagnosticId = diagnosticId;
        MessageArgs = messageArgs;
        DiagnosticLocation = diagnosticLocation;
    }

    /// <inheritdoc/>
    public bool Equals(HandlerErrorData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (DiagnosticId != other.DiagnosticId) return false;
        if (MessageArgs.Length != other.MessageArgs.Length) return false;

        for (int i = 0; i < MessageArgs.Length; i++)
        {
            if (MessageArgs[i] != other.MessageArgs[i]) return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as HandlerErrorData);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = DiagnosticId.GetHashCode();
            foreach (var arg in MessageArgs)
                hash = (hash * 31) + arg.GetHashCode();
            return hash;
        }
    }
}
