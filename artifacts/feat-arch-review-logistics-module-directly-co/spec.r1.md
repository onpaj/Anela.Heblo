# Specification: Decouple Logistics Module from Catalog-Owned Interfaces

## Summary
Three Logistics handlers/services currently depend directly on Catalog-owned interfaces (`ICatalogRepository`, `IStockUpProcessingService`) and a Catalog-owned domain type (`StockUpSourceType`), violating the documented consumer-owns-the-contract rule. This work introduces Logistics-owned contracts in `Application/Features/Logistics/Contracts/`, implements thin adapters in the Catalog module that delegate to existing Catalog services, and rewires the three Logistics files to inject the new contracts.

## Background
Per `docs/architecture/development_guidelines.md` § Forbidden Practices, modules must not share repositories across modules or directly access another module's entities. Cross-module communication must flow through contracts **owned by the consumer module** and implemented by the producer module. The daily arch-review routine (2026-05-28) identified three Logistics files that breach this rule:

| Logistics file | Catalog-owned imports |
|---|---|
| `Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` | `ICatalogRepository`, `IStockUpProcessingService`, `StockUpSourceType` |
| `Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` | `IStockUpProcessingService`, `StockUpSourceType` |
| `Application/Features/Logistics/UseCases/GetTransportBoxByCode/GetTransportBoxByCodeHandler.cs` | `ICatalogRepository` |

Concrete consequences:
- Catalog internals cannot be renamed, split, or refactored without cascading edits into Logistics handlers.
- `ICatalogRepository` exposes the full Catalog aggregate surface area to Logistics, well beyond what Logistics reads.
- Logistics tests cannot mock these dependencies without dragging in Catalog test infrastructure.
- The stated long-term goal of each module being deployable as an independent microservice is structurally blocked.

## Functional Requirements

### FR-1: Define Logistics-owned catalog-source contract
Introduce a new interface `ILogisticsCatalogSource` in `Application/Features/Logistics/Contracts/` that exposes **only** the Catalog read operations the three Logistics files actually consume. The exact method set must be derived from the call sites (no speculative surface area). At minimum the interface must support:
- Lookup by product code/id (used by `GetTransportBoxByCodeHandler` and `GiftPackageManufactureService`)
- Whatever list/query operation `GiftPackageManufactureService` uses (e.g. fetching gift-package components / BOM-relevant data)

The return type must be a Logistics-owned read model (DTO/record under `Application/Features/Logistics/Contracts/Models/`) — **not** the Catalog aggregate or any Catalog domain entity. Field set is restricted to what the Logistics call sites actually read.

**Acceptance criteria:**
- `ILogisticsCatalogSource` is declared under `Application/Features/Logistics/Contracts/` (Logistics namespace).
- It exposes only methods invoked by the three call sites; no broader CRUD surface.
- Returned types are Logistics-namespaced DTOs; no Catalog domain types leak into the interface signature.
- The interface compiles and is referenced by Logistics code without any `using` statement from `Domain.Features.Catalog` or `Application.Features.Catalog`.

### FR-2: Define Logistics-owned stock-operation contract
Introduce `ILogisticsStockOperationService` in `Application/Features/Logistics/Contracts/` exposing **only** `CreateOperationAsync` (or the equivalent currently invoked by Logistics on `IStockUpProcessingService`). Signature must take Logistics-owned parameters — including a Logistics-owned replacement for `StockUpSourceType` (see FR-3).

**Acceptance criteria:**
- Interface exposes only the operations Logistics actually calls; no other `IStockUpProcessingService` members.
- Signature uses Logistics-owned types only.
- `GiftPackageManufactureService` and `ChangeTransportBoxStateHandler` reference this contract instead of `IStockUpProcessingService`.

### FR-3: Replace cross-module use of `StockUpSourceType`
`StockUpSourceType` is a Catalog-owned domain enum currently passed by Logistics code. Re-declare an equivalent Logistics-owned enum `LogisticsStockOperationSource` (or named per Logistics ubiquitous language) in `Application/Features/Logistics/Contracts/`. The Catalog-side adapter (FR-4) is responsible for mapping the Logistics enum value to the Catalog enum value when invoking `IStockUpProcessingService`.

**Acceptance criteria:**
- A new Logistics-namespaced enum exists with members covering exactly the values Logistics passes today (`GiftPackageManufacture`, `TransportBoxStateChange`, or whichever subset is in use — verify against current call sites).
- No Logistics file imports `Domain.Features.Catalog.Stock.StockUpSourceType` after the change.
- The adapter maps every Logistics enum value to a defined Catalog value; mapping is exhaustive and fails fast on unmapped values.

### FR-4: Implement adapters in the Catalog module
In `Infrastructure/Features/Catalog/` (or the existing equivalent folder for Catalog infrastructure), implement:
- `CatalogToLogisticsCatalogSourceAdapter : ILogisticsCatalogSource` — delegates to `ICatalogRepository`, projects Catalog aggregates to the Logistics DTOs.
- `CatalogToLogisticsStockOperationAdapter : ILogisticsStockOperationService` — delegates to `IStockUpProcessingService`, maps the Logistics enum to `StockUpSourceType`.

Adapters live in the Catalog module (producer side) — Logistics has no knowledge of their existence.

**Acceptance criteria:**
- Both adapter classes exist in the Catalog module namespace.
- Each adapter performs only translation/delegation; no business logic.
- Projection from Catalog aggregate → Logistics DTO is explicit (no AutoMapper magic that hides the field set).
- Catalog internals can be refactored without touching Logistics code (verified by FR-7).

### FR-5: Register adapter bindings
Register the two adapters as the implementations of the Logistics-owned contracts in **Catalog's module-registration file** (the existing `AddCatalogModule` / equivalent DI registration entry point used by the Catalog module).

**Acceptance criteria:**
- DI bindings live in Catalog's registration file, not Logistics'.
- Logistics' registration file contains no reference to Catalog types.
- Application starts and resolves both contracts at runtime (verified by an integration/smoke test or app boot).

### FR-6: Update the three Logistics files
Rewire constructor injection in each file to take the new Logistics-owned contracts:
- `GiftPackageManufactureService.cs` — replace `ICatalogRepository` and `IStockUpProcessingService` parameters with `ILogisticsCatalogSource` and `ILogisticsStockOperationService`; replace `StockUpSourceType` usages with the Logistics enum.
- `ChangeTransportBoxStateHandler.cs` — replace `IStockUpProcessingService` with `ILogisticsStockOperationService`; replace `StockUpSourceType` usages.
- `GetTransportBoxByCodeHandler.cs` — replace `ICatalogRepository` with `ILogisticsCatalogSource`.

Behavior must be preserved exactly. No business-logic edits in these files beyond what the type swap requires.

**Acceptance criteria:**
- None of the three files contain `using Domain.Features.Catalog…` or `using Application.Features.Catalog…` after the change.
- All existing unit/integration tests covering these files pass without modification of their behavioral assertions (mocks may need to be retargeted to the new interfaces).
- `dotnet build` and `dotnet format` succeed.

### FR-7: Verify decoupling by static check
A grep across `Application/Features/Logistics/**/*.cs` must return zero matches for the strings `Domain.Features.Catalog` and `Application.Features.Catalog` (other than test files that may legitimately reference Catalog adapters under test).

**Acceptance criteria:**
- The above grep returns no production-code matches.
- This check is documented in the PR description so future arch-review can verify regressions.

## Non-Functional Requirements

### NFR-1: Performance
The adapter layer adds at most one method call indirection and one projection per request. No additional database round-trips. Existing query plans are preserved (adapter calls the same repository methods Logistics called directly before). No measurable latency regression expected; no benchmark required.

### NFR-2: Security
No change to authentication, authorization, or data-sensitivity boundaries. The Logistics DTOs must not expose fields beyond what Logistics already saw via the Catalog aggregate.

### NFR-3: Testability
Logistics unit tests must be able to mock `ILogisticsCatalogSource` and `ILogisticsStockOperationService` using only Logistics-owned test fakes/mocks. No Catalog test infrastructure (in-memory repos, test doubles for `IStockUpProcessingService`) is required to unit-test the three affected files after the change.

### NFR-4: Backward compatibility
This is an internal refactor. No public API endpoints, request/response contracts, persisted data, or external integration surfaces change. No migration required.

## Data Model
No schema or persistence changes. New types introduced are in-memory contracts only:
- `ILogisticsCatalogSource` (interface)
- `ILogisticsStockOperationService` (interface)
- `LogisticsStockOperationSource` (enum) — mirrors the subset of `StockUpSourceType` actually used by Logistics
- One or more Logistics-owned read DTOs (record/class) capturing the Catalog projection fields Logistics consumes

All live under `Application/Features/Logistics/Contracts/` (and a `Contracts/Models/` subfolder for DTOs).

## API / Interface Design

### Logistics-side (consumer-owned)

```
Application/Features/Logistics/Contracts/
  ILogisticsCatalogSource.cs
  ILogisticsStockOperationService.cs
  LogisticsStockOperationSource.cs        // enum
  Models/
    LogisticsCatalogItem.cs               // DTO — exact fields TBD by call-site audit
    LogisticsStockOperationRequest.cs     // DTO carrying the operation parameters
```

### Catalog-side (producer-owned adapters)

```
Infrastructure/Features/Catalog/Adapters/      (or existing adapter folder)
  CatalogToLogisticsCatalogSourceAdapter.cs
  CatalogToLogisticsStockOperationAdapter.cs
```

### DI registration
Added to Catalog's module registration:

```csharp
services.AddScoped<ILogisticsCatalogSource, CatalogToLogisticsCatalogSourceAdapter>();
services.AddScoped<ILogisticsStockOperationService, CatalogToLogisticsStockOperationAdapter>();
```

(Lifetime should match the lifetime of the underlying `ICatalogRepository` / `IStockUpProcessingService` registrations — verify at implementation time.)

## Dependencies
- Existing Catalog services remain unchanged: `ICatalogRepository`, `IStockUpProcessingService`, `StockUpSourceType`. This work depends on them continuing to exist and behave as today.
- Existing DI container and module-registration mechanism (no new infra).
- Existing test framework (xUnit + Moq or whichever is in use) — no new test libraries.

## Out of Scope
- Refactoring Catalog internals or splitting `ICatalogRepository` into smaller Catalog-side interfaces.
- Auditing other Logistics files for unrelated cross-module dependencies.
- Auditing other modules (Manufacturing, Purchase, etc.) for the same pattern violation — each is a separate finding.
- Extracting Logistics into a separate deployable/microservice. This refactor unblocks that goal but does not perform it.
- Renaming any existing Catalog types.
- Changes to MediatR commands/queries themselves; only handler/service constructor signatures change.
- Frontend changes — none are required.

## Open Questions
None.

## Status: COMPLETE