# Review: SmartsuppRepository DateTime Kind=Unspecified crash — residual hardening (FR-1, FR-2)

**Verdict: done**

## What was checked

Reviewed the diff (`cb3a5f1b^..HEAD`, restricted to the actually-changed product/test files — the
raw `main..HEAD` diff is dominated by unrelated concurrent task branches and is not this task's
work) against `plan-01.md` / `design-01.md` / `architecture-01.md`, and independently re-ran
verification rather than trusting `development-01.md`'s reported numbers:

- `dotnet build Anela.Heblo.sln -c Debug` → **0 errors** (confirmed live).
- `dotnet format Anela.Heblo.sln --include <6 touched files> --verify-no-changes` → **exit 0, no
  output** (confirmed live).
- `dotnet test ... --filter "FullyQualifiedName~Smartsupp" --no-build` → **Passed: 210, Failed: 0,
  Skipped: 0** (confirmed live, including the new Postgres-testcontainer-backed integration test
  actually spinning up and executing — not skipped).
- `git status --short` → clean; nothing left uncommitted.

## Conformance to spec/design/architecture

- **FR-1**: `AsUtc(DateTime)` helper added to `SmartsuppPayloadMapper` exactly as designed (pure
  `SpecifyKind` relabel, not `.ToUniversalTime()` — correctly avoids the shift bug the codebase's
  own gotcha doc warns about). Applied at all four sites identified in plan/design/architecture:
  `MapContact`'s `SyncedAt`, `MapConversation`'s `SyncedAt`, and `LastClosedAt` in both
  `ConversationClosedReaction` and `ConversationClosedByContactReaction`. Grepped the rest of the
  Smartsupp module for other unguarded `SyncedAt =` / `LastClosedAt =` assignments — the only other
  hits are `SmartsuppRepository.MarkConversationResolvedAsync` (EF-tracked `SaveChangesAsync` path,
  not the raw-SQL upsert, so Npgsql's normal ADO writer handles `Unspecified` against a `timestamp
  without time zone` column fine — out of scope, not a regression) and
  `RefreshOrphanContactsHandler.cs:59`, which the plan explicitly identifies as a related-but-distinct
  latent bug on a different call site and explicitly defers to a follow-up ticket. Nothing missed.
- **FR-2**: regression tests added in all three places the architecture step specified, including
  the corrected placement (Postgres-backed `SmartsuppRepositoryUpsertIntegrationTests`, not the
  mocked-`IMediator` `ReplayWebhookEventHandlerTests.cs`) — the architecture review's redirect was
  followed precisely. Mapper tests, reaction tests, and the integration test all follow each file's
  existing conventions (helper reuse, `FluentAssertions`, `Moq`/`NSubstitute` patterns already in
  place) — no new test infrastructure introduced.
- **FR-3/FR-4**: correctly left as code-free operational follow-ups per the plan's own scoping
  (production data recovery via existing admin endpoints; `gh` CLI issue reconciliation) — nothing
  to review at the diff level, and the design/architecture steps already agreed no new
  endpoints/schema are warranted.
- **Scope discipline**: no schema/migration, DTO, or API contract changes, matching the design's
  explicit "no schema changes" constraint. No unrelated refactoring.

## Incidental fix (6 pre-existing failing tests)

`SmartsuppRepositoryUpsertIntegrationTests.MakeContact`/`MakeConversation` had their fixture
`CreatedAt`/`UpdatedAt`/`SyncedAt` changed from `DateTimeKind.Unspecified` to `DateTimeKind.Utc`.
This is the same bug class hitting test fixtures rather than product code, doesn't touch the
tests' actual intent (upsert ordering / `COALESCE` semantics — confirmed by reading the call sites;
`DateTime.Equals` ignores `Kind`, so the existing `Should().Be(t1)`-style assertions are unaffected),
and was necessary to get a clean baseline in a file this change was already touching. Reasonable
and low-risk; not something I'd have blocked on had it been submitted separately either.

## Correctness

No logic errors found. `AsUtc` is idempotent on already-`Utc` input (verified by the
`*_UtcSyncedAt_PassesThroughUnchanged` tests), which protects the live webhook path's existing
behavior per the design's non-functional requirement. The call-path safety argument in
`architecture-01.md` (webhook path was already safe; only the audit-replay path was exposed) is
borne out by the code — `SmartsuppWebhookController.Receive` always constructs `Kind=Utc`
timestamps, and `ReplayWebhookEventHandler.Handle` is the only producer that could hand back
`Unspecified`.

No blocking issues. Ready to proceed.

```json
{"outcome": "done", "summary": "FR-1 (AsUtc helper applied at all 4 unguarded SyncedAt/LastClosedAt sites) and FR-2 (regression tests in the architecture-corrected locations) match the design exactly. Independently re-ran build (0 errors), dotnet format --verify-no-changes (clean), and the full Smartsupp test filter (210/210 passing, including the Postgres-backed integration test actually executing) — all confirmed live, not just trusted from development-01.md. No missed unguarded DateTime sites, no scope creep, no schema/contract changes. FR-3/FR-4 correctly left as non-code operational follow-ups per the plan's own scoping."}
```
