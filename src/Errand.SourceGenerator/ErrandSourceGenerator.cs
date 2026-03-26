using Errand.SourceGenerator.Analyzers;
using Errand.SourceGenerator.Generators;
using Microsoft.CodeAnalysis;

namespace Errand.SourceGenerator;

/// <summary>
/// Roslyn incremental source generator that scans the compilation for
/// <c>IErrandHandler&lt;TRequest, TResponse&gt;</c> implementations and emits a
/// compile-time, reflection-free dispatcher class (<c>Errand.Generated.ErrandSender</c>).
/// </summary>
/// <remarks>
/// <para>
/// The generator runs in two logical phases:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Scan phase</b> — <see cref="HandlerScanner.CreateHandlerProvider"/> sets up an
///       incremental pipeline that filters class declarations syntactically and then uses the
///       semantic model to extract <see cref="HandlerMetadata"/> for each valid handler.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Emit phase</b> — <see cref="HandlerDispatcherGenerator.Execute"/> collects all
///       metadata, detects conflicts (ERR002), reports diagnostics, and writes the generated
///       source file (<c>Errand.g.cs</c>) to the compilation.
///     </description>
///   </item>
/// </list>
/// <para>
/// Because this implements <see cref="IIncrementalGenerator"/>, Roslyn caches the outputs of
/// each pipeline step. Re-generation is triggered only when the relevant syntax or semantic
/// information changes, keeping IDE responsiveness high.
/// </para>
/// </remarks>
[Generator]
public sealed class ErrandSourceGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Code-generation pipeline ──────────────────────────────────────────
        // 1. Scan — emit one HandlerMetadata per valid handler found in the compilation.
        // 2. Collect — gather the per-handler values into a single ImmutableArray so the
        //    emit step can see all handlers at once (required for conflict detection).
        // 3. RegisterSourceOutput — run the emitter whenever the collected set changes.

        var handlerProvider = HandlerScanner.CreateHandlerProvider(context);
        var collectedHandlers = handlerProvider.Collect();

        context.RegisterSourceOutput(collectedHandlers, HandlerDispatcherGenerator.Execute);

        // ── Diagnostic pipeline ───────────────────────────────────────────────
        // Runs independently of code generation. Reports ERR003 (abstract handler),
        // ERR004 (request type mismatch), and ERR005 (inaccessible handler) for
        // handler candidates that cannot be included in the generated dispatcher.
        // Each error value is reported individually as it flows through the pipeline.

        var diagnosticProvider = HandlerScanner.CreateDiagnosticProvider(context);

        context.RegisterSourceOutput(
            diagnosticProvider,
            static (ctx, error) =>
                ctx.ReportDiagnostic(DiagnosticHelper.CreateHandlerErrorDiagnostic(error)));
    }
}
