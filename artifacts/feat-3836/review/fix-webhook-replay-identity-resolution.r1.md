# Code Review: fix-webhook-replay-identity-resolution

## Summary
The implementation correctly relocates identity resolution for the Smartsupp webhook replay endpoint out of the controller and into `ReplayWebhookEventHandler`, using the injected `ICurrentUserService` per ADR-005. All four touched files match the task-context's exact specification, the request DTO no longer carries a client-settable identity field, and the updated test suite passes.

## Review Result: PASS

### task: fix-webhook-replay-identity-resolution
**Status:** PASS

Verified against acceptance criteria:
- `ReplayWebhookEventRequest` no longer declares `ReplayedBy` — confirmed; only `Id` remains.
- `SmartsuppWebhookAuditController.Replay` no longer references `User.Identity` and sends `new ReplayWebhookEventRequest { Id = id }` — confirmed.
- `ReplayWebhookEventHandler` takes `ICurrentUserService` as a constructor dependency and uses it to populate `entry.LastReplayedBy` in `Handle` — confirmed (`_currentUserService.GetCurrentUser().Name ?? "unknown"`).
- Existing behavior (`ReplayCount += 1`, `LastReplayedAt`, not-found/malformed-JSON error paths, `ProcessWebhookEventRequest` dispatch) is unchanged — confirmed by diff inspection; only the `LastReplayedBy` line's source changed.
- `ReplayWebhookEventHandlerTests` updated to mock `ICurrentUserService` and assert `LastReplayedBy` against the mocked name — confirmed; all 3 tests pass (`Passed! - Failed: 0, Passed: 3, Total: 3`).
- Grep confirms no stray `ReplayedBy` references remain outside `LastReplayedBy`.
- `dotnet build Anela.Heblo.sln` succeeds; `dotnet format --verify-no-changes` reports no findings in any of the four touched files (the only findings are pre-existing, in an untouched file).
- Full Smartsupp suite: 202 passed, 12 failed — all 12 failures are pre-existing Docker/Testcontainers-dependent integration tests unrelated to this change (no Docker available in this environment), not caused by this diff.

No functional requirement, architecture guideline, or acceptance criterion is unmet.

## Docs to Update
(None — this is an internal handler/controller refactor with no change to public API shape, route, response type, or status codes; no new concept or operational behavior was introduced.)

## Overall Notes
Clean, minimal, single-purpose fix. Matches the established `ICurrentUserService` injection pattern used elsewhere in the codebase (e.g. `CreateAdjustmentHandler`). No concerns.
