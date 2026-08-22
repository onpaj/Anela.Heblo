# Architecture Review: Route Smartsupp webhook-audit access through a repository contract

## Skip Design: true

Pure backend refactor — no new or changed UI, no visual components, no API response shape changes.
Designer phase is a no-op pass-through.

## Architectural Fit Assessment

This fits the codebase's dominant pattern exactly: every other feature reaches persistence through a
Domain-declared repository interface (`ISmartsuppRepository`, `ISmartsuppPresenceRepository`,
`IPhotobankPhotoRepository`, etc.), implemented in `Anela.Heblo.Persistence`, bound in the owning
feature's `{Feature}Module.cs` per ADR-004. The five Smartsupp classes named in the brief are the
outliers, not a competing pattern that needs reconciling — there is exactly one prior decision to
apply here, not a new one to make. `ISmartsuppRepository` and `SmartsuppRepository` (verified by
reading both in full) are the template to copy: constructor-injected `ApplicationDbContext`,
`AsNoTracking()` on pure reads, tracked entities for read-modify-`SaveChangesAsync` flows, and a
`SaveChangesAsync(CancellationToken)` pass-through method on the interface itself for handlers that
need to commit after mutating a returned tracked entity (`RefreshOrphanContactsHandler` already relies
on this from `ISmartsuppRepository`).

Two things in the spec need to be pinned down before implementation starts, and I'm pinning them here
so the planner doesn't hand developers an ambiguous task:

1. Whether the audit table gets its own repository (`ISmartsuppWebhookAuditRepository`) or whether its
   methods are added directly to `ISmartsuppRepository`.
2. Whether `ReplayWebhookEventHandler`'s get-mutate-save flow is a tracked-get + generic
   `SaveChangesAsync` (mirrors `ISmartsuppRepository`'s shape) or a single atomic
   `RecordReplayAsync(...)` method.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain/Features/Smartsupp/
  ISmartsuppRepository.cs                  (existing — gains FindConversationAsync)
  ISmartsuppWebhookAuditRepository.cs       (NEW)
  SmartsuppWebhookAuditEntry.cs             (existing, unchanged)

Anela.Heblo.Persistence/Smartsupp/
  SmartsuppRepository.cs                    (existing — gains FindConversationAsync)
  SmartsuppWebhookAuditRepository.cs        (NEW — replaces SmartsuppWebhookAuditWriter.cs)
  ISmartsuppWebhookAuditWriter.cs           (DELETED)
  SmartsuppWebhookAuditWriter.cs            (DELETED)

Anela.Heblo.Application/Features/Smartsupp/
  SmartsuppModule.cs                        (binding swapped: writer -> audit repository)
  UseCases/ListWebhookAudit/ListWebhookAuditHandler.cs        (ctx -> ISmartsuppWebhookAuditRepository)
  UseCases/GetWebhookAuditEntry/GetWebhookAuditEntryHandler.cs (ctx -> ISmartsuppWebhookAuditRepository)
  UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs    (ctx -> ISmartsuppWebhookAuditRepository)
  UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs (drop ctx, add FindConversationAsync)
  Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs      (ctx -> ISmartsuppWebhookAuditRepository)

Anela.Heblo.API/Controllers/
  SmartsuppWebhookController.cs             (ISmartsuppWebhookAuditWriter -> ISmartsuppWebhookAuditRepository)
```

Dependency direction after the change (unchanged shape, just one more box in the existing shape):

```
API.Controllers.SmartsuppWebhookController ──┐
Application.UseCases.{List,Get,Replay}*Handler ├─▶ Domain.ISmartsuppWebhookAuditRepository ◀── Persistence.SmartsuppWebhookAuditRepository ──▶ ApplicationDbContext
Application.Infrastructure.Jobs.CleanupJob ──┘

Application.UseCases.RefreshOrphanContactsHandler ─▶ Domain.ISmartsuppRepository ◀── Persistence.SmartsuppRepository ──▶ ApplicationDbContext
```

No class outside `Anela.Heblo.Persistence` references `ApplicationDbContext` after this change, within
the Smartsupp feature.

### Key Design Decisions

#### Decision 1: One new repository for the audit table, not an extension of `ISmartsuppRepository`

**Options considered:**
- (a) Fold audit-entry access into the existing `ISmartsuppRepository` (it already owns
  `SmartsuppConversation`/`SmartsuppContact`/`SmartsuppMessage`).
- (b) A separate `ISmartsuppWebhookAuditRepository` scoped to `SmartsuppWebhookAuditEntry` only.

**Chosen approach:** (b) — separate repository.

**Rationale:** `SmartsuppWebhookAuditEntry` is not part of the conversation/contact/message aggregate
`ISmartsuppRepository` owns — it's an independent audit-log table with its own lifecycle (retention
purge, replay counters) and a different primary consumer set (the audit UI + the webhook controller,
not the conversation sync pipeline). `ISmartsuppRepository` already has 12 methods; adding 5 more
unrelated ones would blur what it represents and make its own tests noisier. This also matches
`ISmartsuppPresenceRepository`'s existing precedent in the same feature — presence tracking already
has its own repository sitting next to `ISmartsuppRepository` rather than being folded in, for exactly
this reason (independent table, independent lifecycle, independent consumer). Keep the same
granularity: one repository per table-family with a distinct lifecycle. This is a change from a purely
literal reading of the brief's "`RefreshOrphanContactsHandler`'s single direct query is already close
to `ISmartsuppRepository`'s existing surface" (which is only about the *conversation* lookup in FR-5,
correctly folded into `ISmartsuppRepository` — see Decision 3) versus the *audit-entry* access in
FR-1/2/4, which is a different table and belongs in its own contract.

#### Decision 2: Replay uses a tracked get + the interface's own `SaveChangesAsync`, not a single atomic method

**Options considered:**
- (a) `Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken)` (tracked) +
  `Task SaveChangesAsync(CancellationToken)` on the interface — handler mutates the returned entity's
  `ReplayCount`/`LastReplayedAt`/`LastReplayedBy` in place, then calls `SaveChangesAsync`.
- (b) `Task<(int ReplayCount, DateTime LastReplayedAt)?> RecordReplayAsync(Guid id, string replayedBy,
  DateTime replayedAt, CancellationToken)` — single call, no exposed mutable entity.

**Chosen approach:** (a).

**Rationale:** `ISmartsuppRepository` already exposes this exact shape —
`Task SaveChangesAsync(CancellationToken)` on the interface, paired with get-then-mutate-in-place
handler code (see `RefreshOrphanContactsHandler`'s existing `_repository.UpsertConversationAsync(...)`
+ `_repository.SaveChangesAsync(...)` pair, and `ISmartsuppRepository`'s doc-implicit contract).
Matching that shape means `ISmartsuppWebhookAuditRepository` reads as "the same kind of thing" as its
sibling, keeps `ReplayWebhookEventHandler`'s existing code structure (fetch → mutate 3 fields → save)
almost line-for-line, and avoids introducing a second repository-mutation idiom into a codebase that
has exactly one already. Name the get method `GetForReplayAsync` (not `GetByIdAsync`) to make the
tracked-vs-no-tracking distinction from `GetWebhookAuditEntryHandler`'s no-tracking read
(`GetByIdAsync`) visible at the call site, not just in a doc comment.

#### Decision 3: `RefreshOrphanContactsHandler`'s lookup goes into `ISmartsuppRepository`, named `FindConversationByIdAsync`

**Options considered:**
- (a) Add to `ISmartsuppRepository` (it already owns `SmartsuppConversation`).
- (b) Route through the new audit repository (wrong aggregate — rejected without further discussion).

**Chosen approach:** (a), method name `FindConversationByIdAsync(string conversationId,
CancellationToken)`, tracked (no `AsNoTracking()`), returns `SmartsuppConversation?`.

**Rationale:** `ISmartsuppRepository.GetConversationAsync` already exists but is `AsNoTracking()` and
`Include`s `Messages`/`Contact` — wrong shape for this call site, which needs a *tracked* bare
conversation so the handler can set `local.ContactId`/`local.SyncedAt` in place before calling
`UpsertConversationAsync`. Don't reuse `GetConversationAsync` and don't add tracking/includes as
optional parameters to it (that couples an unrelated call site's needs onto a method three other
callers already use in its current no-tracking form). A new, narrowly-scoped method matches ADR-002's
"extended per feature" spirit — one purpose-built method beats a parameterized do-everything one here.

#### Decision 4: `_db.ChangeTracker.Clear()` in the `RefreshOrphanContactsHandler` catch block is dropped, not relocated

**Options considered:**
- (a) Drop it — once the handler holds no `ApplicationDbContext` reference, it has no tracker to clear.
- (b) Expose a `ClearTracking()`/similar method on `ISmartsuppRepository` so the per-iteration reset
  can continue.

**Chosen approach:** (a) — drop it, with a short comment explaining why it's safe.

**Rationale:** The original `_db.ChangeTracker.Clear()` exists to stop a failed iteration's
partially-tracked `SmartsuppConversation` from poisoning the *next* iteration's `FirstOrDefaultAsync`
(EF's identity map would otherwise hand back the same broken tracked instance). But
`ISmartsuppRepository.SaveChangesAsync` already runs `await _db.SaveChangesAsync(cancellationToken)`
without a preceding manual `Clear()` in *every other* consumer of that repository today — no other
handler using `ISmartsuppRepository` in a loop guards against this, and `SmartsuppRepository`'s own
`UpsertConversationAsync`/`SaveChangesAsync` pair, once wrapped behind the interface, is the same
pattern `ISmartsuppRepository` already uses successfully elsewhere without tracker resets. Adding a
`ClearTracking()` escape hatch to the interface purely to preserve one caller's defensive habit
re-exposes exactly the abstraction leak this issue exists to close — a raw EF concern reaching into
Application-layer control flow. If failed-iteration state actually causes test failures once the
refactor lands (verify with FR-5's existing failure-path test, if one exists, or add one), the fix
belongs inside `SmartsuppRepository.FindConversationByIdAsync`/`UpsertConversationAsync` (e.g. querying
fresh with `.AsNoTracking()` then re-attaching, or calling `_db.Entry(local).State =
EntityState.Detached` internally on failure) — not by handing the handler a tracker handle again.
Flag this as a verification step for the developer, not a silent drop: add/keep a test that exercises
the "first conversation fails, second conversation in the same batch still succeeds" path.

## Implementation Guidance

### Directory / Module Structure

New files:
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppWebhookAuditRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditRepository.cs`

Deleted files:
- `backend/src/Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs`
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditWriter.cs`

Modified files (constructor/import changes only, no relocation):
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` (+1 method)
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` (+1 method)
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ListWebhookAudit/ListWebhookAuditHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetWebhookAuditEntry/GetWebhookAuditEntryHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs`
- `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`

Test files to update (verified to exist under
`backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/`):
- `ListWebhookAuditHandlerTests.cs`, `GetWebhookAuditEntryHandlerTests.cs`,
  `ReplayWebhookEventHandlerTests.cs`, `SmartsuppWebhookAuditCleanupJobTests.cs` — switch from
  constructing an in-memory `ApplicationDbContext` and passing it straight to the unit under test, to
  constructing a real `SmartsuppWebhookAuditRepository` wrapping that same in-memory context and
  passing *that* in. This is the lower-risk migration: it preserves every existing assertion and
  arrange-block verbatim (they seed via `ctx.SmartsuppWebhookAuditEntries.Add(...)`), only the
  constructor call for the unit under test changes. Do not switch to `Mock<...>` for these four files —
  it would require re-deriving every filter/ordering/paging assertion against mocked return values
  instead of a real query, which is unnecessary churn for a pure relocation and would leave the actual
  ported query logic unverified by any test.
- `SmartsuppWebhookAuditWriterTests.cs` → rename to `SmartsuppWebhookAuditRepositoryTests.cs` (same
  directory), keep its two existing tests (`CreateAsync_PersistsEntry_WithGeneratedId` and whatever the
  `UpdateOutcomeAsync` test is), add coverage for the five methods absorbed from the handlers/job
  (list/get/get-for-replay/purge) — this file becomes the one EF-backed test of the real repository
  implementation.
- `SmartsuppWebhookAuditControllerTests.cs` — no logic change expected (exercises the full stack via
  `HebloWebApplicationFactory`); confirm it still compiles and passes.
- `RefreshOrphanContactsHandler` — check for an existing test file (none was found under
  `Features/Smartsupp/` at architecture-review time; if the developer finds one was added since,
  update it the same way — mock or real `ISmartsuppRepository` for `FindConversationByIdAsync`,
  consistent with how that handler's existing dependencies are tested). If no test file exists today,
  adding one is not required by this issue (out of scope per spec) but the failed-iteration coverage
  named in Decision 4 should be added if a test file exists or an integration path already covers it.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppWebhookAuditRepository.cs
namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppWebhookAuditRepository
{
    Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken);

    Task UpdateOutcomeAsync(
        Guid id,
        SmartsuppWebhookProcessingStatus status,
        string? error,
        int durationMs,
        CancellationToken cancellationToken);

    Task<(List<SmartsuppWebhookAuditEntry> Items, int Total)> ListAsync(
        DateTime? from,
        DateTime? to,
        string? eventName,
        SmartsuppWebhookSignatureStatus? signatureStatus,
        SmartsuppWebhookProcessingStatus? processingStatus,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<SmartsuppWebhookAuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Tracked — caller mutates ReplayCount/LastReplayedAt/LastReplayedBy then calls SaveChangesAsync.
    Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken);
}
```

Notes on this contract for the developer:
- `ListAsync` returns raw `SmartsuppWebhookAuditEntry` domain entities, not the Application-layer
  `WebhookAuditSummaryDto` — DTO projection stays in `ListWebhookAuditHandler` (Domain must not know
  about Application DTOs). This is a deliberate change from today's handler, which currently projects
  directly inside the EF query (`.Select(e => new WebhookAuditSummaryDto {...})`); moving the
  projection out of the query into an in-memory `Select` after `ListAsync` returns is an acceptable,
  expected consequence of the refactor — call it out in the PR description, it is not a regression
  (the query still only pages `take` rows from the DB either way, since paging/skip/take happen inside
  `ListAsync`, before materialization).
- `skip`/`take` passed into `ListAsync` are already clamped by the handler (`MaxTake = 200` logic stays
  in `ListWebhookAuditHandler`, per spec FR-4) — the repository does not re-clamp.
- `PurgeOlderThanAsync` takes an already-computed `cutoff` (job keeps `RetentionDays = 7` and does
  `DateTime.UtcNow.AddDays(-RetentionDays)` itself, per spec FR-4) and returns the deleted count,
  mirroring `ISmartsuppPresenceRepository.PurgeExpiredAsync`'s existing return-count convention used in
  the same job today.

```csharp
// Addition to Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs
Task<SmartsuppConversation?> FindConversationByIdAsync(
    string conversationId,
    CancellationToken cancellationToken);
```
Tracked (no `.AsNoTracking()`), no `Include`s — bare entity lookup by primary key, for
`RefreshOrphanContactsHandler`'s local-existence check + in-place mutation.

```csharp
// SmartsuppModule.cs — replace this line:
services.AddScoped<ISmartsuppWebhookAuditWriter, SmartsuppWebhookAuditWriter>();
// with:
services.AddScoped<ISmartsuppWebhookAuditRepository, SmartsuppWebhookAuditRepository>();
```

### Data Flow

**List (`GET /api/admin/smartsupp/webhooks`):**
`SmartsuppWebhookAuditController.List` → `IMediator.Send(ListWebhookAuditRequest)` →
`ListWebhookAuditHandler` clamps `skip`/`take` → `ISmartsuppWebhookAuditRepository.ListAsync(...)` →
`SmartsuppWebhookAuditRepository` runs the filtered/ordered/paged/counted EF query against
`ApplicationDbContext.SmartsuppWebhookAuditEntries` → handler projects the returned entities into
`WebhookAuditSummaryDto` → `ListWebhookAuditResponse`. Unchanged externally.

**Replay (`POST /api/admin/smartsupp/webhooks/{id}/replay`):**
`SmartsuppWebhookAuditController.Replay` → `ReplayWebhookEventHandler` calls
`ISmartsuppWebhookAuditRepository.GetForReplayAsync(id)` (tracked) → parses `RawBody` JSON → dispatches
`IMediator.Send(ProcessWebhookEventRequest)` (unchanged, still goes through the full reaction
pipeline) → mutates the tracked entry's `ReplayCount`/`LastReplayedAt`/`LastReplayedBy` → calls
`ISmartsuppWebhookAuditRepository.SaveChangesAsync(cancellationToken)` → returns
`ReplayWebhookEventResponse`. Unchanged externally.

**Webhook receive (`POST /api/webhooks/smartsupp`):**
`SmartsuppWebhookController.Receive` calls `ISmartsuppWebhookAuditRepository.CreateAsync`/
`UpdateOutcomeAsync` at the same four call sites as today (signature-missing/mismatch, malformed-JSON,
app-id-mismatch, post-dispatch outcome) — only the injected type name changes.

**Cleanup job (nightly, `30 3 * * *`):**
`SmartsuppWebhookAuditCleanupJob.ExecuteAsync` purges presence rows via
`ISmartsuppPresenceRepository.PurgeExpiredAsync` (unchanged), then computes `cutoff =
DateTime.UtcNow.AddDays(-7)` and calls `ISmartsuppWebhookAuditRepository.PurgeOlderThanAsync(cutoff)`
instead of loading + `RemoveRange` + `SaveChangesAsync` inline.

**Orphan-contact backfill (on-demand, `RefreshOrphanContactsHandler`):**
Per conversation id from `ISmartsuppRepository.ListOrphanContactConversationIdsAsync`, calls the remote
API, then `ISmartsuppRepository.FindConversationByIdAsync(conversationId)` (tracked) instead of
`_db.SmartsuppConversations.FirstOrDefaultAsync(...)`, mutates `ContactId`/`SyncedAt` in place, then
`UpsertConversationAsync` + `SaveChangesAsync` as today.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ListAsync`'s in-memory DTO projection (moved out of the EF query) accidentally changes what's selected vs. today's `.Select(e => new WebhookAuditSummaryDto {...})` inside the query | Low | Copy the exact field list from today's `ListWebhookAuditHandler.Handle` into the handler's post-`ListAsync` projection; add/keep a test asserting all `WebhookAuditSummaryDto` fields are populated for a known entry. |
| Dropping `_db.ChangeTracker.Clear()` in `RefreshOrphanContactsHandler`'s catch block (Decision 4) silently reintroduces stale-tracked-entity bugs across loop iterations under `SmartsuppRepository`'s new `FindConversationByIdAsync` | Medium | Add or confirm a test: two orphan conversation ids in one batch, first one's remote-API call or upsert throws, assert the second one still succeeds and is `Updated`. If it fails without a tracker reset, resolve inside `SmartsuppRepository`, not by re-exposing the tracker to the handler (see Decision 4 rationale). |
| Renaming `ISmartsuppWebhookAuditWriter` → `ISmartsuppWebhookAuditRepository` breaks anything outside Smartsupp that references the old interface/impl by name | Low | `grep -rn "ISmartsuppWebhookAuditWriter\|SmartsuppWebhookAuditWriter"` across `backend/` before deleting; the brief's own audit already confirms `SmartsuppWebhookController` is the only consumer, but re-verify at implementation time since this spec was written from a point-in-time read. |
| `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings` could still pass even if a stray `ISmartsuppWebhookAuditRepository` binding is accidentally left in `PersistenceModule.cs` if that test only checks for a `*Repository` name suffix pattern that doesn't match | Low | Read `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`'s actual matching logic before finishing; if it's name-pattern-based confirm `SmartsuppWebhookAuditRepository` matches the pattern it guards against. |
| Test migration for the four handler/job test files (real repo wrapping in-memory context, per Decision above) still leaves `ApplicationDbContext` referenced in test *files* (not in the handlers themselves) | None — not a risk | This is intentional and acceptable: NFR-2 requires the *handler/job* be testable without EF in its own constructor; the *test file* is allowed to use EF to seed/arrange via the repository, same as `SmartsuppWebhookAuditWriterTests` does today for the writer. |

## Specification Amendments

1. **FR-1 (Domain contract)**: Use the concrete interface shown under Interfaces and Contracts above —
   `ISmartsuppWebhookAuditRepository` with `CreateAsync`, `UpdateOutcomeAsync`, `ListAsync`,
   `GetByIdAsync`, `GetForReplayAsync`, `SaveChangesAsync`, `PurgeOlderThanAsync`. This resolves the
   spec's "(a) or (b), architect to decide" for the replay flow: **(a) — tracked get + generic
   `SaveChangesAsync`** (Decision 2).
2. **FR-1 list query**: `ListAsync` returns domain entities, not `WebhookAuditSummaryDto` — DTO
   projection moves to the handler (in-memory, after the already-paged rows are materialized). Spec's
   "delegate the actual filtered/paged/counted query to the repository" is refined: the repository
   returns entities; the handler still owns DTO shaping.
3. **FR-5 method name**: `ISmartsuppRepository.FindConversationByIdAsync(string conversationId,
   CancellationToken)` (spec offered `FindConversationAsync` as an example name; the architecture
   review fixes it to `FindConversationByIdAsync` for clarity against `GetConversationAsync`, which
   also takes an id but does more).
4. **FR-5 tracker-clear handling**: Spec left this as "drop it, or expose through the repository" —
   architecture review resolves it: **drop it** (Decision 4), with a required regression test for the
   multi-iteration-failure path (see Risks table).
5. **FR-7 test strategy for the four handler/job test files**: Spec offered mock-vs-real-wrapper as a
   planner/architect choice — architecture review resolves it: **real
   `SmartsuppWebhookAuditRepository` wrapping an in-memory `ApplicationDbContext`**, not
   `Mock<ISmartsuppWebhookAuditRepository>`, for `ListWebhookAuditHandlerTests`,
   `GetWebhookAuditEntryHandlerTests`, `ReplayWebhookEventHandlerTests`,
   `SmartsuppWebhookAuditCleanupJobTests`. Rationale in Implementation Guidance above.

## Prerequisites

None. No migrations, no config, no infrastructure changes — this is a same-schema, same-runtime code
reorganization. Implementation can start immediately once the task plan is written.
