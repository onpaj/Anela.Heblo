# Architecture assessment: SmartsuppRepository DateTime Kind=Unspecified — residual hardening (FR-1–FR-4)

Verdict: **approved with one correction to test placement (FR-2 / design §4, item 3)**. Everything else in `design-01.md` matches the current repo state exactly — I re-read every file it cites and confirm the code, signatures, column types, and endpoint contracts are as described. Ship FR-1 and the mapper/reaction-level tests as designed; redirect the "real replay" regression test to the Postgres-backed integration suite instead of `ReplayWebhookEventHandlerTests.cs`.

## What I verified against the live codebase

- `SmartsuppPayloadMapper.MapContact`/`MapConversation` (`backend/src/Anela.Heblo.Application/.../Mappers/SmartsuppPayloadMapper.cs`) — confirmed `SyncedAt = syncedAt` is a direct, unguarded assignment in both methods, while every other DateTime field goes through `ReadUtc`/`ReadOptionalUtc`. `ReadUtc`/`ReadOptionalUtc` are already `public static` — `AsUtc` slots in next to them cleanly.
- `ConversationClosedReaction`/`ConversationClosedByContactReaction` — confirmed both do `conversation.LastClosedAt = ctx.Timestamp;` as a raw overwrite after `MapConversation`, both already `using` the `Mappers` namespace (no new `using` needed).
- `WebhookEventContext.Timestamp` — confirmed its only two producers are `SmartsuppWebhookController.Receive` (always `Utc`) and `ReplayWebhookEventHandler.Handle`, which does `entry.EventTimestamp ?? DateTime.UtcNow`.
- `SmartsuppWebhookAuditEntryConfiguration.cs:23` — confirmed `EventTimestamp` is mapped `HasColumnType("timestamp without time zone")`, so EF/Npgsql round-trips it as `Kind=Unspecified` even though it was UTC when written. This is the actual mechanism behind the design's "replay path is unsafe" claim — correct.
- `SmartsuppRepository.UpsertContactAsync`/`UpsertConversationAsync` — confirmed both use `ExecuteSqlInterpolatedAsync` with bare `DateTime` parameters, and `MapContactDataToEntity` (the #3622 fix) already stamps `CreatedAt`/`UpdatedAt`/`SyncedAt`/`BannedAt` via `DateTime.SpecifyKind(..., DateTimeKind.Utc)` with a comment explicitly documenting the Npgsql `timestamp with time zone`-inference gotcha. `AsUtc`'s relabel-not-shift semantics match this existing fix exactly — good consistency.
- `SmartsuppContactConfiguration`/`SmartsuppConversationConfiguration` — confirmed all relevant columns (`CreatedAt`, `UpdatedAt`, `SyncedAt`, `BannedAt`, `LastClosedAt`) are `timestamp without time zone`, i.e. the design's "no schema changes needed" claim holds; the bug is purely in Npgsql's raw-SQL parameter-type inference, not the column definitions.
- `SmartsuppWebhookAuditController` + `ListWebhookAuditRequest`/`GetWebhookAuditEntryRequest`/`ReplayWebhookEventRequest` — confirmed routes, query params (`processingStatus`, `from`, `to`, `skip`, `take`), `MaxTake = 200`, and that `GetWebhookAuditEntryResponse.ProcessingError` exists. FR-3's two-step "list (no ProcessingError) → get (has ProcessingError) → replay" procedure is exactly right against the real DTOs; no new endpoint is needed.
- `RefreshOrphanContactsHandler.cs:59` — confirmed it independently does `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` into `SyncedAt` feeding `UpsertConversationAsync`. The plan correctly identifies this as a related-but-distinct latent instance of the same bug class and correctly scopes it out as a follow-up rather than folding it in.
- `memory/gotchas/smartsupp-staged-contact-datetime-kind.md` and `memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` — both exist, as the design assumes.

## Correction: FR-2's third test does not belong in `ReplayWebhookEventHandlerTests.cs`

Design §4 proposes adding, to `ReplayWebhookEventHandlerTests.cs`, "a scenario that models the real replay failure mode end-to-end through the mediator pipeline" — seed an audit entry with `Unspecified` `EventTimestamp`, call `Handle`, and assert no `ArgumentException` propagates through `ProcessWebhookEventHandler` → a reaction → the real `UpsertContactAsync`.

That file's existing tests (all three of them) construct `ApplicationDbContext` via `UseInMemoryDatabase` and pass a **mocked** `IMediator` into `ReplayWebhookEventHandler` — `Handle`'s `_mediator.Send(...)` never reaches a real `ProcessWebhookEventHandler` in any current test in this class. To make the proposed scenario real, the mock would need to be replaced with a live mediator wired to the actual reactions and a real `ISmartsuppRepository` — but `SmartsuppRepository.UpsertContactAsync`/`UpsertConversationAsync` call `ExecuteSqlInterpolatedAsync`, and this repo has *already hit and documented* the fact that raw SQL doesn't work against EF Core's InMemory provider. See `SmartsuppWebhookControllerTests.cs:311-320` (`SmartsuppWebhookFactory.ConfigureTestServices`):

> "Replace `ISmartsuppRepository` with a no-op stub: the InMemory provider rejects `ExecuteSqlInterpolatedAsync`. Controller tests verify HTTP/audit behavior; repository persistence is tested in `SmartsuppRepositoryUpsertIntegrationTests`."

That's the established convention in this codebase: **InMemory-DB tests never exercise the raw-SQL upsert; the `[Collection("PostgresIntegration")]` tests in `SmartsuppRepositoryUpsertIntegrationTests.cs` are the only place that proves Npgsql accepts the parameters.** Adding the proposed scenario to `ReplayWebhookEventHandlerTests.cs` as currently scoped would either (a) silently not exercise the actual bug (if `IMediator` stays mocked, nothing calls `UpsertContactAsync` at all — the test would pass regardless of whether FR-1 is applied, i.e. it wouldn't be a regression guard), or (b) fail to run at all against a live mediator + InMemory context (the raw SQL throws `InvalidOperationException`, not `ArgumentException`, so the intended assertion is wrong either way).

**Fix:** move this specific regression test into `SmartsuppRepositoryUpsertIntegrationTests.cs` (or a sibling class in the same `[Collection("PostgresIntegration")]`), not `ReplayWebhookEventHandlerTests.cs`. Concretely:

```csharp
[Fact]
public async Task UpsertContactAsync_ReplayedUnspecifiedSyncedAt_DoesNotThrow()
{
    // Simulates SmartsuppPayloadMapper.AsUtc's input on the replay path: an Unspecified-kind
    // timestamp, as EventTimestamp round-trips from a "timestamp without time zone" column.
    var unspecified = DateTime.SpecifyKind(new DateTime(2026, 7, 10, 12, 0, 0), DateTimeKind.Unspecified);
    var contactJson = /* minimal contact.* payload */;
    var contact = SmartsuppPayloadMapper.MapContact(Parse(contactJson), unspecified);

    var repo = CreateRepository();
    var act = () => repo.UpsertContactAsync(contact, CancellationToken.None);

    await act.Should().NotThrowAsync();
}
```

This proves the fix against real Npgsql/Postgres, at the correct layer, using infra that already exists in this repo — no new fixtures, no DI wiring for a full mediator pipeline. `ReplayWebhookEventHandlerTests.cs` should keep its current scope (audit-row bookkeeping: `ReplayCount`, `LastReplayedAt`, error codes) with `IMediator` mocked, exactly as it does today — that's the right boundary for that test class, and the design shouldn't blur it.

The other two FR-2 items — `SmartsuppPayloadMapperTests` additions and `ConversationReactionsTests` additions, both using mocked `ISmartsuppRepository`/`Moq` — are correctly scoped as-is; they only need to observe the `Kind` on the object passed to `UpsertConversationAsync`/`UpsertContactAsync`, never execute it, so InMemory-vs-Postgres is irrelevant there.

## Everything else: no changes needed

- **FR-1 fix location and shape** — correct. One `AsUtc` helper in `SmartsuppPayloadMapper`, applied at the two `SyncedAt` sites plus the two `LastClosedAt` sites. This is the minimal, surgical fix and matches the existing `MapContactDataToEntity` precedent exactly (relabel via `SpecifyKind`, not `.ToUniversalTime()`, which would shift already-UTC-valued `Unspecified` timestamps).
- **FR-3 data recovery** — reuses existing, already-deployed admin endpoints with correct request/response shapes; no new endpoint or bulk-replay tooling is needed given the confirmed volume (~262, well within a scripted loop over `List` → `Get` → `Replay`). Sequencing requirement (replay only after FR-1 deploys) is correctly called out in both plan and design — carry it into the implementation step as an explicit precondition, since replaying before the fix lands reproduces the exact crash on the replay path.
- **FR-4 issue reconciliation** — pure `gh` CLI, no design surface, consistent with this repo's "GitHub access via `gh` CLI only" rule.
- **No schema/migration, no DTO, no API contract changes** — confirmed; this stays entirely inside the mapper/reaction layer plus tests.
- **Scope boundary decisions** (deferring `RefreshOrphanContactsHandler`'s own latent `Unspecified` bug, and deferring the systemic `NpgsqlParameter`/`NpgsqlDbType` fix) — both correctly characterized against the code and reasonably deferred; recommend a follow-up issue for `RefreshOrphanContactsHandler.cs:59` since it's the same bug class on the same `UpsertConversationAsync` call site, but it must not block this fix.

## Prerequisites before implementation starts

1. None beyond the correction above — FR-1's fix has no dependencies and can be implemented immediately.
2. FR-3 (data recovery replay) must wait until FR-1 is merged and deployed; do not replay any `contact.*`/`conversation.closed*` audit entries before that, or the replay will reproduce the exact crash this task is fixing.
