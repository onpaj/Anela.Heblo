# Specification: Resolve `ReplayedBy` Identity Inside `ReplayWebhookEventHandler`

## Summary
`SmartsuppWebhookAuditController.Replay` currently resolves the acting user's identity itself (`User.Identity?.Name`) and stamps it onto the `ReplayWebhookEventRequest` DTO as a client/controller-settable `ReplayedBy` field, which the handler then writes verbatim to `SmartsuppWebhookAuditEntry.LastReplayedBy`. This violates ADR-005 (User Identity Resolution), which requires all identity resolution to happen inside the MediatR handler via `ICurrentUserService`. This fix removes `ReplayedBy` from the request DTO and moves identity resolution into `ReplayWebhookEventHandler`, matching the pattern already used by 60+ other handlers.

## Background
ADR-005 (`docs/architecture/development_guidelines.md:288`) establishes a single canonical path for obtaining the current user: injected `ICurrentUserService` resolved inside the MediatR handler. Controllers must never resolve identity themselves (no `GetCurrentUserId()` helper, no direct `IHttpContextAccessor`), and request DTOs must carry no client-settable `UserId`/`ModifiedBy`-style fields, because such fields bypass `CurrentUserService`'s documented claim-priority chain and can drift from — or be spoofed relative to — the identity used everywhere else in the app.

`SmartsuppWebhookAuditController.Replay` (`backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs:67`) resolves `User.Identity?.Name` in the controller and passes it through `ReplayWebhookEventRequest.ReplayedBy` to the handler, which stamps it onto `entry.LastReplayedBy`. This is exactly the pattern ADR-005 was written to eliminate (precedent: issue #3521 / InvoiceClassification). The established replacement pattern is visible in `CreateAdjustmentHandler` (`backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/UseCases/CreateAdjustment/CreateAdjustmentHandler.cs`), which injects `ICurrentUserService` and resolves `_currentUserService.GetCurrentUser().Name ?? "unknown"` inside the handler.

## Functional Requirements

### FR-1: Resolve `ReplayedBy` inside `ReplayWebhookEventHandler` via `ICurrentUserService`
Remove the `ReplayedBy` property from `ReplayWebhookEventRequest`. Remove the `User.Identity?.Name` resolution and the `ReplayedBy = replayedBy` assignment from `SmartsuppWebhookAuditController.Replay`, so the controller sends only `new ReplayWebhookEventRequest { Id = id }`. Inject `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`) into `ReplayWebhookEventHandler`'s constructor alongside the existing `ApplicationDbContext` and `IMediator` dependencies, and use it inside `Handle` to resolve the acting user's name (matching the existing codebase convention `_currentUserService.GetCurrentUser().Name ?? "unknown"`) when setting `entry.LastReplayedBy`, instead of reading it from `request.ReplayedBy`.

**Acceptance criteria:**
- `ReplayWebhookEventRequest` no longer declares a `ReplayedBy` (or any other client-settable identity) property.
- `SmartsuppWebhookAuditController.Replay` no longer references `User.Identity` and sends `ReplayWebhookEventRequest` with only `Id` set.
- `ReplayWebhookEventHandler` takes `ICurrentUserService` as a constructor dependency and uses it to populate `entry.LastReplayedBy` in `Handle`.
- `entry.LastReplayedBy` is set from the value returned by `ICurrentUserService.GetCurrentUser().Name` (falling back to `"unknown"` when null/empty), not from any request field.
- Existing behavior for replay counting (`entry.ReplayCount += 1`), `entry.LastReplayedAt`, the not-found (`ErrorCodes.ResourceNotFound`) and malformed-JSON (`ErrorCodes.InvalidOperation`) error paths, and the downstream `ProcessWebhookEventRequest` dispatch are unchanged.
- `ReplayWebhookEventHandlerTests` (`backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs`) is updated to construct `ReplayWebhookEventHandler` with a mocked/stubbed `ICurrentUserService` instead of passing `ReplayedBy` on the request, and to assert `entry.LastReplayedBy` against the value the mock returns.
- `dotnet build` and `dotnet format` succeed; all tests in the touched test file (and any other tests referencing `ReplayWebhookEventRequest.ReplayedBy`) pass.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. `ICurrentUserService` is already resolved per-request elsewhere in the application via standard DI; adding it to this handler introduces no new I/O or measurable latency.

### NFR-2: Security
Closes an identity-integrity gap: `LastReplayedBy` will now always reflect the same claim-priority-resolved identity as every other audit field in the application, rather than a raw `User.Identity?.Name` read that could diverge from `CurrentUserService`'s resolution logic. The endpoint's existing `[FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]` authorization is unchanged.

## Data Model
No schema changes. `SmartsuppWebhookAuditEntry.LastReplayedBy` (existing column) continues to store the acting user's display name as a string; only the source of that value changes (handler-resolved via `ICurrentUserService` instead of controller-resolved via `User.Identity`).

## API / Interface Design
`POST /api/admin/smartsupp/webhooks/{id}/replay` — route, method, response shape (`ReplayWebhookEventResponse`), and status codes are unchanged. This is an internal contract change only: `ReplayWebhookEventRequest` loses its `ReplayedBy` property, and `ReplayWebhookEventHandler`'s constructor gains an `ICurrentUserService` dependency (resolved automatically via existing DI registration in `UsersModule.cs` — no new module wiring required).

## Dependencies
- `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`), already registered in DI via `UsersModule.cs` and used by 60+ existing handlers — no new registration needed.

## Out of Scope
- Any change to the GUID-based audit-field convention (`Guid.TryParse(user.Id, out var id) ? id : null`) described in ADR-005 — `LastReplayedBy` is a display-name string field, not a GUID field, and this fix does not change its type.
- Any change to `ProcessWebhookEventRequest`, `ListWebhookAuditRequest`, `GetWebhookAuditEntryRequest`, or any other Smartsupp webhook audit endpoint.
- Any change to authorization/authentication on `SmartsuppWebhookAuditController`.
- Any broader repository-wide sweep for other ADR-005 violations beyond this controller/handler pair (issue #3836 scopes this fix to the Smartsupp replay endpoint only).

## Open Questions
None — the brief's evidence, the current source (`SmartsuppWebhookAuditController.cs`, `ReplayWebhookEventRequest.cs`, `ReplayWebhookEventHandler.cs`), ADR-005's exact rule text, and an existing correct-pattern handler (`CreateAdjustmentHandler`) together fully determine the required change.

## Status: COMPLETE
