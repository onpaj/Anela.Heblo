# Implementation: remove-rest-dependency-from-smartsupp-repository

## What was implemented

Satisfied FR-1 and FR-2 from `spec.r1.md`: `SmartsuppRepository` no longer references
`ISmartsuppApiClient` at all. Followed the task context's steps exactly:

- Removed the `ISmartsuppApiClient _apiClient` field, its constructor parameter, and the
  now-unneeded `Microsoft.Extensions.Logging.Abstractions` using directive from
  `SmartsuppRepository`.
- Simplified `UpsertConversationAsync` to only do the local EF lookup (`_db.SmartsuppContacts...
  FirstOrDefaultAsync`) for denormalization; removed the REST-fallback branch and the
  wipe-`ContactId`-on-miss branch entirely.
- Deleted `TryFetchAndStageContactAsync` and `MapContactDataToEntity` from `SmartsuppRepository`
  (both were already ported to `SmartsuppContactEnricher` by Task 1).
- Updated the three target test files' `SmartsuppRepository` construction call sites to drop the
  `apiClient` argument, per Steps 6-8.

**One deviation from the task context, required to keep the build green:** the task context's file
list (4 files) did not include
`backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactMappingTests.cs`, but that file
called the now-deleted `SmartsuppRepository.MapContactDataToEntity` directly (it tests the UTC
timestamp-stamping logic, and was apparently missed when Task 1 ported the method to
`SmartsuppContactEnricher`). Deleting `MapContactDataToEntity` from `SmartsuppRepository` broke this
file's compile. Since the exact same `internal static MapContactDataToEntity` method already exists
on `SmartsuppContactEnricher` (byte-for-byte identical logic — confirmed by diff), the minimal fix
was to repoint this test file at the new home: changed the `using Anela.Heblo.Persistence.Smartsupp;`
import to `using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;` and both call sites from
`SmartsuppRepository.MapContactDataToEntity(...)` to `SmartsuppContactEnricher.MapContactDataToEntity(...)`.
No test logic, assertions, or behavior changed — the test still exercises the exact same mapping
code, just calling it where it now lives. `Anela.Heblo.Application`'s `InternalsVisibleTo` already
grants `Anela.Heblo.Tests` access to the `internal` method, so no visibility change was needed.

## Files created/modified

- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — removed `ISmartsuppApiClient`
  field/ctor param, removed REST-fallback branch from `UpsertConversationAsync`, deleted
  `TryFetchAndStageContactAsync` and `MapContactDataToEntity`.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUnknownContactFetchTests.cs` —
  deleted the four REST-behavior `[Fact]`s (already covered by `SmartsuppContactEnricherTests.cs`),
  removed the now-unused `MakeContactData` helper, dropped the `apiClient` argument from the
  remaining test's `SmartsuppRepository` construction, removed unused `using Moq;`.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs` —
  `SmartsuppRepositoryTestFactory.New` no longer takes/passes an `apiClient`.
- `backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs` —
  `CreateRepository` no longer constructs/passes an `ISmartsuppApiClient` substitute; removed unused
  `using NSubstitute;`.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactMappingTests.cs` — (not in the
  original task file list; fixed as a direct compile-break consequence of this task, see Notes)
  repointed at `SmartsuppContactEnricher.MapContactDataToEntity` instead of the deleted
  `SmartsuppRepository.MapContactDataToEntity`.

## Tests

No new tests added — this task only deletes now-redundant REST-behavior coverage from
`SmartsuppRepositoryUnknownContactFetchTests.cs` (already duplicated in `SmartsuppContactEnricherTests.cs`
by Task 1) and updates constructor call sites. `SmartsuppContactMappingTests.cs`'s two `[Fact]`s
(`MapContactDataToEntity_StampsAllTimestampsAsUtc`, `MapContactDataToEntity_NullBannedAt_StaysNull`)
keep exercising the same mapping logic, now via its new home.

## How to verify

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs
grep -rn "ISmartsuppApiClient" backend/src/Anela.Heblo.Persistence   # expect: no output
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Smartsupp"
dotnet format Anela.Heblo.sln --verify-no-changes
```

Results:
- `grep -rn "ISmartsuppApiClient" backend/src/Anela.Heblo.Persistence`: **no output** — FR-1/FR-2
  acceptance bar met.
- `dotnet build Anela.Heblo.sln`: **Build succeeded, 0 Error(s)** (87 pre-existing nullable/obsolete-API
  warnings across the solution; none touch Smartsupp files changed by this task).
- Smartsupp filter: **203 Passed, 12 Failed, Total 215**. All 12 failures are the pre-existing
  `SmartsuppRepositoryUpsertIntegrationTests` Postgres-Testcontainers cases
  (`System.ArgumentException: Docker is either not running or misconfigured`) — same cause/count
  pattern already documented in `impl/wire-reactions-to-contact-enricher.r1.md` for the prior task;
  not a regression from this change (Docker is unavailable in this sandbox). Per the task context's
  Step 10, this is expected to be verified in CI where Docker is available.
- `dotnet format Anela.Heblo.sln --verify-no-changes`: reports 4 whitespace errors, all in
  `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs` — a file
  from an unrelated, pre-existing PR (#3911, "feat: overtime ledger"), not touched by this task and
  confirmed via `git diff origin/main` to already have that formatting issue on `main`. No format
  errors in any file this task modified.

## Notes

- `_logger` (`ILogger<SmartsuppRepository>`) is now unused within the class after removing the only
  `LogWarning` call site (it lived in the deleted `TryFetchAndStageContactAsync`). Left the field and
  constructor parameter in place exactly as specified by the task context's Step 1 "change to" code
  block — the task explicitly kept it, presumably for future use, and removing it was out of scope
  for this task's surgical instructions.
- Confirmed via `grep -rn "SmartsuppRepositoryTestFactory.New\|new SmartsuppRepository("` across
  `backend/test` before editing that no other call site outside the three target test files
  constructs `SmartsuppRepository` directly — all other consumers resolve it via DI through
  `ISmartsuppRepository`, which needed no changes.
- Did not run the Postgres integration test suite specifically (`SmartsuppRepositoryUpsertIntegrationTests`)
  in isolation beyond what's covered by the full `~Smartsupp` filter above — same 12 Docker-dependent
  failures apply; environment has no Docker daemon.

## PR Summary
Removes `ISmartsuppApiClient` entirely from `Anela.Heblo.Persistence.Smartsupp.SmartsuppRepository`,
the final step of issue #3878: after this change `Anela.Heblo.Persistence` has zero outbound-HTTP call
sites. The REST-fetch-on-miss behavior (moved to `ISmartsuppContactEnricher` by an earlier task in this
plan) is deleted from the repository along with its now-dead helper methods; `UpsertConversationAsync`
keeps only its local EF denormalization lookup. Test files updated to match the simplified constructor,
and a pre-existing test (`SmartsuppContactMappingTests.cs`, missed by an earlier task in this plan) is
repointed at the mapping method's real current home in `SmartsuppContactEnricher`.

### Changes
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — removed `ISmartsuppApiClient` dependency, REST-fallback branch, and the two now-dead helper methods
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUnknownContactFetchTests.cs` — removed the four REST-behavior tests now covered by `SmartsuppContactEnricherTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppRepositoryUpdatedAtGuardTests.cs` — factory no longer takes an `apiClient`
- `backend/test/Anela.Heblo.Tests/Persistence/Smartsupp/SmartsuppRepositoryUpsertIntegrationTests.cs` — `CreateRepository` no longer takes an `apiClient`
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppContactMappingTests.cs` — repointed at `SmartsuppContactEnricher.MapContactDataToEntity`

## Status
DONE
