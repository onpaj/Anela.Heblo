# Specification: Decouple Logistics Module From Catalog-Owned Interfaces

## Summary
Eliminate the remaining direct dependencies from the Logistics module on Catalog-module-owned interfaces and domain types. The bulk of the originally-identified violations (in `GiftPackageManufactureService`, `ChangeTransportBoxStateHandler`, `GetTransportBoxByCodeHandler`) was resolved by PR #2201 via Logistics-owned contracts (`ILogisticsCatalogSource`, `ILogisticsStockOperationService`, `LogisticsStockOperationSource`); this spec finishes the job by addressing a residual leak in `TransportBoxCompletionService` that was not covered by that PR and adds light hardening to keep the boundary from re-eroding.

## Background

The architectural review (filed 2026-05-28) found three Logistics files that imported Catalog-owned interfaces directly, in violation of the development guidelines (`docs/architecture/development_guidelines.md` § Forbidden Practices: "shared repositories across modules" and "direct access to another module's entities"). The guideline pattern requires the **consumer** module to own the contract and the **provider** module to register an adapter — exemplified in the codebase by `ILeafletKnowledgeSource`.

Verification (2026-06-02) shows that commit `802a66f8` (PR #2201) introduced the Logistics-owned contracts and updated the three named files. However, a fourth file — `Anela.Heblo.Application.Features.Logistics.Services.TransportBoxCompletionService` — was not in the original brief and still leaks Catalog domain types. Specifically it:

- Imports `Anela.Heblo.Domain.Features.Catalog.Stock` (line 1).
- Injects `IStockUpOperationRepository` (Catalog-owned domain repository) directly via constructor (line 11, 16, 20).
- Calls `_stockUpOperationRepository.GetBySourceAsync(StockUpSourceType.TransportBox, ...)` (lines 80–83).
- Reads `StockUpOperationState` enum members (`Completed`, `Failed`, `Pending`, `Submitted`) on Catalog-owned `StockUpOperation` entities (lines 100–104, 122).

This is the same class of violation flagged in the original brief. Because it is fully discoverable and architecturally identical to the issues already fixed, closing it as part of this work keeps the boundary verifiable in one pass rather than spawning another arch-review item.

The Logistics module is intended to be deployable as an independent microservice (`docs/architecture/development_guidelines.md` § Required Practices). Any remaining direct reference to a Catalog-owned domain type blocks that goal and forces lockstep edits whenever Catalog refactors.

## Functional Requirements

### FR-1: Add Logistics-owned query contract for stock-up operations
Extend the Logistics contracts surface so that `TransportBoxCompletionService` can query the state of stock-up operations created against a transport box without referencing any Catalog-owned type.

A new Logistics-owned interface is preferred over extending `ILogisticsStockOperationService` because that interface today is purely command-side (`CreateOperationAsync`). The new contract is query-side and operates on a different responsibility (status inspection of previously created operations).

- New file: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ILogisticsStockOperationQueryService.cs`
- New interface: `ILogisticsStockOperationQueryService` with a single method:
  ```csharp
  Task<IReadOnlyList<LogisticsStockOperationStatus>> GetOperationsBySourceAsync(
      LogisticsStockOperationSource sourceType,
      int sourceId,
      CancellationToken cancellationToken = default);
  ```
- New file: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/Models/LogisticsStockOperationStatus.cs`
- New type: `LogisticsStockOperationStatus` — class (per project DTO rule), with only the fields Logistics actually reads:
  - `string DocumentNumber { get; init; }`
  - `LogisticsStockOperationState State { get; init; }`
- New file: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/LogisticsStockOperationState.cs`
- New enum: `LogisticsStockOperationState` mirroring the four members Logistics consumes today: `Pending`, `Submitted`, `Completed`, `Failed`. Values must be explicitly assigned (`= 0/1/2/3`) so future additions in Catalog do not silently shift values across the boundary.

**Acceptance criteria:**
- `ILogisticsStockOperationQueryService`, `LogisticsStockOperationStatus`, and `LogisticsStockOperationState` exist in the `Anela.Heblo.Application.Features.Logistics.Contracts` namespace (or `…Contracts.Models` for the DTO, matching the existing layout).
- The new types reference **no** `Anela.Heblo.Domain.Features.Catalog.*` or `Anela.Heblo.Application.Features.Catalog.*` namespace.
- `LogisticsStockOperationState` enum members have explicit integer values.

### FR-2: Implement provider adapter in the Catalog module
Per the consumer-owns-contract pattern, the Catalog module supplies an adapter that delegates to its existing `IStockUpOperationRepository`.

- New file: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/LogisticsStockOperationQueryAdapter.cs` (or alongside the existing adapter that already implements `ILogisticsStockOperationService` — co-locating both keeps the bridge surface in one place).
- The adapter:
  - Implements `ILogisticsStockOperationQueryService`.
  - Maps the inbound `LogisticsStockOperationSource` value to the Catalog-owned `StockUpSourceType` enum (one-to-one mapping today: `TransportBox` ↔ `TransportBox`, `GiftPackageManufacture` ↔ `GiftPackageManufacture`).
  - Calls `IStockUpOperationRepository.GetBySourceAsync(...)`.
  - Projects each returned `StockUpOperation` into `LogisticsStockOperationStatus` (only `DocumentNumber` and the mapped `State`).
  - Maps the Catalog-owned `StockUpOperationState` enum to the Logistics-owned `LogisticsStockOperationState` enum.
  - Throws `ArgumentOutOfRangeException` on unmapped enum values so an unhandled state surfaces loudly rather than as a silent default.

**Acceptance criteria:**
- The adapter lives in the Catalog module's `Infrastructure/` folder (not in Logistics).
- The adapter is `internal sealed` (matching `LogisticsCatalogTransportSourceAdapter` style).
- Enum-mapping uses an exhaustive `switch` expression; any unmapped value throws.

### FR-3: Register the binding in the Catalog module
The DI registration lives in the **provider** (Catalog), not the consumer (Logistics). This mirrors the existing pattern where Logistics registers `ICatalogTransportSource → LogisticsCatalogTransportSourceAdapter` in `LogisticsModule`.

- Update `Anela.Heblo.Application.Features.Catalog`'s module registration file (typically `CatalogModule.cs`; locate the existing `services.AddScoped<ILogisticsStockOperationService, …>(…)` line and add the new binding next to it).
- New binding: `services.AddScoped<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>();`
- Add a single-line comment matching the existing convention noting that the consumer (Logistics) owns the contract and the provider (Catalog) owns the registration.

**Acceptance criteria:**
- App boots without DI resolution errors.
- No DI registration for `ILogisticsStockOperationQueryService` exists in `LogisticsModule.cs` or any file under `Anela.Heblo.Application/Features/Logistics/`.

### FR-4: Rewire `TransportBoxCompletionService` to the new contract
Remove all direct Catalog imports and types from `TransportBoxCompletionService`.

- File: `backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`
- Replace `using Anela.Heblo.Domain.Features.Catalog.Stock;` with `using Anela.Heblo.Application.Features.Logistics.Contracts;` (and `…Contracts.Models` for the DTO).
- Replace the `IStockUpOperationRepository _stockUpOperationRepository` field with `ILogisticsStockOperationQueryService _stockOperationQueryService`.
- Update the constructor parameter list and field assignment accordingly.
- Update the `GetBySourceAsync` call site (lines 80–83) to call `_stockOperationQueryService.GetOperationsBySourceAsync(LogisticsStockOperationSource.TransportBox, box.Id, cancellationToken)`.
- Update state comparisons to use `LogisticsStockOperationState` enum members (`Completed`, `Failed`, `Pending`, `Submitted`). Field reads remain `op.State` and `op.DocumentNumber` since the DTO mirrors those names.
- Existing log messages, control flow, transactional behavior, and `box.Error(...)` / `box.ToPick(...)` calls remain unchanged.

**Acceptance criteria:**
- `grep -rn "Anela.Heblo.Domain.Features.Catalog\|Anela.Heblo.Application.Features.Catalog" backend/src/Anela.Heblo.Application/Features/Logistics` returns only the existing `ICatalogTransportSource` adapter (which is the reverse direction — Logistics implementing a Catalog-owned consumer contract — and is correct under the pattern).
- `grep -rn "IStockUpOperationRepository\|StockUpSourceType\|StockUpOperationState\b" backend/src/Anela.Heblo.Application/Features/Logistics` and the same grep against `backend/src/Anela.Heblo.Domain/Features/Logistics` both return zero matches.
- `dotnet build` succeeds.

### FR-5: Architecture test enforces the boundary
A reflection-based test already exists at `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (referenced by `development_guidelines.md` for the Leaflet/KnowledgeBase pair). Add an assertion that no type whose namespace begins with `Anela.Heblo.Application.Features.Logistics` or `Anela.Heblo.Domain.Features.Logistics` references any type in `Anela.Heblo.Application.Features.Catalog` or `Anela.Heblo.Domain.Features.Catalog`, **except** for the two known reverse-direction adapters (Logistics implementing Catalog-owned consumer contracts — currently `LogisticsCatalogTransportSourceAdapter`).

The exception list should be expressed as a small allow-list of `(consumingType, allowedReferencedType)` pairs, not a namespace-wide carve-out. This prevents new violations from sneaking in under the carve-out while leaving the legitimate inverted-dependency adapters in place.

**Acceptance criteria:**
- New test `Logistics_DoesNotReferenceCatalog_ExceptAllowList` passes on this branch.
- Deliberately re-adding `using Anela.Heblo.Domain.Features.Catalog.Stock;` to `TransportBoxCompletionService` causes the test to fail with a message that identifies the offending type and namespace.
- The allow-list is documented at the top of the test method with a comment explaining when an entry is acceptable (provider-side adapters for consumer-owned contracts).

### FR-6: Verify the original three files remain clean
PR #2201 fixed the three files named in the brief. As a regression guard, confirm via the FR-5 architecture test that none of them re-acquire a direct Catalog reference. No code change is required in:

- `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`
- `Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- `Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs`

**Acceptance criteria:**
- Manual code read confirms these three files import only from `Anela.Heblo.Application.Features.Logistics.Contracts`, `…Contracts.Models`, and other Logistics-owned namespaces for cross-module dependencies.
- The FR-5 architecture test covers all three.

## Non-Functional Requirements

### NFR-1: Performance
No behavior change. The adapter introduces a single in-process projection per call (Catalog domain entity → Logistics DTO) over a list that is bounded by the number of stock-up operations associated with a single transport box (typically ≤ 20 in production). No additional database round trips, no new I/O, no new locks.

### NFR-2: Security
No new attack surface. Adapter and DTO are internal application types not exposed via any HTTP endpoint. No new secrets, no new external calls, no auth changes.

### NFR-3: Maintainability
The Logistics module must compile and unit-test in isolation without referencing Catalog assemblies *at the project level* once Phase 2 of the persistence guidelines (each module in its own assembly) lands. This spec moves a step closer by removing source-level Catalog references from Logistics code. Project-level reference removal is out of scope (depends on Phase 2 of the broader architecture roadmap).

### NFR-4: Testability
Existing unit tests for `TransportBoxCompletionService` must be updated to mock `ILogisticsStockOperationQueryService` instead of `IStockUpOperationRepository`. Test setup should construct `LogisticsStockOperationStatus` instances directly (no need to instantiate Catalog-owned `StockUpOperation` entities). Coverage targets stay at 80%+ per the project testing rule.

## Data Model

No database schema changes. No new tables, no new columns, no migrations.

New in-process types only:

| Type | Kind | Namespace | Purpose |
|---|---|---|---|
| `ILogisticsStockOperationQueryService` | interface | `Anela.Heblo.Application.Features.Logistics.Contracts` | Logistics-owned query contract for stock-up operation status |
| `LogisticsStockOperationStatus` | class | `Anela.Heblo.Application.Features.Logistics.Contracts.Models` | Minimal projection over `StockUpOperation` |
| `LogisticsStockOperationState` | enum | `Anela.Heblo.Application.Features.Logistics.Contracts` | Mirror of `StockUpOperationState` with explicit integer values |
| `LogisticsStockOperationQueryAdapter` | internal sealed class | `Anela.Heblo.Application.Features.Catalog.Infrastructure` | Provider-side adapter implementing the consumer-owned contract |

Existing types (`LogisticsStockOperationSource`, `ILogisticsStockOperationService`, `ILogisticsCatalogSource`) are unchanged.

## API / Interface Design

No public HTTP API change. This is an internal refactor.

Cross-module call graph after the change:

```
Logistics.TransportBoxCompletionService
        │  depends on
        ▼
Logistics.Contracts.ILogisticsStockOperationQueryService     [owned by Logistics]
        ▲  implemented by
        │
Catalog.Infrastructure.LogisticsStockOperationQueryAdapter   [owned by Catalog]
        │  delegates to
        ▼
Catalog.Domain.IStockUpOperationRepository                   [internal to Catalog]
```

DI registration is a single line in `CatalogModule`. No changes to `Program.cs` composition.

## Dependencies

- **Code**: depends on PR #2201 already being merged to `main` (verified — commit `802a66f8`). This spec builds on the Logistics contracts namespace and the `LogisticsStockOperationSource` enum introduced there.
- **Build / tooling**: standard `dotnet build`, `dotnet format`, `dotnet test`. No new NuGet packages.
- **External services**: none.

## Out of Scope

- **Splitting Logistics into its own `.csproj`.** Project-level isolation is Phase 2 of the persistence guidelines and depends on multi-DbContext work. This spec only removes source-level Catalog references from Logistics code.
- **Removing the `Anela.Heblo.Persistence` shared DbContext dependency.** `TransportBoxRepository` and `StockUpOperationRepository` will continue to share `ApplicationDbContext`. This is a known Phase 1 / Phase 2 trade-off in the guidelines.
- **Migrating other modules.** Purchase, Manufacture, Analytics, etc. may have similar leaks; they are not in this spec's scope. Use a separate arch-review pass.
- **Renaming `ITransportBoxCompletionService` or restructuring the completion workflow.** Surgical change only — same logic, different dependency.
- **Adding new query methods to `ILogisticsStockOperationQueryService`** beyond `GetOperationsBySourceAsync`. YAGNI — add others only when a Logistics caller actually needs them.
- **Migrating `ExpeditionList` / `ExpeditionListArchive` print-queue contracts** (covered by a separate spec — see `answers.md` Q1 in this worktree, which is unrelated to logistics).

## Open Questions

None. All design choices in this spec are derived from the existing codebase pattern (`ILeafletKnowledgeSource`, `ICatalogTransportSource`, `ILogisticsStockOperationService`) and the development guidelines.

## Status: COMPLETE
