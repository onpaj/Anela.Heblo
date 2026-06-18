# PR Context

- **PR**: #3204 — #3116: Encapsulate ManufactureOrder state transition rules in the domain entity
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3204
- **Branch**: `feature/3116-arch-review-manufacture-state-transition-rules-liv` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +1437 / -162 across 22 files
- **Absorbed**: backmerged with `main` (clean, no conflicts), all PR tests passing (693 Manufacture tests). Pre-existing FlexiBee integration-test failures are environment-only (require live FlexiBee config) and exist on `main` too — unrelated to this PR.

## Description

## What the issue was
`ManufactureOrder` exposed `State` as an unguarded public setter, and the legal state-transition table lived in `UpdateManufactureOrderStatusHandler.IsValidStateTransition`. Keeping the rule in the Application layer violated *business logic belongs in the domain*: any handler could assign an arbitrary state directly (`order.State = ...`) bypassing validation, and the rule could only be tested through the handler, never against the entity in isolation.

## How it was fixed / handled
Relocated the transition rules into the `ManufactureOrder` aggregate, following the existing `TransportBox` precedent — no runtime or HTTP-contract change.

### Changes
- **`ManufactureOrder.cs`** — `State`/`StateChangedAt`/`StateChangedByUser` setters tightened to `internal set`; added `InitializeState(...)` (sanctioned seeding path), `CanTransitionTo(...)` (pure predicate reproducing the existing matrix verbatim, type-agnostic), and a guarded `ChangeState(...)` that throws `ValidationException` on an illegal transition (validate-before-mutate, all three audit fields set atomically).
- **`UpdateManufactureOrderStatusHandler.cs`** — pre-checks via `order.CanTransitionTo(...)` (identical `InvalidOperation { oldState, newState }` early return preserved — not a thrown-exception path), mutates via `order.ChangeState(...)`; the private `IsValidStateTransition` was deleted. All side effects and response shapes unchanged.
- **`CreateManufactureOrderHandler.cs` / `DuplicateManufactureOrderHandler.cs`** — seed the initial `Draft` state via `InitializeState` (the latter also assigned `State` in an initializer and had to be redirected to compile under `internal set`).
- **Tests** — `ManufactureOrderStateTransitionTests` rewritten: removed the three type-aware private mirror helpers and their theories (which never exercised production code and would have silently changed behaviour), replaced with an exhaustive `CanTransitionTo` matrix theory plus legal/illegal `ChangeState` tests against the real entity. Arrange-phase `State = ...` seeding across 8 affected fixtures routed through `InitializeState`; no expected outcomes changed.

### Verification
- `dotnet build` — succeeds (pre-existing warnings only).
- `dotnet format --verify-no-changes` — clean.
- `dotnet test --filter "FullyQualifiedName~Manufacture"` — **693 passed, 0 failed, 0 skipped** (includes EF materialization tests confirming `State` still binds under the non-public setter).

## Artifacts
- Brief, spec, arch review, design, task plan, impl, and review markdown are committed under `artifacts/feat-3116/` in this branch.

Closes #3116
