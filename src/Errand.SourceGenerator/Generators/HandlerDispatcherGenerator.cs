using System.Collections.Generic;
using System.Collections.Immutable;
using Errand.SourceGenerator.Analyzers;
using Errand.SourceGenerator.Generators.Templates;
using Microsoft.CodeAnalysis;

namespace Errand.SourceGenerator.Generators;

/// <summary>
/// Orchestrates the code-generation step of the Errand source generator pipeline.
/// Accepts the validated, conflict-free set of handler metadata, runs conflict
/// detection, emits any ERR002 diagnostics, and then delegates source rendering
/// to <see cref="DispatcherTemplate"/>.
/// </summary>
internal static class HandlerDispatcherGenerator
{
    /// <summary>The hint name used when adding the generated source to the compilation.</summary>
    public const string GeneratedFileName = "Errand.g.cs";

    /// <summary>
    /// Processes <paramref name="handlers"/>, reports any duplicate-handler errors
    /// via <paramref name="ctx"/>, and returns the generated source code.
    /// </summary>
    /// <param name="ctx">
    /// The <see cref="SourceProductionContext"/> used to emit diagnostics and add
    /// the generated source to the compilation.
    /// </param>
    /// <param name="handlers">
    /// All handlers collected from the current compilation by <see cref="HandlerScanner"/>.
    /// </param>
    public static void Execute(
        SourceProductionContext ctx,
        ImmutableArray<HandlerMetadata> handlers)
    {
        ctx.CancellationToken.ThrowIfCancellationRequested();

        // Detect duplicate handlers and emit ERR002 for each conflict group.
        var result = ConflictDetector.Detect(handlers);

        foreach (var conflict in result.Conflicts)
        {
            ctx.ReportDiagnostic(DiagnosticHelper.CreateConflictDiagnostic(conflict));
        }

        // Generate code from the valid (non-conflicting) handlers.
        var validHandlers = (IReadOnlyList<HandlerMetadata>)result.ValidHandlers;
        var source = DispatcherTemplate.Render(validHandlers);

        ctx.AddSource(GeneratedFileName, source);
    }
}
