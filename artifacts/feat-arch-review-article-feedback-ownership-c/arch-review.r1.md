Got everything I need. Writing the architecture review now.

# Architecture Review: Stable User Identifier for Article Ownership

## Skip Design: true

This is a backend correctness/security fix with no new UI components, screens, or visual decisions. The downstream UI implication (an OID may render in `GenericFeedbackDetailModal.tsx:51` after migration) is real but the spec scopes display changes out — design work, if any, is a follow-up.

## Architectural Fit Assessment

The change brings `Article` into line with the **established pattern already implemented in the Leaflet module**: `SubmitLeafletFeedbackHandler.cs:36` compares `generation.UserId` directly against `currentUser.Id`. `Article` is the outlier — it persists `currentUser.Name` and compares by name. Aligning it with `GetIdentifier()` (defined at `CurrentUserExtensions.cs:16` but currently unused anywhere) removes inconsistency rather than introducing a new pattern.

Integration points touched:
- `GenerateArticleHandler.cs:46` — write site (1 line)
- `SubmitArticleFeedbackHandler.cs:34-36` — read/compare site (1 line + stale comment to remove)
- `Article` table data (existing rows hold display names)
- `GET /articles/feedback/list?requestedBy=…` filter (`ArticleRepository.cs:75-76`) — the URL parameter semantics flip from "filter by name" to "filter by identifier"; frontend already feeds it from `userId`, so no contract drift
- `GenericFeedbackDetailModal.tsx:48-51` — renders `requestedBy` raw; user-visible string changes from "John Doe" to an OID

The fix does **not** introduce a new architectural seam (no user-lookup service for runtime, no new abstraction). The runtime stays a string comparison.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────┐
│ HTTP request (Entra-authenticated)                               │
│       │                                                          │
│       ▼                                                          │
│  CurrentUserService ──────► CurrentUser { Id, Name, Email, … }   │
│  (reads "oid"/"sub" claim)                │                      │
│                                           │ .GetIdentifier()     │
│                                           ▼                      │
│                                     "abc-123" (stable string)    │
│                                           │                      │
│       ┌───────────────────────────────────┴─────────────────┐    │
│       │                                                     │    │
│       ▼ (write)                                             ▼    │
│  GenerateArticleHandler                       SubmitArticleFeedbackHandler
│  Article.RequestedBy = "abc-123"             if (article.RequestedBy != id) Forbidden
│       │                                             ▲            │
│       ▼                                             │            │
│  Articles table (Postgres)  ──── RequestedBy ───────┘            │
└──────────────────────────────────────────────────────────────────┘

Migration (one-off, offline):
  Articles.RequestedBy (= display name) ──► Graph lookup ──► Articles.RequestedBy (= OID)
                                            │
                                            └─► ambiguous/missing: leave + log
```

### Key Design Decisions

#### Decision 1: Use `GetIdentifier()` extension vs. `currentUser.Id` directly
**Options considered:**
- (A) Use `currentUser.GetIdentifier()` → returns `Id ?? Email ?? "system"`.
- (B) Use `currentUser.Id` directly (matches the Leaflet implementation at `SubmitLeafletFeedbackHandler.cs:32`).

**Chosen approach:** (A) `GetIdentifier()` — per the spec.

**Rationale:** The extension exists explicitly to centralise stable-ID resolution and gives a deterministic fallback if `Id` ever turns up null (e.g. in a future auth path that exposes only `preferred_username`). This pushes the codebase toward consistent use of the helper rather than two near-identical patterns. **However**, store and compare must use exactly the same expression — never store via `GetIdentifier()` and compare via `Id`, or vice versa, because the fallback path could change between the two calls.

#### Decision 2: Migration vehicle — EF data migration vs. standalone CLI
**Options considered:**
- (A) EF Core data migration in `backend/src/Anela.Heblo.Persistence/Migrations/`.
- (B) Standalone one-off script (a `dotnet run` console entry, or a SQL script driven by a Graph dump).

**Chosen approach:** (B) Standalone — a small `dotnet run` admin command that reads from the Articles table, resolves via `IGraphService`, writes back per row, and writes an unresolved-rows report.

**Rationale:** Migrations are documented as manual in `docs/architecture/development_guidelines.md` and the project rule "Database migrations are manual" (CLAUDE.md). EF data migrations also have no clean way to reach a Graph client without forcing infrastructure dependencies into the migration assembly, and they execute under the same `OnModelCreating` snapshot machinery as schema changes — coupling a Graph-dependent data fix to schema versioning is fragile. A standalone command can be re-run safely, logs unresolved rows, and is removable after execution. The standalone command also avoids dragging `Microsoft.Identity.Web`/Graph dependencies into `Anela.Heblo.Persistence`, which currently has none.

#### Decision 3: Resolve display name → identifier via existing `IGraphService` (extended) vs. a one-off lookup script
**Options considered:**
- (A) Extend `IGraphService` with `Task<UserDto?> ResolveByDisplayNameAsync(string name)`.
- (B) Use `IGraphService.GetGroupMembersAsync` for the marketing group, build an in-memory map, resolve from it.
- (C) Run a one-off SQL script driven by a Graph CSV exported manually.

**Chosen approach:** (B) — call `GetGroupMembersAsync` on the marketing group(s) whose members generate articles (the same group used for `MarketingReader` authorization), build a `displayName → id` dictionary, resolve from it.

**Rationale:** Graph's `$filter=displayName eq '…'` is slow and ambiguous by design. A single group fetch is one Graph call, caches naturally, and matches the existing access pattern. If a row's `RequestedBy` matches multiple group members, log and skip (NFR-3). If it matches zero, log and skip — those rows go to manual triage. Adding a new `ResolveByDisplayNameAsync` to `IGraphService` is YAGNI for a one-off operation.

#### Decision 4: Idempotency marker for the migration
**Options considered:**
- (A) Add a `RequestedByType` discriminator column ("name" vs. "id").
- (B) Detect already-migrated rows by shape: looks like a GUID or like an email → skip.
- (C) Track migrated row IDs in a side table or column comment.

**Chosen approach:** (B) — shape detection: if `RequestedBy` parses as a `Guid` (Entra OID format) or contains `'@'` (email fallback path), treat as already migrated.

**Rationale:** No schema change required (NFR consistency: "Column type and nullability are unchanged"). Display names in this codebase are Czech names with diacritics, never GUIDs, so the heuristic is safe in practice. The CLI logs the decision per row so any false positive is visible.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/
  GenerateArticle/GenerateArticleHandler.cs       # one-line change at :46
  SubmitFeedback/SubmitArticleFeedbackHandler.cs  # change at :36, delete stale comment at :34

backend/src/Anela.Heblo.Application/Features/Article/Admin/
  BackfillRequestedByCommand.cs    # NEW — one-off admin command (option: gate behind --enable flag in Program.cs)
  BackfillRequestedByReport.cs     # NEW — report struct (resolved / ambiguous / unresolved counts + per-row log)

backend/test/Anela.Heblo.Tests/Article/UseCases/
  GenerateArticleHandlerTests.cs        # update existing test at :73 — expect "user-id", not "John Doe"
  SubmitArticleFeedbackHandlerTests.cs  # update SetCurrentUser at :34-36 to seed Id; add NFR-1 collision test, NFR-2 rename test

docs/operations/   (or wherever runbooks live — check existing convention before creating)
  article-requestedby-backfill.md   # NEW — how to run the backfill, dry-run flag, rollback
```

### Interfaces and Contracts

No public API contract changes. The DTO `ArticleFeedbackSummary.RequestedBy` keeps its type and name — only the stored value's semantics change.

Internal contract additions for the backfill command:

```csharp
public sealed record BackfillResult(
    int Total,
    int Resolved,
    int Ambiguous,
    int Unresolved,
    int AlreadyMigrated,
    IReadOnlyList<UnresolvedRow> Unresolveds);

public sealed record UnresolvedRow(Guid ArticleId, string OriginalValue, string Reason);
```

The command must:
1. Accept a `--dry-run` flag (default true).
2. Take the Graph group ID(s) from configuration — do not hardcode.
3. Log per-row decisions at `Information` for resolved, `Warning` for ambiguous/unresolved.
4. Wrap the writes in a single transaction.

### Data Flow

**Generate (after change):**
```
POST /articles/generate
  → GenerateArticleHandler.Handle
  → currentUser = CurrentUserService.GetCurrentUser()
  → identifier = currentUser.IsAuthenticated ? currentUser.GetIdentifier() : null
  → Article { RequestedBy = identifier, … }  // identifier is e.g. "abc-123" (Entra OID)
  → repository.AddAsync + SaveChangesAsync
  → enqueue Hangfire job
```

**Submit feedback (after change):**
```
POST /articles/{id}/feedback
  → SubmitArticleFeedbackHandler.Handle
  → article = repository.GetForUpdateAsync(id)
  → user = currentUser.GetCurrentUser()
  → if (article.RequestedBy != user.GetIdentifier()) → Forbidden
  → (existing status / already-submitted checks unchanged)
  → article.PrecisionScore = …; SaveChangesAsync
```

**Backfill (one-off, manual run):**
```
dotnet run --project backend/src/Anela.Heblo.Admin -- backfill-articles --dry-run=false
  → load Articles where RequestedBy IS NOT NULL
  → for each row:
       if RequestedBy is GUID-shaped or contains '@' → skip (already migrated)
       else lookup in GraphGroupMembers map
            if 1 match → row.RequestedBy = match.Id; log Resolved
            else      → leave; log Ambiguous / Unresolved
  → SaveChanges (single transaction)
  → emit BackfillResult to stdout + persist UnresolvedRows to CSV
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Migration overwrites a row that was already migrated (e.g. someone re-runs the command after a partial deploy) | HIGH | Shape-based skip (Decision 4) + transactional execution + `--dry-run` default + explicit row-count diff printed before commit |
| Display-name collision in Graph group → ambiguous resolution silently picks one | CRITICAL | Migration **must not** pick when ambiguous; leave row untouched, surface in report (NFR-3). Add automated test for the ambiguous case in the command's unit tests |
| Rows that fail to resolve permanently lock out their original author | MEDIUM | Documented expected outcome in the runbook; provide a manual-fix SQL template; flag count surfaces in deploy verification |
| Frontend feedback detail modal (`GenericFeedbackDetailModal.tsx:51`) now displays an opaque OID instead of "Jan Novák" — UX regression | MEDIUM | Spec defers UI in scope. Track as follow-up: resolve OID → display name in `GetArticleFeedbackListHandler.cs:49` via the same Graph member map (cached). File a separate UX ticket before this ships, do not bury it |
| `GetIdentifier()` returns `"system"` if both `Id` and `Email` are null — an unauthenticated/anonymous path could write `"system"`, then any other anonymous path can submit feedback | HIGH | Existing guard `currentUser.IsAuthenticated ? GetIdentifier() : null` at the write site preserves null-store for anonymous. **Add a defensive guard at the read site too**: if `article.RequestedBy is null` → `Forbidden` (spec FR-2 already requires verifying this — confirm and harden) |
| Test seam: `SetCurrentUser` in existing tests at `SubmitArticleFeedbackHandlerTests.cs:34-36` builds `CurrentUser` with `Id: "id-of-" + name`, so the existing happy-path test will *accidentally* still pass after the change. Real bug coverage requires the NFR-1 collision test | HIGH | Mandatory new test: two `CurrentUser` instances with `Name = "Same Name"` but `Id = "id-A"` vs `Id = "id-B"`; second user must get `Forbidden`. Existing test rewrite is in scope, not new-test-only |
| Entra OID claim mapping not actually present in this app's auth pipeline | HIGH | `CurrentUserService.cs:26-28` already reads `ClaimTypes.NameIdentifier` / `sub` / `oid` → verified. No infrastructure work required. Add an integration test that hits the controller through `WebApplicationFactory` to confirm |
| Marketing group membership doesn't cover every historical article author (e.g. someone left the org) | MEDIUM | Migration logs them as Unresolved; runbook covers manual SQL fix using Graph search by displayName via Azure portal as a fallback |

## Specification Amendments

1. **FR-3 (migration) — change vehicle to a standalone admin command**, not an EF data migration. Update the spec's "EF Core migration (data migration only, no schema change)" reference under §API / Interface Design accordingly.

2. **FR-2 — explicit null-owner semantics**. The spec currently says "verify and document — likely `Forbidden`". Make it concrete: when `article.RequestedBy is null`, return `Forbidden`. Add a unit test. This closes a real exposure: a row created by an anonymous request would otherwise be claimable by another anonymous request whose `GetIdentifier()` returns `"system"`.

3. **FR-1 / FR-2 — store-vs-compare consistency invariant**. Add: "Both the write site and the read site must call `currentUser.GetIdentifier()` — never one and `currentUser.Id` at the other." This is a one-liner but worth pinning, because the Leaflet handler uses `.Id` directly and a developer following that pattern in Article would silently break ownership.

4. **FR-4 — explicitly list `ArticleRepository.GetFeedbackPagedAsync` (filter parameter) and `GetArticleFeedbackListHandler` (DTO field) as known read sites**. Their classification: filter by stored value (now an identifier), DTO field emits the stored value (now an identifier). Both are non-issues for backend logic but trigger the UX follow-up (display resolution).

5. **NFR-1 — clarify the collision test setup**: two `CurrentUser` records with identical `Name`, **different non-null `Id`**, must give different stored `RequestedBy` values; submitting feedback as the wrong-id user yields `Forbidden`. The existing `SubmitArticleFeedbackHandlerTests` `SetCurrentUser` helper at lines 34–36 already constructs distinct `Id`s by string concatenation — replace it with a more explicit fixture that the test reader cannot miss.

6. **Add a frontend UX follow-up item to "Out of Scope"** as a tracked follow-up rather than a side note: "Resolve `RequestedBy` (identifier) → display name in `GetArticleFeedbackListHandler` and `GenericFeedbackDetailModal` after this change ships." This is the user-visible regression and must not be lost.

## Prerequisites

- **Entra OID claim verified end-to-end.** `CurrentUserService.cs:26-28` already maps `oid`/`sub`/`NameIdentifier`. Confirm against a live staging token before deploying so no in-flight article is written with a `null` Id that silently degrades to email (or worse, `"system"`).
- **Marketing group ID configuration available.** The backfill command needs the Graph group(s) whose members historically generated articles. Confirm value in appsettings or environment configuration before the run.
- **`Microsoft.Graph` Application permission `User.Read.All` (or `GroupMember.Read.All`) granted.** Already in place per `GraphService.cs` usage (`GetGroupMembersAsync`), but re-verify in the target environment before running the backfill.
- **Row-count baseline.** Per NFR-4, run `SELECT count(*) FROM "Articles" WHERE "RequestedBy" IS NOT NULL;` on production before execution; surface in the runbook. If row count exceeds ~10k, batch the writes.
- **Backup of `Articles` table** taken immediately before the backfill (pg_dump of the single table is sufficient). The migration is reversible only via this backup, since the original display names are not preserved post-write.
- **Stale code-comment cleanup.** The comment at `SubmitArticleFeedbackHandler.cs:34` (`"RequestedBy stores currentUser.Name (set in GenerateArticleHandler). Compare by Name."`) is now actively misleading and must be removed in the same PR as the behaviour change.