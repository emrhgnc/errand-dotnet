using Errand.Abstractions;

namespace Errand.Tests.Fixtures;

public sealed class EchoHandler : IErrandHandler<EchoRequest, string>
{
    public ValueTask<string> HandleAsync(EchoRequest request, CancellationToken ct)
        => new(request.Message);
}

public sealed class AddHandler : IErrandHandler<AddRequest, int>
{
    public ValueTask<int> HandleAsync(AddRequest request, CancellationToken ct)
        => new(request.A + request.B);
}

public sealed class PingHandler : IErrandHandler<PingRequest, bool>
{
    public ValueTask<bool> HandleAsync(PingRequest request, CancellationToken ct)
        => new(true);
}
