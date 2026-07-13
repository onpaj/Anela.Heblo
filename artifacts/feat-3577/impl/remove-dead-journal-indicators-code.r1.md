# Implementation: remove-dead-journal-indicators-code

## What was implemented

Removed the confirmed dead `GetJournalIndicatorsAsync` code path end to end:
- `IJournalRepository.GetJournalIndicatorsAsync` signature removed from the interface.
- `JournalRepository.GetJournalIndicatorsAsync` implementation removed, along with the
  `RecentEntriesDays` constant it was the sole consumer of.
- `JournalIndicatorSnapshot` (return type, unused elsewhere) deleted entirely.
- `JournalIndicatorDto` (unused contract, unused elsewhere) deleted entirely.
- The 4 integration tests that existed solely to exercise the dead method
  (`GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount`,
  `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZeroIndicator`,
  `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries`,
  `GetJournalIndicatorsAsync_WhenEntryIsSoftDeleted_ExcludesFromCount`) were removed from
  `JournalRepositoryIntegrationTests.cs`, matching text boundaries exactly so no other test
  or blank-line spacing was disturbed.

A repo-wide `grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/`
confirms zero residual references.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/Journal/IJournalRepository.cs` — removed the
  `GetJournalIndicatorsAsync` signature.
- `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` — removed the
  `RecentEntriesDays` constant and the `GetJournalIndicatorsAsync` method body.
- `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicatorSnapshot.cs` — deleted (`git rm`).
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` — deleted (`git rm`).
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` — removed
  the 4 dead `[Fact]` tests (two blocks).

## Tests
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors (one pre-existing, unrelated warning from
  the `AccessMatrixGen` tool failing to parse a JSON file — reproduces on unrelated changes too,
  out of scope for this task).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"`
  — **Passed: 93, Failed: 0** (4 fewer than before the removal, as expected).
- `dotnet test Anela.Heblo.sln` (full solution) — passes cleanly for all unit-test-only projects
  (`Anela.Heblo.Adapters.OpenMeteo.Tests`, `Anela.Heblo.Adapters.Plaud.Tests`,
  `Anela.Heblo.Adapters.HomeAssistant.Tests`). `Anela.Heblo.Tests.dll` shows 66 failures, all in
  `*.Integration` test classes that construct `Testcontainers.PostgreSql` fixtures — every failure's
  stack trace bottoms out in `System.ArgumentException : Docker is either not running or
  misconfigured`. `Anela.Heblo.Adapters.Flexi.Tests` (72 failures) and
  `Anela.Heblo.Adapters.Shoptet.Tests` (13 failures) fail similarly, needing live external
  service configuration/secrets (`Missing Shoptet:StatusId:EXP in configuration`) not present in
  this sandbox. None of these failures reference `Journal`, `GetJournalIndicatorsAsync`,
  `JournalIndicatorDto`, or `JournalIndicatorSnapshot` — confirmed via grep across the full test
  log. They are pre-existing environment limitations (no Docker, no live Shoptet credentials in
  this sandbox), unrelated to this change.

## How to verify
1. `dotnet build Anela.Heblo.sln` — should succeed.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Journal"` — all Journal tests should pass.
3. `grep -rn "GetJournalIndicatorsAsync\|JournalIndicatorDto\|JournalIndicatorSnapshot" backend/` — should return no results.

## Notes
The full-solution `dotnet test` run has pre-existing failures unrelated to this change, caused by
missing Docker/Testcontainers and missing live Shoptet secrets in this sandbox environment — not
by this diff. Flagging as `DONE_WITH_CONCERNS` only to surface that context to the reviewer, not
because the implementation itself is incomplete.

## Status
DONE_WITH_CONCERNS
