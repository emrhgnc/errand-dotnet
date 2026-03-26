# Migrating from MediatR to Errand

This guide covers a step-by-step migration from MediatR 12.x to Errand 0.1.x.

---

## Key Differences at a Glance

| Aspect | MediatR | Errand |
|---|---|---|
| Request marker | `IRequest<T>` | `IErrandRequest<T>` |
| Handler interface | `IRequestHandler<TRequest, TResponse>` | `IErrandHandler<TRequest, TResponse>` |
| Handler method | `Task<T> Handle(req, ct)` | `ValueTask<T> HandleAsync(req, ct)` |
| Sender interface | `IMediator` | `IErrandSender` |
| Send method | `mediator.Send(req, ct)` | `sender.SendAsync(req, ct)` |
| Registration | `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` | `AddErrand<T>()` |
| Pipeline behaviors | `IPipelineBehavior<,>` | Not yet supported (roadmap) |
| Notifications | `INotification` / `INotificationHandler<>` | Not supported |
| Handler lifecycle | Registered in DI | `new THandler()` per call (no DI injection into the handler) ¹ |
| "No handler" error | Runtime exception | Build error |

> ¹ Errand currently instantiates handlers directly (`new THandler()`). If your handlers
> depend on services via constructor injection, see the [Handler Dependencies](#handler-dependencies)
> section below.

---

## Step 1 — Replace packages

Remove MediatR:

```
dotnet remove package MediatR
dotnet remove package MediatR.Extensions.Microsoft.DependencyInjection
```

Add Errand:

```
dotnet add package Errand.Abstractions
dotnet add package Errand.Core
dotnet add package Errand.SourceGenerator
```

---

## Step 2 — Update request types

**Before (MediatR):**

```csharp
using MediatR;

public sealed class CreateUserRequest : IRequest<UserId>
{
    public string Name  { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
```

**After (Errand):**

```csharp
using Errand.Abstractions;

public sealed class CreateUserRequest : IErrandRequest<UserId>
{
    public string Name  { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
```

**Changes:**
- `using MediatR` → `using Errand.Abstractions`
- `: IRequest<T>` → `: IErrandRequest<T>`

---

## Step 3 — Update handlers

**Before (MediatR):**

```csharp
using MediatR;

public sealed class CreateUserHandler : IRequestHandler<CreateUserRequest, UserId>
{
    private readonly IUserRepository _repo;

    public CreateUserHandler(IUserRepository repo) => _repo = repo;

    public async Task<UserId> Handle(CreateUserRequest request, CancellationToken ct)
    {
        return await _repo.CreateAsync(request.Name, request.Email, ct);
    }
}
```

**After (Errand):**

```csharp
using Errand.Abstractions;

public sealed class CreateUserHandler : IErrandHandler<CreateUserRequest, UserId>
{
    private readonly IUserRepository _repo;

    public CreateUserHandler(IUserRepository repo) => _repo = repo;

    public async ValueTask<UserId> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        return await _repo.CreateAsync(request.Name, request.Email, ct);
    }
}
```

**Changes:**
- `using MediatR` → `using Errand.Abstractions`
- `: IRequestHandler<TReq, TRes>` → `: IErrandHandler<TReq, TRes>`
- `Task<T> Handle(...)` → `ValueTask<T> HandleAsync(...)`

> **`ValueTask` vs `Task`:** `ValueTask<T>` avoids a heap allocation when the handler
> completes synchronously (e.g., cache hits). For handlers that always go async this
> makes no difference, but it is the Errand convention.

---

## Step 4 — Update DI registration

**Before (MediatR):**

```csharp
// Program.cs
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
```

**After (Errand):**

```csharp
// Program.cs
builder.Services.AddErrand<Program>();

// Register handlers yourself (so DI can inject their dependencies):
builder.Services.AddScoped<CreateUserHandler>();
// ... repeat for each handler
```

---

## Step 5 — Update send call sites

**Before (MediatR):**

```csharp
public class UserController(IMediator mediator) : ControllerBase
{
    public async Task<IActionResult> Create(CreateUserDto dto, CancellationToken ct)
    {
        var id = await mediator.Send(new CreateUserRequest { Name = dto.Name, ... }, ct);
        return Ok(id);
    }
}
```

**After (Errand):**

```csharp
public class UserController(IErrandSender sender) : ControllerBase
{
    public async Task<IActionResult> Create(CreateUserDto dto, CancellationToken ct)
    {
        var id = await sender.SendAsync(new CreateUserRequest { Name = dto.Name, ... }, ct);
        return Ok(id);
    }
}
```

**Changes:**
- `IMediator` → `IErrandSender`
- `.Send(...)` → `.SendAsync(...)`

---

## Handler Dependencies

Errand currently generates `new THandler()` directly. This means handlers with
constructor-injected dependencies **will not compile** because the generated call
has no way to resolve those dependencies.

**Workaround until full DI injection is supported in a future release:**

Use a service locator pattern via a static `IServiceProvider` accessor, or refactor
the handler to accept a factory/repository interface that is resolved through a static
accessor:

```csharp
// Option A — pass the service provider via a static accessor (not recommended for production)
public sealed class CreateUserHandler : IErrandHandler<CreateUserRequest, UserId>
{
    public ValueTask<UserId> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        var repo = ServiceLocator.GetRequired<IUserRepository>();
        return repo.CreateAsync(request.Name, request.Email, ct);
    }
}
```

```csharp
// Option B — make dependencies static/singleton and inject at startup
public sealed class CreateUserHandler : IErrandHandler<CreateUserRequest, UserId>
{
    // Handlers with no constructor args compile fine today.
    // Move heavy dependencies behind a singleton service.
    public async ValueTask<UserId> HandleAsync(CreateUserRequest request, CancellationToken ct)
        => await UserHandlerServices.Repository.CreateAsync(request.Name, request.Email, ct);
}
```

> **Roadmap:** Full DI-injected handler instantiation (resolving handlers from
> `IServiceProvider` rather than `new`) is planned for a future release.

---

## Notifications / Publish

MediatR's `INotification` / `INotificationHandler<T>` / `mediator.Publish()` pattern
has **no equivalent in Errand** at this time. If you use notifications, keep MediatR
installed alongside Errand for that specific functionality, or replace notifications
with your preferred event-bus abstraction.

---

## Pipeline Behaviors

MediatR's `IPipelineBehavior<TRequest, TResponse>` is **not yet supported** in Errand.
Cross-cutting concerns (logging, validation, etc.) currently require wrapper handlers or
application middleware:

```csharp
// Logging wrapper example — until pipeline behaviors are available
public sealed class LoggingCreateUserHandler : IErrandHandler<CreateUserRequest, UserId>
{
    private readonly ILogger<LoggingCreateUserHandler> _logger;

    public LoggingCreateUserHandler(ILogger<LoggingCreateUserHandler> logger)
        => _logger = logger;

    public async ValueTask<UserId> HandleAsync(CreateUserRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Handling {Request}", request);
        var inner = new CreateUserHandler(/* deps */);
        var result = await inner.HandleAsync(request, ct);
        _logger.LogInformation("Handled → {Result}", result);
        return result;
    }
}
```

---

## Frequently Asked Questions

**Q: Can I keep MediatR for some handlers and use Errand for others?**

Yes. Both `IMediator` and `IErrandSender` can be registered simultaneously. Migrate
handler by handler at your own pace.

**Q: MediatR supported `void` responses via `IRequest` (no type parameter). How do I
do that in Errand?**

Use `bool` (always return `true`) or define your own `Unit` struct:

```csharp
public readonly struct Unit { public static readonly Unit Value = default; }

public sealed class DeleteOrderRequest : IErrandRequest<Unit> { ... }
```

**Q: Does Errand support streaming / `IAsyncEnumerable`?**

Not yet. Streaming is on the roadmap.

**Q: The build suddenly shows ERR002 after migration — why?**

ERR002 means two handlers exist for the same request type. This can happen if you
accidentally left an old MediatR handler in the codebase after creating the new Errand
handler. Delete the duplicate.

**Q: I get an `InvalidOperationException: Could not find Errand.Generated.ErrandSender`
at startup.**

The source generator did not run. Check:
1. `Errand.SourceGenerator` is referenced in the project that calls `AddErrand()`.
2. The project has been built (not just restored).
3. No build errors prevented the generator from running.

---

## Quick Reference — Method / Type Rename Table

| MediatR | Errand |
|---|---|
| `using MediatR;` | `using Errand.Abstractions;` |
| `IRequest<T>` | `IErrandRequest<T>` |
| `IRequestHandler<TReq, TRes>` | `IErrandHandler<TReq, TRes>` |
| `Task<T> Handle(req, ct)` | `ValueTask<T> HandleAsync(req, ct)` |
| `IMediator` | `IErrandSender` |
| `mediator.Send(req, ct)` | `sender.SendAsync(req, ct)` |
| `AddMediatR(cfg => ...)` | `AddErrand<T>()` |
| `IPipelineBehavior<,>` | *(roadmap)* |
| `INotification` | *(not supported)* |
| `INotificationHandler<T>` | *(not supported)* |
| `mediator.Publish(...)` | *(not supported)* |
