namespace Errand.Abstractions;

/// <summary>
/// Marks a class as an Errand request handler, enabling the source generator to include
/// it in the compile-time handler registry.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to any class that implements
/// <see cref="IErrandHandler{TRequest,TResponse}"/>. The Errand source generator scans
/// for this attribute during compilation to build the reflection-free dispatch table.
/// </para>
/// <para>
/// Omitting this attribute on a handler class will cause a compile-time warning (WRN001),
/// because the handler will not be reachable via <see cref="IErrandSender"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ErrandHandler]
/// public sealed class CreateOrderCommandHandler : IErrandHandler&lt;CreateOrderCommand, OrderId&gt;
/// {
///     public async ValueTask&lt;OrderId&gt; HandleAsync(CreateOrderCommand request, CancellationToken ct)
///     {
///         // ...
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ErrandHandlerAttribute : Attribute { }
