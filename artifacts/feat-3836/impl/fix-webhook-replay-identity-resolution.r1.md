# Implementation: fix-webhook-replay-identity-resolution

## What was implemented
Fixed the ADR-005 identity-resolution violation in the Smartsupp webhook replay endpoint. `SmartsuppWebhookAuditController.Replay` no longer resolves `User.Identity?.Name` itself; identity resolution now happens inside `ReplayWebhookEventHandler` via an injected `ICurrentUserService`, matching the established codebase pattern (e.g. `CreateAdjustmentHandler`).

## Files created/modified
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs` — removed `User.Identity?.Name` read and the `ReplayedBy` assignment; the controller now sends `new ReplayWebhookEventRequest { Id = id }`.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs` — removed the client-settable `ReplayedBy` property; the request now carries only `Id`.
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs` — injected `ICurrentUserService` via the constructor; `Handle` now sets `entry.LastReplayedBy = _currentUserService.GetCurrentUser().Name ?? "unknown"` instead of reading `request.ReplayedBy`. No other logic changed (replay count increment, `LastReplayedAt`, error paths, and the `ProcessWebhookEventRequest` dispatch are unchanged).
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs` — rewritten to construct `ReplayWebhookEventHandler` with a mocked `ICurrentUserService` (returning `CurrentUser(Id: "user-1", Name: ..., Email: "ondra@anela.cz", IsAuthenticated: true)`) instead of passing `ReplayedBy` on the request, and asserts `entry.LastReplayedBy` against the mock's returned name.

## Tests
- `ReplayWebhookEventHandlerTests.Handle_DispatchesProcessWebhookEvent_AndIncrementsReplayCount` — verifies replay dispatch, `ReplayCount` increment, and `LastReplayedBy` now sourced from the mocked `ICurrentUserService`.
- `ReplayWebhookEventHandlerTests.Handle_ReturnsResourceNotFound_WhenIdMissing` — unchanged behavior, updated constructor call.
- `ReplayWebhookEventHandlerTests.Handle_ReturnsInvalidOperation_WhenRawBodyIsMalformedJson` — unchanged behavior, updated constructor call.

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReplayWebhookEventHandlerTests"
# Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3

grep -rn "ReplayedBy" --include="*.cs" src/ test/ | grep -v "LastReplayedBy"
# (no output)

cd .. && dotnet build Anela.Heblo.sln
# Build succeeded
```

## Notes
- Full Smartsupp test suite (`--filter "FullyQualifiedName~Smartsupp"`) run: 202 passed, 12 failed. All 12 failures are pre-existing `Testcontainers.PostgreSql`-backed integration tests (`SmartsuppPresenceRepositoryIntegrationTests`, `SmartsuppRepositoryUpsertIntegrationTests`) that require Docker, which is unavailable in this sandbox — unrelated to this change and not touched by it.
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports pre-existing whitespace findings only in `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs`, a file untouched by this change. None of the four files modified here have formatting findings.
- `dotnet build` also emits a pre-existing `MSB3073` warning from the `Anela.Heblo.AccessMatrixGen` post-build code-gen tool (exit code 134) — unrelated to this change; build still succeeds overall.

## PR Summary
Moved identity resolution for the Smartsupp webhook replay endpoint out of the controller and into the MediatR handler, closing the ADR-005 violation flagged by the architecture review (issue #3836). `SmartsuppWebhookAuditController.Replay` previously read `User.Identity?.Name` directly and stamped it onto `ReplayWebhookEventRequest.ReplayedBy`; it now sends only `{ Id }`, and `ReplayWebhookEventHandler` resolves the acting user via an injected `ICurrentUserService` — the same pattern used by 60+ other handlers in this codebase.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs` — `Replay` action no longer touches `User.Identity`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs` — dropped `ReplayedBy` property
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs` — added `ICurrentUserService` dependency, resolves `LastReplayedBy` internally
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs` — updated to mock `ICurrentUserService` instead of passing `ReplayedBy`

## Status
DONE
