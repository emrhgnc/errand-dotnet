# Getting Started with Errand

This guide walks you through adding Errand to a new or existing .NET 8 application.

---

## Prerequisites

- .NET 8 SDK or later
- C# 12 or later
- Any host that supports `Microsoft.Extensions.DependencyInjection`
  (ASP.NET Core, Worker Service, Console, etc.)

---

## Step 1 — Install the packages

Add all three packages to your application project:

```
dotnet add package Errand.Abstractions
dotnet add package Errand.Core
dotnet add package Errand.SourceGenerator
```

Your `.csproj` should end up with entries like:

```xml
<PackageReference Include="Errand.Abstractions"    Version="0.1.0" />
<PackageReference Include="Errand.Core"            Version="0.1.0" />
<PackageReference Include="Errand.SourceGenerator" Version="0.1.0">
  <!-- Source generators should not flow into consumers of your library -->
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

---

## Step 2 — Define a request

A request is a plain class (or record) that implements `IErrandRequest<TResponse>`.
The type parameter `TResponse` declares what the handler will return.

```csharp
using Errand.Abstractions;

// Returns the newly created order's ID.
public sealed class PlaceOrderRequest : IErrandRequest<OrderId>
{
    public Guid CustomerId { get; init; }
    public IReadOnlyList<OrderLine> Lines { get; init; } = [];
}
```

There are no base classes to inherit, no marker attributes to apply — just the interface.

---

## Step 3 — Implement a handler

```csharp
using Errand.Abstractions;

public sealed class PlaceOrderHandler : IErrandHandler<PlaceOrderRequest, OrderId>
{
    private readonly IOrderRepository _orders;
    private readonly IEventBus _events;

    public PlaceOrderHandler(IOrderRepository orders, IEventBus events)
    {
        _orders = orders;
        _events = events;
    }

    public async ValueTask<OrderId> HandleAsync(
        PlaceOrderRequest request,
        CancellationToken ct)
    {
        var order = Order.Create(request.CustomerId, request.Lines);
        await _orders.SaveAsync(order, ct);
        await _events.PublishAsync(new OrderPlacedEvent(order.Id), ct);
        return order.Id;
    }
}
```

**Rules enforced at compile time:**
- Each request type must have **exactly one** handler (`IErrandHandler<TRequest, TResponse>`).
- The handler must be `public` or `internal`.
- The handler must not be `abstract`.

---

## Step 4 — Register with the DI container

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Register Errand — the type anchor tells the generator which assembly to scan.
builder.Services.AddErrand<Program>();

// Register your handlers normally (they receive their own dependencies via DI).
builder.Services.AddScoped<PlaceOrderHandler>();
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();
builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
```

> **Tip:** `AddErrand<T>()` registers the generated `IErrandSender` implementation.
> You still register your handler classes separately so DI can inject their dependencies.

---

## Step 5 — Inject and use `IErrandSender`

```csharp
[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly IErrandSender _sender;

    public OrdersController(IErrandSender sender) => _sender = sender;

    [HttpPost]
    public async Task<IActionResult> Place(
        [FromBody] PlaceOrderDto dto,
        CancellationToken ct)
    {
        var orderId = await _sender.SendAsync(
            new PlaceOrderRequest
            {
                CustomerId = dto.CustomerId,
                Lines = dto.Lines.Select(l => new OrderLine(l.ProductId, l.Quantity)).ToList()
            },
            ct);

        return CreatedAtAction(nameof(Get), new { id = orderId }, orderId);
    }
}
```

---

## Common Patterns

### Query (read-only operation)

```csharp
public sealed class GetOrderQuery : IErrandRequest<OrderDto?>
{
    public Guid OrderId { get; init; }
}

public sealed class GetOrderQueryHandler : IErrandHandler<GetOrderQuery, OrderDto?>
{
    private readonly IOrderRepository _repo;

    public GetOrderQueryHandler(IOrderRepository repo) => _repo = repo;

    public async ValueTask<OrderDto?> HandleAsync(GetOrderQuery query, CancellationToken ct)
        => await _repo.FindAsync(query.OrderId, ct) is { } order
            ? new OrderDto(order)
            : null;
}
```

### Command with no meaningful return value

Use `bool` (always `true`) or a dedicated `Unit` type as the response to keep
the interface uniform:

```csharp
public sealed class DeleteOrderRequest : IErrandRequest<bool>
{
    public Guid OrderId { get; init; }
}

public sealed class DeleteOrderHandler : IErrandHandler<DeleteOrderRequest, bool>
{
    public async ValueTask<bool> HandleAsync(DeleteOrderRequest request, CancellationToken ct)
    {
        await _repo.DeleteAsync(request.OrderId, ct);
        return true;
    }
}
```

### Cancellation

`CancellationToken` is a first-class citizen — pass it through to every `async` call:

```csharp
public async ValueTask<OrderId> HandleAsync(PlaceOrderRequest request, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    var order = await _orders.SaveAsync(request, ct);   // ← always forward ct
    return order.Id;
}
```

### Organising handlers in separate assemblies

If your handlers live in a different assembly than `Program.cs`, use the assembly overload:

```csharp
// Reference any type that lives in the handlers assembly.
builder.Services.AddErrand(typeof(PlaceOrderHandler).Assembly);
```

---

## Build Errors You Might See

| Error | Cause | Fix |
|---|---|---|
| `ERR002` | Two handlers for the same request type | Remove or rename one of them |
| `ERR003` | Handler class is `abstract` | Make it a concrete class |
| `ERR005` | Handler is `private` or `file`-scoped | Change access modifier to `public` or `internal` |

---

## Viewing the Generated Code

The generated file is called `Errand.g.cs`. In Visual Studio / Rider you can find it
under **Analyzers → Errand.SourceGenerator → Errand.g.cs** in the Solution Explorer.

In the terminal:

```
dotnet build && cat bin/Debug/net8.0/generated/Errand.SourceGenerator/Errand.SourceGenerator.ErrandSourceGenerator/Errand.g.cs
```

---

## Next Steps

- [Architecture](ARCHITECTURE.md) — understand how the source generator and dispatcher work
- [Migrating from MediatR](MIGRATION_FROM_MEDIATR.md) — step-by-step migration guide
