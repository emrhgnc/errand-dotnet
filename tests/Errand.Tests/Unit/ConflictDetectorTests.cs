using System.Collections.Immutable;
using Errand.SourceGenerator;
using Errand.SourceGenerator.Analyzers;

namespace Errand.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="ConflictDetector"/>.
/// These tests exercise the conflict-detection logic directly,
/// independent of the full source-generator pipeline.
/// </summary>
public sealed class ConflictDetectorTests
{
    // -------------------------------------------------------------------------
    // Empty / single-handler cases
    // -------------------------------------------------------------------------

    [Fact]
    public void Detect_EmptyInput_ReturnsEmptyCollections()
    {
        var result = ConflictDetector.Detect(ImmutableArray<HandlerMetadata>.Empty);

        result.ValidHandlers.Should().BeEmpty();
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Detect_SingleHandler_ReturnsItAsValid()
    {
        var handler = MakeHandler("global::MyApp.MyRequest", "MyHandler");

        var result = ConflictDetector.Detect(ImmutableArray.Create(handler));

        result.ValidHandlers.Should().HaveCount(1);
        result.Conflicts.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Multi-handler, no conflicts
    // -------------------------------------------------------------------------

    [Fact]
    public void Detect_TwoHandlersDifferentRequests_ReturnsAllAsValid()
    {
        var h1 = MakeHandler("global::MyApp.RequestA", "HandlerA");
        var h2 = MakeHandler("global::MyApp.RequestB", "HandlerB");

        var result = ConflictDetector.Detect(ImmutableArray.Create(h1, h2));

        result.ValidHandlers.Should().HaveCount(2);
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public void Detect_ManyHandlersDifferentRequests_ReturnsAllAsValid()
    {
        var handlers = Enumerable.Range(0, 10)
            .Select(i => MakeHandler($"global::MyApp.Request{i}", $"Handler{i}"))
            .ToImmutableArray();

        var result = ConflictDetector.Detect(handlers);

        result.ValidHandlers.Should().HaveCount(10);
        result.Conflicts.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Conflict cases
    // -------------------------------------------------------------------------

    [Fact]
    public void Detect_TwoHandlersSameRequest_ReturnsOneConflict()
    {
        var h1 = MakeHandler("global::MyApp.MyRequest", "HandlerA");
        var h2 = MakeHandler("global::MyApp.MyRequest", "HandlerB");

        var result = ConflictDetector.Detect(ImmutableArray.Create(h1, h2));

        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].RequestFullName.Should().Be("global::MyApp.MyRequest");
        result.Conflicts[0].ConflictingHandlers.Should().HaveCount(2);
    }

    [Fact]
    public void Detect_Conflict_KeepsFirstHandlerInValidList()
    {
        var h1 = MakeHandler("global::MyApp.MyRequest", "HandlerA");
        var h2 = MakeHandler("global::MyApp.MyRequest", "HandlerB");

        var result = ConflictDetector.Detect(ImmutableArray.Create(h1, h2));

        // Exactly one valid handler is kept so code generation can proceed.
        result.ValidHandlers.Should().HaveCount(1);
        result.ValidHandlers[0].HandlerTypeName.Should().Be("HandlerA");
    }

    [Fact]
    public void Detect_ThreeHandlersSameRequest_ReportsAllInConflict()
    {
        var h1 = MakeHandler("global::MyApp.MyRequest", "HandlerA");
        var h2 = MakeHandler("global::MyApp.MyRequest", "HandlerB");
        var h3 = MakeHandler("global::MyApp.MyRequest", "HandlerC");

        var result = ConflictDetector.Detect(ImmutableArray.Create(h1, h2, h3));

        result.Conflicts[0].ConflictingHandlers.Should().HaveCount(3);
    }

    [Fact]
    public void Detect_MixedConflictAndValid_PartitionsCorrectly()
    {
        var conflictA1 = MakeHandler("global::MyApp.RequestA", "HandlerA1");
        var conflictA2 = MakeHandler("global::MyApp.RequestA", "HandlerA2");
        var validB     = MakeHandler("global::MyApp.RequestB", "HandlerB");

        var result = ConflictDetector.Detect(ImmutableArray.Create(conflictA1, conflictA2, validB));

        // One conflict group (RequestA) + two valid entries (first of A + B).
        result.Conflicts.Should().HaveCount(1);
        result.ValidHandlers.Should().HaveCount(2);
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private static HandlerMetadata MakeHandler(string requestFullName, string handlerTypeName) =>
        new HandlerMetadata(
            handlerFullName: "global::MyApp." + handlerTypeName,
            handlerTypeName: handlerTypeName,
            handlerNamespace: "MyApp",
            requestFullName: requestFullName,
            requestTypeName: requestFullName.Split('.')[^1],
            responseFullName: "global::System.String",
            responseTypeName: "String",
            diagnosticLocation: null);
}
