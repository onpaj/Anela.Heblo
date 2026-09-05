# Specification: Remove unused `IJournalRepository.GetEntriesByProductAsync`

## Summary
`IJournalRepository.GetEntriesByProductAsync` is a dead method: it is declared on the domain repository interface, implemented in `JournalRepository`, and exercised by six integration test cases, but it has zero production callers. The equivalent capability — journal entries for a product, matched by prefix — is already served in production by `SearchJournalEntriesHandler` via `SearchEntriesAsync`'s `productCodePrefix` parameter, which the frontend's `useJournalEntriesByProduct` hook calls through the `journal_SearchJournalEntries` generated client method. This is a small cleanup task: delete the unused interface method, its implementation, and its dedicated test cases.

## Background
Verified directly against the code in this worktree:

- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` (lines 27-30) declares `Task<List<JournalEntry>> GetEntriesByProductAsync(string productCode, CancellationToken cancellationToken = default)`.
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` (lines 139-151) implements it: queries `JournalEntry` including `ProductAssociations`/`TagAssignments`, filters `Where(x => x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix)))`, and orders by `EntryDate desc, CreatedAt desc`.
- A repo-wide grep for `GetEntriesByProductAsync` across `backend/` and `frontend/` returns only: the interface, the implementation, and `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`. No MediatR handler, no controller, no other production code references it.
- `SearchJournalEntriesHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`) calls `IJournalRepository.SearchEntriesAsync(...)`, which (in `JournalRepository.cs`, lines 96-101) applies the same "requested code starts with stored prefix" logic: `query.Where(x => x.ProductAssociations.Any(pa => productCodePrefix.StartsWith(pa.ProductCodePrefix)))`.
- The frontend hook `useJournalEntriesByProduct` (`frontend/src/api/hooks/useJournal.ts`, lines 191-215) calls `client.journal_SearchJournalEntries(...)` passing the product code as `productCodePrefix`, `pageSize: 100`, `sortBy: "entryDate"`, `sortDirection: "desc"` — this is the live, in-use "entries for a product" code path, and it does not go through `GetEntriesByProductAsync`.
- `JournalRepositoryIntegrationTests.cs` has 18 `[Fact]` tests total; 6 of them directly exercise `GetEntriesByProductAsync`:
  - `GetEntriesByProductAsync_WithProductCodePrefix_ShouldFindMatchingEntries` (line 30)
  - `GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries` (line 60)
  - `GetEntriesByProductAsync_ProductStartsWithPrefix_ShouldMatchFamilyEntry` (line 114)
  - `GetEntriesByProductAsync_DifferentPrefix_ShouldNotMatch` (line 144)
  - `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries` (line 170)
  - `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults` (line 629)

Carrying an unused method on a domain repository interface is a real cost: every current and future implementation of `IJournalRepository` (test doubles, alternate persistence adapters) must satisfy it, and it signals a production code path that does not exist. Removing it is low-risk because there are no callers to migrate — only the test cases that specifically target the method need to go, and the behavior they cover (prefix matching, family-entry matching, soft-delete exclusion) is already independently covered for the `SearchEntriesAsync`/`productCodePrefix` path elsewhere in the same test file (confirmed present: `productCodePrefix` filtering and soft-delete-exclusion assertions exist for `SearchEntriesAsync` separately, e.g. around line 624's `Searchable live`/`Searchable deleted` case).

## Functional Requirements

### FR-1: Remove `GetEntriesByProductAsync` from the domain interface
Delete the method signature (lines 27-30) from `IJournalRepository` in `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`.

**Acceptance criteria:**
- `IJournalRepository` no longer declares `GetEntriesByProductAsync`.
- The interface still declares `GetEntriesAsync` and `SearchEntriesAsync` unchanged.

### FR-2: Remove the implementation from `JournalRepository`
Delete the `GetEntriesByProductAsync` method body (lines 139-151) from `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`.

**Acceptance criteria:**
- `JournalRepository` no longer defines `GetEntriesByProductAsync`.
- `JournalRepository` still compiles and implements `IJournalRepository` fully (no other members are touched).
- No other class in the codebase implements `IJournalRepository` (verify via search before/after removal) — if one exists, it must be updated too, but none was found during analysis.

### FR-3: Remove the dedicated integration test cases
Delete the six `[Fact]` test methods listed in Background from `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`:
`GetEntriesByProductAsync_WithProductCodePrefix_ShouldFindMatchingEntries`, `GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries`, `GetEntriesByProductAsync_ProductStartsWithPrefix_ShouldMatchFamilyEntry`, `GetEntriesByProductAsync_DifferentPrefix_ShouldNotMatch`, `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`, `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`.

**Acceptance criteria:**
- None of the six named test methods remain in the file.
- The `CreateEntryWithFamily` helper (used only by `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries` per current usage) is removed only if it becomes unused after the deletion; otherwise it is left in place. Verify with a search for `CreateEntryWithFamily` after deletion before removing it.
- All remaining tests in the file (the other 12, covering `GetEntriesAsync`, `SearchEntriesAsync`, and the sort matrix) are untouched and still compile.
- The full `JournalRepositoryIntegrationTests` class still builds and every remaining test passes.

### FR-4: No other references remain
Confirm (e.g. via repo-wide search) that after FR-1 through FR-3, no reference to `GetEntriesByProductAsync` remains anywhere in `backend/` or `frontend/`.

**Acceptance criteria:**
- `grep -rn "GetEntriesByProductAsync"` across the repository returns no matches.

## Non-Functional Requirements
N/A — this is a removal of dead code with no production callers; there is no behavior change to any live code path, so there are no new performance, security, scalability, or reliability considerations. Existing NFRs for the Journal module are unaffected.

## Data Model
N/A — no entity, DTO, or persistence schema changes. `JournalEntry` and `ProductAssociation` are untouched.

## API / Interface Design
No public HTTP API, MediatR request/response, or frontend-facing contract changes. `GetEntriesByProductAsync` was never exposed through a controller or handler, so removing it has no effect on `journal_SearchJournalEntries` or any other generated OpenAPI client method. The `useJournalEntriesByProduct` frontend hook (`frontend/src/api/hooks/useJournal.ts`) continues to use `SearchJournalEntries` unchanged.

## Dependencies
None. This change touches only the Journal module's domain interface, its Persistence implementation, and its own integration test file. No other module, service, or external dependency is involved.

## Out of Scope
- Adding a dedicated "get entries by product" endpoint/handler — the brief explicitly notes that if such a feature is wanted later, the correct order is to implement the handler first and add only what it needs to the interface at that time.
- Any change to `SearchEntriesAsync`, `SearchJournalEntriesHandler`, `SearchJournalEntriesRequest`/`Response`, or the frontend `useJournalEntriesByProduct` hook — these already cover the use case and are not modified.
- Any change to `JournalRepositoryIntegrationTests` beyond removing the six named test cases (and, conditionally, the now-unused `CreateEntryWithFamily` helper).
- Broader dead-code audit of the Journal module or other modules.

## Open Questions
None.

## Status: COMPLETE
