# Implementation: add-error-code

## What was implemented
Added a new `ErrorCodes` enum member, `LeafletEmptyRetrieval = 2504`, decorated with `[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]`, inserted immediately after `LeafletFeedbackAlreadySubmitted = 2503` in the Leaflet module errors (25XX) block. No existing members were renumbered or reordered.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `LeafletEmptyRetrieval = 2504` (HTTP 422 UnprocessableEntity) to the Leaflet module errors (25XX) section.

## Tests
None. This is a pure enum-member addition with no branching logic to unit test in isolation; its effect will be exercised by tests added in later tasks once the handler/controller/MCP tool actually reference this error code.

## How to verify
1. Inspect `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` lines ~276-285 to confirm `LeafletEmptyRetrieval = 2504` is present with the `UnprocessableEntity` attribute, and that `LeafletChunkNotFound` (2501), `LeafletFeedbackNotFound` (2502), and `LeafletFeedbackAlreadySubmitted` (2503) remain unchanged.
2. Build the Application project: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — builds with 0 errors (51 pre-existing warnings, unrelated to this change).

## Notes
- `dotnet build Anela.Heblo.sln` (the full solution) fails with a pre-existing, unrelated error: `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs(95,37): error CS0117: 'ConfigurationConstants' does not contain a definition for 'APP_VERSION'`. I confirmed via `git stash` that this error exists on the branch independent of this change (i.e., it reproduces with `ErrorCodes.cs` reverted to its pre-task state), so it is out of scope for this task. Verification was instead done by building the `Anela.Heblo.Application` project directly, which contains `ErrorCodes.cs` and compiles cleanly.

## Status
DONE_WITH_CONCERNS
