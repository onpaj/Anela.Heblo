# Specification: Remove dead mutable static `PrintPickingListRequest.DefaultCarriers`

## Summary
Eliminate a global-mutation hazard in `PrintPickingListRequest.DefaultCarriers` (a public-setter static field that holds shared mutable state). The property is dead code in the production path — production callers use `ExpeditionPickingRequest.DefaultCarriers` — so the safest fix is to remove the property entirely and update the lone integration test reference.

## Background
A daily architecture review flagged `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs:16`:

```csharp
public static IList<Carriers> DefaultCarriers { get; set; } = new List<Carriers>()
{
    Carriers.Zasilkovna, Carriers.GLS, Carriers.PPL, Carriers.Osobak
};
```

Two distinct problems:

1. **Mutation hazard.** The public setter on a static, process-wide list lets any caller silently swap the default carrier set for the lifetime of the process. There is no diagnostic signal if a test (or other code) forgets to restore state, which can cause downstream expedition list runs to use an unexpected carrier set.

2. **Dead code in production.** The property is referenced only from one integration test (`backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs:88`). All production code — `PrintPickingListJob`, `RunExpeditionListPrintFixHandler`, and other ExpeditionList handlers — uses `ExpeditionPickingRequest.DefaultCarriers` (already read-only at `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs:16`).

Removing the property eliminates the hazard, reduces duplication, and aligns the test with the production code path it claims to cover.

## Functional Requirements

### FR-1: Remove `PrintPickingListRequest.DefaultCarriers` entirely
Delete the static `DefaultCarriers` property from `PrintPickingListRequest`. Do not retain it as read-only — production code does not reference it, so leaving a read-only duplicate of `ExpeditionPickingRequest.DefaultCarriers` would only add maintenance drift.

**Acceptance criteria:**
- `PrintPickingListRequest.cs` no longer declares any `DefaultCarriers` member.
- A repo-wide search for `PrintPickingListRequest.DefaultCarriers` returns zero results.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` is unchanged and remains the single source of truth for the default carrier set.

### FR-2: Update the integration test to use `ExpeditionPickingRequest.DefaultCarriers`
The only consumer (`PickingListIntegrationTests.cs:88`) must be updated to reference `ExpeditionPickingRequest.DefaultCarriers` — the same list production code uses — so the test exercises the production default rather than a parallel one.

**Acceptance criteria:**
- The test continues to assert the same behavior it asserted before (no change in test intent, only in which constant it reads).
- `PickingListIntegrationTests` references `ExpeditionPickingRequest.DefaultCarriers` and does not reintroduce a local mutable carrier list.
- The integration test passes when run against the normal test environment (Postgres container per the shared-container setup in commit `7542d689`).

### FR-3: Verify no behavioral change in production
Production handlers (`PrintPickingListJob`, `RunExpeditionListPrintFixHandler`, any other ExpeditionList consumer) must continue to receive the same default carrier set they do today.

**Acceptance criteria:**
- `dotnet build` succeeds with no new warnings related to this change.
- Existing unit and integration tests for `PrintPickingListJob`, `RunExpeditionListPrintFixHandler`, and ExpeditionList carrier defaults all pass without modification (other than the FR-2 test update).
- Manual review confirms the carrier set used at runtime is `{ Zasilkovna, GLS, PPL, Osobak }` — identical to today.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact expected. This is a compile-time / source-level change. Static list initialization cost is unchanged (one initialization, in `ExpeditionPickingRequest`).

### NFR-2: Security
No direct security implications. Indirectly improves robustness by removing a vector for accidental shared-state corruption that could cause incorrect carrier selection during expedition processing.

### NFR-3: Maintainability
After this change there is exactly one canonical default carrier list (`ExpeditionPickingRequest.DefaultCarriers`), reducing the risk of the two lists diverging when carriers are added or removed in the future.

## Data Model
No data model changes. `Carriers` enum is untouched. No database migrations.

## API / Interface Design

**Removed surface:**
- `PrintPickingListRequest.DefaultCarriers` (static property) — gone.

**Unchanged surface:**
- `PrintPickingListRequest` class itself remains; only the static `DefaultCarriers` member is removed. Instance members and the request contract for MediatR/picking flows are not touched.
- `ExpeditionPickingRequest.DefaultCarriers` remains as the sole read-only default-carrier source.

**Test surface:**
- `PickingListIntegrationTests.cs:88` switches its reference target. No test method signatures change.

No HTTP endpoints, no MediatR contracts, no OpenAPI schema, and no generated TypeScript client are affected.

## Dependencies
- None added or removed.
- Touches only files under:
  - `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`
  - `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`
- Before completion, run repo-wide search to confirm no other consumer exists outside these two files.

## Out of Scope
- Refactoring `PrintPickingListRequest` more broadly (other members, naming, location).
- Refactoring or consolidating `ExpeditionPickingRequest` and `PrintPickingListRequest` into a shared contract.
- Auditing other static-mutable-state hazards elsewhere in the codebase. (A separate sweep can be filed if desired.)
- Changing the default carrier set itself (`{ Zasilkovna, GLS, PPL, Osobak }`) — values stay identical.
- Any frontend changes — this is backend-only.

## Open Questions
None.

## Status: COMPLETE