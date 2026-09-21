# Architecture Review: Split IGiftPackageManufactureService into query and command interfaces

## Skip Design: true

## Architectural Fit Assessment

This is a pure Application-layer refactor confined to one existing vertical slice
(`Features/Logistics/UseCases/GiftPackageManufacture`). It touches no domain entities, no
persistence, no controllers, and no contracts consumed outside the module — spec's own
workspace-wide search (4 production consumers + DI registration + 2 test files) is consistent
with what a repo-wide grep for `IGiftPackageManufactureService` here confirms.

The change is not a new pattern for this codebase — it is bringing one interface in line with
an established one:

- `docs/architecture/development_guidelines.md` already states the rule this spec cites
  ("Consumer... exposing only the operations it actually consumes (no speculative methods)"),
  documented under the cross-module contract section but stated as a general principle.
- The **same module** already has a live example of exactly this read/write split:
  `Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs` (one method, reads)
  sits alongside `Features/Logistics/Contracts/ILogisticsStockOperationService.cs` (writes) as
  two independent interfaces. That pair crosses a module boundary (Catalog provides the query
  adapter, Logistics owns the write contract) so it doesn't share a concrete implementation the
  way this spec's split will — but it establishes both the `...QueryService` naming convention
  and the general comfort level with narrow, single-purpose interfaces in this exact module.
- `Features/UserManagement/Services/IDepartmentQueryService.cs`,
  `Features/Catalog/Contracts/IProductCatalogQueryService.cs` confirm `...QueryService` as the
  project-wide naming convention for read-only contracts (not `...ReadService` or
  `...QueryHandler` or similar).
- `docs/architecture/filesystem.md`'s "Complex Features" layout puts service interfaces in the
  feature's own `Services/` folder (`I{Entity}Service.cs` next to `{Entity}Service.cs`) — the
  existing `GiftPackageManufacture/Services/` folder already follows this, and the spec keeps
  both interfaces there. No move to a `Contracts/` folder is warranted: `Contracts/` in this
  codebase is reserved for DTOs and cross-module contracts (per
  `development_guidelines.md`'s "Consumer (A) defines the contract... in its own Contracts/
  folder" — that rule targets **cross-module** boundaries; `IGiftPackageQueryService` is
  consumed only inside this module).

Net assessment: low architectural risk, high conformance to existing conventions. The one
judgment call the spec leaves open — the DI wiring shape for "two interfaces, one shared
instance" (FR-4) — has no exact precedent in this codebase (the module's own DI registrations
use the factory pattern only for constructing a repository from `ApplicationDbContext`, not for
aliasing one instance to a second interface), so this review picks a concrete approach below
rather than leaving it to the implementer to improvise.

## Proposed Architecture

### Component Overview

```
Before:
  GetAvailableGiftPackagesHandler ──┐
  GetGiftPackageDetailHandler ──────┼──► IGiftPackageManufactureService ──► GiftPackageManufactureService
  CreateGiftPackageManufactureHandler ─┤        (4 methods: 2 read, 2 write)
  DisassembleGiftPackageHandler ────┘

After:
  GetAvailableGiftPackagesHandler ──┐
  GetGiftPackageDetailHandler ──────┴──► IGiftPackageQueryService ────────┐
                                                                            ├──► GiftPackageManufactureService
  CreateGiftPackageManufactureHandler ─┐                                  │      (implements both interfaces,
  DisassembleGiftPackageHandler ───────┴──► IGiftPackageManufactureService┘       single class, single file)

  GetManufactureLogHandler ──► IGiftPackageManufactureRepository   (unaffected, no dependency on either interface)
```

DI scope guarantee: both interfaces must resolve to the **same object instance** within one
scope — see Decision 2.

### Key Design Decisions

#### Decision 1: Interface split shape and naming

**Options considered:**
1. Split into `IGiftPackageQueryService` (reads) / keep `IGiftPackageManufactureService` name
   for writes (spec's proposal, brief's proposal).
2. Split into `IGiftPackageReadService` / `IGiftPackageWriteService`, renaming both away from
   the current name.
3. Extract the two writes into a new `IGiftPackageManufactureCommandService` and leave
   `IGiftPackageManufactureService` as the read-only name.

**Chosen approach:** Option 1, exactly as specified — new `IGiftPackageQueryService` for the
two reads, `IGiftPackageManufactureService` narrowed in place to the two writes.

**Rationale:** `...QueryService` is the established convention for read-only contracts in this
codebase (`IDepartmentQueryService`, `IProductCatalogQueryService`,
`ILogisticsStockOperationQueryService` — the last one in this very module). Keeping
`IGiftPackageManufactureService` as the write-only name avoids a rename that would touch two
handlers, two test files, and the DI registration for no benefit — "manufacture" already reads
naturally as a create/mutate operation, and `CreateGiftPackageManufactureHandler` /
`DisassembleGiftPackageHandler` (the two consumers left on this interface) both perform
manufacturing-adjacent stock mutations. Option 2 would be a wider, unjustified rename; option 3
adds an interface nobody asked for and still requires the same DI-sharing problem.

#### Decision 2: DI registration — sharing one instance across two interfaces

**Options considered:**
1. Two independent `AddScoped<TInterface, GiftPackageManufactureService>()` calls. Simple, but
   **wrong**: ASP.NET Core's DI container does not deduplicate two separate service
   registrations of the same implementation type against different interfaces — each resolution
   constructs its own instance. Within a request that both reads and writes through this
   service in sequence (there is no such direct case among the four handlers today, since each
   handler injects only one interface — but a hypothetical future handler, or any code that
   resolves both interfaces in the same scope, e.g. tests using a DI container instead of
   direct mocks) would get two different service instances with two different constructed
   dependency graphs. This violates the spec's own FR-4 acceptance criterion
   ("resolving `IGiftPackageManufactureService` and `IGiftPackageQueryService` from the same DI
   scope returns the same object reference") and is explicitly ruled out.
2. Register the concrete class once as `AddScoped<GiftPackageManufactureService>()`, then alias
   both interfaces to it via factory delegates:
   ```csharp
   services.AddScoped<GiftPackageManufactureService>();
   services.AddScoped<IGiftPackageManufactureService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
   services.AddScoped<IGiftPackageQueryService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
   ```
3. Register `IGiftPackageManufactureService` normally, then alias `IGiftPackageQueryService` to
   it via a factory that resolves and casts (the shape the spec sketches in FR-4):
   ```csharp
   services.AddScoped<IGiftPackageManufactureService, GiftPackageManufactureService>();
   services.AddScoped<IGiftPackageQueryService>(sp => (IGiftPackageQueryService)sp.GetRequiredService<IGiftPackageManufactureService>());
   ```

**Chosen approach:** Option 2 — register the **concrete type** as the scoped service, then
alias both interfaces to it via `GetRequiredService<GiftPackageManufactureService>()`.

**Rationale:** Option 3 (the spec's literal sketch) works, but it makes `IGiftPackageQueryService`
resolution semantically depend on `IGiftPackageManufactureService`'s registration and requires a
runtime cast — a reader has to know that "resolve the write interface, then cast it to the read
interface" is what's happening, and the cast would throw an opaque `InvalidCastException` if
anyone ever re-pointed `IGiftPackageManufactureService`'s registration at a different
implementation later. Option 2 registers the one thing that's actually shared — the concrete
class — and makes both interface registrations symmetric, equally-derived aliases of it. Neither
interface registration depends on the other's continued existence or shape. This costs one extra
`AddScoped<GiftPackageManufactureService>()` line and is a well-known, idiomatic
Microsoft.Extensions.DependencyInjection pattern (resolving a concrete type as the "root"
registration and exposing role interfaces as thin aliases). Note the concrete class itself does
not need to be `public` beyond its current visibility for this to work — it already is public.

#### Decision 3: Internal cross-calls between write and read methods

**Options considered:**
1. `CreateManufactureAsync` / `DisassembleGiftPackageAsync` keep calling
   `GetGiftPackageDetailAsync` as a plain `this.` call (spec's proposal).
2. Inject `IGiftPackageQueryService` into `GiftPackageManufactureService`'s own constructor and
   call through that interface reference instead of `this`.

**Chosen approach:** Option 1 — plain `this.GetGiftPackageDetailAsync(...)` calls, unchanged
from today's implicit `this.` calls (the current code already calls it this way; nothing here
actually changes).

**Rationale:** Option 2 would mean injecting the service into itself, which .NET DI does not
support for a self-referential constructor parameter without a second registration trick, and
buys nothing: the method is already on `this` because both interfaces are implemented by the one
class. This is a "pure interface-shape refactor" per the spec — the implementation's internals
must not change at all beyond the `class ... : X, Y` declaration line.

## Implementation Guidance

### Directory / Module Structure

No new folders. Exactly one new file:

```
backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/
├── IGiftPackageManufactureService.cs   # MODIFIED: reduced to 2 write methods
├── IGiftPackageQueryService.cs         # NEW: 2 read methods
└── GiftPackageManufactureService.cs    # MODIFIED: class declaration line only (`: IGiftPackageManufactureService, IGiftPackageQueryService`)
```

Modified outside `Services/`:
```
GiftPackageManufacture/GiftPackageManufactureModule.cs                                  # DI registration (Decision 2)
GiftPackageManufacture/UseCases/GetAvailableGiftPackages/GetAvailableGiftPackagesHandler.cs  # inject IGiftPackageQueryService
GiftPackageManufacture/UseCases/GetGiftPackageDetail/GetGiftPackageDetailHandler.cs          # inject IGiftPackageQueryService
```

Unmodified (verified by direct read): `CreateGiftPackageManufactureHandler.cs`,
`DisassembleGiftPackageHandler.cs`, `GetManufactureLogHandler.cs` (depends only on
`IGiftPackageManufactureRepository`, never on either service interface), all `Contracts/*Dto.cs`
files, `GiftPackageManufactureMappingProfile.cs`, all four `*Request.cs`/`*Response.cs` files.

### Interfaces and Contracts

`IGiftPackageQueryService` (new file, namespace
`Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services`):

```csharp
public interface IGiftPackageQueryService
{
    Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);

    Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
}
```

`IGiftPackageManufactureService` (modified — remove the two read methods, keep the
`[DisplayName]` attribute verbatim on `CreateManufactureAsync`):

```csharp
public interface IGiftPackageManufactureService
{
    [DisplayName("GiftPackageManufacture-{0}-{1}x")]
    Task<GiftPackageManufactureDto> CreateManufactureAsync(string giftPackageCode, int quantity, bool allowStockOverride, string userName, CancellationToken cancellationToken = default);

    Task<GiftPackageDisassemblyDto> DisassembleGiftPackageAsync(string giftPackageCode, int quantity, string userName, CancellationToken cancellationToken = default);
}
```

`GiftPackageManufactureService` class declaration (only line that changes at the class level):

```csharp
public class GiftPackageManufactureService : IGiftPackageManufactureService, IGiftPackageQueryService
```

Confirmed: the `[DisplayName]` mismatch between interface (`"...{1}x"`) and implementation
(`"...{1}"`, line 138 of `GiftPackageManufactureService.cs`) is pre-existing and has no runtime
consumer today — no `BackgroundJob.Enqueue`/`RecurringJob` call against this service exists
anywhere in the codebase (`HangfireBackgroundWorker.cs` reads `[DisplayName]` off whatever
`Job.Method` Hangfire resolves at enqueue time via an expression tree, but this service is never
enqueued through Hangfire — it's called synchronously from MediatR handlers). This confirms the
spec's "out of scope, do not fix" call is safe: the mismatch is inert either way.

### Data Flow

Unchanged for all four use cases — this refactor changes only which compile-time interface a
handler's constructor names, never which concrete instance is resolved, which methods run, or in
what order:

1. `GetAvailableGiftPackagesRequest` → `GetAvailableGiftPackagesHandler` → now
   `IGiftPackageQueryService.GetAvailableGiftPackagesAsync` → same catalog-source calls as today.
2. `GetGiftPackageDetailRequest` → `GetGiftPackageDetailHandler` → now
   `IGiftPackageQueryService.GetGiftPackageDetailAsync` → same catalog + manufacture-client calls.
3. `CreateGiftPackageManufactureRequest` → `CreateGiftPackageManufactureHandler` (dependency type
   unchanged) → `IGiftPackageManufactureService.CreateManufactureAsync` → internally still calls
   `this.GetGiftPackageDetailAsync(...)` (same class, same instance) → same repository +
   stock-operation calls.
4. `DisassembleGiftPackageRequest` → `DisassembleGiftPackageHandler` (dependency type unchanged)
   → `IGiftPackageManufactureService.DisassembleGiftPackageAsync` → same internal
   `GetGiftPackageDetailAsync` call → same repository + stock-operation calls.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| DI aliasing gets the two-interfaces-one-instance requirement wrong (e.g. two independent `AddScoped` calls), silently creating two service instances per scope | Medium | Follow Decision 2 exactly (register the concrete class, alias both interfaces to it); add or reuse an integration-style test that resolves both interfaces from one `IServiceScope` and asserts `ReferenceEquals` — this is testable and should not be left to manual inspection alone |
| A future handler is added that needs both read and write capability and mistakenly re-adds a fat interface or duplicates methods across both interfaces | Low | None needed for this change; flag in code review of future PRs touching this module — no action required now |
| Test files (`CreateGiftPackageManufactureHandlerTests.cs`, `DisassembleGiftPackageHandlerTests.cs`) fail to compile if the write interface's shape changes in an unexpected way | Low | Both files only mock `CreateManufactureAsync`/`DisassembleGiftPackageAsync`, both of which remain untouched on `IGiftPackageManufactureService` — verified by direct read; no changes needed to these files |
| `GiftPackageManufactureServiceTests.cs` (the service-level test class, distinct from the two handler-test files named in the spec) references `GiftPackageManufactureService` directly and calls all four methods on the concrete class | None | This file is unaffected: it instantiates the concrete class and calls methods directly, not through either interface, so a class implementing two interfaces instead of one changes nothing for it |

## Specification Amendments

1. **FR-4 DI registration snippet should be replaced with Decision 2's shape.** The spec's
   sketch (`AddScoped<IGiftPackageManufactureService, GiftPackageManufactureService>()` +
   `AddScoped<IGiftPackageQueryService>(provider => (IGiftPackageQueryService)provider.GetRequiredService<IGiftPackageManufactureService>())`)
   is left explicitly non-binding by the spec itself ("exact registration style... is left to the
   architect/implementer"). This review exercises that discretion: implement as
   ```csharp
   services.AddScoped<GiftPackageManufactureService>();
   services.AddScoped<IGiftPackageManufactureService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
   services.AddScoped<IGiftPackageQueryService>(sp => sp.GetRequiredService<GiftPackageManufactureService>());
   ```
   instead, per Decision 2's rationale (symmetric aliasing of a concrete-type root registration,
   no runtime cast).
2. **Add one DI-scope test.** The spec's FR-4 acceptance criterion ("resolving
   `IGiftPackageManufactureService` and `IGiftPackageQueryService` from the same DI scope returns
   the same object reference") is stated as a criterion but no test is scoped for it anywhere in
   FR-1 through FR-6. Recommend adding one small test (e.g. in
   `GiftPackageManufactureModule`'s test coverage, or a new focused test) that builds a service
   collection via `AddGiftPackageManufactureModule()` (with its real dependencies faked/mocked as
   needed, or reusing whatever DI test harness already exists in this test project) and asserts
   `ReferenceEquals(scope.GetRequiredService<IGiftPackageManufactureService>(), scope.GetRequiredService<IGiftPackageQueryService>())`.
   This is the one behavioral guarantee introduced by this refactor that unit tests on the
   handlers or the service class alone cannot catch.

## Prerequisites

None. No migrations, no config, no infrastructure changes. This can be implemented directly
against the current `main`/feature branch state.
