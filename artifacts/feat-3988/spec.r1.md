# Specification: Derive `currentStatusName` for the desired-state case from configuration instead of a hardcoded literal

## Summary
`PrintExpeditionOrderHandler` rejects a print attempt when the order is already in the configured "desired" Shoptet state (`_options.Value.DesiredStateId`), returning a `currentStatusName` of the hardcoded literal `"Balí se"`. Because `DesiredStateId` is configurable (default 26, overridable via Key Vault/appsettings), the display name can silently go stale if the ID is ever changed. This spec adds a companion configuration property, `DesiredStateName`, so the ID and its display name always travel together, matching the pattern already used for `NonPrintableStates`.

## Background
`PrintExpeditionOrderHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs:57-65`) has two branches that reject printing and report a human-readable status name to the caller via `PrintExpeditionOrderResponse.Params["currentStatusName"]`:

1. `currentStatusId == _options.Value.DesiredStateId` → hardcoded `"Balí se"` (the bug).
2. `NonPrintableStates.TryGetValue(currentStatusId, out var stateName)` → looked up from a static `IReadOnlyDictionary<int, string>` keyed by state ID (correct pattern).

`currentStatusName` is not just internal bookkeeping — it is interpolated directly into the user-facing frontend error message via the `ExpeditionOrderInvalidState` i18n template: `"Zakázku nelze vytisknout – je ve stavu {currentStatusName}"` (`frontend/src/i18n.ts:226`). If `DesiredStateId` is ever reconfigured away from 26, the frontend would show a wrong/stale status name to the user while still correctly blocking the print action based on the ID comparison.

`PrintPickingListOptions` (`backend/src/Anela.Heblo.Application/Features/ExpeditionList/PrintPickingListOptions.cs`) already carries `DesiredStateId` as a bound configuration option (`ConfigurationKey = "ExpeditionList"`, resolved via `IOptions<PrintPickingListOptions>`) with a default of `26` — matching `appsettings.json:540` (`"DesiredStateId": 26, // Bali se`) and `ExpeditionPickingRequest.DefaultDesiredStateId`.

An existing test, `PrintExpeditionOrderHandlerTests.Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26`, currently asserts the old buggy behavior on purpose (it exists to lock in that the *ID comparison* correctly uses the configured value, not that the *name* does) — it asserts `currentStatusName` is still `"Balí se"` even when `DesiredStateId = 99`. This assertion must be updated as part of this fix (see FR-2).

## Functional Requirements

### FR-1: Add `DesiredStateName` to `PrintPickingListOptions`
Add a new string property `DesiredStateName` to `PrintPickingListOptions`, defaulting to `"Balí se"` to preserve current behavior when not overridden.

**Acceptance criteria:**
- `PrintPickingListOptions` exposes `public string DesiredStateName { get; set; } = "Balí se";`.
- No existing configuration consumer breaks — the property is additive.
- `appsettings.json` under the `"ExpeditionList"` section gets an explicit `"DesiredStateName": "Balí se"` entry alongside the existing `"DesiredStateId": 26` for discoverability and symmetry (not strictly required for correct runtime behavior given the C# default, but keeps the two values visibly paired for whoever edits the Key Vault / appsettings override later).

### FR-2: Use the configured name in `PrintExpeditionOrderHandler`
Replace the hardcoded `"Balí se"` literal in the desired-state branch with `_options.Value.DesiredStateName`.

**Acceptance criteria:**
- `PrintExpeditionOrderHandler.cs:64` reads `{ "currentStatusName", _options.Value.DesiredStateName }` instead of the literal.
- `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` is updated: when constructed with a non-default `DesiredStateId = 99` in the test, the options should also set a matching non-default `DesiredStateName` (e.g. `"Custom State"`) and the assertion changes from `.Should().Be("Balí se")` to expect that configured name — proving the name, like the ID, now tracks configuration rather than being hardcoded.
- The existing `[InlineData(26, "Balí se")]` case in `Handle_OrderInNonPrintableState_ReturnsInvalidStateError` continues to pass unchanged (default options still yield `"Balí se"` for ID 26).
- All other existing tests in `PrintExpeditionOrderHandlerTests` continue to pass unmodified.

## Non-Functional Requirements

### NFR-1: Backward compatibility
Default behavior (both `DesiredStateId = 26` and the displayed name `"Balí se"`) must be unchanged for any deployment that does not explicitly override the new `DesiredStateName` setting. This is a pure internal correctness fix, not a behavior change for current production configuration.

### NFR-2: No new external dependencies or migrations
This is a same-assembly, compile-time change to a POCO options class and one handler. No database, API contract, or infrastructure changes are required.

## Data Model
No data model changes. `PrintPickingListOptions` gains one new plain string property (`DesiredStateName`), bound the same way as its siblings via the .NET Options pattern (`IOptions<PrintPickingListOptions>`, configuration section key `"ExpeditionList"`).

## API / Interface Design
No public API contract changes. `PrintExpeditionOrderResponse` already carries `Params["currentStatusName"]` as a free-form string keyed dictionary entry (`Dictionary<string, string>`); this fix only changes which string value is placed there for the desired-state branch. The frontend's `ExpeditionOrderInvalidState` i18n interpolation and `PrintOrderModal`/`ExpeditionListArchivePage` consumers require no changes.

## Dependencies
- `Microsoft.Extensions.Options` (already in use) for binding the new property.
- No changes needed to `NonPrintableStates` — this fix is scoped to the desired-state branch only, per the issue's suggested smallest fix. (Optionally, a future cleanup could merge `NonPrintableStates` and the desired-state case into one ID→name map, but that is a larger refactor than this issue's scope — see Out of Scope.)

## Out of Scope
- Merging `NonPrintableStates` and the desired-state ID/name pair into a single unified dictionary structure (the issue explicitly calls out the smaller, additive fix as sufficient).
- Any change to how `DesiredStateId` itself is resolved, validated, or overridden in Key Vault/appsettings — only the display-name pairing is addressed.
- Any frontend changes — the frontend already correctly interpolates whatever `currentStatusName` the backend sends.
- Auditing other hardcoded Shoptet state-name literals outside `PrintExpeditionOrderHandler` (e.g. in `Packaging`, `ShoptetOrders`, dashboard tiles) — out of scope for this issue, which is specifically about `PrintExpeditionOrderHandler`'s desired-state branch.

## Open Questions

None.

## Status: COMPLETE
