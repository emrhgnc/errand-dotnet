using System.Collections.Immutable;
using Errand.Abstractions;
using Errand.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Errand.Tests.SourceGenerator;

/// <summary>
/// In-memory source-generator tests.
/// Each test compiles a small C# snippet with <see cref="CSharpGeneratorDriver"/>,
/// then asserts on the diagnostics the generator emits and on the generated source text.
/// </summary>
public sealed class SourceGeneratorTests
{
    // Pre-built reference set: all trusted platform assemblies + Errand.Abstractions.
    // Static so it is only evaluated once for the entire test class.
    private static readonly IReadOnlyList<MetadataReference> References = BuildReferences();

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var trustedPaths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var refs = trustedPaths
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // Errand.Abstractions must be available so in-memory code can reference IErrandRequest etc.
        refs.Add(MetadataReference.CreateFromFile(typeof(IErrandRequest<>).Assembly.Location));
        return refs;
    }

    /// <summary>
    /// Runs <see cref="ErrandSourceGenerator"/> on <paramref name="source"/> and returns
    /// the generator-emitted diagnostics and generated source files.
    /// </summary>
    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Sources)
        RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var updatedDriver = CSharpGeneratorDriver
            .Create(new ErrandSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiags);

        var runResult = updatedDriver.GetRunResult();
        var sources = runResult.Results.IsEmpty
            ? ImmutableArray<GeneratedSourceResult>.Empty
            : runResult.Results[0].GeneratedSources;

        return (generatorDiags, sources);
    }

    // -------------------------------------------------------------------------
    // Generated code shape
    // -------------------------------------------------------------------------

    [Fact]
    public void Generator_EmitsSenderClass_ForSingleHandler()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;

public class MyRequest : IErrandRequest<string> {}

public class MyHandler : IErrandHandler<MyRequest, string>
{
    public ValueTask<string> HandleAsync(MyRequest req, CancellationToken ct) => new(""ok"");
}";
        var (diags, sources) = RunGenerator(source);

        diags.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        sources.Should().ContainSingle(s => s.HintName == "Errand.g.cs");

        var generated = sources.Single(s => s.HintName == "Errand.g.cs").SourceText.ToString();
        generated.Should().Contain("ErrandSender");
        generated.Should().Contain("Dispatch_MyRequest");
        // Generated code uses fully-qualified "global::" prefix for all type names.
        generated.Should().Contain("new global::MyHandler().HandleAsync");
    }

    [Fact]
    public void Generator_EmitsSenderWithThrowBody_WhenNoHandlersPresent()
    {
        const string source = "// no handlers in this file";

        var (diags, sources) = RunGenerator(source);

        diags.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        sources.Should().ContainSingle(s => s.HintName == "Errand.g.cs");

        var generated = sources.Single(s => s.HintName == "Errand.g.cs").SourceText.ToString();
        generated.Should().Contain("throw new global::System.InvalidOperationException");
        generated.Should().Contain("No Errand handlers were found");
    }

    [Fact]
    public void Generator_EmitsSenderInErrandGeneratedNamespace()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class R : IErrandRequest<bool> {}
public class H : IErrandHandler<R, bool>
{
    public ValueTask<bool> HandleAsync(R req, CancellationToken ct) => new(true);
}";
        var (_, sources) = RunGenerator(source);

        var generated = sources.Single(s => s.HintName == "Errand.g.cs").SourceText.ToString();
        generated.Should().Contain("namespace Errand.Generated");
        generated.Should().Contain("internal sealed class ErrandSender");
    }

    [Fact]
    public void Generator_DispatchMethodName_UsesUnderscoreSeparatedQualifiedName()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
namespace My.App.Orders
{
    public class CreateOrderRequest : IErrandRequest<int> {}
    public class CreateOrderHandler : IErrandHandler<CreateOrderRequest, int>
    {
        public ValueTask<int> HandleAsync(CreateOrderRequest req, CancellationToken ct) => new(1);
    }
}";
        var (_, sources) = RunGenerator(source);

        var generated = sources.Single(s => s.HintName == "Errand.g.cs").SourceText.ToString();
        generated.Should().Contain("Dispatch_My_App_Orders_CreateOrderRequest");
    }

    [Fact]
    public void Generator_MultipleHandlers_EmitsDispatchMethodForEach()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class RequestA : IErrandRequest<string> {}
public class RequestB : IErrandRequest<int> {}
public class HandlerA : IErrandHandler<RequestA, string>
{
    public ValueTask<string> HandleAsync(RequestA req, CancellationToken ct) => new(""a"");
}
public class HandlerB : IErrandHandler<RequestB, int>
{
    public ValueTask<int> HandleAsync(RequestB req, CancellationToken ct) => new(1);
}";
        var (diags, sources) = RunGenerator(source);

        diags.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        var generated = sources.Single(s => s.HintName == "Errand.g.cs").SourceText.ToString();
        generated.Should().Contain("Dispatch_RequestA");
        generated.Should().Contain("Dispatch_RequestB");
    }

    // -------------------------------------------------------------------------
    // Diagnostic emission
    // -------------------------------------------------------------------------

    [Fact]
    public void Generator_ReportsERR002_ForDuplicateHandlers()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class MyRequest : IErrandRequest<string> {}
public class HandlerA : IErrandHandler<MyRequest, string>
{
    public ValueTask<string> HandleAsync(MyRequest req, CancellationToken ct) => new(""a"");
}
public class HandlerB : IErrandHandler<MyRequest, string>
{
    public ValueTask<string> HandleAsync(MyRequest req, CancellationToken ct) => new(""b"");
}";
        var (diags, _) = RunGenerator(source);

        diags.Should().Contain(d => d.Id == "ERR002");
    }

    [Fact]
    public void Generator_ReportsERR003_ForAbstractHandler()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class MyRequest : IErrandRequest<string> {}
public abstract class AbstractHandler : IErrandHandler<MyRequest, string>
{
    public abstract ValueTask<string> HandleAsync(MyRequest req, CancellationToken ct);
}";
        var (diags, _) = RunGenerator(source);

        diags.Should().Contain(d => d.Id == "ERR003");
    }

    [Fact]
    public void Generator_ReportsERR005_ForPrivateNestedHandler()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class MyRequest : IErrandRequest<string> {}
public class Container
{
    private class PrivateHandler : IErrandHandler<MyRequest, string>
    {
        public ValueTask<string> HandleAsync(MyRequest req, CancellationToken ct) => new(""priv"");
    }
}";
        var (diags, _) = RunGenerator(source);

        diags.Should().Contain(d => d.Id == "ERR005");
    }

    [Fact]
    public void Generator_DoesNotEmitERR002_ForHandlersDifferentRequests()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class RequestA : IErrandRequest<string> {}
public class RequestB : IErrandRequest<int> {}
public class HandlerA : IErrandHandler<RequestA, string>
{
    public ValueTask<string> HandleAsync(RequestA req, CancellationToken ct) => new(""a"");
}
public class HandlerB : IErrandHandler<RequestB, int>
{
    public ValueTask<int> HandleAsync(RequestB req, CancellationToken ct) => new(1);
}";
        var (diags, _) = RunGenerator(source);

        diags.Should().NotContain(d => d.Id == "ERR002");
    }

    [Fact]
    public void Generator_AbstractHandler_IsExcludedFromGeneratedDispatcher()
    {
        const string source = @"
using Errand.Abstractions;
using System.Threading;
using System.Threading.Tasks;
public class MyRequest : IErrandRequest<string> {}
public abstract class AbstractHandler : IErrandHandler<MyRequest, string>
{
    public abstract ValueTask<string> HandleAsync(MyRequest req, CancellationToken ct);
}";
        var (_, sources) = RunGenerator(source);

        // The dispatcher should not contain a dispatch method for the abstract handler.
        var generated = sources.Single(s => s.HintName == "Errand.g.cs").SourceText.ToString();
        generated.Should().NotContain("new AbstractHandler()");
    }
}
