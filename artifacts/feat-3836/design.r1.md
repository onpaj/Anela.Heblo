# Design: Resolve `ReplayedBy` Identity Inside `ReplayWebhookEventHandler`

## Component Design

No new components are introduced. This is a mechanical relocation of identity resolution from the controller into the existing MediatR handler, bringing `ReplayWebhookEventHandler` in line with ADR-005 and the 60+ other handlers that already follow this pattern (e.g. `CreateAdjustmentHandler`).

- **`SmartsuppWebhookAuditController.Replay`** (`backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs`)
  Loses its `User.Identity?.Name` read and the `ReplayedBy` assignment. Its only responsibility for this action becomes routing the `id` into a `ReplayWebhookEventRequest` and returning `HandleResponse(response)`:
  ```csharp
  var response = await _mediator.Send(new ReplayWebhookEventRequest { Id = id }, cancellationToken);
  return HandleResponse(response);
  ```

- **`ReplayWebhookEventRequest`** (`.../Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventRequest.cs`)
  Drops the `ReplayedBy` property entirely (not just stops populating it — the property is deleted so it can't be reintroduced/misused). Retains only `Id`.

- **`ReplayWebhookEventHandler`** (`.../Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs`)
  Gains a third constructor dependency, `ICurrentUserService` (`Anela.Heblo.Domain.Features.Users`), alongside the existing `ApplicationDbContext` and `IMediator`. Inside `Handle`, identity is resolved internally and used to stamp the audit field:
  ```csharp
  entry.LastReplayedBy = _currentUserService.GetCurrentUser().Name ?? "unknown";
  ```
  replacing the previous `entry.LastReplayedBy = request.ReplayedBy;`. `ICurrentUserService` is already registered in DI via `UsersModule.cs` — no new module wiring is required. All other handler behavior (not-found / malformed-JSON error paths, `entry.ReplayCount += 1`, `entry.LastReplayedAt`, and the downstream `ProcessWebhookEventRequest` dispatch) is unchanged.

- **`ReplayWebhookEventHandlerTests`** (`backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs`)
  Updated to construct the handler with a `Mock<ICurrentUserService>` stubbing `GetCurrentUser()` to return a `CurrentUser(Id, Name, Email, IsAuthenticated)`, and to drop `ReplayedBy` from all `ReplayWebhookEventRequest` construction sites. Assertions on `entry.LastReplayedBy` now check against the mock's `Name` value instead of a request field.

No changes to module boundaries, DI registration, authorization (`[FeatureAuthorize(Feature.Admin_Administration, AccessLevel.Write)]` is untouched), or the `ProcessWebhookEventRequest` dispatch this handler triggers.

## Data Schemas

**`ReplayWebhookEventRequest` (after fix)** — internal MediatR request, not a public request body (route carries only `{id}` in the path):
```csharp
public class ReplayWebhookEventRequest : IRequest<ReplayWebhookEventResponse>
{
    public Guid Id { get; set; }
}
```
`ReplayedBy` is removed; no other properties change.

**`ReplayWebhookEventResponse`** — unchanged; response shape and status codes for `POST /api/admin/smartsupp/webhooks/{id}/replay` are not affected by this fix.

**`SmartsuppWebhookAuditEntry.LastReplayedBy`** — unchanged column, still a `string`. Only the *source* of the value changes: previously `request.ReplayedBy` (controller-resolved via `User.Identity?.Name`), now `_currentUserService.GetCurrentUser().Name ?? "unknown"` (handler-resolved). No migration required.

**`ICurrentUserService.GetCurrentUser()` → `CurrentUser`** (existing, unchanged, consumed for the first time by this handler):
```csharp
public record CurrentUser(string? Id, string? Name, string? Email, bool IsAuthenticated);
```
