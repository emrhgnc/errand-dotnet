using Errand.Abstractions;
using Errand.Tests.Fixtures;

namespace Errand.Tests.Integration;

/// <summary>
/// End-to-end dispatch tests that exercise the <c>Errand.Generated.ErrandSender</c> class
/// produced by the source generator when it processes this test project's handler fixtures.
/// </summary>
public sealed class DispatcherTests
{
    // Use the interface type so tests stay decoupled from the concrete generated class.
    private readonly IErrandSender _sender = new Errand.Generated.ErrandSender();

    [Fact]
    public async Task SendAsync_EchoRequest_ReturnsMessage()
    {
        var result = await _sender.SendAsync(new EchoRequest { Message = "hello" });

        result.Should().Be("hello");
    }

    [Fact]
    public async Task SendAsync_EchoRequest_EmptyMessage_ReturnsEmptyString()
    {
        var result = await _sender.SendAsync(new EchoRequest { Message = string.Empty });

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_AddRequest_ReturnsSum()
    {
        var result = await _sender.SendAsync(new AddRequest { A = 3, B = 5 });

        result.Should().Be(8);
    }

    [Fact]
    public async Task SendAsync_AddRequest_NegativeNumbers_ReturnsCorrectSum()
    {
        var result = await _sender.SendAsync(new AddRequest { A = -10, B = 4 });

        result.Should().Be(-6);
    }

    [Fact]
    public async Task SendAsync_PingRequest_ReturnsTrue()
    {
        var result = await _sender.SendAsync(new PingRequest());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithCancellationToken_CompletesSuccessfully()
    {
        using var cts = new CancellationTokenSource();

        // Token is not cancelled; handler should complete normally.
        var result = await _sender.SendAsync(new PingRequest(), cts.Token);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_MultipleRequests_EachDispatchesToCorrectHandler()
    {
        var echoResult = await _sender.SendAsync(new EchoRequest { Message = "test" });
        var addResult  = await _sender.SendAsync(new AddRequest { A = 1, B = 2 });
        var pingResult = await _sender.SendAsync(new PingRequest());

        echoResult.Should().Be("test");
        addResult.Should().Be(3);
        pingResult.Should().BeTrue();
    }
}
