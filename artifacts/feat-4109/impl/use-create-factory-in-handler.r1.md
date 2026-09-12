# Implementation: use-create-factory-in-handler

## What was implemented
`CreateJournalEntryHandler.Handle()` previously bypassed the `JournalEntry` aggregate's
invariants by building the entity with a raw object initializer (setting `Title`,
`Content`, `EntryDate`, `CreatedAt`, `ModifiedAt`, `CreatedByUserId`, `CreatedByUsername`
directly). This was replaced with a call to the existing `JournalEntry.Create(title,
content, entryDate, userId, username, now)` static factory, which already performs the
same trimming/date-normalization internally. All surrounding logic (auth check,
blank-title check, product-association loop, tag-assignment loop, repository calls,
logging, response construction) is unchanged.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs:49-58` — replaced the `new JournalEntry { ... }` object initializer with `JournalEntry.Create(request.Title, request.Content, request.EntryDate, userId, currentUser.Name ?? "Unknown User", now)`.

## Tests
No new tests were required or added — the existing regression suite fully covers this
change:
- `backend/test/Anela.Heblo.Tests/Features/Journal/CreateJournalEntryHandlerTests.cs` (7 tests) — asserts observable `Handle()` behavior, unaffected by the internal construction mechanism.
- Full `Anela.Heblo.Tests.Features.Journal` namespace (92 tests) — `JournalEntryTests`, `JournalEntryMapperTests`, `GetJournalEntryHandlerTests`, `CreateJournalEntryHandlerTests`, `UpdateJournalEntryHandlerTests`, `DeleteJournalEntryHandlerTests`.

## How to verify
```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Journal"
```
All 92 tests should pass. `dotnet format Anela.Heblo.sln --verify-no-changes` (run from repo root) reports no style drift.

## Notes
- Followed the repo-specific build/test rules (build first, `--no-build -p:UseSharedCompilation=false`, filtered test runs) instead of the raw commands as literally written in the task file's example (`dotnet build backend/...` from repo root — the sln itself is at the repo root, not under `backend/`, so `dotnet format` was run as `dotnet format Anela.Heblo.sln` from the repo root).
- Baseline (pre-change) run of `CreateJournalEntryHandlerTests` was 7/7 passing; post-change run is identical 7/7 passing — no regression, no argument-order mismatch.
- A mechanical, low-risk refactor; did not dispatch implementer/reviewer subagents given the task's explicit guidance ("~10-line mechanical refactor; do not over-engineer the process") — implemented and verified directly, matching the task file's exact prescribed diff.
- The working tree had a pre-existing unrelated modification to `artifacts/feat-4109/state.json` at session start; it was deliberately left out of this commit (only the handler file was staged and committed) since it is orchestrator-owned state, not part of this task's change.

## PR Summary
Replaced the inline `new JournalEntry { ... }` object initializer in `CreateJournalEntryHandler.Handle()` with a call to the existing `JournalEntry.Create()` factory method, so entry construction goes through the domain aggregate's invariants instead of bypassing them via public setters. This is a mechanical, behavior-preserving refactor — the factory already performs the same title/content trimming and entry-date normalization that the handler was doing manually.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs` — construct `JournalEntry` via `JournalEntry.Create(title, content, entryDate, userId, username, now)` instead of an object initializer.

## Status
DONE
