# Architecture Review: Remove dead mutable static `PrintPickingListRequest.DefaultCarriers`

## Skip Design: true

This is a backend-only dead-code/hazard removal. No UI, no API surface change, no new visual components.

## Architectural Fit Assessment

The change strengthens existing patterns rather than introducing new ones.

- **Duplication is the root cause.** Two static `DefaultCarriers` lists exist with byte-identical contents: one read-only (`ExpeditionPickingRequest.DefaultCarriers` at `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs:16`) and one with a public setter (`PrintPickingListRequest.DefaultCarriers` at `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs:16`). The read-only variant is the one production uses; the mutable variant is the hazard.
- **Production callers are already aligned.** Verified consumers of the carrier defaults are `RunExpeditionListPrintFixHandler.cs:27` and `PrintPickingListJob.cs:51` — both read from `ExpeditionPickingRequest.DefaultCarriers`. No production reference to the mutable property exists.
- **The integration test is the only remaining consumer.** `PickingListIntegrationTests.cs:88` reads `PrintPickingListRequest.DefaultCarriers`. Switching it to `ExpeditionPickingRequest.DefaultCarriers` actually improves test fidelity — the test starts exercising the same constant the production path uses.
- **Cross-assembly reference is already acceptable.** `Anela.Heblo.Adapters.Shoptet.Tests` already references `Anela.Heblo.Application.Features.ExpeditionList` (line 2 of the test). Pulling in `…ExpeditionList.Contracts` introduces no new project dependency.
- **Naming asymmetry is preserved (intentionally).** The class `PrintPickingListRequest` is retained — only the static member is removed. The two request DTOs (`PrintPickingListRequest` and `ExpeditionPickingRequest`) remain as separate concerns and any consolidation is explicitly out of scope per the spec.

Verdict: Fit is clean. This is a deletion, not a redesign.

## Proposed Architecture

### Component Overview

```
                   ┌─────────────────────────────────────────────┐
                   │  Application.Features.ExpeditionList        │
                   │  ┌─────────────────────────────────────┐    │
                   │  │ ExpeditionPickingRequest            │    │
                   │  │  + DefaultCarriers (get only) ◄─────┼────┼─ SINGLE SOURCE
                   │  └──────────────▲──────────────────────┘    │   OF TRUTH
                   │                 │                            │
                   │  ┌──────────────┴──────────────┐             │
                   │  │ PrintPickingListJob         │             │
                   │  │ RunExpeditionListPrintFix   │             │
                   │  │ (read DefaultCarriers)      │             │
                   │  └─────────────────────────────┘             │
                   └─────────────────────────────────────────────┘
                                       ▲
                                       │ reads
                   ┌───────────────────┼─────────────────────────┐
                   │ Adapters.Shoptet.Tests.Integration          │
                   │  PickingListIntegrationTests (line 88)      │  ← updated
                   └─────────────────────────────────────────────┘

                   ┌─────────────────────────────────────────────┐
                   │  Application.Features.Logistics.Picking     │
                   │  ┌─────────────────────────────────────┐    │
                   │  │ PrintPickingListRequest             │    │
                   │  │   - DefaultCarriers (REMOVED)       │    │
                   │  │   ✓ DefaultSourceStateId (kept)     │    │
                   │  │   ✓ DefaultDesiredStateId (kept)    │    │
                   │  │   ✓ instance members (kept)         │    │
                   │  └─────────────────────────────────────┘    │
                   └─────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Remove the property entirely (not convert to read-only)

**Options considered:**
- (A) Change `{ get; set; }` to `{ get; }` — fixes the mutation hazard but leaves a redundant, identical list duplicated across two contracts.
- (B) Remove the property entirely and redirect the lone test consumer to `ExpeditionPickingRequest.DefaultCarriers`.

**Chosen approach:** B (matches the spec).

**Rationale:** The mutation hazard is only half the problem; the other half is duplication. Option A eliminates the hazard but creates two read-only lists that must be kept in sync forever (when carriers are added/removed). Option B converges on the single canonical list that production already uses, removing both the hazard and the drift risk in one move. The test's intent — "run picking with the production default carriers" — is better expressed by reading the production constant directly.

#### Decision 2: Keep `PrintPickingListRequest` and its other static constants

**Options considered:**
- (A) Delete only `DefaultCarriers` and leave `DefaultSourceStateId`, `DefaultDesiredStateId`, and all instance members untouched.
- (B) Broader cleanup — consolidate `PrintPickingListRequest` and `ExpeditionPickingRequest` into one shared contract.

**Chosen approach:** A.

**Rationale:** `DefaultSourceStateId` and `DefaultDesiredStateId` are `const int` (compile-time constants, no mutation hazard) and are actively referenced by the same integration test (`PickingListIntegrationTests.cs:22, 87`). They are not part of this finding. Consolidation of the two request DTOs is explicitly out of scope and would expand blast radius far beyond a dead-code removal. Surgical change wins.

## Implementation Guidance

### Directory / Module Structure

No new files. No renames. Two existing files are touched:

```
backend/
└── src/Anela.Heblo.Application/Features/Logistics/Picking/
│       └── PrintPickingListRequest.cs                          (delete lines 16-22)
└── test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/
        └── PickingListIntegrationTests.cs                      (update line 88 + add using)
```

### Interfaces and Contracts

**Removed:**
- `PrintPickingListRequest.DefaultCarriers` (public static property with public setter) — gone.

**Unchanged (do not touch):**
- `PrintPickingListRequest` class itself.
- `PrintPickingListRequest.DefaultSourceStateId` (`const int = -2`).
- `PrintPickingListRequest.DefaultDesiredStateId` (`const int = 26`).
- `PrintPickingListRequest` instance members: `Carriers`, `SourceStateId`, `DesiredStateId`, `ChangeOrderState`, `SendToPrinter`.
- `ExpeditionPickingRequest` in any way — it stays as the single source of truth.

**Test surface:**
- `PickingListIntegrationTests.cs` adds `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` (the test currently has `using Anela.Heblo.Application.Features.ExpeditionList;` but not the `.Contracts` sub-namespace where `ExpeditionPickingRequest` lives).
- Line 88 changes from `Carriers = PrintPickingListRequest.DefaultCarriers,` to `Carriers = ExpeditionPickingRequest.DefaultCarriers,`.

### Data Flow

Unchanged at runtime. Both before and after:

```
Hangfire trigger
    └─► PrintPickingListJob.cs:51    ── reads ExpeditionPickingRequest.DefaultCarriers
            │
            └─► new PrintPickingListRequest { Carriers = […] }
                    └─► IPickingListSource.CreatePickingList(request, …)
                            └─► ShoptetApiExpeditionListSource (filters orders by carrier)

Manual fix flow
    └─► RunExpeditionListPrintFixHandler.cs:27  ── reads ExpeditionPickingRequest.DefaultCarriers
            └─► (same as above)

Integration test (after change)
    └─► PickingListIntegrationTests.cs:88  ── reads ExpeditionPickingRequest.DefaultCarriers
            └─► (same as above)
```

The carrier set `{ Zasilkovna, GLS, PPL, Osobak }` flows through unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A hidden reflection-based or string-named consumer of `PrintPickingListRequest.DefaultCarriers` is missed. | Low | Repo-wide grep already confirmed zero non-test references (verified during this review). Reflection on a property named `DefaultCarriers` is implausible for this codebase. Acceptance criterion already requires a final search. |
| Test file forgets the new `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` and breaks the build. | Low | `dotnet build` will flag this immediately. CI runs build on every PR. |
| Someone re-adds a `DefaultCarriers` member later under the same drift pattern. | Low | Acceptance criterion explicitly forbids reintroducing a local mutable carrier list. Code review on follow-up PRs should catch this. |
| Documentation (`docs/superpowers/plans/…`) shows the old mutable form as a code sample. | Negligible | Those plans are historical artifacts describing past work and are not API contracts. Do not edit. |
| Test asserts behavior that depended (silently) on a mutation elsewhere. | Negligible | The test never mutates `DefaultCarriers` and no other code does either. The list contents are identical, so test outcomes are unchanged. |

## Specification Amendments

The spec is sufficient and matches the codebase. One small implementation detail to add explicitly:

- **The test must add `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;`** — the current `using Anela.Heblo.Application.Features.ExpeditionList;` does not cover the `.Contracts` sub-namespace where `ExpeditionPickingRequest` lives. Without this, the build fails with `CS0246`. The spec implies the change but does not call out the new `using`; recording it here avoids a stumble during implementation.

No other amendments needed.

## Prerequisites

None.

- No migrations.
- No config or infrastructure changes.
- No new packages or NuGet references.
- No assembly reference changes — the test project already references the Application assembly that contains `ExpeditionPickingRequest`.
- No coordination with frontend, OpenAPI clients, or generated TypeScript code.

Implementation can begin immediately.