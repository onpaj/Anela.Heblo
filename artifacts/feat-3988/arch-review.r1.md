# Architecture Review: Derive `currentStatusName` for the desired-state case from configuration

## Skip Design: true

## Architectural Fit Assessment
This fully aligns with the existing pattern in the same handler. `NonPrintableStates` already proves the codebase's convention for this exact situation: pair a Shoptet state ID with its display name so they can never drift apart. The desired-state branch is the one place that convention was not followed — it hardcoded the name as a bare string literal while sourcing the ID from `IOptions<PrintPickingListOptions>`. This is a same-module, same-class correctness fix; it does not touch module boundaries, contracts, persistence, or any cross-module interface (`docs/architecture/development_guidelines.md` rules on DTO/contract placement and module independence are not implicated — nothing here is a DTO, and no other module talks to `PrintPickingListOptions`).

There is no UI/UX work: the frontend already has a working, unaffected consumer (`ExpeditionOrderInvalidState: "Zakázku nelze vytisknout – je ve stavu {currentStatusName}"` in `frontend/src/i18n.ts:226`) that interpolates whatever string the backend sends. It requires no changes and no new design decisions — hence `Skip Design: true`.

## Proposed Architecture

### Component Overview
```
PrintExpeditionOrderHandler.Handle()
        │
        ├─ currentStatusId == _options.Value.DesiredStateId ?
        │        └─ YES → Params["currentStatusName"] = _options.Value.DesiredStateName   (was: literal "Balí se")
        │
        └─ NonPrintableStates.TryGetValue(currentStatusId, out stateName) ?
                 └─ YES → Params["currentStatusName"] = stateName   (unchanged — already ID-driven)

PrintPickingListOptions (IOptions<T>, section "ExpeditionList")
        ├─ DesiredStateId   : int     (existing, default 26)
        └─ DesiredStateName : string  (NEW, default "Balí se")   ← added as a sibling property
```
No new components, services, or interfaces. One property added to an existing options POCO; one line changed in an existing handler.

### Key Design Decisions

#### Decision 1: Sibling property (`DesiredStateName` on `PrintPickingListOptions`) vs. folding the desired state into `NonPrintableStates`
**Options considered:**
1. Add `DesiredStateName` as a new property on `PrintPickingListOptions`, read directly in the handler's existing `if (currentStatusId == _options.Value.DesiredStateId)` branch (the issue's suggested smallest fix).
2. Merge the desired-state ID/name pair into `NonPrintableStates` (or a superset dictionary) built at handler-construction time from `_options.Value`, and drop the separate `if` branch in favor of one unified lookup.

**Chosen approach:** Option 1 — add the sibling property, keep the two branches as they are today.

**Rationale:** `NonPrintableStates` is a `static readonly` dictionary of states that are structurally identical regardless of configuration (cancelled, already packed, handed to carrier — these IDs are Shoptet-wide constants, not tenant-configurable). `DesiredStateId` is fundamentally different: it is the *one* state that has separate business meaning (it triggers the "already printed, don't double-print" short-circuit) and is deployment-configurable. Collapsing it into `NonPrintableStates` would require building that dictionary per-request (or per-`IOptions`-change) from configuration instead of as a `static readonly` literal, which is a larger structural change than this issue calls for, and would blur the semantic distinction the code comments already draw out (`// "Desired state after printing" is checked separately below against _options.Value.DesiredStateId.`). Option 1 is strictly additive, touches the minimum surface area, and mirrors exactly how `DesiredStateId` itself is already declared and consumed — a new option, read once, in the branch that already exists. This also matches the issue's own suggested fix verbatim.

#### Decision 2: Default value for `DesiredStateName`
**Options considered:**
1. Default to `"Balí se"` (matches current hardcoded behavior and the current default `DesiredStateId = 26`).
2. No default (require every environment to set it explicitly) or default to `string.Empty`.

**Chosen approach:** Option 1 — default `"Balí se"`.

**Rationale:** Every other property on `PrintPickingListOptions` has a sensible default matching current production values (e.g. `DesiredStateId = 26`, `NoteStateId = 35`). An empty or missing default would silently blank the user-facing error message for any environment that doesn't explicitly set `DesiredStateName` — worse than the bug being fixed. Defaulting to `"Balí se"` preserves current behavior with zero configuration changes required in Key Vault or `appsettings.json` (NFR-1 in the spec).

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Two existing files change:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs` — add `DesiredStateName` property.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — replace the literal at line 64 with `_options.Value.DesiredStateName`.

One config file gets an additive entry for symmetry/documentation (not required for the C# default to apply, since `appsettings.json` currently doesn't set it and doesn't need to):
- `backend/src/Anela.Heblo.API/appsettings.json` — add `"DesiredStateName": "Balí se", // Bali se` immediately after the existing `"DesiredStateId": 26, // Bali se` line in the `"ExpeditionList"` section (`appsettings.json:540`), so a future editor changing `DesiredStateId` sees the paired name right next to it.

One existing test updates to stop asserting the old (buggy) behavior it was written to characterize:
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — in `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26`, construct `PrintPickingListOptions` with both a non-default `DesiredStateId` (99, unchanged) *and* a non-default `DesiredStateName` (e.g. `"Custom State"`), and assert `currentStatusName` equals that configured name instead of the literal `"Balí se"`. This is the test that currently locks in the bug for the name half of the pairing (it already correctly locks in the fix for the ID half) — see "Specification Amendments" below for why this counts as fixing a pre-existing bad assertion, not weakening test coverage.

### Interfaces and Contracts
No public contract changes. `PrintPickingListOptions` remains a plain POCO bound via `IOptions<PrintPickingListOptions>` under configuration key `"ExpeditionList"` (`PrintPickingListOptions.ConfigurationKey`). `PrintExpeditionOrderResponse.Params` remains `Dictionary<string, string>` — only the value placed under the `"currentStatusName"` key changes for one branch.

### Data Flow
Unchanged. `Handle()` still: (1) resolves `currentStatusId` from Shoptet via `IEshopOrderClient.GetOrderStatusIdAsync`, (2) compares it against `_options.Value.DesiredStateId`, (3) on match, builds the same `PrintExpeditionOrderResponse` shape with `ErrorCodes.ExpeditionOrderInvalidState`, now sourcing `currentStatusName` from `_options.Value.DesiredStateName` instead of a literal. The response flows to the API layer and frontend exactly as today; the frontend's `ExpeditionOrderInvalidState` i18n template is untouched.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `appsettings.json` change is applied without a corresponding Key Vault/staging override, causing environment drift between default and configured values | Low | The C# default (`"Balí se"`) matches the `appsettings.json` value being added, so no drift is introduced; this is purely additive/documentary, per CLAUDE.md's Key Vault secret rule (not a secret, so plain appsettings is correct — no KV change needed) |
| Updating the pre-existing test could be read as "weakening" a test that currently passes | Low | The test's own name and comments make clear its intent was to lock in that the *ID* comparison uses configuration, not the literal 26 — the `currentStatusName` assertion using the hardcoded string was itself an artifact of the bug being reported by this issue, not an intentional coverage decision. The update actually strengthens the test by proving the *name* is now equally configuration-driven |
| Someone later reintroduces a hardcoded status-name literal elsewhere in the file (e.g. copy-pasting the old pattern) | Low | Out of scope for this fix; no code-level guard proposed — acceptable given issue scope, flagged for future arch-review sweep if it recurs |

## Specification Amendments
None — the spec (`spec.r1.md`) already correctly identifies the test update as part of FR-2's acceptance criteria. No additional functional or non-functional requirements are needed.

## Prerequisites
None. No migrations, no new infrastructure, no config secrets to provision ahead of implementation — this can be implemented and merged as a single, self-contained change.
