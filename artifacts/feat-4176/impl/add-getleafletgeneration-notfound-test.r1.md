# Implementation: add-getleafletgeneration-notfound-test

## What was implemented

Added a unit test covering the not-found error path of `GetLeafletGenerationHandler`: when the repository returns `null` for the requested generation id, the handler must return a response with `Success == false`, `ErrorCode == ErrorCodes.LeafletFeedbackNotFound`, and every other property at its type default.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs` — new test file with `GetLeafletGenerationHandlerTests` class, mocking `ILeafletGenerationRepository` via Moq and asserting the full default-response shape.

## Tests

- `GetLeafletGenerationHandlerTests.Handle_returns_not_found_error_code_and_default_fields_when_generation_missing` — arranges the repo mock to return `(LeafletGeneration?)null` for a random `Guid`, invokes the handler, and asserts `Success`, `ErrorCode`, and every DTO field (`Id`, `Topic`, `Audience`, `Length`, `FinalMarkdown`, `KbSourceCount`, `LeafletSourceCount`, `DurationMs`, `CreatedAt`, `UserId`, `PrecisionScore`, `StyleScore`, `FeedbackComment`) against their expected defaults.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletGenerationHandlerTests"
```

Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

A full-suite `dotnet test` was also run from the repo root (`Anela.Heblo.sln`) to check for regressions: `Failed: 110, Passed: 7125, Skipped: 4, Total: 7239`. All 110 failures are pre-existing `Testcontainers`/PostgreSQL integration tests failing with `Docker is either not running or misconfigured` — this sandbox has no Docker daemon running (`docker ps` fails to connect to `/var/run/docker.sock`). None of the failures touch Leaflet, and the new test itself uses only mocks (no Docker dependency), so this is an environment limitation, not a regression introduced by this change.

`dotnet format` (run against `Anela.Heblo.sln`) made no changes. `dotnet build` succeeded with 0 errors (pre-existing nullable-reference warnings only, unrelated to this change).

## Notes

- The task-context's Step 4/5 commands (`cd backend && dotnet test` / `cd backend && dotnet format && dotnet build`) don't resolve because there is no `.sln` inside `backend/` — the solution file (`Anela.Heblo.sln`) lives at the repo root. Ran the equivalent commands from the repo root instead; intent (full regression + format/build check) is unchanged.
- No production code was touched — this task only adds test coverage for an already-implemented branch of `GetLeafletGenerationHandler`.

## PR Summary

Added a unit test for `GetLeafletGenerationHandler`'s not-found path, closing the coverage gap flagged in issue #4176: when `ILeafletGenerationRepository.GetGenerationByIdAsync` returns `null`, the handler must surface `ErrorCodes.LeafletFeedbackNotFound` with every response field left at its default. No production code changes.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs` — new test asserting the not-found error response shape

## Status
DONE
