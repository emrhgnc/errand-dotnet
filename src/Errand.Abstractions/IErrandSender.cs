namespace Errand.Abstractions;

/// <summary>
/// The primary entry point for dispatching Errand requests to their handlers.
/// </summary>
/// <remarks>
/// <para>
/// Register the default implementation via <c>services.AddErrand()</c> from
/// <c>Errand.Core</c>. The source generator wires up all handlers at compile time,
/// so <see cref="SendAsync{TResponse}"/> incurs no reflection overhead at runtime.
/// </para>
/// <para>
/// This interface is intentionally kept minimal to enable easy mocking in unit tests.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // ASP.NET Core controller
/// public class UsersController : ControllerBase
/// {
///     private readonly IErrandSender _sender;
///
///     public UsersController(IErrandSender sender) => _sender = sender;
///
///     [HttpGet("{id}")]
///     public async Task&lt;UserDto&gt; Get(int id, CancellationToken ct)
///         => await _sender.SendAsync(new GetUserQuery { UserId = id }, ct);
/// }
/// </code>
/// </example>
public interface IErrandSender
{
    /// <summary>
    /// Dispatches the specified <paramref name="request"/> to its registered handler
    /// and returns the response asynchronously.
    /// </summary>
    /// <typeparam name="TResponse">The response type produced by the request's handler.</typeparam>
    /// <param name="request">The request to dispatch. Must not be <see langword="null"/>.</param>
    /// <param name="ct">
    /// A cancellation token to propagate to the handler. Defaults to <see cref="CancellationToken.None"/>.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous dispatch operation.
    /// The task result contains the response produced by the handler.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if no handler is registered for <paramref name="request"/>'s type.
    /// Under normal usage this cannot happen because the source generator enforces
    /// handler registration at compile time.
    /// </exception>
    ValueTask<TResponse> SendAsync<TResponse>(IErrandRequest<TResponse> request, CancellationToken ct = default);
}
