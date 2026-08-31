# Architecture Review: Remove unused `IJournalRepository.GetEntriesByProductAsync`

## Skip Design: true

## Architectural Fit Assessment

This is a pure dead-code removal confined entirely to the Journal module's own files: the domain repository interface, its single Persistence implementation, and that module's own integration test class. It touches no controller, no MediatR handler, no contract/DTO, and no frontend code — so it cannot violate any module boundary, and there is no "fit" question beyond "does removing this break anything." I verified directly against the code in this worktree:

- `IJournalRepository` (`backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`, lines 27-30) declares `GetEntriesByProductAsync`.
- `JournalRepository` (`backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`, lines 139-151) is the **only** class implementing `IJournalRepository` in the entire repo — a grep for `IJournalRepository` across `backend/` shows five MediatR handlers and one DI module *consuming* the interface, plus their handler tests, but no second implementation and no in-memory/fake implementer. There is nothing else to update to keep the interface contract satisfied.
- A repo-wide grep for `GetEntriesByProductAsync` across `backend/` and `frontend/` returns exactly three files: the interface, the implementation, and `JournalRepositoryIntegrationTests.cs` (6 of its 18 `[Fact]`s). No handler, controller, or frontend code calls it.
- The equivalent capability is already live in production: `SearchJournalEntriesHandler` → `IJournalRepository.SearchEntriesAsync(...)` applies the identical `productCodePrefix.StartsWith(pa.ProductCodePrefix)` filter (lines 99-104 of `JournalRepository.cs`), and the frontend's `useJournalEntriesByProduct` hook (`frontend/src/api/hooks/useJournal.ts`, lines 191-215) already goes through that path via the generated `journal_SearchJournalEntries` client method, not through `GetEntriesByProductAsync`.
- `CreateEntryWithFamily` (the test helper at line 668 of the test file) is used exclusively inside the one test being deleted (`GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`, lines 173-175) — confirmed by grep, no other caller.

This aligns with the project's own stated boundary rules in `docs/architecture/development_guidelines.md` (module independence, no shared repositories across modules) simply by virtue of not crossing any boundary. There are no design-system, layout, or component implications — `docs/design/*` and `docs/architecture/filesystem.md` have no Journal-specific guidance to reconcile against, since nothing about directory layout or naming conventions changes.

## Proposed Architecture

### Component Overview

No new or restructured components. Three existing files shrink; nothing is added, moved, or renamed.

```
Domain/Features/Journal/IJournalRepository.cs        [interface: remove 1 method]
Persistence/Journal/JournalRepository.cs              [impl:      remove 1 method]
test/.../JournalRepositoryIntegrationTests.cs          [tests:     remove 6 [Fact]s (+ orphaned helper)]
        │
        ▼ (unaffected — already the production path)
SearchJournalEntriesHandler ──▶ IJournalRepository.SearchEntriesAsync(productCodePrefix, ...)
        ▲
useJournalEntriesByProduct (frontend hook) ──▶ journal_SearchJournalEntries (generated client)
```

### Key Design Decisions

#### Decision 1: Delete outright vs. deprecate-then-remove
**Options considered:**
- Mark `[Obsolete]` for a release cycle before deleting.
- Delete immediately.

**Chosen approach:** Delete immediately in this change.

**Rationale:** `[Obsolete]` exists to give external/other-team consumers a migration window. This is an internal domain interface with a single implementation and zero production callers, in a solo-developer + AI-review repo with no external API surface exposed by this method (it was never behind a controller). There is no consumer to warn. A deprecation cycle would only prolong carrying dead surface area — exactly the cost the brief is removing. Delete now.

#### Decision 2: Scope of test removal
**Options considered:**
- Remove only the 6 named `[Fact]` methods, leave everything else untouched.
- Also re-derive/replace lost coverage (e.g. add prefix-matching or soft-delete-exclusion assertions to the `SearchEntriesAsync` test group if not already present).

**Chosen approach:** Remove exactly the 6 named tests and, conditionally, the now-orphaned `CreateEntryWithFamily` helper. Do not add replacement tests.

**Rationale:** Both the finding and the spec explicitly confirm the same behaviors (prefix matching, family-entry matching, soft-delete exclusion) are already independently covered by the `SearchEntriesAsync`/`productCodePrefix` test group in the same file (referenced around line 624, the `Searchable live` / `Searchable deleted` case). Adding new tests here would be scope creep on a task defined as removal-only, and would duplicate coverage that already exists for the code path that actually ships. This matches the spec's explicit Out-of-Scope statement.

## Implementation Guidance

### Directory / Module Structure

No new files, no new directories. Edit in place:
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`

### Interfaces and Contracts

Remove exactly this member from `IJournalRepository` (lines 27-30):

```csharp
Task<List<JournalEntry>> GetEntriesByProductAsync(
    string productCode,
    CancellationToken cancellationToken = default);
```

Leave `GetEntriesAsync` and `SearchEntriesAsync` signatures byte-for-byte unchanged — nothing about them is in scope. Remove the corresponding implementation block (lines 139-151) from `JournalRepository`, and nothing else in that class. No `contracts/` DTOs are involved (this interface lives in `Domain/`, not a module's `contracts/` folder), so `development_guidelines.md`'s DTO/contract rules don't apply here — this is a straightforward internal removal, not a public contract change.

### Data Flow

Unaffected. The only live "journal entries for a product" flow remains:

```
Frontend: useJournalEntriesByProduct(productCode)
   → client.journal_SearchJournalEntries({ productCodePrefix: productCode, pageSize: 100, sortBy: "entryDate", sortDirection: "desc" })
Backend: SearchJournalEntriesHandler
   → IJournalRepository.SearchEntriesAsync(..., productCodePrefix, ...)
   → EF query: Where(x => x.ProductAssociations.Any(pa => productCodePrefix.StartsWith(pa.ProductCodePrefix)))
```

This diagram is provided only to confirm the removal doesn't touch it — no code changes are proposed here.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A second `IJournalRepository` implementation (test double, alternate adapter) exists somewhere not caught by grep and would fail to compile | Low | Already verified: `grep -rln "IJournalRepository" backend` shows exactly one class (`JournalRepository`) implementing it; everything else is a consumer. Developer should re-run this grep after editing, before considering the task done (this is FR-2's own acceptance criterion). |
| `CreateEntryWithFamily` helper is left orphaned, or removed while still used elsewhere | Low | Verified now: it is used only inside the one test (`GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`) being deleted. Developer should re-grep for `CreateEntryWithFamily` in the test file after deleting the six tests, per FR-3's acceptance criterion, before deciding whether to remove it. |
| Removing 6 tests silently drops real coverage of prefix/family/soft-delete-exclusion behavior for the *live* code path | Low | That behavior is exercised by `SearchEntriesAsync`-focused tests already present in the same file (the `productCodePrefix` / soft-delete assertions near line 624). No new tests are needed; nothing to mitigate beyond confirming those tests still exist post-edit. |
| `dotnet build` / `dotnet format` surfaces an unrelated warning as a build-breaking issue in CI-adjacent tooling | Low | Standard validation gate from `CLAUDE.md` (`dotnet build` + `dotnet format`) applies as-is; no special handling needed for a removal of this size. |

## Specification Amendments

None. The spec (`spec.r1.md`) is accurate, fully verified against the current code, and already scoped correctly (FR-1 through FR-4, explicit Out of Scope list). No architectural concern requires expanding or narrowing it.

## Prerequisites

None. No migration, no config, no infrastructure change, no feature flag. This can be implemented directly against current `main`/branch state.
