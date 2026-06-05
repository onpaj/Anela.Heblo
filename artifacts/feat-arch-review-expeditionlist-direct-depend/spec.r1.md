# Specification: Invert ExpeditionList → Logistics Picking Dependency

## Summary
Refactor `ExpeditionList` so it no longer directly consumes types from the `Logistics.Picking` namespace. ExpeditionList will own the contract (`IPickingListSource` and its request/result DTOs); Logistics will provide an adapter that implements it. Add a CI-enforced architecture test to prevent regression.

## Background
The codebase follows a consistent cross-module communication pattern documented in `docs/architecture/development_guidelines.md`: when module A needs read-only access to data in module B, the dependency is inverted — the consumer owns the contract, the provider implements an adapter. Examples already in the codebase: `ILeafletKnowledgeSource` (owned by Leaflet), `ILogisticsStockOperationQueryService` (owned by the consumer).

`ExpeditionList` violates this pattern in two files:

- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/ExpeditionListService.cs:1` imports `Anela.Heblo.Application.Features.Logistics.Picking` and uses `IPickingListSource`, `PrintPickingListRequest`, `PrintPickingListResult`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListJob.cs:3` imports the same namespace and uses `PrintPickingListRequest`.

`IPickingListSource`, `PrintPickingListRequest`, and `PrintPickingListResult` are defined in `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/` and are owned by Logistics.

`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces `ExpeditionListArchive → ExpeditionList` (line 327) but has no rule covering `ExpeditionList → Logistics`, so this violation is invisible to CI and likely to grow over time.

Renaming or reshaping the Logistics picking contract today silently breaks ExpeditionList with no compile-time warning at the boundary; ExpeditionList cannot be reasoned about, tested, or evolved independently.

## Functional Requirements

### FR-1: ExpeditionList owns a `IPickingListSource` contract
Create `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListSource.cs` defining an interface owned by ExpeditionList. The interface must expose only the operations ExpeditionList actually invokes today (i.e. the equivalent of the `PrintPickingList` operation currently consumed via the Logistics-owned interface).

**Acceptance criteria:**
- New file `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/IPickingListSource.cs` exists in the ExpeditionList namespace (e.g. `Anela.Heblo.Application.Features.ExpeditionList.Contracts`).
- The interface declares only the methods/operations that `ExpeditionListService` and `PrintPickingListJob` use today; no unused members are copied over.
- No `using Anela.Heblo.Application.Features.Logistics...` appears in the new contract file.

### FR-2: ExpeditionList owns request/result DTOs
Introduce ExpeditionList-owned request and result types (`ExpeditionPickingRequest`, `ExpeditionPickingResult`) under `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/` that contain **only the fields ExpeditionList reads or writes** — no speculative parity with the Logistics-side types.

**Acceptance criteria:**
- `ExpeditionPickingRequest` and `ExpeditionPickingResult` are defined in the ExpeditionList contracts namespace.
- Each field on these DTOs traces to actual consumption in `ExpeditionListService` or `PrintPickingListJob` (or to data ExpeditionList passes into the picking operation).
- Naming uses the `Expedition*` prefix to signal ownership by ExpeditionList (per the brief's suggestion).
- Types are classes, not C# records (per project DTO rule).

### FR-3: ExpeditionListService and PrintPickingListJob use only ExpeditionList-owned types
Replace all `Anela.Heblo.Application.Features.Logistics.Picking` references in `ExpeditionListService.cs` and `PrintPickingListJob.cs` with the ExpeditionList-owned interface and DTOs.

**Acceptance criteria:**
- `grep -r "Anela.Heblo.Application.Features.Logistics" backend/src/Anela.Heblo.Application/Features/ExpeditionList/` returns zero matches.
- ExpeditionList still produces the same observable behavior (same picking print is triggered with the same effective input).
- Existing unit/integration tests for ExpeditionList pass without behavioral change.

### FR-4: Logistics provides an adapter for the ExpeditionList contract
Add `LogisticsExpeditionListSourceAdapter` (or similarly named) inside the Logistics module that implements the ExpeditionList-owned `IPickingListSource` and delegates to the existing Logistics picking implementation. The adapter is the only place where the two type families meet.

**Acceptance criteria:**
- Adapter lives under `backend/src/Anela.Heblo.Application/Features/Logistics/` (Logistics owns the adapter).
- Adapter implements the ExpeditionList-owned interface and translates between `ExpeditionPicking*` DTOs and the existing Logistics-internal types.
- No ExpeditionList code references the adapter type directly; it is consumed only via the interface.

### FR-5: Provider-side DI registration
Register the binding `IPickingListSource (ExpeditionList) → LogisticsExpeditionListSourceAdapter` inside `LogisticsModule` (the provider registers the binding, consistent with the project's inversion pattern).

**Acceptance criteria:**
- DI registration appears in the Logistics module composition (`LogisticsModule` or its equivalent registration class), not in ExpeditionList.
- Application starts and resolves `IPickingListSource` for ExpeditionList without runtime errors.
- A simple resolution smoke test (existing app startup or a targeted test) confirms the binding.

### FR-6: Architecture test enforces the new boundary
Add a `ModuleBoundaryRule` in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` for `ExpeditionList → Logistics` with an empty allowlist, mirroring the style of the existing `ExpeditionListArchive → ExpeditionList` rule at line 327.

**Acceptance criteria:**
- New rule exists in `ModuleBoundariesTests.cs`.
- Allowlist is empty (no exceptions permitted).
- Test passes after the refactor; test would fail if any `Anela.Heblo.Application.Features.ExpeditionList` source file imported `Anela.Heblo.Application.Features.Logistics.*`.
- A deliberate temporary re-introduction of a Logistics import in ExpeditionList during local validation causes the test to fail (verifies the rule is active, not vacuously passing).

### FR-7: Behavior parity
The end-to-end picking-print flow triggered from ExpeditionList must behave identically after the refactor — same documents printed, same side effects, same error handling.

**Acceptance criteria:**
- All existing ExpeditionList tests pass unchanged.
- The Hangfire job `PrintPickingListJob` enqueues and executes with equivalent payload and outcomes.
- No new logged errors or warnings at startup or during the print flow under normal operation.

## Non-Functional Requirements

### NFR-1: Performance
The adapter is a thin translation layer. Translation overhead per call must be negligible (sub-millisecond on typical payloads) and must not introduce additional I/O, allocations on hot paths beyond DTO construction, or extra round-trips.

### NFR-2: Security
No change to security posture. The adapter must not loosen access, expose additional fields beyond what ExpeditionList already had access to, or bypass any authorization that the original Logistics types enforced.

### NFR-3: Maintainability
- No duplication beyond what the inversion requires: only fields ExpeditionList actually uses appear in its DTOs.
- The adapter is the single translation point — no scattered conversion logic elsewhere.
- File sizes stay within project norms (interface and DTO files small and focused, per `coding-standards`).

### NFR-4: Testability
- ExpeditionList unit tests can mock the ExpeditionList-owned `IPickingListSource` without referencing any Logistics type.
- The architecture test runs in the standard backend test suite (`dotnet test`) with no special setup.

### NFR-5: Backwards compatibility
Internal-only refactor. No public API, persisted data, or external contract changes. No migration required.

## Data Model
No persistent data model changes.

New in-process contract types (ExpeditionList-owned, under `Anela.Heblo.Application.Features.ExpeditionList.Contracts`):

- `IPickingListSource` — interface exposing only the operation ExpeditionList consumes today.
- `ExpeditionPickingRequest` — class DTO with only the fields ExpeditionList provides as input.
- `ExpeditionPickingResult` — class DTO with only the fields ExpeditionList reads from the result.

Existing Logistics-side types (`IPickingListSource`, `PrintPickingListRequest`, `PrintPickingListResult` under `Anela.Heblo.Application.Features.Logistics.Picking`) remain in place and continue to serve Logistics-internal consumers. The Logistics-side `IPickingListSource` is unaffected by this change unless analysis during implementation reveals ExpeditionList was its only external consumer (see Open Questions).

## API / Interface Design
No HTTP endpoint, MediatR contract, or UI change.

Internal interfaces:

```
namespace Anela.Heblo.Application.Features.ExpeditionList.Contracts;

public interface IPickingListSource
{
    Task<ExpeditionPickingResult> PrintPickingListAsync(
        ExpeditionPickingRequest request,
        CancellationToken cancellationToken);
}
```

(Exact method signature derived from current consumption in `ExpeditionListService.cs` and `PrintPickingListJob.cs`; the implementation must inspect actual call sites and mirror them.)

Adapter (Logistics-side):

```
namespace Anela.Heblo.Application.Features.Logistics; // or appropriate Logistics namespace

internal sealed class LogisticsExpeditionListSourceAdapter
    : ExpeditionList.Contracts.IPickingListSource
{
    // delegates to existing Logistics picking implementation
    // translates ExpeditionPicking* <-> Logistics PrintPickingList* types
}
```

DI registration is performed in `LogisticsModule` (provider side).

## Dependencies
- Existing Logistics picking implementation (no change required to its public surface).
- `Anela.Heblo.Tests.Architecture.ModuleBoundariesTests` infrastructure (already present, used in identical fashion at line 327).
- DI container used by `LogisticsModule`.

No new NuGet packages, external services, or infrastructure.

## Out of Scope
- Refactoring or renaming the Logistics-internal `IPickingListSource`, `PrintPickingListRequest`, `PrintPickingListResult`.
- Auditing or fixing other (unrelated) module boundary violations.
- Introducing inversions for any other ExpeditionList ↔ external-module crossings not named in the brief.
- Changes to ExpeditionListArchive (separate module, governed by its own existing boundary rule).
- UI, API surface, or persistence schema changes.
- Adding new picking-related functionality.

## Open Questions
None.

## Status: COMPLETE