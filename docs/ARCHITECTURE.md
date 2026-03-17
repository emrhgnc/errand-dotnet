# Errand — Architecture

This document describes the internal design of Errand for contributors and advanced users.

---

## Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        User Application                         │
│                                                                 │
│   CreateUserRequest  ──►  IErrandSender.SendAsync()             │
│   CreateUserHandler        (resolved from DI container)         │
└────────────────────┬────────────────────────────────────────────┘
                     │
          ┌──────────▼──────────┐
          │  Errand.Core        │   AddErrand() extension method
          │  AddErrand()        │   looks up the generated class
          └──────────┬──────────┘   by name in the assembly
                     │
          ┌──────────▼──────────────────────────────────────┐
          │  Errand.Generated (inside the user's assembly)  │
          │                                                  │
          │  internal sealed class ErrandSender              │
          │      : IErrandSender                             │
          │  {                                               │
          │      switch (request) {                          │
          │          case CreateUserRequest r:               │
          │              return Dispatch_...(r, ct);         │
          │      }                                           │
          │  }                                               │
          └──────────────────────────────────────────────────┘
                  ▲  generated at build time by
          ┌───────┴────────────┐
          │  Errand.SourceGenerator  │
          │  ErrandSourceGenerator   │  IIncrementalGenerator
          └──────────────────────────┘
```

---

## Project Layout

```
src/
├── Errand.Abstractions/          Interfaces only — no logic
│   ├── IErrandRequest.cs
│   ├── IErrandHandler.cs
│   ├── IErrandSender.cs
│   └── Attributes/
│       └── ErrandHandlerAttribute.cs
│
├── Errand.SourceGenerator/       Roslyn incremental generator
│   ├── ErrandSourceGenerator.cs  Entry point — IIncrementalGenerator
│   ├── Analyzers/
│   │   ├── HandlerMetadata.cs    Equatable data model for one handler
│   │   ├── HandlerScanner.cs     Syntax predicate + semantic transform
│   │   ├── RequestTypeValidator.cs  Type-level validation helpers
│   │   └── ConflictDetector.cs   Groups duplicates, returns conflicts
│   ├── Generators/
│   │   ├── DiagnosticHelper.cs   Diagnostic descriptors + factories
│   │   ├── HandlerDispatcherGenerator.cs  Orchestrates code gen
│   │   └── Templates/
│   │       └── DispatcherTemplate.cs  StringBuilder-based renderer
│   └── Polyfills.cs              IsExternalInit for netstandard2.0
│
└── Errand.Core/
    └── DependencyInjection/
        └── ServiceCollectionExtensions.cs   AddErrand() overloads

tests/
└── Errand.Tests/
    ├── Fixtures/         Reusable requests and handlers
    ├── Integration/      E2E dispatch + DI tests
    ├── Unit/             ConflictDetector unit tests
    └── SourceGenerator/  In-memory CSharpGeneratorDriver tests
```

---

## Source Generator Pipeline

Errand uses the **Roslyn `IIncrementalGenerator`** API introduced in .NET 6 SDK.
The pipeline is designed to maximise incremental caching: expensive semantic analysis
only re-runs when relevant source actually changes.

```
SyntaxProvider.CreateSyntaxProvider()
       │
       │  predicate: IsPotentialHandler()      ← cheap SyntaxNode check
       │  (filters to ClassDeclarationSyntax nodes that have a base list
       │   containing the string "IErrandHandler")
       │
       ▼
  transform: ExtractHandlerMetadata()          ← semantic model access
       │  - GetDeclaredSymbol → INamedTypeSymbol
       │  - Walk AllInterfaces for IErrandHandler<TRequest, TResponse>
       │  - Validate accessibility + concrete type args
       │  - Return HandlerMetadata (null = skip silently)
       │
       │  transform: ExtractHandlerError()     ← parallel diagnostic pipeline
       │  - Same predicate, different transform
       │  - Returns HandlerErrorData for invalid handlers
       │    (ERR003 abstract, ERR004 request mismatch, ERR005 inaccessible)
       │
       ▼
  .Where(x => x is not null).Select(x => x!)
       │
       ▼
  .Collect()                                   ← ImmutableArray<HandlerMetadata>
       │
       ▼
  RegisterSourceOutput(Execute)
       │
       ├── ConflictDetector.Detect()            ← groups by RequestFullName
       │       → reports ERR002 for duplicates
       │
       └── DispatcherTemplate.Render()          ← StringBuilder code gen
               → ctx.AddSource("Errand.g.cs", ...)
```

The diagnostic pipeline runs independently and calls
`ctx.ReportDiagnostic()` per invalid handler.

---

## HandlerMetadata Equality Design

`HandlerMetadata` is a plain class (not a `record`) that implements
`IEquatable<HandlerMetadata>` manually. The `DiagnosticLocation` property is
**intentionally excluded** from equality.

```csharp
public bool Equals(HandlerMetadata? other)
{
    return HandlerFullName  == other.HandlerFullName
        && RequestFullName  == other.RequestFullName
        && ResponseFullName == other.ResponseFullName;
}
```

**Rationale:** `Location` objects embed line/column numbers. A trivial source edit
(adding a blank line, changing a comment) shifts locations without changing handler
semantics. Including `Location` in equality would invalidate the incremental cache
on every keystroke, defeating the entire point of incremental generators.

The same pattern applies to `HandlerErrorData`.

---

## Dispatch Code Generation

`DispatcherTemplate.Render()` produces a file structured as:

```csharp
// <auto-generated/>
#nullable enable

namespace Errand.Generated
{
    internal sealed class ErrandSender : global::Errand.Abstractions.IErrandSender
    {
        [MethodImpl(AggressiveInlining)]
        public ValueTask<TResponse> SendAsync<TResponse>(
            IErrandRequest<TResponse> request, CancellationToken ct = default)
        {
            switch (request)
            {
                case global::MyApp.CreateUserRequest r:
                    return (ValueTask<TResponse>)(object)
                        Dispatch_MyApp_CreateUserRequest(r, ct);
                // ... one case per handler
                default:
                    throw new InvalidOperationException(...);
            }
        }

        [MethodImpl(AggressiveInlining)]
        private static ValueTask<global::MyApp.UserId>
            Dispatch_MyApp_CreateUserRequest(
                global::MyApp.CreateUserRequest request, CancellationToken ct)
            => new global::MyApp.CreateUserHandler().HandleAsync(request, ct);
    }
}
```

### Key design decisions

| Decision | Reason |
|---|---|
| `switch` over `Dictionary<Type, Delegate>` | Dictionary requires boxing; `DynamicInvoke` uses reflection — not AOT-safe |
| Double cast `(ValueTask<TResponse>)(object)` | Erases the concrete `ValueTask<TId>` to `ValueTask<TResponse>` without reflection |
| `global::` prefix on all type names | Avoids namespace collisions regardless of the user's `using` declarations |
| Per-handler `Dispatch_*` method | Allows the JIT to inline small handlers; keeps `SendAsync` readable |
| `[AggressiveInlining]` | Hot-path hint — the JIT may eliminate the method call entirely for single-handler scenarios |
| `internal sealed` generated class | Not exposed as a public API; consumers always talk to `IErrandSender` |

---

## DI Registration Strategy

`Errand.Core` cannot have a compile-time dependency on `Errand.Generated.ErrandSender`
because the generated class lives inside the **user's** assembly, which does not exist
yet when `Errand.Core` is compiled.

The solution is a single reflection call at **startup** (not per-request):

```csharp
var senderType = assembly.GetType("Errand.Generated.ErrandSender")
    ?? throw new InvalidOperationException(...);

services.Add(ServiceDescriptor.Singleton(typeof(IErrandSender), senderType));
```

`ServiceDescriptor.Singleton(Type, Type)` is used instead of the
`AddSingleton<T, TImpl>()` extension to depend only on
`Microsoft.Extensions.DependencyInjection.Abstractions` (not the full DI package).

The no-argument `AddErrand()` overload is decorated with
`[MethodImpl(MethodImplOptions.NoInlining)]` to prevent the JIT from merging its
stack frame with the caller, ensuring `Assembly.GetCallingAssembly()` always returns
the user's assembly.

---

## Native AOT Compatibility

| Concern | Mitigation |
|---|---|
| Handler discovery | Source generator — no `GetTypes()` at runtime |
| Handler instantiation | `new MyHandler()` in generated code — static, linker-visible |
| Dispatch | Switch expression — no `DynamicInvoke`, no `Expression.Compile` |
| DI registration | One `assembly.GetType(name)` at startup only — acceptable for AOT |
| Type metadata | `FullyQualifiedFormat` names in generated code keep all references explicit |

> **Note:** The single `assembly.GetType(name)` call in `AddErrand()` requires the
> `Errand.Generated.ErrandSender` type to be present in the trimming root. Add a
> `[DynamicallyAccessedMembers]` annotation or a trimming root descriptor if your AOT
> publish pipeline reports it as trimmed.

---

## Diagnostic Codes

| Code | Stage | Trigger |
|---|---|---|
| ERR002 | Code gen | Two or more handlers for the same `RequestFullName` |
| ERR003 | Scanner | Handler class is `abstract` |
| ERR004 | Scanner | `TRequest` does not implement `IErrandRequest<TResponse>` |
| ERR005 | Scanner | Handler accessibility is `Private` or `Protected` |

All diagnostics are `DiagnosticSeverity.Error` and are emitted as standard Roslyn
diagnostics — they appear in IDE error lists and break `dotnet build`.

---

## netstandard2.0 Polyfills

`Errand.SourceGenerator` targets `netstandard2.0` because the Roslyn compiler host
that loads source generators may not run on .NET 8. Two polyfills are included:

- **`IsExternalInit`** (`Polyfills.cs`) — required by C# `record` types, which use
  `init`-only setters that depend on this compiler-service type.
- **`System.Threading.Tasks.Extensions` 4.5.4** in `Errand.Abstractions` — provides
  `ValueTask<T>` on `netstandard2.0` targets.
