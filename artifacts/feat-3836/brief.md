## Evidence

`backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookAuditController.cs:67`:

```csharp
var replayedBy = User.Identity?.Name ?? "unknown";
var response = await _mediator.Send(new ReplayWebhookEventRequest { Id = id, ReplayedBy = replayedBy }, cancellationToken);
```

The request DTO carries a server-resolved identity field (`ReplayWebhookEventRequest.ReplayedBy`, `.../ReplayWebhookEvent/ReplayWebhookEventRequest.cs:8`) which the handler writes to `entry.LastReplayedBy` (`ReplayWebhookEventHandler.cs:56`).

## Rule violated

**ADR-005 — User Identity Resolution** (`docs/architecture/development_guidelines.md:288`):

> "**Identity is resolved inside MediatR handlers** via injected `ICurrentUserService` — never in controllers, never via a controller helper, never via direct `IHttpContextAccessor`. Request DTOs carry no client-settable `UserId`/`ModifiedBy`."

Practices section: "Controllers never resolve identity — no `GetCurrentUserId()` helper … no stamping `UserId`/`ModifiedBy` onto requests." `ReplayedBy` is exactly such a "who did this" audit field. ADR-005 explicitly directs arch-review to treat controller-side identity resolution as a violation of an accepted decision (precedent: closed issue #3521, InvoiceClassification/ADR-005).

## Why it matters

`User.Identity?.Name` bypasses `CurrentUserService`'s documented claim-priority chain, so `LastReplayedBy` is derived from a different identity source than every other audit field in the application — re-introducing exactly the identity-resolution drift ADR-005 was written to eliminate.

## Suggested direction

Remove `ReplayedBy` from `ReplayWebhookEventRequest` and resolve the acting user inside `ReplayWebhookEventHandler` via injected `ICurrentUserService`, consistent with the 60+ other handlers that follow ADR-005.

<!-- harness-issue:tsk_b7c30a1020a14e79:1035dc3a -->

