# Technical Debt

Known gaps, deferred decisions, and planned improvements.
Each item includes the affected component, a brief description, and suggested remediation.

---

## Runtime / Dispatch

### TD-001 — Handlers are instantiated with `new THandler()` (no DI injection)

**Severity:** High
**Component:** `Errand.SourceGenerator` → `DispatcherTemplate`

The generated dispatcher calls `new MyHandler().HandleAsync(request, ct)` directly.
Handler dependencies cannot be constructor-injected, which prevents real-world usage
patterns where handlers depend on repositories, services, loggers, etc.

**Impact:** Any handler that takes constructor parameters will cause a compile error
in the generated file. Documented as a known limitation in MIGRATION_FROM_MEDIATR.md.

**Remediation options:**
1. Resolve handlers from `IServiceProvider` at dispatch time:
   ```csharp
   // Generated code with DI:
   private readonly IServiceProvider _sp;
   public ErrandSender(IServiceProvider sp) => _sp = sp;

   case CreateUserRequest r:
       return (ValueTask<TResponse>)(object)
           _sp.GetRequiredService<CreateUserHandler>().HandleAsync(r, ct);
   ```
2. Inject a `Func<Type, object>` factory delegate to keep the generated code
   decoupled from the DI container type.

---

### TD-002 — No pipeline behavior support (`IPipelineBehavior<,>` equivalent)

**Severity:** High
**Component:** `Errand.SourceGenerator` → `DispatcherTemplate`

There is no mechanism to wrap handler calls with cross-cutting concerns (logging,
validation, transactions, retry, etc.) without modifying each handler.

**Remediation:** Define an `IErrandBehavior<TRequest, TResponse>` interface. The source
generator should discover registered behaviors and emit a composable pipeline chain
(similar to middleware) around each `Dispatch_*` call at code-generation time to keep
the AOT guarantee.

---

### TD-003 — No Notifications / Publish-Subscribe pattern

**Severity:** Medium
**Component:** Entire library

`INotification` / `INotificationHandler<T>` / `Publish()` from MediatR have no Errand
equivalent. Projects that use event-driven patterns must resort to a separate event bus.

**Remediation:** Add `IErrandEvent` (marker), `IErrandEventHandler<T>`, and
`PublishAsync(IErrandEvent)` to `IErrandSender`. The generator emits a multi-cast
dispatcher that calls all registered handlers for a given event type.

---

### TD-004 — No streaming support (`IAsyncEnumerable<T>`)

**Severity:** Low
**Component:** `Errand.Abstractions`, `Errand.SourceGenerator`

Long-running query responses that produce results incrementally cannot be streamed.

**Remediation:** Add `IErrandStreamRequest<TItem>` and a `StreamAsync<TItem>()` method
to `IErrandSender`. The generator emits a separate streaming dispatch table.

---

## Source Generator

### TD-005 — ERR001 diagnostic is defined but never emitted

**Severity:** Medium
**Component:** `Errand.SourceGenerator` → `DiagnosticHelper`

`DiagnosticHelper.MissingHandler` (ERR001) exists as a descriptor but is never reported.
Emitting it requires scanning all `IErrandRequest<T>` usages and cross-referencing
them against discovered handlers — a second incremental provider was deferred.

**Remediation:** Add a `RequestUsageScanner` that collects every invocation of
`IErrandSender.SendAsync<TResponse>(IErrandRequest<TResponse>)` and matches the
concrete `IErrandRequest<T>` argument against the known handler set. Report ERR001
for any request type that has no handler.

---

### TD-006 — `[ErrandHandlerAttribute]` is defined but not used by the generator

**Severity:** Low
**Component:** `Errand.Abstractions` → `ErrandHandlerAttribute.cs`,
`Errand.SourceGenerator` → `HandlerScanner`

The attribute was declared in Phase 2 as an explicit opt-in marker. The scanner
currently ignores it and instead discovers handlers purely by their `IErrandHandler<,>`
interface implementation. The attribute has no effect.

**Remediation options:**
1. Remove the attribute (breaking change if published) and rely solely on the interface.
2. Make the scanner prefer `[ErrandHandlerAttribute]` over structural discovery,
   giving users explicit control.
3. Repurpose the attribute to carry handler metadata (e.g., a display name or
   ordering hint for future pipeline behaviors).

---

### TD-007 — Open-generic handlers are silently skipped with no diagnostic

**Severity:** Low
**Component:** `Errand.SourceGenerator` → `HandlerScanner.ExtractHandlerMetadata`

When a class implements `IErrandHandler<TRequest, TResponse>` with unbound type
parameters (e.g., `class BaseHandler<T> : IErrandHandler<T, string>`), the scanner
returns `null` with no feedback. The developer has no indication that the type was
ignored.

**Remediation:** Add an informational diagnostic (e.g., INF001) so the developer knows
the open-generic handler was skipped intentionally.

---

### TD-008 — ERR004 check fires only during error-recovery compilation

**Severity:** Low
**Component:** `Errand.SourceGenerator` → `HandlerScanner.ExtractHandlerError`

`ERR004` ("request type does not implement IErrandRequest") is a defensive check.
Because `IErrandHandler<TRequest, TResponse>` declares `where TRequest : IErrandRequest<TResponse>`,
the C# compiler enforces this constraint before the generator runs. ERR004 can only
be reached when Roslyn's error-recovery mode still surfaces the interface in
`AllInterfaces` despite a constraint-violation error already being present.
The check is therefore redundant in practice.

**Remediation:** Either document it as a pure defensive measure (no action needed),
or remove it and rely on the compiler constraint, reducing scanner complexity.

---

### TD-009 — `HandlerScanner` runs two `CreateSyntaxProvider` passes over the same nodes

**Severity:** Low (performance)
**Component:** `Errand.SourceGenerator` → `ErrandSourceGenerator.Initialize`

`CreateHandlerProvider` and `CreateDiagnosticProvider` both call
`context.SyntaxProvider.CreateSyntaxProvider` with an identical predicate. Although
Roslyn may deduplicate the syntax traversal internally, the semantic transform runs
twice per handler candidate — once to extract valid metadata and once to extract
error data.

**Remediation:** Return a discriminated union (`HandlerScanResult`) from a single
provider, then split into valid/error streams with `.Where()` inside the generator's
`Initialize` method:

```csharp
var all      = HandlerScanner.CreateUnifiedProvider(context);  // single pass
var valid    = all.Where(r => r.IsValid).Select(r => r.Metadata!);
var errored  = all.Where(r => !r.IsValid).Select(r => r.Error!);
```

---

## Dependency Injection

### TD-010 — `AddErrand()` searches only one assembly

**Severity:** Medium
**Component:** `Errand.Core` → `ServiceCollectionExtensions`

The three `AddErrand()` overloads all accept or derive a single `Assembly`. Projects
that spread handlers across multiple assemblies must call `AddErrand()` multiple times
and ensure each assembly has its own generated `Errand.Generated.ErrandSender`.

**Remediation:** Add an overload `AddErrand(params Assembly[] assemblies)` that
merges all discovered `ErrandSender` types into a composite sender.
Alternatively, make the source generator output a partial class so multiple assembly
results can be combined at link time.

---

### TD-011 — Single `assembly.GetType()` call is not fully AOT-trim-safe

**Severity:** Low
**Component:** `Errand.Core` → `ServiceCollectionExtensions`

`assembly.GetType("Errand.Generated.ErrandSender")` is a runtime reflection call.
Native AOT's IL trimmer cannot statically verify that the type will be present and
may trim it. Startup will throw if the type is trimmed away.

**Remediation:** Add a trimming-root descriptor or annotate the `AddErrand` method
with `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]`
on the assembly parameter, and document the required `rd.xml` entry for Native AOT
publish pipelines.

---

## Testing

### TD-012 — `xunit` and `FluentAssertions` pinned to old versions

**Severity:** Low
**Component:** `tests/Errand.Tests/Errand.Tests.csproj`

| Package | Pinned | Latest stable |
|---|---|---|
| `xunit` | 2.4.1 | 2.9.x |
| `FluentAssertions` | 5.10.3 | 6.x / 7.x |
| `xunit.runner.visualstudio` | 2.4.1 | 2.8.x |

Older versions were chosen for offline-cache availability during initial development.
`FluentAssertions` 6.x introduced breaking API changes.

**Remediation:** Update all test packages when the local NuGet cache is replenished
or the project is moved to an online environment.

---

### TD-013 — No test for the `AddErrand()` no-argument overload

**Severity:** Low
**Component:** `tests/Errand.Tests/Integration/DependencyInjectionTests.cs`

`AddErrand()` (the overload using `Assembly.GetCallingAssembly()`) is not covered by
`DependencyInjectionTests`. The method's correctness depends on the JIT not inlining
the call across assembly boundaries, which is guaranteed by `[MethodImpl(NoInlining)]`
but is difficult to assert in a unit test.

**Remediation:** Add an integration test that calls `services.AddErrand()` directly
(not via a helper method) and verifies `IErrandSender` is resolved. The xUnit test
runner preserves the call stack such that `GetCallingAssembly()` returns `Errand.Tests`.

---

### TD-014 — Source generator tests do not cover the incremental caching behaviour

**Severity:** Low
**Component:** `tests/Errand.Tests/SourceGenerator/SourceGeneratorTests.cs`

Tests in `SourceGeneratorTests` compile each snippet from scratch.
There are no tests that run the generator twice with a trivially different source
(e.g., adding a blank line) and assert that the code-generation step was **not**
re-triggered (i.e., verify the incremental cache hit).

**Remediation:** Use `GeneratorDriver.RunGenerators` twice on the same driver instance
with a modified syntax tree, then check `GeneratorRunResult.TrackedSteps` to assert
the `Collect → Execute` step was served from cache on the second run.

---

## Packaging (Phase 10 deferred)

### TD-015 — NuGet package metadata is incomplete

**Severity:** Medium
**Component:** `src/*/\*.csproj`

The `.csproj` files contain minimal NuGet metadata. The following fields are missing
or need finalising before a public release:

| Field | Status |
|---|---|
| `<Version>` | Not set (defaults to `1.0.0`) |
| `<Authors>` | Not set |
| `<PackageProjectUrl>` | Not set |
| `<RepositoryUrl>` | Not set |
| `<PackageLicenseExpression>` | Not set |
| `<PackageReadmeFile>` | Not set (README.md exists but not wired) |
| `<PackageIcon>` | Not set |
| `<PackageReleaseNotes>` | Not set |

---

### TD-016 — `Errand.SourceGenerator` is not packaged as a Roslyn analyzer

**Severity:** High (blocks NuGet release)
**Component:** `src/Errand.SourceGenerator/Errand.SourceGenerator.csproj`

For a source generator to work when installed via NuGet (as opposed to a project
reference), its DLL must be placed under `analyzers/dotnet/cs/` inside the `.nupkg`.
This requires either:
- A custom `.nuspec` with the correct `<file>` entries, or
- MSBuild targets that copy the output to the right path and set
  `<BuildOutputTargetFolder>` / `<DevelopmentDependency>true</DevelopmentDependency>`
  in the `.csproj`.

Without this, consumers who install `Errand.SourceGenerator` from NuGet will have the
package present on disk but the generator will never run.

---

### TD-017 — No `CHANGELOG.md`

**Severity:** Low
**Component:** Repository root

There is no changelog. Consumers cannot track what changed between versions.

**Remediation:** Adopt [Keep a Changelog](https://keepachangelog.com) format. Populate
it before the first public release.
