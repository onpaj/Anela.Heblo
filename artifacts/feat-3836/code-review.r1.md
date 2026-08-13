## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

### Notes
Reviewed the full feature diff (`SmartsuppWebhookAuditController.cs`, `ReplayWebhookEventRequest.cs`, `ReplayWebhookEventHandler.cs`, `ReplayWebhookEventHandlerTests.cs`) against `spec.r1.md` (FR-1) and ADR-005.

- `SmartsuppWebhookAuditController.Replay` no longer touches `User.Identity`; it sends `new ReplayWebhookEventRequest { Id = id }` only — matches spec.
- `ReplayWebhookEventRequest` no longer declares `ReplayedBy` — matches spec.
- `ReplayWebhookEventHandler` now takes `ICurrentUserService` via constructor injection and sets `entry.LastReplayedBy = _currentUserService.GetCurrentUser().Name ?? "unknown"`, mirroring the established `CreateAdjustmentHandler` pattern verbatim. `ICurrentUserService`/`CurrentUser` shapes (`GetCurrentUser(): CurrentUser`, `Name` nullable) were checked directly and the call is well-typed.
- Unchanged and intact: `entry.ReplayCount += 1`, `entry.LastReplayedAt`, the `ResourceNotFound`/`InvalidOperation` error paths, and the `ProcessWebhookEventRequest` dispatch — no regressions introduced in the surrounding logic.
- `ReplayWebhookEventHandlerTests` now constructs the handler with a mocked `ICurrentUserService` (`CreateCurrentUserServiceMock`) instead of passing `ReplayedBy` on the request, and asserts `updated.LastReplayedBy.Should().Be("ondra@anela.cz")` against the mock's return value — matches the spec's testing acceptance criterion.
- `dotnet build` on the full solution succeeds with 0 errors (252 pre-existing warnings, none introduced by this change, none in the touched files).

No client-settable identity field remains on the request DTO, and identity resolution now happens exclusively inside the handler via `ICurrentUserService`, closing the ADR-005 violation this feature targeted. No correctness bugs found; nothing advisory-worthy either — the diff is a minimal, faithful, in-pattern fix.
