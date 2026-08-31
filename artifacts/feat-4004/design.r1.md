# Design: Remove unused `IJournalRepository.GetEntriesByProductAsync`

## Component Design

No new or restructured components. This is a subtractive change to three existing files in the Journal module; nothing is added, moved, or renamed, and no other component's contract changes.

### `IJournalRepository` (`backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`)
- **Responsibility:** Domain-level persistence contract for `JournalEntry` — retrieval, search, and mutation of journal entries.
- **Change:** Remove the `GetEntriesByProductAsync(string productCode, CancellationToken cancellationToken = default)` member (lines 27-30).
- **Contract after removal:** `GetEntriesAsync` and `SearchEntriesAsync` remain byte-for-byte unchanged — they are the interface's full retrieval surface going forward. `SearchEntriesAsync`'s `productCodePrefix` parameter already implements the same "requested product code starts with a stored `ProductCodePrefix`" matching logic that `GetEntriesByProductAsync` provided, so no capability is lost for any consumer.
- **Why safe:** `JournalRepository` is confirmed (by repo-wide grep in the architecture review) to be the sole class implementing this interface — no test double, in-memory fake, or alternate persistence adapter also implements it. Removing a member therefore cannot break a second implementer, because none exists. If one is ever introduced later, it would need to satisfy only the reduced (already-live) surface.

### `JournalRepository` (`backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`)
- **Responsibility:** EF Core implementation of `IJournalRepository`.
- **Change:** Remove the `GetEntriesByProductAsync` method body (lines 139-151) — the query against `JournalEntry`/`ProductAssociations`/`TagAssignments` filtered by `productCode.StartsWith(pa.ProductCodePrefix)` and ordered by `EntryDate desc, CreatedAt desc`.
- **Contract after removal:** The class continues to implement `IJournalRepository` fully; no other member is touched. `SearchEntriesAsync` (lines 96-101), which applies the equivalent `productCodePrefix.StartsWith(pa.ProductCodePrefix)` filter, is untouched and remains the sole production code path for "entries matching a product code prefix."
- **Why contract-preserving:** Deleting a method that is not part of any smaller/derived interface and has zero callers cannot change behavior observable by any existing caller — there are none. `SearchJournalEntriesHandler` never called `GetEntriesByProductAsync`; it has always used `SearchEntriesAsync`.

### `JournalRepositoryIntegrationTests` (`backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`)
- **Responsibility:** Integration test coverage for `JournalRepository`'s query methods against the persistence layer.
- **Change:** Remove the six `[Fact]` methods that exercise `GetEntriesByProductAsync` exclusively:
  - `GetEntriesByProductAsync_WithProductCodePrefix_ShouldFindMatchingEntries`
  - `GetEntriesByProductAsync_WithProductCode_ShouldFindFamilyEntries`
  - `GetEntriesByProductAsync_ProductStartsWithPrefix_ShouldMatchFamilyEntry`
  - `GetEntriesByProductAsync_DifferentPrefix_ShouldNotMatch`
  - `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`
  - `GetEntriesByProductAsync_WhenEntryIsSoftDeleted_ExcludesFromResults`
- **Conditional removal:** The `CreateEntryWithFamily` helper is removed only if, after deleting the six tests above, a grep for `CreateEntryWithFamily` in the file shows no remaining callers (per spec/arch-review, it is currently used only by `GetEntriesByProductAsync_MultipleProducts_ShouldFindCorrectFamilyEntries`).
- **Why coverage-preserving:** The 12 remaining tests in the file (covering `GetEntriesAsync`, `SearchEntriesAsync`, and the sort matrix) are untouched. The behaviors the six deleted tests targeted — prefix matching, family-entry matching, soft-delete exclusion — are independently exercised by the `SearchEntriesAsync`/`productCodePrefix` test group already present in the same file (e.g. the `Searchable live`/`Searchable deleted` case near line 624), so no coverage of the *live* code path is lost. This class must still build and every remaining test must still pass after the deletion.

### Data flow (unaffected, shown only to confirm no interaction)

```
Frontend: useJournalEntriesByProduct(productCode)
   → client.journal_SearchJournalEntries({ productCodePrefix: productCode, pageSize: 100, sortBy: "entryDate", sortDirection: "desc" })
Backend:  SearchJournalEntriesHandler
   → IJournalRepository.SearchEntriesAsync(..., productCodePrefix, ...)
   → EF query: Where(x => x.ProductAssociations.Any(pa => productCodePrefix.StartsWith(pa.ProductCodePrefix)))
```

`GetEntriesByProductAsync` was never part of this flow; nothing above changes as a result of this removal.

## Data Schemas

No schema changes of any kind. This is a pure code deletion:

- No entity, DTO, database table, column, index, or migration is added, changed, or removed. `JournalEntry` and `ProductAssociation` are untouched.
- No MediatR request/response shape changes — `GetEntriesByProductAsync` was never wrapped by a handler.
- No HTTP API / controller route changes — the method was never exposed through a controller.
- No OpenAPI contract or generated TypeScript client changes — `journal_SearchJournalEntries` and all other generated methods are unaffected.
- No frontend-facing type or hook signature changes — `useJournalEntriesByProduct` continues to call `journal_SearchJournalEntries` exactly as before.
- No event payloads are involved anywhere in this change.

What is "described" here is the removal itself, detailed above under Component Design: one interface member, one implementation method, and six test methods (plus a conditionally-orphaned test helper) are deleted, with no new or altered data shape anywhere in the system.
