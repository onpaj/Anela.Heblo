# Specification: Route Smartsupp webhook-audit access through a repository contract instead of ApplicationDbContext

## Summary

Five classes in the Smartsupp Application slice — four MediatR handlers and one recurring job — inject
`Anela.Heblo.Persistence.ApplicationDbContext` directly instead of going through a Domain-declared
repository abstraction, as every other feature in the codebase does. This is a pure internal
refactor: introduce a Domain-owned repository contract for the webhook-audit table (and fold in the
lone write-only `ISmartsuppWebhookAuditWriter` that currently lives in the wrong assembly), implement
it in `Anela.Heblo.Persistence`, bind it in `SmartsuppModule` per ADR-004, and update the five
offending classes plus `SmartsuppWebhookController` to depend on the new interface instead of the raw
`DbContext`. No externally observable behavior, API contract, or DTO shape changes.

## Background

`docs/architecture/development_guidelines.md` (*Forbidden Practices*: "Shared DbContext" — violates
separation, creates coupling; *Common Pitfalls to Avoid* #5: "Don't bypass contracts — always
communicate through interfaces") and **ADR-002** (generic repository pattern) establish the house rule:
Application-layer handlers reach persistence exclusively through a Domain-declared repository
interface, implemented in `Anela.Heblo.Persistence`, with the DI binding owned by the feature's
`{Feature}Module.cs` (**ADR-004**). Across the entire `Anela.Heblo.Application` assembly this rule
holds everywhere except five Smartsupp classes:

- `Features/Smartsupp/UseCases/ListWebhookAudit/ListWebhookAuditHandler.cs` — injects
  `ApplicationDbContext`, builds the whole filtered/paged query against
  `_context.SmartsuppWebhookAuditEntries` inline, including the `MaxTake = 200` clamp.
- `Features/Smartsupp/UseCases/GetWebhookAuditEntry/GetWebhookAuditEntryHandler.cs` — injects
  `ApplicationDbContext`, does a single `AsNoTracking().SingleOrDefaultAsync` by id.
- `Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs` — injects
  `ApplicationDbContext`, reads the entry (tracked), re-dispatches `ProcessWebhookEventRequest`, mutates
  `ReplayCount`/`LastReplayedAt`/`LastReplayedBy`, and calls `_context.SaveChangesAsync` directly.
- `Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs` — injects **both**
  `ISmartsuppRepository` and `ApplicationDbContext` side by side; the repository is used for
  `ListOrphanContactConversationIdsAsync`/`UpsertConversationAsync`/`SaveChangesAsync`, but a single
  read (`_db.SmartsuppConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ...)`) still goes
  straight to the context.
- `Features/Smartsupp/Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs` — injects
  `ApplicationDbContext` for the retention-window purge (`RetentionDays = 7`), alongside a properly
  injected `ISmartsuppPresenceRepository` for the presence-table purge in the same job.

Related and in scope: `ISmartsuppWebhookAuditWriter` (create-audit-row +
`UpdateOutcomeAsync`) is declared in `Anela.Heblo.Persistence/Smartsupp/` instead of
`Anela.Heblo.Domain/Features/Smartsupp/`, where every other Smartsupp contract
(`ISmartsuppRepository`, `ISmartsuppPresenceRepository`, `ISmartsuppApiClient`) lives. Its only
consumer, `SmartsuppWebhookController`, is consequently the only controller in the API project with a
feature-level `using Anela.Heblo.Persistence`.

This is an established arch-review class in this repository (#1827 Photobank, #3278/#3393 Bank, #1952
Analytics). Consequences called out in the filed issue:

- **ADR-001 Phase 2 (per-module DbContext) is blocked for Smartsupp** until these five classes stop
  being typed against the shared `ApplicationDbContext`.
- **The four audit handlers/job cannot be unit-tested without EF** — every other feature's handlers are
  tested against a mocked repository interface (see `SmartsuppWebhookAuditCleanupJobTests`'s
  `Mock<ISmartsuppPresenceRepository>` for the pattern already used for the presence half of the same
  job).
- **Audit-table access policy has no single home** — `ListWebhookAuditHandler`'s `MaxTake` clamp and the
  cleanup job's `RetentionDays` window both encode query/retention policy in the Application layer.

## Functional Requirements

### FR-1: Declare `ISmartsuppWebhookAuditRepository` in the Domain layer

Add a new interface `ISmartsuppWebhookAuditRepository` to
`Anela.Heblo.Domain/Features/Smartsupp/` (new file, alongside `ISmartsuppRepository.cs`,
`ISmartsuppPresenceRepository.cs`, `ISmartsuppApiClient.cs`). Its surface must cover every audit-table
access currently performed by the five offending classes and by `ISmartsuppWebhookAuditWriter`,
i.e. the union of:

- **Create** — persist a new `SmartsuppWebhookAuditEntry`, generating `Id` if empty, and return the
  generated id (absorbed from `ISmartsuppWebhookAuditWriter.CreateAsync`).
- **Update outcome** — set `ProcessingStatus`, `ProcessingError`, `ProcessingDurationMs`, `ProcessedAt`
  for a given id (absorbed from `ISmartsuppWebhookAuditWriter.UpdateOutcomeAsync`).
- **List (filtered + paged)** — the `ListWebhookAuditHandler` query: filters on `From`/`To`/`EventName`/
  `SignatureStatus`/`ProcessingStatus`, ordered by `ReceivedAt` descending, with skip/take and a total
  count. The `MaxTake = 200` clamp is application-level *request validation*, not persistence policy —
  keep it in the handler (see FR-4); the repository method takes the already-clamped `skip`/`take`.
- **Get by id** — the `GetWebhookAuditEntryHandler` read, no-tracking, returning `null` when absent.
- **Get by id for replay (tracked)** — the `ReplayWebhookEventHandler` read: fetch the entity, mutate
  `ReplayCount`/`LastReplayedAt`/`LastReplayedBy`, then persist. Model this as either (a) a tracked
  get plus a generic `SaveChangesAsync`, mirroring `ISmartsuppRepository`'s existing
  get-then-mutate-then-`SaveChangesAsync` shape, or (b) a single
  `RecordReplayAsync(Guid id, string replayedBy, DateTime replayedAt, CancellationToken)` that returns
  the updated `ReplayCount`/`LastReplayedAt` (or `null` if the id doesn't exist). Prefer (a) for
  consistency with `ISmartsuppRepository`'s existing pattern (see `SaveChangesAsync` there) unless the
  architect phase determines (b) reads better against ADR-002.
- **Purge stale entries** — the cleanup job's retention sweep: delete entries with `ReceivedAt` older
  than a supplied cutoff, returning the count deleted (mirroring
  `ISmartsuppPresenceRepository.PurgeExpiredAsync`'s existing return-count convention used in the same
  job). The `RetentionDays = 7` constant is job-level policy — keep it in the job (see FR-4); the
  repository method takes the already-computed cutoff.

**Acceptance criteria:**
- New interface file exists under `Anela.Heblo.Domain/Features/Smartsupp/`.
- Every method needed by FR-4/FR-5's refactored classes is present; no speculative/unused methods are
  added.
- `ISmartsuppWebhookAuditWriter`'s two methods (`CreateAsync`, `UpdateOutcomeAsync`) are represented
  (name reuse is fine — the point is one contract, not name preservation).

### FR-2: Implement the repository in `Anela.Heblo.Persistence`

Add `SmartsuppWebhookAuditRepository : ISmartsuppWebhookAuditRepository` under
`Anela.Heblo.Persistence/Smartsupp/`, injecting `ApplicationDbContext`, following the existing
`SmartsuppRepository`/`SmartsuppPresenceRepository` style (constructor-injected `_db`, `public sealed
class`). Port the existing query/mutation logic verbatim from the five source classes and from
`SmartsuppWebhookAuditWriter` — this is a relocation, not a rewrite; do not change query semantics,
ordering, tracking behavior (`AsNoTracking()` where currently used), or the count-then-page shape of
the list query.

**Acceptance criteria:**
- `SmartsuppWebhookAuditRepository.cs` exists in `Anela.Heblo.Persistence/Smartsupp/`, implements
  `ISmartsuppWebhookAuditRepository` in full.
- Ported query logic is behaviorally identical (same filters, same ordering, same paging math, same
  tracking mode) to what `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`,
  `ReplayWebhookEventHandler`, and `SmartsuppWebhookAuditCleanupJob` do today.
- The old `SmartsuppWebhookAuditWriter.cs` / `ISmartsuppWebhookAuditWriter.cs` under
  `Anela.Heblo.Persistence/Smartsupp/` are removed once their functionality is folded in (see FR-6).

### FR-3: Bind the new repository in `SmartsuppModule` (ADR-004)

In `Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`, add
`services.AddScoped<ISmartsuppWebhookAuditRepository, SmartsuppWebhookAuditRepository>();` and remove
the now-superseded `services.AddScoped<ISmartsuppWebhookAuditWriter, SmartsuppWebhookAuditWriter>();`
line.

**Acceptance criteria:**
- `SmartsuppModule.cs` registers exactly one binding for webhook-audit persistence access
  (`ISmartsuppWebhookAuditRepository`); the old writer binding is gone.
- No binding for this repository is added to `PersistenceModule.cs` (would violate ADR-004 and trip
  `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`).

### FR-4: Refactor the four Application-layer audit classes to depend on `ISmartsuppWebhookAuditRepository`

Update each of the following to take `ISmartsuppWebhookAuditRepository` in its constructor instead of
`ApplicationDbContext`, and drop the `using Anela.Heblo.Persistence;` / `using
Microsoft.EntityFrameworkCore;` imports that become unused as a result:

- `ListWebhookAuditHandler` — keep the `MaxTake = 200` clamp and `skip`/`take` normalization in the
  handler (request-shape validation belongs at the Application boundary); delegate the actual
  filtered/paged/counted query to the repository.
- `GetWebhookAuditEntryHandler` — delegate the by-id lookup to the repository; keep the
  `ErrorCodes.ResourceNotFound` mapping and DTO projection in the handler.
- `ReplayWebhookEventHandler` — delegate the entry fetch + replay-stamp persistence to the repository;
  keep the JSON-payload parsing, `ProcessWebhookEventRequest` re-dispatch via `IMediator`, and response
  shaping in the handler (those are Application-layer concerns, not persistence).
- `SmartsuppWebhookAuditCleanupJob` — keep the `RetentionDays = 7` constant and the cutoff computation
  in the job; delegate the purge to the repository, keeping the existing log lines (entry count,
  cutoff timestamp) driven by the repository's return value.

**Acceptance criteria:**
- None of the four classes references `Anela.Heblo.Persistence.ApplicationDbContext` or
  `Microsoft.EntityFrameworkCore` after the change.
- Each class's constructor takes `ISmartsuppWebhookAuditRepository` (plus its other existing,
  unrelated dependencies — `IMediator` for the replay handler, `ISmartsuppPresenceRepository` +
  `ILogger` for the cleanup job — unchanged).
- Existing behavior is preserved: same filters/ordering/paging for list, same 404 mapping for get,
  same replay side effects (`ReplayCount` incremented, `LastReplayedAt`/`LastReplayedBy` set, downstream
  `ProcessWebhookEventRequest` still dispatched) for replay, same 7-day retention purge for cleanup.

### FR-5: Remove `RefreshOrphanContactsHandler`'s direct `ApplicationDbContext` dependency

`RefreshOrphanContactsHandler` already injects `ISmartsuppRepository`; its only direct-context use is
`_db.SmartsuppConversations.FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken)` to
check whether a locally-known conversation exists before re-attaching the remote `ContactId`. Add a
method to the existing `ISmartsuppRepository` (Domain contract, `Anela.Heblo.Domain/Features/Smartsupp/
ISmartsuppRepository.cs`) — e.g. `Task<SmartsuppConversation?> FindConversationAsync(string
conversationId, CancellationToken cancellationToken)` — that performs this exact lookup (tracked, not
`AsNoTracking`, since the handler mutates the returned entity's `ContactId`/`SyncedAt` before calling
`UpsertConversationAsync`). Implement it in `SmartsuppRepository`. Update
`RefreshOrphanContactsHandler` to call the new repository method instead of `_db.SmartsuppConversations
...`, and remove its `ApplicationDbContext _db` field, constructor parameter, and the `using
Anela.Heblo.Persistence;` / `using Microsoft.EntityFrameworkCore;` imports that become unused. Note
`_db.ChangeTracker.Clear()` in the `catch` block also depends on the raw context — either drop it (no
longer needed once the handler holds no tracked `DbContext` reference itself) or, if a per-iteration
tracker reset is still required for `_repository`'s underlying context, expose that need through the
repository (do not reintroduce a raw `ApplicationDbContext` field to satisfy it).

**Acceptance criteria:**
- `RefreshOrphanContactsHandler` no longer references `ApplicationDbContext` in any form (field,
  constructor parameter, or method body) and no longer imports
  `Anela.Heblo.Persistence`/`Microsoft.EntityFrameworkCore`.
- `ISmartsuppRepository` gains exactly the one new method needed for this lookup — no speculative
  additions.
- Existing per-conversation try/catch/continue error handling and logging in the loop is preserved
  behaviorally (failed conversations still counted in `response.Failed`/`FailedIds`, don't abort the
  loop).

### FR-6: Move the webhook-audit write contract out of `Anela.Heblo.Persistence` and update its controller consumer

Delete `Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs` and
`SmartsuppWebhookAuditWriter.cs` (superseded by `ISmartsuppWebhookAuditRepository` /
`SmartsuppWebhookAuditRepository` from FR-1/FR-2). Update `SmartsuppWebhookController` (`Anela.Heblo.API/
Controllers/SmartsuppWebhookController.cs`) to inject `ISmartsuppWebhookAuditRepository` instead of
`ISmartsuppWebhookAuditWriter`, calling the equivalent `CreateAsync`/`UpdateOutcomeAsync` (or
renamed-equivalent) methods at each of its four existing call sites (signature-missing/mismatch,
malformed-JSON, app-id-mismatch, and the success/failure outcome update after dispatch). Remove the
now-unused `using Anela.Heblo.Persistence.Smartsupp;` import and replace it with `using
Anela.Heblo.Domain.Features.Smartsupp;` (already present) as needed for the new interface's namespace.

**Acceptance criteria:**
- `Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs` and
  `SmartsuppWebhookAuditWriter.cs` no longer exist.
- `SmartsuppWebhookController.cs` contains no `using Anela.Heblo.Persistence` of any form (feature-level
  or otherwise) and injects `ISmartsuppWebhookAuditRepository`.
- All four existing audit-write call sites in the controller (signature-missing, signature-mismatch,
  malformed-JSON, app-id-mismatch, plus the post-dispatch success/failure outcome update) behave
  identically — same fields populated, same order of operations relative to the HTTP response returned.

### FR-7: Update or replace existing unit/integration tests for the moved classes

Every test file under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/` that currently
constructs an in-memory `ApplicationDbContext` and passes it directly to the handler/job/writer under
test must be updated to match the new constructor signatures:

- `ListWebhookAuditHandlerTests`, `GetWebhookAuditEntryHandlerTests`, `ReplayWebhookEventHandlerTests`,
  `SmartsuppWebhookAuditCleanupJobTests` — these currently new up an in-memory `ApplicationDbContext`
  and construct the handler/job with it directly. After the refactor they must either (a) construct a
  real `SmartsuppWebhookAuditRepository` wrapping the in-memory `ApplicationDbContext` and pass *that*
  to the handler/job (preserves the existing EF-backed assertion style with minimal churn), or (b) move
  to `Mock<ISmartsuppWebhookAuditRepository>` (matches the pattern the issue calls for — handlers
  testable without EF). Prefer (b) where the test is really exercising handler logic (list-handler's
  clamp math, replay-handler's re-dispatch + field updates, cleanup-job's log lines) and keep an
  EF-backed test only for the repository implementation itself (see next bullet). This choice is left to
  the architect/planner to finalize per-file; both are acceptable as long as no handler test still
  depends on `ApplicationDbContext` after the change.
- `SmartsuppWebhookAuditWriterTests` — becomes (or is replaced by) `SmartsuppWebhookAuditRepositoryTests`
  covering the same `CreateAsync`/`UpdateOutcomeAsync` behavior, plus new coverage for the
  list/get/replay-stamp/purge methods absorbed from the four handlers/job (an EF-backed test is
  appropriate here since this is the persistence-layer implementation itself).
- `SmartsuppWebhookAuditControllerTests` — integration test using `HebloWebApplicationFactory` and real
  DB seeding; should need no logic change since it exercises the controller through MediatR end-to-end,
  only verify it still compiles/passes against the new wiring.
- `RefreshOrphanContactsHandler`'s existing tests (if any — confirm during implementation) must be
  updated the same way: replace direct `ApplicationDbContext` use with the new
  `ISmartsuppRepository.FindConversationAsync` method (mocked or via a real repository, consistent with
  that handler's existing test style for its other `ISmartsuppRepository` calls).

**Acceptance criteria:**
- No test file constructs a handler, job, or the audit repository's *consumers* by passing an
  `ApplicationDbContext` directly, except tests that specifically target the new
  `SmartsuppWebhookAuditRepository`/`SmartsuppRepository` implementations themselves.
- All existing test assertions (row ordering, filter behavior, 404 mapping, replay side effects,
  retention cutoff, presence-purge interplay) are preserved with equivalent coverage.
- Full existing test suite for the Smartsupp feature area passes.

## Non-Functional Requirements

### NFR-1: No behavior change

This is a structural refactor only. HTTP responses, DTO shapes, MediatR request/response contracts,
error codes, logging content (log lines may move from handler/job to repository if that's a natural
consequence of relocating the code they describe, but must retain equivalent information), retention
window (7 days), pagination clamp (`MaxTake = 200`), and query filters/ordering must all remain
identical pre- and post-refactor.

### NFR-2: Testability

After the refactor, `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`,
`ReplayWebhookEventHandler`, `SmartsuppWebhookAuditCleanupJob`, and `RefreshOrphanContactsHandler` must
be unit-testable using a mocked `ISmartsuppWebhookAuditRepository`/`ISmartsuppRepository` alone, with no
EF Core or `ApplicationDbContext` dependency in the test's construction of the unit under test.

### NFR-3: No new module-boundary or persistence-binding violations

The new repository binding must live in `SmartsuppModule.cs`, not `PersistenceModule.cs` (ADR-004),
and must not trip `PersistenceModuleTests.AddPersistenceServices_RegistersNoRepositoryBindings`. The
new Domain interface must not introduce a dependency from `Anela.Heblo.Domain` on
`Anela.Heblo.Persistence` or EF Core types.

## Data Model

No schema changes. `SmartsuppWebhookAuditEntry` (existing entity, `Anela.Heblo.Domain/Features/
Smartsupp/SmartsuppWebhookAuditEntry.cs`) and its EF mapping (`SmartsuppWebhookAuditEntryConfiguration`,
`Anela.Heblo.Persistence/Smartsupp/`) are unchanged. This is a pure access-layer relocation — no new
tables, columns, or migrations.

## API / Interface Design

No public HTTP API changes. Internal interface changes only:

- **New**: `Anela.Heblo.Domain.Features.Smartsupp.ISmartsuppWebhookAuditRepository` (Domain contract).
- **New**: `Anela.Heblo.Persistence.Smartsupp.SmartsuppWebhookAuditRepository` (implementation).
- **Removed**: `Anela.Heblo.Persistence.Smartsupp.ISmartsuppWebhookAuditWriter` and its implementation
  `SmartsuppWebhookAuditWriter`.
- **Extended**: `Anela.Heblo.Domain.Features.Smartsupp.ISmartsuppRepository` gains one new
  conversation-lookup method (name TBD by architect/planner, e.g. `FindConversationAsync`).
- **Changed constructors** (no behavior/signature change to public request/response DTOs):
  `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`, `ReplayWebhookEventHandler`,
  `SmartsuppWebhookAuditCleanupJob`, `RefreshOrphanContactsHandler`, `SmartsuppWebhookController`.

## Dependencies

- Existing `ApplicationDbContext` and its `SmartsuppWebhookAuditEntries`/`SmartsuppConversations`
  `DbSet`s (unchanged).
- Existing `SmartsuppRepository`, `SmartsuppPresenceRepository`, `SmartsuppModule.cs` as the patterns to
  follow.
- ADR-001, ADR-002, ADR-004 in `docs/architecture/development_guidelines.md`.
- No new NuGet packages, no new external services.

## Out of Scope

- Splitting `ApplicationDbContext` into a per-module context (ADR-001 Phase 2) — this issue only removes
  the blocker for Smartsupp; the actual Phase 2 migration is a separate, repo-wide effort.
- Any change to the webhook HMAC verification, event processing/reaction pipeline, presence tracking, or
  any other Smartsupp sub-feature not named in the Functional Requirements above.
- Any change to the `SmartsuppWebhookAuditController`'s HTTP contract, routes, or authorization.
- The standalone `tools/SmartsuppWebhookReplay` console tool (talks to the HTTP API, not to
  `ApplicationDbContext` — unaffected by this refactor).
- Renaming or restructuring `ISmartsuppRepository`'s existing methods beyond the one addition in FR-5.

## Open Questions

None.

## Status: COMPLETE
