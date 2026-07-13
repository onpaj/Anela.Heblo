# Design: Remove dead code — `GetJournalIndicatorsAsync` and `JournalIndicatorDto`

## Component Design

No new or modified components — this is a subtractive change to the Journal module. The following existing artifacts are removed in place, with no replacement and no rewiring, because none has any production caller (confirmed by repo-wide grep in both `spec.r1.md` and `arch-review.r1.md`):

- **`IJournalRepository` (Domain, `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs`)** — remove the `GetJournalIndicatorsAsync` method signature (lines 31-34). The interface's other three members (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`) are untouched. Safe because `JournalRepository` is the interface's only implementation and no Moq-based test sets up this member.

- **`JournalRepository` (Persistence, `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`)** — remove the `GetJournalIndicatorsAsync` method body (lines 154-202) and the `RecentEntriesDays` constant (line 12), which exists solely to support that method. Safe because removing an implementation of a now-deleted interface member cannot break any other method on the class (`GetEntriesAsync`, `SearchEntriesAsync`, `GetEntriesByProductAsync`, `ApplySort`, etc. are unaffected and unread by the deleted code).

- **`JournalIndicatorSnapshot` (Domain, `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs`)** — delete the file entirely. This `readonly record struct` exists only as the return-value shape of `GetJournalIndicatorsAsync`; once that method is gone it has zero references anywhere in `backend/src` or `backend/test`. It was never a persisted entity (no EF Core mapping, no table, no migration), so deleting it has no data-layer effect.

- **`JournalIndicatorDto` (Application, `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs`)** — delete the file entirely. It was scaffolded to eventually expose indicator data over the API but was never referenced by any MediatR handler or MVC controller, so it never entered the generated OpenAPI/TypeScript contract surface. Safe to remove with no client regeneration diff expected.

- **`JournalRepositoryIntegrationTests.cs` (Test, `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs`)** — remove the four `[Fact]` methods that exist solely to exercise the deleted method: `GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount`, `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator`, `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries`, `GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount`. These tests share the class's existing `_context`/`_repository` fixture fields with the remaining tests, so no fixture/setup changes are needed — only the four method bodies are deleted, and every other test in the file (`GetEntriesByProductAsync` cases, sort-matrix cases) is left unchanged.

Why this is safe as a unit: all four artifacts have exactly one reason to exist — `GetJournalIndicatorsAsync` — and none is reachable from any MediatR handler, MVC controller, or frontend code. Deleting the method first and its two supporting types second (rather than leaving orphaned types behind) avoids creating a second generation of dead code. If per-product journal indicators become a real requirement later, the logic is fully recoverable from git history once there is a concrete consumer.

## Data Schemas

No schema or API change. `JournalIndicatorSnapshot` was a transient, in-memory read-model projection — never mapped by EF Core, never persisted, no database table or migration involved. `JournalIndicatorDto` was never referenced by any controller or handler, so it never appeared in the generated OpenAPI spec or TypeScript client; its removal produces no request/response contract diff. No endpoint, database schema, or event payload is affected by this change.
