# Plan: SmartsuppRepository.UpsertContactAsync DateTime Kind=Unspecified crash (telemetry signal, #3443 lineage)

## Summary

The telemetry signal this task was filed against (262 occurrences, 2026-07-03 → 2026-07-10, escalating from 2026-07-07) is a **duplicate of an already-fixed bug**: PR #3622 / commit `51a4b58c` ("fix(smartsupp): stamp REST-staged contact timestamps as Utc so Messenger chats ingest") merged **2026-07-13** — three days after this alert fired — and directly patches the exact crash site (`SmartsuppRepository.MapContactDataToEntity`, called from `TryFetchAndStageContactAsync` at line 326, called from `UpsertContactAsync` at line 61). Root cause and fix are already confirmed in code, commit message, an added regression test (`SmartsuppContactMappingTests`), and a written gotcha (`memory/gotchas/smartsupp-staged-contact-datetime-kind.md`). The work remaining is **not** re-diagnosis or re-fixing this path — it's (1) closing a residual latent gap the fix didn't cover, (2) recovering data dropped during the outage window, and (3) reconciling the GitHub issue trail so groomers stop re-flagging a closed problem.

## Context

`ExecuteSqlInterpolatedAsync` (used by both `UpsertContactAsync` and `UpsertConversationAsync` in `SmartsuppRepository`) infers a bare `DateTime` parameter as PostgreSQL `timestamp with time zone`, independent of the actual column type (`timestamp without time zone` per `SmartsuppContactConfiguration`/`SmartsuppConversationConfiguration`). Npgsql accepts `Kind=Utc` for that inferred type but throws `ArgumentException` on `Kind=Unspecified`. Two contact-population paths feed `UpsertContactAsync`:

- **Webhook path** (`SmartsuppPayloadMapper.MapContact`, driven by `contact.*` webhook events) — always produced `Kind=Utc` (`ReadUtc`/`ReadOptionalUtc` call `.ToUniversalTime()`, which always returns `Kind=Utc`).
- **REST-staged path** (`SmartsuppRepository.MapContactDataToEntity`, driven by `UpsertConversationAsync` fetching an unseen `ContactId` via REST when a conversation event references it — the dominant path for Facebook Messenger, ~81% of events per the PR #3622 commit message) — stamped `Kind=Unspecified` until #3622, causing the entire conversation upsert to throw and the conversation to be silently dropped.

\#3443 was closed 2026-07-08 without a confirmed fix (its last comment, dated 2026-07-01, explicitly said root cause was unconfirmed). The crash rate then rose ~11× through 2026-07-10 (this filing) and continued until the #3622 fix landed 2026-07-13. This filing's own hypothesis — that PR #3248 (2026-06-22 atomic-upsert refactor) introduced a regression — is not what the evidence in the repo supports: `git log -p` shows `MapContactDataToEntity` stamped `DateTimeKind.Unspecified` as far back as commit `3b853803` (2026-06-2x, "fetch unknown contact via REST instead of dropping ContactId", i.e. when the REST-staged path was *introduced*), predating #3248. The bug was latent from the moment the REST-staged path existed; #3248 didn't cause it, and the volume swing is more plausibly explained by a change in incoming Messenger traffic mix, not a code regression on 2026-07-07. This correction matters for how the GitHub issues get reconciled (see FR-4) — nobody should keep hunting for a #3248-shaped cause.

## Investigation already completed (would otherwise be step 1 of this plan)

Read straight from the current repository state — no further "confirm root cause" work is needed:

- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs:336-352` (`MapContactDataToEntity`) already stamps `CreatedAt`/`UpdatedAt`/`SyncedAt`/`BannedAt` as `DateTimeKind.Utc` — this is the #3622 fix, present in the working tree.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactMappingTests.cs` already regression-guards it.
- `memory/gotchas/smartsupp-staged-contact-datetime-kind.md` already documents the incident, including a data-recovery instruction to replay dropped audit entries via `POST /api/admin/smartsupp/webhooks/{id}/replay`.

## Residual gap found during this pass (new, not previously flagged)

The #3622 fix hardened `MapContactDataToEntity` but **not** the sibling webhook-path mapper, `SmartsuppPayloadMapper.MapContact` (and `MapConversation`), which both do `SyncedAt = syncedAt` — a **direct, unguarded assignment** of the caller-supplied timestamp, unlike `CreatedAt`/`UpdatedAt`/`BannedAt` in the same methods, which go through `ReadUtc`/`ReadOptionalUtc` (safe, always `Kind=Utc`). Similarly, `ConversationClosedReaction`/`ConversationClosedByContactReaction` do `conversation.LastClosedAt = ctx.Timestamp` directly.

This is safe today only because `ctx.Timestamp` is always `Kind=Utc` on the **live webhook path** (`SmartsuppWebhookController.Receive` builds it via `TryGetUtc(...) ?? DateTime.UtcNow`, both `Kind=Utc`). It is **not** safe on the **replay path**: `ReplayWebhookEventHandler.Handle` sets `var timestamp = entry.EventTimestamp ?? DateTime.UtcNow` — and `SmartsuppWebhookAuditEntry.EventTimestamp` is mapped `HasColumnType("timestamp without time zone")`, which Npgsql/EF reads back as `Kind=Unspecified`. So replaying any audited `contact.*` event (or a `conversation.closed*` event) will re-throw the identical `ArgumentException` this whole investigation is about — via a different call path, so it wouldn't show up in this specific telemetry signal, but it would sabotage exactly the recovery procedure the existing gotcha doc recommends for the outage window.

## Functional requirements

**FR-1 — Harden `SmartsuppPayloadMapper.MapContact` and `MapConversation` `SyncedAt`, and the two `LastClosedAt` assignments, to always produce `Kind=Utc`.**
Acceptance criteria:
- `MapContact(data, syncedAt)` returns `SyncedAt.Kind == DateTimeKind.Utc` for any input `syncedAt` Kind (`Utc`, `Unspecified`, or `Local`), preserving wall-clock value (relabel/convert, not shift, matching the existing `MapContactDataToEntity` test's assertion style).
- Same for `MapConversation(data, syncedAt)`.
- `ConversationClosedReaction`/`ConversationClosedByContactReaction` stamp `conversation.LastClosedAt` as `Kind=Utc` regardless of `ctx.Timestamp`'s incoming Kind.
- A single shared helper (e.g. a `private static DateTime AsUtc(DateTime dt)` in `SmartsuppPayloadMapper`, reused by the two reactions) is preferred over four separate inline fixes — keeps the pattern consistent with how `ReadUtc`/`ReadOptionalUtc` already centralize this for the other fields.

**FR-2 — Regression tests proving the replay path no longer crashes.**
Acceptance criteria:
- New tests mirroring `SmartsuppContactMappingTests` that call `MapContact`/`MapConversation` with an `Unspecified`-kind `syncedAt` and assert `Kind=Utc` on the output (and wall-clock preservation).
- At least one test exercising the realistic replay scenario end-to-end-ish: an `Unspecified`-kind timestamp (as would come back from reading `EventTimestamp` off a `timestamp without time zone` column) flowing through `ReplayWebhookEventHandler` → `ProcessWebhookEventHandler` → a `contact.created` reaction → `UpsertContactAsync`, without throwing. (Existing test infra for `SmartsuppRepositoryUpsertIntegrationTests` / `SmartsuppRepositoryUpdatedAtGuardTests` shows the pattern for hitting the real Postgres-backed upsert in tests — reuse that.)

**FR-3 — Recover data dropped during the outage window.**
Acceptance criteria:
- Identify `SmartsuppWebhookAuditEntries` rows with `ProcessingStatus = HandlerException` and `ReceivedAt` between 2026-07-07 00:00 and 2026-07-13 09:16 (the #3622 deploy time) whose `ProcessingError` matches this exception (`Cannot write DateTime with Kind=Unspecified...UpsertContactAsync`/`UpsertConversationAsync`).
- Each matching entry is replayed via the existing `POST /api/admin/smartsupp/webhooks/{id}/replay` endpoint (or a small bulk-replay-by-filter addition if the count makes one-by-one impractical — see Open Questions) **after** FR-1 is deployed, since replaying a `contact.*` event before FR-1 lands would hit the exact residual gap described above and fail again.
- After replay, spot-check that previously-orphaned Messenger conversations (`ContactName`/`ContactEmail` null, `channel.type` Facebook-ish) now have contact data populated — `ListOrphanContactConversationIdsAsync` / the existing `RefreshOrphanContacts` admin action is a reasonable secondary sweep for anything replay doesn't catch (e.g. entries whose replay itself still errors for unrelated reasons).

**FR-4 — Reconcile the GitHub issue trail.**
Acceptance criteria:
- Comment on/close this new filing pointing at #3622 as the actual fix (merged 2026-07-13, i.e. after this alert fired but before this task started), with a corrected root-cause note: the REST-staged contact path (`MapContactDataToEntity`), not a PR #3248 regression.
- Update/comment on #3443 linking it forward to #3622 so the "closed without confirmed fix" record is corrected rather than left dangling.
- Confirm via telemetry that this exact signal (`ArgumentException@SmartsuppRepository.UpsertContactAsync`) has zero occurrences after 2026-07-13 — if any post-fix occurrences exist, that reopens the investigation (would mean another gap besides the one found here).

## Non-functional requirements

- **Correctness over convenience**: any new `Kind=Utc` normalization must be a relabel/convert (`SpecifyKind` or `.ToUniversalTime()`), never silently shifting a wall-clock value — consistent with the existing test assertions and the gotcha doc's explicit callout ("relabel, not shift").
- **No schema/migration changes** — this is a code-only fix; column types (`timestamp without time zone`) stay as-is.
- **No behavior change on the already-working live webhook path** — `Kind=Utc` inputs must pass through FR-1's helper unchanged (idempotent).
- Backfill (FR-3) must not double-process: replaying an audit entry increments `ReplayCount`/`LastReplayedAt` (existing behavior) — no new idempotency mechanism needed since the underlying upserts are already `ON CONFLICT ... WHERE EXCLUDED."UpdatedAt" >= ...` guarded.

## Data model

No entity/schema changes. Affected entities (unchanged shape, only mapper logic changes):
- `SmartsuppContact` (`CreatedAt`, `UpdatedAt`, `SyncedAt`, `BannedAt` — all `timestamp without time zone`, all must arrive as `Kind=Utc` values from C# before the raw-SQL upsert).
- `SmartsuppConversation` (`SyncedAt`, `LastClosedAt`, plus other nullable DateTimes already guarded via `ReadOptionalUtc`).
- `SmartsuppWebhookAuditEntry` (read-only for this work — `EventTimestamp`/`ProcessingStatus` are the filter criteria for FR-3, not modified in shape).

## Interfaces

- No new endpoints required for FR-1/FR-2 (pure mapper-layer fix + tests).
- FR-3 reuses `POST /api/admin/smartsupp/webhooks/{id}/replay` (existing, `Feature.Admin_Administration` write-gated). If the number of affected entries is large, consider (open question below) a small addition to `SmartsuppWebhookAuditController`/`ListWebhookAuditRequest` to filter+bulk-replay by `processingStatus` + date range, rather than driving the existing single-entry endpoint in a loop from a script.
- FR-4 is GitHub issue/comment activity only (`gh` CLI, per repo convention — no MCP GitHub tools).

## Dependencies and scope

**In scope**: `SmartsuppPayloadMapper.MapContact`/`MapConversation` SyncedAt hardening, `LastClosedAt` hardening in the two `ConversationClosed*Reaction` classes, associated tests, replay-based data recovery for the 2026-07-07→07-13 window, GitHub issue reconciliation (#3443, #3622, this filing).

**Out of scope**:
- The separate Photobank `ArgumentException@DateTimeConverterResolver.Get` signal (#3444) — different call site, not touched here.
- `ConversationAgentJoinedReaction`'s `DateTime.SpecifyKind(ctx.Timestamp, DateTimeKind.Unspecified)` — this is correct as-is; `SmartsuppConversationPresence` is a genuinely `timestamp without time zone`-only table with no raw-SQL-timestamptz-inference involved, unrelated to this bug family.
- `RefreshOrphanContactsHandler`'s own `local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` (feeds `UpsertConversationAsync`, not `UpsertContactAsync`) — this is a *related* latent instance of the same bug class (unguarded `Unspecified` DateTime into the conversation raw-SQL upsert) but is a distinct code path with its own exception site; worth a follow-up ticket, not bundled into this fix, to keep this change surgical and reviewable.
- Any move away from `ExecuteSqlInterpolatedAsync`'s implicit-timestamptz-inference toward explicit `NpgsqlParameter`/`NpgsqlDbType.Timestamp` typing (the systemic fix that would make this whole bug class impossible) — bigger, riskier change; flagged as an open question for the architecture step, not undertaken here.

## Rough plan

1. **Fix**: add a small `AsUtc(DateTime)` helper in `SmartsuppPayloadMapper` (or reuse/extend `ReadUtc`'s pattern) and apply it to `MapContact`'s and `MapConversation`'s `SyncedAt` assignment; apply the same normalization to `LastClosedAt` in `ConversationClosedReaction` and `ConversationClosedByContactReaction`.
2. **Test**: add regression tests per FR-2, including one that models the replay-from-DB scenario (`Unspecified` input → `Utc` output, no exception through the real upsert).
3. **Verify**: `dotnet build` + `dotnet format` + run the full Smartsupp test subset (`SmartsuppContactMappingTests`, `SmartsuppRepositoryUpsertIntegrationTests`, `SmartsuppPayloadMapperTests`, `SmartsuppRepositoryUpdatedAtGuardTests`, `SmartsuppRepositoryUnknownContactFetchTests`) plus the full suite per repo validation rules.
4. **Deploy** the fix (normal release process for this repo).
5. **Recover data**: query `SmartsuppWebhookAuditEntries` for `ProcessingStatus = HandlerException` in the 2026-07-07→07-13 window matching this exception; replay each via the admin endpoint; spot-check recovered conversations.
6. **Reconcile issues**: comment/close this filing and #3443 referencing #3622 and this follow-up fix; confirm zero post-2026-07-13 occurrences of the signal in telemetry.

## Open questions

- **Bulk replay tooling**: is the per-entry `POST .../{id}/replay` endpoint sufficient for the expected recovery volume (potentially 260+ entries), or is a filtered bulk-replay endpoint worth adding? Default assumption: start with per-entry replay driven by a short list of IDs from a direct query; only build bulk tooling if the count turns out to be large enough to be impractical (defer the decision to the development step once the actual count is queried).
- **`RefreshOrphanContactsHandler`'s own Unspecified `SyncedAt`**: confirmed as a related latent bug on a different exception site (`UpsertConversationAsync`, not `UpsertContactAsync`). Default assumption: file as a separate follow-up issue rather than fixing inline here, to keep this change scoped to the exact telemetry signal plus its directly-adjacent replay-path gap. Flag to the user/architecture step in case they'd rather fold it in now since it's a one-line, same-shaped fix.
- **Systemic fix (explicit Npgsql parameter typing)**: worth doing at some point to eliminate this bug class entirely, but out of scope for this task — surfaced for the architecture step to decide if/when to schedule it.
