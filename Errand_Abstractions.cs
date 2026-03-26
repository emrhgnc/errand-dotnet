namespace Errand.Abstractions;

/// <summary>
/// Defines a request that returns a response of type TResponse.
/// </summary>
public interface IErrandRequest<out TResponse> { }

/// <summary>
/// Defines a handler for an Errand request.
/// </summary>
public interface IErrandHandler<in TRequest, TResponse> 
    where TRequest : IErrandRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}

/// <summary>
/// Marker attribute for the Source Generator to identify handlers.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ErrandHandlerAttribute : Attribute { }

/// <summary>
/// The main entry point for sending errands.
/// </summary>
public interface IErrandSender
{
    ValueTask<TResponse> SendAsync<TResponse>(IErrandRequest<TResponse> request, CancellationToken ct = default);
}