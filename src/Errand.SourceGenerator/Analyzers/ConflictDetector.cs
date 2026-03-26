using System.Collections.Generic;
using System.Collections.Immutable;

namespace Errand.SourceGenerator.Analyzers;

/// <summary>
/// Detects situations where more than one handler is registered for the same request type.
/// The Errand dispatch model requires a strict 1-to-1 mapping between a request type and
/// its handler; any violation must be reported as compile-time error ERR002.
/// </summary>
internal static class ConflictDetector
{
    /// <summary>
    /// Partitions <paramref name="handlers"/> into those with unique request types
    /// and those that conflict (multiple handlers for the same request).
    /// </summary>
    /// <param name="handlers">
    /// All valid handlers discovered in the current compilation
    /// (typically from <see cref="HandlerScanner"/> after a <c>Collect()</c> step).
    /// </param>
    /// <returns>
    /// A <see cref="ConflictDetectionResult"/> containing handlers safe for code
    /// generation and any conflict groups that must be reported as errors.
    /// When a conflict exists, the first handler in the group is kept so the build
    /// can continue and surface as many errors as possible.
    /// </returns>
    public static ConflictDetectionResult Detect(ImmutableArray<HandlerMetadata> handlers)
    {
        var byRequest = new Dictionary<string, List<HandlerMetadata>>(handlers.Length);

        foreach (var handler in handlers)
        {
            if (!byRequest.TryGetValue(handler.RequestFullName, out var group))
            {
                group = new List<HandlerMetadata>(1);
                byRequest[handler.RequestFullName] = group;
            }

            group.Add(handler);
        }

        var validHandlers = new List<HandlerMetadata>(handlers.Length);
        var conflicts = new List<HandlerConflict>();

        foreach (var kvp in byRequest)
        {
            if (kvp.Value.Count == 1)
            {
                validHandlers.Add(kvp.Value[0]);
            }
            else
            {
                // Keep the first handler so code generation can proceed
                // while every handler in the group is included in the diagnostic.
                validHandlers.Add(kvp.Value[0]);
                conflicts.Add(new HandlerConflict(kvp.Key, kvp.Value.ToArray()));
            }
        }

        return new ConflictDetectionResult(validHandlers.ToArray(), conflicts.ToArray());
    }
}

/// <summary>
/// Describes a group of handlers that all claim to handle the same request type.
/// Used when constructing the ERR002 diagnostic.
/// </summary>
/// <param name="RequestFullName">Fully-qualified name of the duplicated request type.</param>
/// <param name="ConflictingHandlers">All handlers registered for <paramref name="RequestFullName"/>.</param>
internal sealed record HandlerConflict(
    string RequestFullName,
    HandlerMetadata[] ConflictingHandlers);

/// <summary>
/// The output of <see cref="ConflictDetector.Detect"/>.
/// </summary>
/// <param name="ValidHandlers">
/// Handlers with unique request types; safe to pass to the code-generation step.
/// </param>
/// <param name="Conflicts">
/// Handler groups that violate the 1-to-1 rule; each must produce an ERR002 diagnostic.
/// </param>
internal sealed record ConflictDetectionResult(
    HandlerMetadata[] ValidHandlers,
    HandlerConflict[] Conflicts);
