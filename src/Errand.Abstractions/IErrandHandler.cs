namespace Errand.Abstractions;

/// <summary>
/// Defines a handler for an Errand request of type <typeparamref name="TRequest"/>
/// that returns a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The request type to handle. Must implement <see cref="IErrandRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">
/// The type of the response produced by this handler.
/// </typeparam>
/// <remarks>
/// Each request type must have exactly one handler. The Errand source generator will emit
/// a compile-time error (ERR002) if multiple handlers are registered for the same request type.
/// </remarks>
/// <example>
/// <code>
/// [ErrandHandler]
/// public sealed class GetUserQueryHandler : IErrandHandler&lt;GetUserQuery, UserDto&gt;
/// {
///     public async ValueTask&lt;UserDto&gt; HandleAsync(GetUserQuery request, CancellationToken ct)
///     {
///         // handler logic
///     }
/// }
/// </code>
/// </example>
public interface IErrandHandler<in TRequest, TResponse>
    where TRequest : IErrandRequest<TResponse>
{
    /// <summary>
    /// Handles the specified <paramref name="request"/> and returns the response asynchronously.
    /// </summary>
    /// <param name="request">The request to handle.</param>
    /// <param name="ct">A cancellation token to observe while waiting for the task to complete.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation.
    /// The task result contains the handler's response of type <typeparamref name="TResponse"/>.
    /// </returns>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}
