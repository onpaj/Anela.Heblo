# Specification: Decouple Manufacture from `ICatalogRepository` via Consumer-Owned Contract

## Summary
The Manufacture module currently injects Catalog's internal domain interface `ICatalogRepository` into 11 application files (services + use-case handlers), violating the cross-module contract pattern mandated by `docs/architecture/development_guidelines.md`. This change introduces a Manufacture-owned `IManufactureCatalogSource` contract, implements it on the Catalog side via an adapter, and replaces every direct `ICatalogRepository` injection in Manufacture with the new abstraction. No business logic changes — pure dependency-direction inversion.

## Background
`development_guidelines.md` (section "Cross-Module Communication") explicitly forbids "Direct access to another module's entities" and requires cross-module communication to flow "exclusively through `contracts/`". The canonical reference implementation is `ILeafletKnowledgeSource` (Leaflet-owned contract) implemented by `KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase-owned adapter), keyed by `ModuleBoundariesTests` enforcement.

The Manufacture module already correctly applies the inverse direction: `ICatalogManufactureSource` lives in `Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs` and is implemented by `ManufactureCatalogSourceAdapter` in `Application/Features/Manufacture/Infrastructure/`, registered in `ManufactureModule.AddManufactureModule()` (see `ManufactureModule.cs:59`). The outbound direction (Manufacture → Catalog) has no equivalent inversion: 11 files inject `Anela.Heblo.Domain.Features.Catalog.ICatalogRepository` directly.

`ICatalogRepository` (in `Anela.Heblo.Domain.Features.Catalog`) is Catalog's internal domain interface. It exposes ~25 members including all `Refresh*Data` mutation methods, load-date properties, the merge scheduler, and analytics queries (`GetProductsWithSalesInPeriod`). Manufacture only uses three read operations: `GetByIdAsync(string id, ct)`, `GetByIdsAsync(IEnumerable<string> ids, ct)`, and `GetAllAsync(ct)` — all inherited from `IReadOnlyRepository<CatalogAggregate, string>`. The current coupling forces Catalog to keep this full surface stable for Manufacture's sake and would block adoption of the architectural test (no `Manufacture -> Catalog` rule exists in `ModuleBoundariesTests.cs` yet — adding it today would explode with violations).

This work is filed by the daily arch-review routine on 2026-06-03. It is a targeted refactor: behavior visible to callers (request/response payloads, control flow, performance characteristics) must remain unchanged.

## Functional Requirements

### FR-1: Introduce `IManufactureCatalogSource` contract in the Manufacture module
A new consumer-owned interface must be defined in `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs`, exposing only the catalog read operations Manufacture actually uses today.

**Detailed description:** The contract surface must be the minimum needed by current Manufacture call sites. Audited usage shows three methods are sufficient:

```csharp
namespace Anela.Heblo.Application.Features.Manufacture.Contracts;

/// <summary>
/// Manufacture-owned read abstraction over Catalog products. Implemented by the
/// Catalog module via an adapter. Returns CatalogAggregate as a deliberate pragmatic
/// leak — symmetric to ICatalogManufactureSource returning ManufactureHistoryRecord.
/// Allowlisted in ModuleBoundariesTests under "Manufacture -> Catalog".
/// </summary>
public interface IManufactureCatalogSource
{
    Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
}
```

**Acceptance criteria:**
- File `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/IManufactureCatalogSource.cs` exists.
- Interface lives in `Anela.Heblo.Application.Features.Manufacture.Contracts` namespace.
- Interface declares exactly the three methods above (signatures match `ICatalogRepository`/`IReadOnlyRepository` for `GetByIdAsync`, `GetByIdsAsync`, `GetAllAsync` so handler code requires no per-call adaptation).
- A documentation XML comment explains the pragmatic `CatalogAggregate` leak and points to the allowlist entry, mirroring the wording style of `ICatalogManufactureSource`.
- `GetAllAsync` returns `IReadOnlyList<CatalogAggregate>` to match `IReadOnlyRepository.GetAllAsync`'s existing return type and avoid changing call-site code (see FR-3 audit).

### FR-2: Provide `CatalogManufactureCatalogSourceAdapter` in the Catalog module
The Catalog module must provide the adapter implementing `IManufactureCatalogSource` by delegating to the existing `ICatalogRepository`.

**Detailed description:** Following the existing `ManufactureCatalogSourceAdapter` pattern in reverse, the adapter lives on the **provider** side (Catalog) and the DI registration is **owned by the provider**.

**Acceptance criteria:**
- File `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogManufactureCatalogSourceAdapter.cs` exists.
- Class is `internal sealed`, lives in `Anela.Heblo.Application.Features.Catalog.Infrastructure` namespace, and implements `IManufactureCatalogSource`.
- Constructor takes a single `ICatalogRepository` dependency.
- Each method delegates 1:1 to the underlying repository, passing through the `CancellationToken`. No transformation, no caching layer, no logging.
- Registered in `CatalogModule.AddCatalogModule()` (next to the existing cross-module adapter registrations around line 48–57) with `services.AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>()`. The registration comment must state: "Cross-module contract: Catalog implements Manufacture's IManufactureCatalogSource via adapter. DI registration is owned by the provider (Catalog), not the consumer (Manufacture)."

### FR-3: Replace every `ICatalogRepository` injection in Manufacture with `IManufactureCatalogSource`
All Manufacture services and handlers must depend on the new contract instead of Catalog's internal repository.

**Detailed description:** The files to migrate (verified by grep of `ICatalogRepository` under `backend/src/Anela.Heblo.Application/Features/Manufacture`):

| File | Used methods |
|---|---|
| `Services/BatchPlanningService.cs` | `GetByIdAsync`, `GetAllAsync` |
| `Services/ResidueDistributionCalculator.cs` | `GetByIdsAsync` |
| `UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` | `GetByIdAsync` |
| `UseCases/GetStockAnalysis/GetManufacturingStockAnalysisHandler.cs` | `GetAllAsync` |
| `UseCases/CreateManufactureOrder/CreateManufactureOrderHandler.cs` | `GetByIdAsync`, `GetByIdsAsync` |
| `UseCases/CalculateBatchByIngredient/CalculateBatchByIngredientHandler.cs` | `GetByIdAsync` |
| `UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` | `GetByIdAsync` |
| `UseCases/CalculatedBatchSize/CalculatedBatchSizeHandler.cs` | `GetByIdAsync` |
| `UseCases/SubmitManufactureStockTaking/SubmitManufactureStockTakingHandler.cs` | `GetByIdAsync` |
| `UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs` | `GetByIdAsync` |
| `UseCases/GetSemiproductRecipePdf/GetSemiproductRecipePdfHandler.cs` | `GetByIdAsync` |

**Note on brief discrepancy:** the brief lists `ManufactureAnalysisMapper`, `IManufactureSeverityCalculator`, `ManufactureSeverityCalculator`, `IManufactureAnalysisMapper`, `IConsumptionRateCalculator`, and `ConsumptionRateCalculator` as containing `ICatalogRepository` references. A repeat grep against the worktree returns no `ICatalogRepository` matches in those files; treat the table above as authoritative and confirm with a fresh `grep -l "ICatalogRepository"` at implementation time.

**Acceptance criteria:**
- In every listed file: the constructor parameter `ICatalogRepository catalogRepository` is renamed to `IManufactureCatalogSource catalogSource`; the private field is renamed correspondingly (e.g. `_catalogRepository` → `_catalogSource`); the `using Anela.Heblo.Domain.Features.Catalog;` directive is removed if it only existed for `ICatalogRepository`, and replaced/augmented with `using Anela.Heblo.Application.Features.Manufacture.Contracts;`.
- Any `using` of `Anela.Heblo.Domain.Features.Catalog` that remains because the file still uses `CatalogAggregate`, `ProductType`, etc. is acceptable per the deliberate-leak design (FR-1) and is allowlisted as a group under the boundary rule (FR-4).
- All method calls (`_catalogRepository.GetByIdAsync(...)` etc.) switch to `_catalogSource.GetByIdAsync(...)` with identical arguments — no `await`/cancellation/argument changes.
- `dotnet build` succeeds with no warnings introduced by the change.
- No reference to `Anela.Heblo.Domain.Features.Catalog.ICatalogRepository` remains anywhere in `backend/src/Anela.Heblo.Application/Features/Manufacture/`.

### FR-4: Add `Manufacture -> Catalog` rule to `ModuleBoundariesTests`
The architectural test must be extended with a new `ModuleBoundaryRule` covering Manufacture, with an allowlist that documents the deliberate `CatalogAggregate` (and related catalog-domain enums/value types) leak through the contract.

**Detailed description:** Today there is no `Manufacture -> Catalog` rule in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (search confirms). Adding one without allowlisting the residual `CatalogAggregate`, `ProductType`, `ManufactureType`, etc. references that remain inside Manufacture code (return-type usage of the contract methods) would explode with violations.

**Acceptance criteria:**
- A new `static readonly HashSet<string> ManufactureCatalogAllowlist` is added to the test class. Each entry has a one-line `//` comment explaining why the dependency is kept.
- Allowlist entries cover the symmetric pragmatic leak: every Manufacture consumer of `IManufactureCatalogSource` (services + handlers from FR-3) referencing `Anela.Heblo.Domain.Features.Catalog.CatalogAggregate`, plus uses of `ProductType` and any other catalog domain enum still referenced after migration. The allowlist must be exhaustive — generating it by running the test, reading violations, and pasting them in is the expected workflow.
- A new entry in `Rules()` (`ModuleBoundariesTests.cs:200+`) is added:
  ```csharp
  new ModuleBoundaryRule(
      Name: "Manufacture -> Catalog",
      InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Manufacture",
      ForbiddenNamespacePrefixes: new[]
      {
          "Anela.Heblo.Domain.Features.Catalog",
          "Anela.Heblo.Application.Features.Catalog",
          "Anela.Heblo.Persistence.Catalog",
      },
      Allowlist: ManufactureCatalogAllowlist),
  ```
- `dotnet test --filter ModuleBoundariesTests` passes locally with the new rule active.
- The allowlist must **not** contain `ICatalogRepository` — that is the violation this work fixes.

### FR-5: Update existing unit tests to use `IManufactureCatalogSource`
Any unit test currently mocking `ICatalogRepository` for a migrated Manufacture service or handler must mock `IManufactureCatalogSource` instead.

**Detailed description:** The Manufacture test project will have tests (xUnit + Moq, per project convention) that construct services/handlers directly with mocked dependencies. After FR-3, the mock target type changes.

**Acceptance criteria:**
- A grep of `backend/test/` for `Mock<ICatalogRepository>` (and `Setup(.*GetByIdAsync` / `GetAllAsync` / `GetByIdsAsync`) inside Manufacture-suffixed test classes returns the list to migrate.
- For each match: the mock generic argument switches to `IManufactureCatalogSource`, and the constructor argument switches to the new mock. `Setup` calls remain unchanged (signatures match).
- `dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Manufacture"` passes with no new failures.
- No Manufacture test references `ICatalogRepository` after the change.

## Non-Functional Requirements

### NFR-1: Performance
No performance change is expected or permitted.
- The adapter is a thin pass-through (constructor capture + delegated method calls); the JIT inlines it under typical conditions. The DI lifetime is `Scoped` to match the underlying `ICatalogRepository` registration (`AddTransient` in `CatalogModule:45`, but `Scoped` is correct for adapter symmetry with `ICatalogManufactureSource` registration at `ManufactureModule:59`).
- No new allocations beyond one adapter instance per scope. No additional caching, batching, or copying.
- Benchmark assertion is not required; absence of `await` / loop / mapping logic in the adapter is sufficient evidence.

### NFR-2: Security
No change to the security posture.
- Same data is exposed through the new contract as before; no privilege escalation, no new endpoints, no new auth surface.
- The contract is internal to `Application` and is not surfaced over any controller or MediatR handler externally.
- No secrets, no logging changes.

### NFR-3: Backwards compatibility
`ICatalogRepository` is **not** removed or modified. Catalog continues to depend on its own internal repository directly (correct — it owns it). Other Catalog-internal consumers and the Catalog refresh background tasks (`CatalogModule.RegisterBackgroundRefreshTasks`) remain unchanged.

## Data Model
Unchanged.
- No new tables, columns, or migrations.
- No change to `CatalogAggregate`, `ICatalogRepository`, `IReadOnlyRepository<,>`, or any persistence type.
- The new contract surfaces existing data without modification.

## API / Interface Design

**New types added (Application layer only — no public API surface change):**

| Type | Project | Layer |
|---|---|---|
| `IManufactureCatalogSource` (interface) | `Anela.Heblo.Application` | `Features/Manufacture/Contracts/` |
| `CatalogManufactureCatalogSourceAdapter` (internal sealed class) | `Anela.Heblo.Application` | `Features/Catalog/Infrastructure/` |

**DI registration:** added to `CatalogModule.AddCatalogModule()` as `services.AddScoped<IManufactureCatalogSource, CatalogManufactureCatalogSourceAdapter>()` next to existing cross-module adapter registrations (`PurchaseMaterialCatalogAdapter`, `LogisticsCatalogSourceAdapter`, etc., `CatalogModule.cs:48-57`).

**No changes to:**
- MVC controllers (none touched by this work)
- MediatR request/response DTOs
- OpenAPI surface / generated TypeScript client
- Frontend
- Database schema or migrations

**No changes to behavior:**
- Every call site preserves the exact same method signature, argument list, cancellation token handling, and return type.

## Dependencies
- `Anela.Heblo.Domain.Features.Catalog.ICatalogRepository` — unchanged, consumed by the new adapter only on the Catalog side.
- `Anela.Heblo.Domain.Features.Catalog.CatalogAggregate` — flows through the contract as a pragmatic leak, allowlisted in `ModuleBoundariesTests`.
- xUnit + Moq + FluentAssertions — test stack already present.
- No new NuGet packages.
- No new external services.

## Out of Scope
- Removing the `CatalogAggregate` leak through `IManufactureCatalogSource` (introducing a Manufacture-owned `ProductCatalogSnapshot` DTO and mapper). Tracked as a follow-up; out of scope to match the existing precedent set by `ICatalogManufactureSource` leaking `ManufactureHistoryRecord`.
- Migrating `IManufactureClient` consumers in Catalog handlers (`UpdateProductCompositionOrderHandler`, `GetProductCompositionHandler`, `GetProductUsageHandler`) — covered by the existing `CatalogManufactureAllowlist` follow-ups (`ModuleBoundariesTests.cs:118-159`).
- Changing the lifetime of `ICatalogRepository` itself (currently `Transient`; out of scope).
- Adding analytics methods like `GetProductsWithSalesInPeriod` to the new contract — not used by Manufacture today, would over-fit the contract.
- Removing or refactoring the `Refresh*Data` methods from `ICatalogRepository`.
- Any UI, controller, or DTO changes.
- Performance instrumentation, telemetry, or new logging.
- E2E test additions (architectural change with zero behavior delta — covered by unit + module-boundary tests).

## Open Questions
None.

## Status: COMPLETE