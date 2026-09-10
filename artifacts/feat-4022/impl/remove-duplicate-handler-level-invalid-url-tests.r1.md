# Implementation: remove-duplicate-handler-level-invalid-url-tests

## What was implemented
Removed the two unit tests in `DownloadFromUrlHandlerTests.cs` that exercised the handler's own (now-dead) invalid-URL branch. Production code was left unchanged in this task — the handler's manual URL validation block still exists, so the pipeline validator/behavior tests still cover the same behavior and the suite stays green throughout.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — deleted the `Handle_InvalidUrl_ShouldReturnErrorResponse` theory (3 `[InlineData]` cases) and the `Handle_ValidationFailure_InvalidUrl_SetsCauseValidation` fact.

## Tests
No new tests added; this task only removes tests that duplicated coverage already provided by `DownloadFromUrlRequestValidator` / `ValidationResultBehavior` pipeline tests.

## How to verify
```bash
grep -n "Handle_InvalidUrl_ShouldReturnErrorResponse\|Handle_ValidationFailure_InvalidUrl_SetsCauseValidation" \
  backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs
# expect: no matches, exit code 1

dotnet build Anela.Heblo.sln
# expect: Build succeeded, 0 errors

dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.FileStorage"
# expect: all tests pass, 0 failed
```

Confirmed: build succeeded with 0 errors; FileStorage test suite passed 123/123, 0 failed.

## Notes
None — mechanical deletion exactly as specified in the task context, no deviations.

## PR Summary
Deleted the two `DownloadFromUrlHandlerTests` cases that exercised the handler's dead invalid-URL branch (`Handle_InvalidUrl_ShouldReturnErrorResponse` and `Handle_ValidationFailure_InvalidUrl_SetsCauseValidation`). This is step 1 of 2 for issue #4022 — production code (the actual dead validation block in `DownloadFromUrlHandler`) is removed in the next task, after these duplicate tests are out of the way.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — removed 2 duplicate tests (40 lines)

## Status
DONE
