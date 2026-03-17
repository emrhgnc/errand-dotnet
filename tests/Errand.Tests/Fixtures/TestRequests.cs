using Errand.Abstractions;

namespace Errand.Tests.Fixtures;

/// <summary>Echoes its <see cref="Message"/> back as the response.</summary>
public sealed class EchoRequest : IErrandRequest<string>
{
    public string Message { get; init; } = string.Empty;
}

/// <summary>Returns the sum of <see cref="A"/> and <see cref="B"/>.</summary>
public sealed class AddRequest : IErrandRequest<int>
{
    public int A { get; init; }
    public int B { get; init; }
}

/// <summary>Simple presence-check request; always responds <see langword="true"/>.</summary>
public sealed class PingRequest : IErrandRequest<bool> { }
