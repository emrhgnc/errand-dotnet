# Errand

> A high-performance, reflection-free Mediator implementation for .NET 8+.
> Handlers are wired at compile time — zero runtime overhead, full Native AOT support.

---

## Why Errand?

Traditional mediator libraries (MediatR, etc.) resolve handlers at runtime via reflection.
Errand takes a different approach: a **Roslyn source generator** scans your handlers during
compilation and emits a fully type-safe, switch-expression dispatcher directly into your
assembly. The result is:

| Concern | MediatR | Errand |
|---|---|---|
| Handler discovery | Reflection at startup | Compile-time scan |
| Dispatch cost | `DynamicInvoke` / boxing | Direct method call |
| "No handler" error | `InvalidOperationException` at runtime | **Build error (ERR002)** |
| Native AOT | ❌ (reflection-based) | ✅ |
| Startup overhead | Scans assemblies | None |
| `ValueTask` first | ❌ (`Task` default) | ✅ |

---

## Packages

| Package | Purpose |
|---|---|
| `Errand.Abstractions` | `IErrandRequest<T>`, `IErrandHandler<,>`, `IErrandSender` |
| `Errand.SourceGenerator` | Roslyn incremental generator — generates the dispatcher |
| `Errand.Core` | `AddErrand()` DI extension — wires everything together |

> **Install all three** for a typical application.

```
dotnet add package Errand.Abstractions
dotnet add package Errand.Core
dotnet add package Errand.SourceGenerator
```

---

## Quick Start

### 1. Define a request and its response

```csharp
// A request always declares its own response type via the generic parameter.
public sealed class CreateUserRequest : IErrandRequest<UserId>
{
    public string Name  { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
```

### 2. Implement a handler

```csharp
// One handler per request type — enforced at compile time.
public sealed class CreateUserHandler : IErrandHandler<CreateUserRequest, UserId>
{
    private readonly IUserRepository _repo;

    public CreateUserHandler(IUserRepository repo) => _repo = repo;

    public async ValueTask<UserId> HandleAsync(
        CreateUserRequest request,
        CancellationToken ct)
    {
        return await _repo.CreateAsync(request.Name, request.Email, ct);
    }
}
```

### 3. Register with DI and send

```csharp
// Program.cs
builder.Services.AddErrand<Program>(); // any type in your application assembly

// Anywhere in your code
public class UserController(IErrandSender sender) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserDto dto, CancellationToken ct)
    {
        var userId = await sender.SendAsync(
            new CreateUserRequest { Name = dto.Name, Email = dto.Email },
            ct);

        return Ok(userId);
    }
}
```

The source generator detects `CreateUserHandler` during the build and emits a dispatcher
equivalent to:

```csharp
// Auto-generated — do not modify
internal sealed class ErrandSender : IErrandSender
{
    public ValueTask<TResponse> SendAsync<TResponse>(
        IErrandRequest<TResponse> request, CancellationToken ct = default)
    {
        switch (request)
        {
            case CreateUserRequest r:
                return (ValueTask<TResponse>)(object)
                    new CreateUserHandler().HandleAsync(r, ct);
            // ... other handlers
        }
    }
}
```

---

## AddErrand() Overloads

```csharp
// Recommended — AOT-safe type anchor, no reflection at startup
services.AddErrand<Program>();

// Explicit assembly — useful in multi-assembly setups
services.AddErrand(typeof(MyHandler).Assembly);

// Convenience overload — uses the calling assembly
services.AddErrand();
```

---

## Compile-Time Diagnostics

| Code | Severity | Description |
|---|---|---|
| **ERR002** | Error | Multiple handlers found for the same request type |
| **ERR003** | Error | Handler class is `abstract` — cannot be instantiated |
| **ERR004** | Error | Request type does not implement `IErrandRequest<TResponse>` |
| **ERR005** | Error | Handler is `private` or `file`-local — not accessible from generated code |

All errors are reported as **build errors** — your application will not compile until the
issue is resolved.

---

## Requirements

- .NET 8.0 or later (runtime)
- C# 12 or later
- `netstandard2.0`-compatible projects can reference `Errand.Abstractions` directly

---

## Design Goals

1. **Zero Reflection at Runtime** — Handler lookup uses a generated switch expression, not
   `GetType()` / `DynamicInvoke`.
2. **Compile-Time Safety** — Missing or duplicate handlers are caught as build errors.
3. **`ValueTask`-First** — Minimises heap allocations on the hot path.
4. **Native AOT Compatible** — No reflection, no `dynamic`, no `Expression.Compile`.
5. **Minimal Dependencies** — `Errand.Core` depends only on
   `Microsoft.Extensions.DependencyInjection.Abstractions`.

---

## Documentation

- [Getting Started](docs/GETTING_STARTED.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Migrating from MediatR](docs/MIGRATION_FROM_MEDIATR.md)

---

## License

MIT
