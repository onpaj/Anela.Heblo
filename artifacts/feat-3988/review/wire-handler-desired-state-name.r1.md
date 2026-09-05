# Code Review: wire-handler-desired-state-name

## Summary
The handler now reads `currentStatusName` from `_options.Value.DesiredStateName` instead of the
hardcoded `"Balí se"` literal, exactly as specified. The regression test was correctly updated to
configure and assert against a non-default `DesiredStateName`, which is what actually proves the
name — not just the id — is sourced from configuration. Both the targeted test class and the
default-configuration case (`[InlineData(26, "Balí se")]`) pass; the only test-suite failures are
pre-existing, unrelated integration tests requiring infrastructure (Postgres, Shoptet secrets)
this sandbox doesn't have.

## Review Result: PASS

### task: wire-handler-desired-state-name
**Status:** PASS

Verified against the task-context spec:
- Handler change (Step 2) matches exactly: `{ "currentStatusName", _options.Value.DesiredStateName }`.
- Test change (Step 1) matches exactly: `DesiredStateName = "Custom State"` configured, assertion changed to `.Be("Custom State")`.
- Full `PrintExpeditionOrderHandlerTests` class: 9/9 passed, including the unchanged
  `[InlineData(26, "Balí se")]` case in `Handle_OrderInNonPrintableState_ReturnsInvalidStateError`,
  confirming backward compatibility (NFR-1) since the default option value is still `"Balí se"`.
- `dotnet format --verify-no-changes` clean for both touched files; the one reported violation
  (`GetIssuedInvoiceDetailHandlerTests.cs`) is pre-existing and out of scope for this task.
- No new external dependencies or migrations introduced (NFR-2).
- Depends-on relationship with `add-desired-state-name-option` correctly assumed and consumed
  (`PrintPickingListOptions.DesiredStateName` already exists with default `"Balí se"`).

## Docs to Update
(none — this is an internal bugfix with no change to public behavior, CLI, or configuration surface beyond what the prior task already introduced)

## Overall Notes
The full backend suite (6734 tests) shows 105 unrelated failures, all in Postgres-backed
integration test classes (`*.Persistence.*`, `*.KnowledgeBase.Integration.*`) and a separate
Shoptet integration test project (13 failures, `Missing Shoptet:StatusId:EXP in configuration`).
None reference `ExpeditionList`, `PrintExpeditionOrder`, or the two files this task touched —
consistent with a sandbox lacking live database/Shoptet infrastructure rather than a regression
introduced by this change.
