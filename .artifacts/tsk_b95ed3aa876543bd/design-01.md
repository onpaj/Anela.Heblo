# Design: SmartsuppRepository.UpsertContactAsync DateTime Kind=Unspecified crash — residual hardening, data recovery, issue reconciliation

Backend-only, no UI. This covers FR-1 through FR-4 from `plan-01.md`.

## Component design

### 1. `SmartsuppPayloadMapper` — new `AsUtc` helper, applied to `SyncedAt`

File: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs`

Add one public static helper, next to `ReadUtc`/`ReadOptionalUtc`:

```csharp
// Relabels a non-Utc DateTime as Utc without shifting the wall-clock value.
// Callers must guarantee the value is already semantically UTC (true for every
// DateTime that reaches this mapper — see call sites below); this is a *relabel*,
// not `.ToUniversalTime()`'s local-offset shift, which would corrupt Unspecified
// values that are already UTC-valued (e.g. read back from a
// `timestamp without time zone` column via EF).
public static DateTime AsUtc(DateTime dt) =>
    dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
```

Why not reuse `ReadUtc`'s `.ToUniversalTime()` pattern: `ReadUtc`/`ReadOptionalUtc` only ever see values freshly parsed from a JSON `Z`-suffixed string, where `System.Text.Json.JsonElement.GetDateTime()` already returns `Kind=Utc` — `.ToUniversalTime()` there is a no-op. `AsUtc`'s actual input (`syncedAt`, see below) is not JSON-parsed; on the replay path it is an `Unspecified`-kind value read back from a `timestamp without time zone` EF column that is already UTC-valued. Calling `.ToUniversalTime()` on that would treat it as local time and shift it — the exact "shift instead of relabel" mistake the existing gotcha doc (`memory/gotchas/smartsupp-staged-contact-datetime-kind.md`) warns against. `AsUtc` is a pure relabel, matching how `SmartsuppRepository.MapContactDataToEntity` already fixed the analogous bug (`DateTime.SpecifyKind(data.CreatedAt, DateTimeKind.Utc)`).

Apply at the two existing unguarded assignment sites:

```csharp
// MapContact  (line 113): SyncedAt = syncedAt,          -> SyncedAt = AsUtc(syncedAt),
// MapConversation (line 58): SyncedAt = syncedAt,       -> SyncedAt = AsUtc(syncedAt),
```

No signature change — both methods keep taking `DateTime syncedAt`; only the assignment changes. Every other field in these two methods already goes through `ReadUtc`/`ReadOptionalUtc` and is untouched.

### 2. `ConversationClosedReaction` / `ConversationClosedByContactReaction` — guard `LastClosedAt`

Files:
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedReaction.cs`
- `.../Reactions/ConversationClosedByContactReaction.cs`

Both currently do `conversation.LastClosedAt = ctx.Timestamp;` right after calling `MapConversation`, which already normalizes every other timestamp on the same object but not this later, direct overwrite. Change both to:

```csharp
conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
```

Both files already `using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;` for `SmartsuppPayloadMapper.MapConversation`/`TryGetString`, so no new `using` is needed.

### 3. Call-path safety argument (why this closes the gap, not just moves it)

`ctx.Timestamp` (`WebhookEventContext.Timestamp`) originates from `ProcessWebhookEventRequest.Timestamp`, which has exactly two producers:

| Producer | Value | Kind before fix |
|---|---|---|
| `SmartsuppWebhookController.Receive` (live webhook) | `TryGetUtc(...) ?? DateTime.UtcNow` | `Utc` — already safe |
| `ReplayWebhookEventHandler.Handle` (audit replay) | `entry.EventTimestamp ?? DateTime.UtcNow` | `Unspecified` — `EventTimestamp` is `timestamp without time zone`; EF/Npgsql reads it back as `Unspecified` even though the value was originally stamped `Utc` when audited |

`AsUtc` makes both producers converge on `Kind=Utc` at the point of use (`SyncedAt`, `LastClosedAt`), so the replay path becomes safe without touching `WebhookEventContext` or `ReplayWebhookEventHandler` themselves — the fix sits at the same layer (`SmartsuppPayloadMapper`) as the rest of the existing DateTime-safety pattern, keeping the change surgical.

### 4. Tests

**`SmartsuppPayloadMapperTests.cs`** (`backend/test/Anela.Heblo.Tests/Features/Smartsupp/Mappers/`) — add to the existing `Theory`/`Fact` set, following its established style (`Parse(json)` helper, `FluentAssertions`):

- `MapContact_UnspecifiedSyncedAt_IsStampedUtc` — call `MapContact(el, DateTime.SpecifyKind(dt, DateTimeKind.Unspecified))`, assert `result.SyncedAt.Kind == DateTimeKind.Utc` and `result.SyncedAt == DateTime.SpecifyKind(dt, DateTimeKind.Utc)` (wall-clock preserved — mirrors `SmartsuppContactMappingTests`' assertion style).
- `MapConversation_UnspecifiedSyncedAt_IsStampedUtc` — same shape for `MapConversation`.
- (Optional, cheap) `MapContact_UtcSyncedAt_PassesThroughUnchanged` — confirms `AsUtc` is a no-op / idempotent on the already-working live path, protecting NFR "no behavior change on live path".

**`ConversationReactionsTests.cs`** (`backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/`) — extend the existing `ConversationClosedReaction_UpsertsConversationWithCloseType` / `ConversationClosedByContactReaction_UpsertsConversation_WithContactCloseType` tests (or add siblings) with `ctx.Timestamp` built as `DateTimeKind.Unspecified`, and assert (via the existing `_repo.Verify(... c => c.LastClosedAt...)` pattern already present) that the conversation passed to `UpsertConversationAsync` has `LastClosedAt.Value.Kind == DateTimeKind.Utc`.

**New: `ReplayWebhookEventHandlerTests.cs`** (already exists at `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/`) — add a scenario that models the real replay failure mode end-to-end through the mediator pipeline: seed a `SmartsuppWebhookAuditEntry` with `EventTimestamp` set via `DateTime.SpecifyKind(dt, DateTimeKind.Unspecified)` (simulating the `timestamp without time zone` round-trip) and a `RawBody` containing a `contact.created` (or `conversation.closed`) payload, call `Handle`, and assert it completes without throwing `ArgumentException`. This is the regression guard for the residual gap itself — the other two test additions guard the unit-level fix, this one guards the actual reported failure mode (a replay crashing).

No changes to `SmartsuppContactMappingTests.cs` — that file already fully covers the #3622 fix and is out of scope here.

### 5. Data recovery (FR-3) — reuse existing endpoints, no new component

No new code is required for recovery; two existing admin endpoints (`SmartsuppWebhookAuditController`, `Feature.Admin_Administration` write-gated) are sufficient, matching the procedure already documented in the gotcha doc:

**Step A — enumerate candidates.**
`GET /api/admin/smartsupp/webhooks?processingStatus=HandlerException&from=2026-07-07T00:00:00Z&to=2026-07-13T09:16:00Z&take=200` (`ListWebhookAuditHandler`, `MaxTake=200`), paginated via `skip` until `Total` is exhausted (~262 expected entries per the telemetry filing → 2 pages). The summary DTO (`WebhookAuditSummaryDto`) does **not** carry `ProcessingError`, so:

**Step B — confirm the match, per candidate.**
`GET /api/admin/smartsupp/webhooks/{id}` (`GetWebhookAuditEntryRequest` → includes `ProcessingError`) — filter to entries whose `ProcessingError` contains `"Cannot write DateTime with Kind=Unspecified"` (rules out any unrelated `HandlerException` rows that happen to fall in the same status/window).

**Step C — replay.**
`POST /api/admin/smartsupp/webhooks/{id}/replay` for each confirmed entry (`ReplayWebhookEventHandler`, already increments `ReplayCount`/`LastReplayedAt`/`LastReplayedBy`). Must run **after** FR-1 is deployed — replaying a `contact.*` or `conversation.closed*` event before the fix lands hits the exact residual gap this design closes and fails identically.

A standalone tool already exists at `backend/tools/SmartsuppWebhookReplay` (`GET /api/audit`, `POST /api/audit/{id}/forward`) that offers the same filtering + one-by-one replay via a small web UI, but it re-POSTs the raw webhook body to an externally reachable target URL and needs its own direct DB connection string (`ConnectionStrings:ReplaySource` in local secrets) — appropriate for local/dev debugging against a copy of the data, not for driving production recovery. **Recommendation: use the in-app admin endpoints (Steps A–C) for production recovery**, not this tool, since they're already authenticated via the app's own auth and require no separate deployment or prod connection string on a dev machine.

**Step D — spot-check.**
`GET /api/admin/smartsupp/conversations?status=...` (existing `ListConversationsAsync`) or `ListOrphanContactConversationIdsAsync` to confirm previously-orphaned Messenger conversations now carry `ContactName`/`ContactEmail`.

### 6. GitHub issue reconciliation (FR-4) — no code component

Pure `gh` CLI activity (per repo convention, no MCP GitHub tools): comment on/close this filing and #3443 pointing at #3622 (actual fix, merged 2026-07-13) plus the new FR-1 hardening commit for the residual replay-path gap; correct the record that PR #3248 was not the regression source. No design surface beyond the comment content itself.

## Data schemas

No database schema or migration changes — `SmartsuppContact`, `SmartsuppConversation`, `SmartsuppWebhookAuditEntry` column shapes are untouched; this is a pure C#-layer DateTime-Kind fix.

No request/response contract changes — `MapContact`/`MapConversation` keep their existing signatures (`(JsonElement, DateTime) -> SmartsuppContact/SmartsuppConversation`); `AsUtc(DateTime) -> DateTime` is a new **internal** helper, not exposed on any API surface. FR-3 reuses existing endpoint contracts unchanged:

- `ListWebhookAuditRequest` / `ListWebhookAuditResponse` (`Items: WebhookAuditSummaryDto[]`, `Total`, `Skip`, `PageSize`) — used as-is with `processingStatus`, `from`, `to` query params.
- `GetWebhookAuditEntryRequest` / response (includes `ProcessingError: string?`) — used as-is to confirm the match text.
- `ReplayWebhookEventRequest { Id: Guid, ReplayedBy: string }` / `ReplayWebhookEventResponse { ReplayCount: int, LastReplayedAt: DateTime? }` — used as-is.

No event payload changes — the Smartsupp webhook JSON shape and `ProcessWebhookEventRequest` are unaffected; only how an already-received timestamp is normalized before persistence changes.
