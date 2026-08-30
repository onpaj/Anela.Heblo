# Architecture Review: Deduplicate Shoptet order-status ID constants (ExpeditionList / Logistics.Picking)

## Skip Design: true

Pure internal `Application`-layer refactor: three `public const int` declarations move from one class to another, plus two comments and two test-constant references. No controller, MediatR contract, DTO shape, or UI surface changes. No new or changed screens, components, or visual elements exist for this change.

## Architectural Fit Assessment

Verified directly against source (not just the spec's description):

- `ExpeditionPickingRequest.cs` (`Application/Features/ExpeditionList/Contracts/`) and `PrintPickingListRequest.cs` (`Application/Features/Logistics/Picking/`) declare byte-for-byte identical `DefaultSourceStateId = -2`, `DefaultDesiredStateId = 26`, `DefaultNoteStateId = 35`, exactly as the spec states, including the stray commented-out line in `PrintPickingListRequest.cs`.
- `LogisticsExpeditionPickingAdapter.cs` already has `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` and implements ExpeditionList's consumer-owned `IExpeditionPickingSource`, translating `ExpeditionPickingRequest` → `PrintPickingListRequest` by copying **runtime property values**, not the default constants. `LogisticsModule.cs` documents this in an explicit comment: "Logistics provides ExpeditionList's `IExpeditionPickingSource` via adapter... DI registration is owned by the provider (Logistics), not the consumer (ExpeditionList)." This is a textbook instance of the documented pattern in `docs/architecture/development_guidelines.md` ("Cross-Module Communication Example: ILeafletKnowledgeSource" — consumer owns the contract, provider implements it via an adapter in its own `Infrastructure/`).
- `PickingListIntegrationTests.cs` already imports both `ExpeditionList.Contracts` and `Logistics.Picking` side by side and references `ExpeditionPickingRequest.DefaultCarriers` alongside `PrintPickingListRequest`'s constants — confirming the spec's claim that this Logistics→ExpeditionList.Contracts edge is already load-bearing, shipped code, not a new boundary being opened.
- **Confirmed the automated boundary guard doesn't forbid this.** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces an `ExpeditionList → Logistics` rule (namespace prefix `Anela.Heblo.Application.Features.ExpeditionList` forbidden from referencing `...Logistics`, with a single allowlisted exception for the `Carriers` enum on `ExpeditionPickingRequest`). There is **no reciprocal rule** inspecting `Logistics → ExpeditionList`. This is architecturally correct and intentional given the pattern above: the *consumer* (ExpeditionList) is the one guarded against reaching into the *provider's* (Logistics) implementation details; the provider is expected to depend on the consumer's contract. Adding `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` to `PrintPickingListRequest.cs` will not trip this test and requires no new allowlist entry.
- Checked for a pre-existing shared "Shoptet order-status" domain type (enum/constants class) that the spec's "Out of Scope" section declines to introduce — none exists. The closest thing is `ShoptetOrdersSettings.cs` (`Application/Features/ShoptetOrders/`), an `IOptions`-bound, environment-configurable settings class (`PackingStateId = 26`, `ProcessingStateId = -2`, among five other state IDs) belonging to a **third, unrelated module** (`ShoptetOrders`, not `ExpeditionList` or `Logistics`) for a **different runtime concern** (order-blocking, the packing dashboard, and an auto-completion job). Two of its defaults happen to numerically coincide with the constants in scope, but it's a different mechanism (bindable settings vs. compile-time `const`), different owner, different call sites. This is correctly out of scope for this refactor — folding it in would violate both the brief's stated boundary and the "surgical changes" project convention — but is worth a one-line note to whoever picks up the next duplication pass (see Risks).
- Verified FR-4's "no other references" claim: a broader grep for the three constant names surfaces `PrintExpeditionOrderHandlerTests.cs`, but that's a false-positive substring match on a test **method name** (`Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26`), not a reference to the constants themselves. Only `PickingListIntegrationTests.cs` needs edits, confirming the spec's dependency analysis is accurate.

**Conclusion:** the spec's chosen direction is not just acceptable — given the existing enforced boundary test, it is the *only* direction that doesn't require touching `ModuleBoundariesTests.cs`'s allowlists. The reverse direction (moving the constants to `PrintPickingListRequest` and having `ExpeditionPickingRequest` reference them) would make ExpeditionList.Contracts depend on a Logistics-owned application type, which is exactly what the `ExpeditionListLogisticsAllowlist` exists to keep minimal — it would require a new allowlist entry for a plain constant, a clear regression relative to today's single, deliberate, already-justified allowlist entry (`Carriers`). Do not reconsider this decision during implementation.

## Proposed Architecture

### Component Overview

```
ExpeditionList module (consumer)                    Logistics module (provider)
┌─────────────────────────────┐                     ┌──────────────────────────────┐
│ Contracts/                   │                     │ Picking/                      │
│  ExpeditionPickingRequest.cs │◄────references───────│  PrintPickingListRequest.cs   │
│   DefaultSourceStateId  = -2 │   (using ExpeditionList.Contracts;                   │
│   DefaultDesiredStateId = 26 │    for its own property-default init only)          │
│   DefaultNoteStateId    = 35 │                     │   SourceStateId  = ExpeditionPickingRequest.DefaultSourceStateId
│   (sole declaration, ★)      │                     │   DesiredStateId = ExpeditionPickingRequest.DefaultDesiredStateId
└──────────────┬────────────────┘                    │   NoteStateId    = ExpeditionPickingRequest.DefaultNoteStateId
               │ implements IExpeditionPickingSource  └──────────────────────────────┘
               │ (existing, unchanged)                              ▲
               ▼                                                    │ constructs (existing, unchanged —
┌───────────────────────────────────────────┐                       │  copies runtime values, not constants)
│ Infrastructure/                            │                      │
│  LogisticsExpeditionPickingAdapter.cs      │──────────────────────┘
│  (already depends on ExpeditionList.Contracts)
└───────────────────────────────────────────┘
```

Nothing in this diagram is new *structurally* — the adapter edge already exists. The only new edge is `PrintPickingListRequest.cs` (a plain DTO, not the adapter) also importing `ExpeditionList.Contracts` for its own default-value initializers.

### Key Design Decisions

#### Decision 1: Which class owns the canonical constants
**Options considered:**
1. `ExpeditionPickingRequest` (ExpeditionList.Contracts) is canonical; `PrintPickingListRequest` (Logistics) references it. *(spec's choice)*
2. `PrintPickingListRequest` (Logistics) is canonical; `ExpeditionPickingRequest` (ExpeditionList.Contracts) references it.
3. Introduce a new shared constants location (e.g. `Anela.Heblo.Domain` or `Xcc`).

**Chosen approach:** Option 1.

**Rationale:** Option 2 makes the *consumer's* contract type depend on the *provider's* concrete application type, which is precisely the direction `ModuleBoundariesTests.cs` guards against for `ExpeditionList → Logistics` (today's allowlist has exactly one narrow, justified exception — `Carriers`). Choosing Option 2 would need a new allowlist entry and represents a real (if small) architecture regression, not just a style preference. Option 3 is unjustified scope creep for a two-class, three-constant duplication — no shared Shoptet-domain-facts location exists anywhere in the codebase today (verified), and inventing one is explicitly and correctly called out as out of scope in the spec. Option 1 is free: it rides the already-existing, already-documented, already-tested Logistics→ExpeditionList.Contracts edge that the adapter uses, costs zero new module-boundary surface, and is the direction the codebase's own automated architecture test already treats as legitimate.

## Implementation Guidance

### Directory / Module Structure
No new files, no new folders. Edit in place:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Contracts/ExpeditionPickingRequest.cs` — add the two missing inline comments (FR-3). No structural change.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs` — remove 3 `const` lines + 1 stray commented-out line, add `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;`, change 3 property-default expressions.
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs` — repoint 2 constant references and their adjacent comment (FR-4). `using ExpeditionList.Contracts` is already present.

### Interfaces and Contracts
No interface changes. `IExpeditionPickingSource`, `LogisticsExpeditionPickingAdapter`, and the property lists/types of both DTOs are untouched — confirmed identical before/after by direct inspection. This is a pure constant-declaration relocation plus reference rewiring.

**One addition beyond the spec's literal text (see Specification Amendments below):** put a short comment on the new `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` line in `PrintPickingListRequest.cs` explaining *why* a Logistics-owned DTO references ExpeditionList's contracts namespace — e.g.:
```csharp
// Sources its state-ID defaults from ExpeditionList's contract (ExpeditionPickingRequest) —
// see LogisticsExpeditionPickingAdapter.cs / LogisticsModule.cs for the established
// provider-depends-on-consumer-contract pattern this follows.
using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
```
This matches the codebase's existing convention of leaving a breadcrumb comment at every intentional cross-module dependency (see `LogisticsModule.cs:34-37`, the adapter's own file-header comment) and prevents a future grep-driven arch-review pass from re-flagging this specific edge as a fresh "unexplained module boundary crossing" finding.

### Data Flow
Unchanged at runtime. `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` continues to copy `ExpeditionPickingRequest`'s *runtime property values* onto a new `PrintPickingListRequest` instance — this method is untouched. The only thing that changes is where each class's own *default* (used when a caller constructs the DTO without specifying a value) is compiled from. `PickingListIntegrationTests.cs` constructs `PrintPickingListRequest` with `SourceStateId` explicitly, so its behavior is unaffected by the default-value change; only the source of the referenced constant changes.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A future arch-review or reviewer misreads the new `Logistics → ExpeditionList.Contracts` reference in `PrintPickingListRequest.cs` as a fresh boundary violation, since it's a different kind of dependency (plain DTO default, not interface implementation) than the adapter's existing edge | Low | Add the explanatory comment described above (Interfaces and Contracts section) at the point of the new `using` directive |
| `ShoptetOrdersSettings.cs` (`ProcessingStateId = -2`, `PackingStateId = 26`) is a third, independent occurrence of two of these same values, in a different module, using `IOptions` binding rather than `const` — could look like this refactor "missed" it | Low | Out of scope by design (different module, different mechanism, different purpose — blocking/packing/auto-completion vs. picking). Leave for a separate, explicitly-scoped finding; do not fold into this PR |
| `PrintPickingListRequest` is also constructed directly by `ShoptetApiExpeditionListSource` (per the `ShoptetApiAdaptersLogisticsAllowlist` entries in `ModuleBoundariesTests.cs`) — a caller outside the ExpeditionList picking flow — so it now transitively pulls in `ExpeditionList.Contracts` even when unrelated to ExpeditionList | Negligible | Same assembly (`Anela.Heblo.Application`), no new project reference, and `const int` is compiler-inlined — zero runtime or packaging cost. Not worth a design change for a 3-constant reference |

## Specification Amendments

The spec (`spec.r1.md`) is accurate and sufficiently detailed; implementation can proceed against it as written. One small addition:

- **Add an explanatory comment on the new `using Anela.Heblo.Application.Features.ExpeditionList.Contracts;` directive in `PrintPickingListRequest.cs`** (not currently specified by FR-2), following the codebase's established convention of commenting every intentional cross-module import (see `LogisticsModule.cs` and `LogisticsExpeditionPickingAdapter.cs` for the existing style). This is cheap, keeps the codebase's self-documentation of cross-module edges consistent, and forecloses the change being re-flagged by a future automated or human architecture pass. Suggested text is given in Implementation Guidance above.

No other amendments. FR-1 through FR-5 are all verified accurate against the current source; no functional requirement needs correction.

## Prerequisites

None. No migrations, no configuration, no new package or project references, no feature flags. Implementation can start immediately from `spec.r1.md` plus the one addition above.

**Validation to run before completion** (per this repo's standard gate, plus the specific guard relevant to this change):
- `dotnet build` + `dotnet format` (backend)
- Full test suite for the touched projects, in particular:
  - `LogisticsExpeditionPickingAdapterTests.cs` — must pass unmodified (FR-5)
  - `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — confirms the new `PrintPickingListRequest.cs` import doesn't trip any inspected boundary rule (expected: passes with no allowlist changes, per the Architectural Fit Assessment above)
  - `PickingListIntegrationTests.cs` is `[Trait("Category", "Integration")]`-excluded from CI (hits live Shoptet) — a compile check is sufficient; it must not be run against the live store as part of this change's validation
