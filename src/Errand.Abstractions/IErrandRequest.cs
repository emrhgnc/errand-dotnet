namespace Errand.Abstractions;

/// <summary>
/// Marker interface for a request that produces a response of type <typeparamref name="TResponse"/>.
/// Implement this interface on your request objects to enable compile-time handler discovery
/// by the Errand source generator.
/// </summary>
/// <typeparam name="TResponse">
/// The type of the response returned when this request is handled.
/// </typeparam>
/// <example>
/// <code>
/// public sealed class GetUserQuery : IErrandRequest&lt;UserDto&gt;
/// {
///     public int UserId { get; init; }
/// }
/// </code>
/// </example>
public interface IErrandRequest<out TResponse> { }
