# Architecture Review: Move Smartsupp contact-enrichment REST call out of SmartsuppRepository

## Skip Design: true

## Architectural Fit Assessment
This is a pure backend refactor with no UI/UX surface — no new screens, components, or visual
changes. It directly enforces a rule already codified in `docs/architecture/development_guidelines.md`
("Persistence Guidelines") and `docs/architecture/filesystem.md` (third-party clients live in
`Adapters/`, orchestrated by the Application layer), and it has a direct precedent already merged
in this repo: issue #3731 moved `AnalyticsRepository`'s misplaced logic out of Persistence the same
way. `RefreshOrphanContactsHandler` (`Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`)
already injects `ISmartsuppApiClient` directly into an Application-layer handler and calls
`ISmartsuppRepository` only to persist — that is the exact shape this change generalizes into a
reusable service. The module's other Persistence classes (`SmartsuppPresenceRepository`,
`SmartsuppWebhookAuditWriter`) do not touch `ISmartsuppApiClient`, confirming `SmartsuppRepository`
is the outlier, not the pattern.

`SmartsuppModule.cs` already registers `ISmartsuppRepository`, `ISmartsuppPresenceRepository`, and
all seven `ISmartsuppWebhookReaction` implementations in one place — the DI change here is additive
and mechanical, no restructuring of that file's shape needed.

## Proposed Architecture

### Component Overview
```
Before:
  ProcessWebhookEventHandler
        |
        v
  {Reaction}.HandleAsync ──────► ISmartsuppRepository.UpsertConversationAsync
                                          │
                                          ├─ EF read (local contact lookup)
                                          ├─ ISmartsuppApiClient.GetContactAsync  ◄── VIOLATION
                                          ├─ EF write (staged contact, raw SQL)
                                          └─ raw SQL upsert (conversation)

After:
  ProcessWebhookEventHandler
        |
        v
  {Reaction}.HandleAsync
        │
        ├─► ISmartsuppContactEnricher.EnrichContactAsync(conversation)
        │        │
        │        ├─ (local contact already resolved on conversation? skip)
        │        ├─ ISmartsuppApiClient.GetContactAsync         [Application → Adapters]
        │        ├─ ISmartsuppRepository.UpsertContactAsync     [Application → Persistence]
        │        └─ returns mutated conversation (ContactId cleared on failure)
        │
        └─► ISmartsuppRepository.UpsertConversationAsync(conversation)
                 │
                 ├─ EF read (local contact lookup, for hydration only — unchanged)
                 └─ raw SQL upsert (conversation)                [pure persistence]
```
`RefreshOrphanContactsHandler` gets the same `EnrichContactAsync` call inserted between its
`local.ContactId = remote.ContactId` assignment and its existing `UpsertConversationAsync` call —
it already owns `ISmartsuppApiClient` and `ISmartsuppRepository`, so this is a one-line insertion,
not a new dependency.

### Key Design Decisions

#### Decision 1: New `ISmartsuppContactEnricher` vs. inlining enrichment into each reaction
**Options considered:**
- (a) Give `ISmartsuppContactEnricher` a single `EnrichContactAsync` method, injected into the 7
  reactions + `RefreshOrphanContactsHandler`.
- (b) Inline the fetch-and-map logic directly into each reaction's `HandleAsync`.
- (c) Push enrichment into `ProcessWebhookEventHandler` itself, before dispatching to the reaction.

**Chosen approach:** (a).
**Rationale:** (b) duplicates ~15 lines of fetch/map/fail-open logic across 7 call sites — a clear
DRY violation the codebase doesn't otherwise tolerate (compare `ContactUpsertWithBackfillReactionBase`,
which exists precisely to share logic across contact reactions). (c) doesn't work: not every
reaction touches a conversation with a possibly-unlinked contact in the same way, and
`ConversationReplyReactionBase`'s message-only branch must not trigger enrichment — the *reaction*
is the right place to decide whether enrichment applies, matching today's call graph where only
`UpsertConversationAsync` callers were affected.

#### Decision 2: Where enrichment decides to clear `ContactId` on failure
**Options considered:**
- (a) `SmartsuppContactEnricher` owns the fail-open decision (mutates `conversation.ContactId = null`
  on REST failure/null, matching today's behavior exactly) before returning to the caller.
- (b) `SmartsuppRepository.UpsertConversationAsync` keeps a "wipe if contact absent" branch, but the
  enricher supplies the missing contact ahead of time so the repository's own lookup already finds it.

**Chosen approach:** (a).
**Rationale:** (b) still leaves persistence deciding a business rule ("what do we do when the contact
can't be resolved") — it just makes the repository's local-lookup-miss path silently correct *by
construction* rather than removing the coupling. It's fragile: if a future caller invokes
`UpsertConversationAsync` without going through the enricher first (easy to do — nothing enforces
call order across a plain constructor-injected dependency), the repository would persist a
`ContactId` pointing at a row that was never written, an FK-integrity gap. (a) makes the repository
correct on its own terms (persist exactly what's given, no hidden clearing) and puts the
"can't resolve → clear" business rule where all other Smartsupp business rules already live: the
Application layer's reactions/handlers.

#### Decision 3: Reuse `SmartsuppRepository.UpsertContactAsync` from the enricher, don't duplicate raw SQL
**Options considered:**
- (a) `SmartsuppContactEnricher` calls `ISmartsuppRepository.UpsertContactAsync(contact, ct)` — the
  interface method already exists and is already called from the Application layer today
  (`ContactUpsertWithBackfillReactionBase.HandleAsync:19`).
- (b) Add a second write path (e.g. a raw ADO.NET call) directly in the enricher.

**Chosen approach:** (a).
**Rationale:** (b) duplicates the raw-SQL INSERT ... ON CONFLICT logic and its
`memory/gotchas/raw-sql-insert-must-match-ef-mapping.md` maintenance burden. (a) is exactly the
existing, already-Application-layer-callable contract; no interface change needed.

## Implementation Guidance

### Directory / Module Structure
- **New file:** `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs`
  — interface + implementation `SmartsuppContactEnricher` (co-locate in the same file, matching
  this module's existing convention, e.g. `ISmartsuppKnowledgeSource.cs` pairs interface +
  usage in `Contracts/`; `KnowledgeBaseSmartsuppKnowledgeSource.cs` shows impl-in-`Infrastructure/`
  is already this module's pattern for cross-cutting Smartsupp integration code). Keep the contact
  DTO→entity mapping (`MapContactDataToEntity`) as a `private static` or `internal static` method
  on `SmartsuppContactEnricher`, moved verbatim from `SmartsuppRepository.cs:336-352` including the
  `DateTimeKind.Utc` comment — do not silently drop that comment, it documents a previously-hit bug
  (`memory/gotchas` territory).
- **Modified file:** `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — remove
  `ISmartsuppApiClient` field/ctor param, remove `TryFetchAndStageContactAsync`, remove
  `MapContactDataToEntity`, remove the `ContactId = null` wipe branch inside `UpsertConversationAsync`.
- **Modified files (7 reactions + 1 handler):**
  - `Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationOpenedReaction.cs`
  - `.../ConversationRatedReaction.cs`
  - `.../ConversationClosedReaction.cs`
  - `.../ConversationClosedByContactReaction.cs`
  - `.../ConversationAgentAssignedReaction.cs`
  - `.../ConversationAgentUnassignedReaction.cs`
  - `.../ConversationReplyReactionBase.cs`
  - `Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
- **Modified file:** `Application/Features/Smartsupp/SmartsuppModule.cs` — add
  `services.AddScoped<ISmartsuppContactEnricher, SmartsuppContactEnricher>();` next to the existing
  `ISmartsuppRepository` registration.
- **Test moves:** new `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactEnricherTests.cs`
  (ported from the REST-behavior tests currently in `SmartsuppRepositoryUnknownContactFetchTests.cs`),
  mocking `ISmartsuppApiClient` and `ISmartsuppRepository` — no `ApplicationDbContext` needed, which
  is itself proof the coupling is gone. `ListOrphanContactConversationIdsAsync_ReturnsOnlyConversationsWithNoNameOrEmail`
  stays where it is (pure EF, unaffected).

### Interfaces and Contracts
```csharp
// Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs
namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure;

public interface ISmartsuppContactEnricher
{
    /// <summary>
    /// Resolves conversation.ContactId to a local SmartsuppContact, fetching and staging it via
    /// REST when not already known locally. On any failure to resolve (REST error or REST returns
    /// null), clears conversation.ContactId so the caller persists an unlinked conversation
    /// (fail-open — matches pre-refactor SmartsuppRepository behavior).
    /// </summary>
    Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);
}
```
`ISmartsuppApiClient` is **unchanged**. `ISmartsuppRepository` gains one additive method
(`ContactExistsAsync`, see Decision 4) — existing members are untouched, so this is additive, not a
breaking interface change; any test double implementing `ISmartsuppRepository` needs one new member
implemented, which the task plan must call out. `SmartsuppRepository`'s constructor signature
changes (drops one parameter), which is source-compatible everywhere except direct
`new SmartsuppRepository(...)` call sites — grep confirms all such sites are test files listed
above; no production code constructs it directly (DI-only in `SmartsuppModule.cs`).

### Data Flow
1. Webhook POST → `ProcessWebhookEventHandler.Handle` → dispatches to the matching
   `ISmartsuppWebhookReaction`.
2. Reaction maps the payload to a `SmartsuppConversation` (unchanged, via `SmartsuppPayloadMapper`).
3. Reaction calls `_enricher.EnrichContactAsync(conversation, ct)` — this is where the REST call
   now happens, if it happens at all. Returns the same or a mutated conversation.
4. Reaction calls `_repository.UpsertConversationAsync(conversation, ct)` — pure DB write, no I/O
   beyond Postgres.
5. `ProcessWebhookEventHandler` calls `_repository.SaveChangesAsync(ct)` once, exactly as today —
   the enricher's `UpsertContactAsync` raw-SQL call already executes synchronously inline (it's not
   `SaveChanges`-deferred, since it uses `ExecuteSqlInterpolatedAsync` directly, same as today), so
   no transactional grouping is lost.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A reaction forgets to call the enricher before `UpsertConversationAsync`, silently losing contact enrichment for that event type | Medium | Enumerate all 7 call sites explicitly in this review and in the task plan; add/keep one test per reaction (or a table-driven test) asserting the enricher is invoked before upsert, mirroring existing `ConversationReactionsTests` structure |
| `RefreshOrphanContactsHandler`'s entire purpose (re-trigger enrichment for orphaned rows) silently breaks if the enricher call is missed there | High | Call out explicitly as FR-4 in the spec; the existing `RefreshOrphanContacts` tests (if any assert `apiClient.GetContactAsync` is invoked) must be re-run/extended to assert the *enricher* path, not the old repository path |
| Behavior drift in the fail-open wipe logic during the move (e.g. clearing `ContactId` before vs. after setting `ContactName`/`ContactEmail`) | Medium | Port the existing `SmartsuppRepositoryUnknownContactFetchTests` assertions almost verbatim onto `SmartsuppContactEnricherTests` — same input/output pairs, different class under test, per FR-5 |
| Missed DI registration causes a runtime resolution failure for all 7 reactions + the orphan handler simultaneously | Low | `dotnet build` won't catch missing DI registration (only a runtime `InvalidOperationException` will) — the task plan must include running the existing webhook integration test (`SmartsuppWebhookControllerTests`) end-to-end, not just unit tests, since that's what would actually exercise container resolution |

## Specification Amendments
The spec was corrected during this review cycle: FR-3's original text proposed deciding whether to
skip the REST fetch based on whether the incoming `SmartsuppConversation` DTO already carried
non-null `ContactName`/`ContactEmail`. That is **not** equivalent to today's behavior —
`SmartsuppPayloadMapper.MapConversation` already reads `contact_name`/`contact_email` straight off
many webhook payloads, so a DTO-field check would silently skip fetching-and-persisting a
brand-new contact into `SmartsuppContacts` whenever Smartsupp happens to inline those fields on the
event, starving that table for such contacts and breaking anything that joins on it later
(`GetSmartsuppContactShoptetInfoHandler`, `KnowledgeBaseSmartsuppKnowledgeSource`). `spec.r1.md`
FR-3 has been corrected to require an actual row-existence check
(`ISmartsuppRepository.ContactExistsAsync`, a new interface method — see Decision 4 below) instead,
matching today's `SmartsuppRepository.UpsertConversationAsync` local-lookup exactly. This review's
"Interfaces and Contracts" and Decision 2/3 sections already assumed the corrected shape (the
existence check happening against the repository, not the DTO) — no further amendment needed
beyond the one already folded into `spec.r1.md`.

Separately: FR-3's exception handling should catch `Exception` broadly (spec already states this)
to avoid narrowing the fail-open contract as a side effect of the refactor — this review confirms
that's correct and should not be "improved" during implementation, per CLAUDE.md's surgical-changes
rule.

#### Decision 4: `ContactExistsAsync` as a new, minimal `ISmartsuppRepository` method
**Options considered:**
- (a) Add `Task<bool> ContactExistsAsync(string contactId, CancellationToken ct)` to
  `ISmartsuppRepository` — a plain `AnyAsync` existence check, no business logic.
- (b) Have `SmartsuppContactEnricher` call `ApplicationDbContext` directly for the existence check.
- (c) Have `SmartsuppContactEnricher` always call REST and let `UpsertConversationAsync`'s existing
  local hydration silently no-op when the contact turns out to already exist (i.e., always fetch,
  never skip).

**Chosen approach:** (a).
**Rationale:** (b) reintroduces a direct `ApplicationDbContext` dependency into the Application
layer, which is exactly the kind of layering violation this issue exists to remove — the module's
own `RefreshOrphanContactsHandler` currently does this (injects `ApplicationDbContext` directly)
and is not a pattern to extend, not one to hold up as precedent. (c) changes observable behavior:
it would call Smartsupp REST on *every* conversation upsert with a contact_id, even when the
contact is already fully known locally — a functional regression from today's skip-when-known
behavviour (covered by `DoesNotCallRest_WhenContactAlreadyInDb`), and unacceptable under NFR-1
(behavior parity). (a) is the smallest correct addition: it's a read-only, no-business-logic method
squarely inside `SmartsuppRepository`'s existing responsibility (it already has
`ListOrphanContactConversationIdsAsync`, an equally simple predicate-style read), and it keeps
`SmartsuppContactEnricher`'s only two dependencies as `ISmartsuppApiClient` and
`ISmartsuppRepository`, matching NFR-2.

## Prerequisites
None. No migration, no new configuration, no new external dependency. This can start immediately —
it's a same-assembly-boundary code move with one new interface and one new DI registration line.
