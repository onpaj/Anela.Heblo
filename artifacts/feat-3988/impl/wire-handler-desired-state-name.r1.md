# Implementation: wire-handler-desired-state-name

## What was implemented
`PrintExpeditionOrderHandler` no longer hardcodes the literal `"Balí se"` when reporting the
desired-state-invalid error — it now reads the display name from the already-configured
`PrintPickingListOptions.DesiredStateName` option (added in the prior task,
`add-desired-state-name-option`), the same way it already reads `DesiredStateId` for the
status-id comparison. The existing regression test that exercises the non-default
`DesiredStateId` path was updated to also configure a non-default `DesiredStateName` and assert
against that configured value instead of the old hardcoded literal, so the test actually proves
the name — not just the id — comes from configuration.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — line 64: `{ "currentStatusName", "Balí se" }` → `{ "currentStatusName", _options.Value.DesiredStateName }`.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` now configures `DesiredStateName = "Custom State"` alongside the non-default `DesiredStateId = 99`, and asserts `result.Params!["currentStatusName"]` equals `"Custom State"` rather than `"Balí se"`.

## Tests
- `PrintExpeditionOrderHandlerTests.Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` — updated per Step 1 of the task; confirmed it failed against the pre-fix handler (asserting `"Custom State"` against the still-hardcoded `"Balí se"`) before the Step 2 fix was applied, then passed after.
- `PrintExpeditionOrderHandlerTests.Handle_OrderInNonPrintableState_ReturnsInvalidStateError` (`[InlineData(26, "Balí se")]` case) — unaffected: default `PrintPickingListOptions.DesiredStateName` is still `"Balí se"`, so default-configuration behavior is unchanged.
- Full `PrintExpeditionOrderHandlerTests` class: 9/9 passed.

## How to verify
```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PrintExpeditionOrderHandlerTests"
```
Expected: `Passed! - Failed: 0, Passed: 9, ...`

## Notes
- Full backend test run (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, all 6734 tests): 6625 passed, 105 failed, 4 skipped. Every failure is a pre-existing integration test requiring live infra this sandbox doesn't have (Postgres-backed repository integration tests under `Anela.Heblo.Tests.Persistence.*` / `Anela.Heblo.Tests.KnowledgeBase.Integration.*`, etc.) — none relate to `ExpeditionList`/`PrintExpeditionOrder`, and none reference the files touched here. Same pattern in the separate `Anela.Heblo.Adapters.Shoptet.Tests` project (13 failures, all `Missing Shoptet:StatusId:EXP in configuration` — live Shoptet integration tests needing secrets not present in this sandbox).
- `dotnet format Anela.Heblo.sln --verify-no-changes` (from repo root, since `backend/` has no solution file of its own) reports exactly one pre-existing, unrelated formatting violation in `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — a file this task never touches. Both files this task modified are clean. Left the unrelated file as-is per the "surgical changes" project rule rather than reformatting a file outside this task's scope.
- No separate `git commit` was made inside `backend/` (Step 7 of the task context) — the pipeline's orchestrator-level commit (`git add -A` at the worktree root) covers these source changes together with the review/impl artifacts, per `implement-orchestrator.md`.

## PR Summary
`PrintExpeditionOrderHandler` reported the desired-state error using a hardcoded `"Balí se"` string even though the corresponding status *id* was already configurable via `PrintPickingListOptions.DesiredStateId`. This task wires the handler to read the display name from `PrintPickingListOptions.DesiredStateName` (added in the preceding task) instead, so both halves of "desired state" — id and name — come from configuration together.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/PrintExpeditionOrder/PrintExpeditionOrderHandler.cs` — use `_options.Value.DesiredStateName` instead of the hardcoded `"Balí se"` literal.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/PrintExpeditionOrderHandlerTests.cs` — updated `Handle_NonDefaultDesiredStateId_ChecksConfiguredValueNotHardcoded26` to configure and assert against a non-default `DesiredStateName`.

## Status
DONE
