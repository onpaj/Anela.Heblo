# Architecture Review: Journal — Remove `GetJournalIndicatorsAsync` and `JournalIndicatorDto` dead code

## Skip Design: true

## Architectural Fit Assessment
This is a pure subtractive change with no architectural implications. It removes one member from `IJournalRepository` (Domain layer), its EF Core implementation (Persistence layer), an unused response DTO (Application/Contracts layer), an orphaned read-model value type (Domain layer), and four integration tests that exist solely to exercise the removed method. No MediatR handler, MVC controller, or frontend code references any of the four symbols.

I independently verified this against the source, not just the spec's claims:

- `grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" --include="*.cs" .` (run from repo root) returns exactly 12 hits, and every one is inside the four files named in the spec: `IJournalRepository.cs`, `JournalRepository.cs`, `JournalIndicatorDto.cs`, `JournalIndicatorSnapshot.cs`, plus the test file `JournalRepositoryIntegrationTests.cs`. There is no fifth file, no handler, no controller.
- `IJournalRepository` has exactly one implementation in the codebase — `JournalRepository : BaseRepository<JournalEntry, int>, IJournalRepository` in `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`. There are no hand-written fakes/stubs of this interface anywhere in `backend/test/` (Journal handler tests mock the interface via Moq, which does not require updating when a member is removed from the interface — Moq mocks are generated against whatever the interface currently declares).
- `JournalIndicatorSnapshot` (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`) is a `readonly record struct` with an XML doc comment that itself references `GetJournalIndicatorsAsync` — confirming it exists purely as that method's return-value shape, with no other consumer.
- `JournalIndicatorDto` (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`) is referenced nowhere outside its own file — not by any handler, not by any controller, not in the OpenAPI-generated contracts.

This confirms the brief and spec's factual claims exactly (including line ranges). No amendment to scope is needed. There is nothing to "design" here (no new/changed UI, no new API surface, no new module) — `Skip Design: true` is correct.

## Proposed Architecture

### Component Overview
No new components. Four existing artifacts are deleted in place; all surrounding structure is untouched:

```
Domain/Features/Journal/
  IJournalRepository.cs        [MODIFY: remove one method signature]
  JournalIndicatorSnapshot.cs  [DELETE: whole file]

Persistence/Journal/
  JournalRepository.cs         [MODIFY: remove one method body + one const]

Application/Features/Journal/Contracts/
  JournalIndicatorDto.cs       [DELETE: whole file]

test/.../Features/Journal/
  JournalRepositoryIntegrationTests.cs  [MODIFY: remove 4 [Fact] methods]
```

No dependency graph changes — nothing else in `JournalModule.cs`, controllers, or MediatR handlers references these symbols, so no rewiring or DI change is required.

### Key Design Decisions

#### Decision 1: Delete now vs. deprecate-then-delete
**Options considered:**
1. Mark `GetJournalIndicatorsAsync` `[Obsolete]` for a release cycle before deleting.
2. Delete immediately.

**Chosen approach:** Delete immediately (option 2), per the spec.

**Rationale:** `[Obsolete]` exists to give external/downstream consumers a migration window. This is an internal repository interface with a single implementation and zero callers; there is no downstream consumer to warn. Deprecation would only prolong carrying dead code with no offsetting benefit — contradicts the YAGNI rationale driving this cleanup.

#### Decision 2: Scope of deletion — method only vs. method + supporting types
**Options considered:**
1. Remove only `GetJournalIndicatorsAsync` from the interface/implementation, leave `JournalIndicatorSnapshot` and `JournalIndicatorDto` in place in case they're wanted later.
2. Remove the method and every type that exists solely to support it (`JournalIndicatorSnapshot`, `JournalIndicatorDto`, the `RecentEntriesDays` constant).

**Chosen approach:** Option 2, matching the spec's FR-1 through FR-4.

**Rationale:** Both supporting types have exactly one reason to exist — this method. Leaving them behind after removing their only consumer just creates a second, more confusing generation of dead code (an unreferenced DTO in `Contracts/`, an unreferenced record struct in `Domain/`) with no clearer path to "future use" than deleting and re-adding from git history if a real consumer materializes. Git history is the correct medium-term storage for "might need this later," not a live orphaned type in the module.

## Implementation Guidance

### Directory / Module Structure
No structural changes — files are deleted or trimmed in place, no files move, no new directories.

Exact edits required:

1. **`backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`**
   Remove lines 31-34 (the `GetJournalIndicatorsAsync` signature). Leave the `using Anela.Heblo.Xcc.Persistance;` import and the other three method signatures untouched.

2. **`backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`**
   Delete the file entirely.

3. **`backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`**
   - Remove the `private const int RecentEntriesDays = 30;` field (line 12) — it has no other reader.
   - Remove the `GetJournalIndicatorsAsync` method body (lines 154-202).
   - Do not touch `GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`, `ApplySort`, `ApplyDefaultSort`, `ApplyDefaultSortWithWarning`, or `GetByIdAsync` — all confirmed unrelated.
   - After deletion, confirm the class still compiles against the trimmed `IJournalRepository` (it will, since all other interface members remain implemented).

4. **`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`**
   Delete the file entirely.

5. **`backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`**
   Remove the four `[Fact]` methods: `GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount`, `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator`, `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries`, `GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount`. All other tests in the file (`GetEntriesByProductAsync` cases, sort-matrix cases, etc.) are unrelated and must be left byte-for-byte unchanged. Check for any shared setup/teardown (`_context`, `_repository` fields, constructor) that these four tests use exclusively — verified they reuse the class's shared `_context`/`_repository` fixture fields, so no fixture cleanup is needed beyond removing the four methods themselves.

### Interfaces and Contracts
`IJournalRepository` shrinks from 4 members to 3:
```csharp
public interface IJournalRepository : IRepository<JournalEntry, int>
{
    Task<PagedResult<JournalEntry>> GetEntriesAsync(...);
    Task<PagedResult<JournalEntry>> SearchEntriesAsync(...);
    Task<List<JournalEntry>> GetEntriesByProductAsync(...);
    // GetJournalIndicatorsAsync removed
}
```
No new interface, no new contract. Any Moq-based mock of `IJournalRepository` in handler tests continues to work unchanged since Moq mocks against the interface as compiled — removing a member from the interface cannot break a mock that never set up that member (none did, per the grep).

### Data Flow
No data flow changes — the method removed never executed in any production or test code path outside its own dedicated (now-deleted) tests. There is no runtime behavior anywhere in the app that is a function of this change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A caller was missed by grep (e.g., reflection-based invocation, dynamic dispatch) | Low | `dotnet build` after deletion will fail loudly if any static C# reference remains; reflection-based calls are not used anywhere in this codebase's Journal module (confirmed by reading all files that reference `IJournalRepository`). |
| OpenAPI/TypeScript client regeneration picks up an unexpected diff | Very Low | `JournalIndicatorDto` was never referenced by a controller/handler, so it was never part of the generated OpenAPI spec (`Contracts/` classes only surface via being used in a MediatR response/request DTO tree that a controller returns). Run the standard `npm run build` client regen step as a defensive check per FR-4; expect zero diff. |
| Deleting `RecentEntriesDays` constant breaks a future reintroduction path silently | Very Low | Not a real risk — if indicator logic is rebuilt later, it will be designed fresh against an actual consumer (per spec's "Out of Scope"), and the old value (30 days) is preserved in git history / this review if ever needed for reference. |

## Specification Amendments
None. I verified every file path, line range, and "zero other callers" claim in `spec.r1.md` against the actual source and found no discrepancies. The spec's FR-1 through FR-6 are implementable exactly as written.

One clarifying note for the implementer (not a spec change, just a precision note): FR-2's acceptance criterion says `JournalRepository.cs` "no longer contains the `GetJournalIndicatorsAsync` method body (currently lines 154-202)" — after removing the `RecentEntriesDays` constant on line 12 as well (per the same FR), all subsequent line numbers in the file shift up by one line before the method deletion is applied. Apply deletions by matching the method/field text, not by hard-coded line numbers, to avoid an off-by-one slip.

## Prerequisites
None. No migration, no config, no infrastructure change, no new package, no feature flag. This can be implemented and merged standalone.
