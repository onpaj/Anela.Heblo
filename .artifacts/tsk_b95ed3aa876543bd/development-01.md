# Development: SmartsuppRepository DateTime Kind=Unspecified — residual hardening (FR-1, FR-2)

Implements FR-1 and FR-2 from `plan-01.md`/`design-01.md`, with the architecture-review correction
from `architecture-01.md` (real-Postgres regression test lives in the integration suite, not
`ReplayWebhookEventHandlerTests.cs`). FR-3 (data recovery) and FR-4 (issue reconciliation) are
operational follow-ups, not code — see "Not implemented here" below.

## What was implemented

### FR-1 — harden `SyncedAt`/`LastClosedAt` against non-Utc input

**`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs`**
- Added `public static DateTime AsUtc(DateTime dt)` next to `ReadUtc`/`ReadOptionalUtc` — a pure
  relabel (`DateTime.SpecifyKind`, not `.ToUniversalTime()`), matching the precedent set by the
  #3622 fix in `SmartsuppRepository.MapContactDataToEntity`.
- `MapContact`: `SyncedAt = syncedAt` → `SyncedAt = AsUtc(syncedAt)`.
- `MapConversation`: `SyncedAt = syncedAt` → `SyncedAt = AsUtc(syncedAt)`.

**`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedReaction.cs`**
and **`ConversationClosedByContactReaction.cs`**
- `conversation.LastClosedAt = ctx.Timestamp` → `conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp)`.

This closes the gap the architecture step identified: on the live webhook path `ctx.Timestamp` was
already always `Utc`, but on the audit-replay path (`ReplayWebhookEventHandler.Handle` →
`entry.EventTimestamp ?? DateTime.UtcNow`) it round-trips as `Unspecified` because
`SmartsuppWebhookAuditEntry.EventTimestamp` is `timestamp without time zone`. Replaying a
`contact.*` or `conversation.closed*` audit entry before this fix would hit the exact
`ArgumentException` this task investigates, at a different call site than the original signal.

### FR-2 — regression tests

**`backend/test/Anela.Heblo.Tests/Features/Smartsupp/Mappers/SmartsuppPayloadMapperTests.cs`**
- `MapConversation_UnspecifiedSyncedAt_IsStampedUtc`, `MapConversation_UtcSyncedAt_PassesThroughUnchanged`
- `MapContact_UnspecifiedSyncedAt_IsStampedUtc`, `MapContact_UtcSyncedAt_PassesThroughUnchanged`

**`backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`**
- `ConversationClosedReaction_UnspecifiedTimestamp_StampsLastClosedAtAsUtc`
- `ConversationClosedByContactReaction_UnspecifiedTimestamp_StampsLastClosedAtAsUtc`

**`backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs`**
(Postgres-backed, `[Collection("PostgresIntegration")]` — per the architecture correction, this is
the only place in the codebase that can actually exercise `ExecuteSqlInterpolatedAsync` against
real Npgsql; `ReplayWebhookEventHandlerTests.cs` uses a mocked `IMediator` and would never reach
the upsert)
- `UpsertContactAsync_ReplayedUnspecifiedSyncedAt_DoesNotThrow` — builds a contact via
  `SmartsuppPayloadMapper.MapContact` with an `Unspecified` `syncedAt` (simulating the replay path)
  and asserts `repo.UpsertContactAsync` does not throw.

### Incidental fix — pre-existing broken test fixtures in the same file

While running the full Smartsupp suite to validate the above, discovered that **6 tests already
failing before any of my changes** (verified via `git stash`/re-run against the unmodified
baseline — identical failure count and stack traces): `SmartsuppRepositoryUpsertIntegrationTests`'
`MakeContact`/`MakeConversation` helpers hand-built entities with `DateTimeKind.Unspecified`
timestamps and passed them straight into `UpsertContactAsync`/`UpsertConversationAsync`, hitting
the same `ArgumentException` this task is about — a test-fixture bug, not product code, and not
one of FR-1–FR-4 (it exercises upsert *ordering*/`COALESCE` semantics, not DateTime-Kind handling,
so no test intent was lost). Since it's the identical bug class, a one-line-per-field fix, and
touches no production code, I fixed it rather than leave 6 unrelated red tests in a file I'd
already modified: `DateTimeKind.Unspecified` → `DateTimeKind.Utc` in both helpers (3 fields each).
`DateTime.Equals` ignores `Kind` (compares ticks only), so the existing round-trip assertions
(`row.UpdatedAt.Should().Be(t1)`) are unaffected.

## Not implemented here (FR-3, FR-4 — operational, not code)

- **FR-3 (recover data dropped during the outage window)**: requires querying/replaying against
  the **production** database via the live admin endpoints (`GET/POST /api/admin/smartsupp/webhooks/...`)
  and must run *after* this fix is deployed — there's no code change to make; it's an operational
  runbook step for whoever deploys this fix, using the two admin endpoints and the plan documented
  in `design-01.md` §5.
- **FR-4 (GitHub issue reconciliation)**: closing/commenting on this filing and #3443 to point at
  this fix is a `gh` CLI action to take once this PR merges, not a development-step deliverable.

Both are unblocked by this change and ready to execute per `plan-01.md`'s rough-plan steps 4–6.

## Verification

```
export PATH="$HOME/.dotnet:$PATH"

# Build
dotnet build Anela.Heblo.sln -c Debug
# → 0 Errors

# Format check on touched files
dotnet format Anela.Heblo.sln --include \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedReaction.cs \
  backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedByContactReaction.cs \
  backend/test/Anela.Heblo.Tests/Features/Smartsupp/Mappers/SmartsuppPayloadMapperTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs \
  backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs \
  --verify-no-changes
# → exit 0, no output

# Full Smartsupp test suite (unit + Postgres-backed integration)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Smartsupp" --no-build -c Debug
# → Passed! Failed: 0, Passed: 210, Skipped: 0, Total: 210
```

Before the fix (confirmed by stashing the change and re-running against the unmodified baseline),
the same filter reported `Failed: 6` — the pre-existing fixture bug described above, unrelated to
the mapper/reaction change but fixed alongside it. After the fix: all 210 pass, 7 of them newly
added by this change (4 mapper tests, 2 reaction tests, 1 Postgres integration test).

## Files changed

- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Mappers/SmartsuppPayloadMapper.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedReaction.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedByContactReaction.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Mappers/SmartsuppPayloadMapperTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`
- `backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs`

No schema/migration, DTO, or API contract changes — matches the design's scope exactly.
