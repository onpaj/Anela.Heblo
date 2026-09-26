# Implementation: implement-mutate-bulk-async

## What was implemented
Added `MutateBulkAsync` to `UserDashboardSettingsMutator`, implementing the interface
member declared by the prior task. It follows the exact same provision → lock → load →
mutate → save scaffold as the existing `MutateAsync`: normalizes `userId` to
`"anonymous"`, sends `GetUserSettingsRequest` via MediatR before acquiring the per-user
lock (provisioning must stay outside the non-reentrant lock), loads settings via the
repository, then for each `UserDashboardTileDto` either updates an existing tile in
place (`IsVisible`, `DisplayOrder`, `LastModified`) or appends a new
`UserDashboardTile`. All touched/appended tiles and the settings row share the single
`TimeProvider.GetUtcNow()` value read once per call. `UpdateAsync` is called
unconditionally once settings are loaded (even for an empty `tiles` list), matching
`SaveUserSettingsHandler`'s existing "always persist on save" semantics per the
interface's `<remarks>`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Dashboard/Infrastructure/UserDashboardSettingsMutator.cs` — added `MutateBulkAsync` method and the `Contracts` using directive for `UserDashboardTileDto`.
- `backend/test/Anela.Heblo.Tests/Features/Dashboard/Infrastructure/UserDashboardSettingsMutatorTests.cs` — new test file (created; did not exist before).

## Tests
`UserDashboardSettingsMutatorTests.cs` — 8 tests covering:
- null settings short-circuits without persisting
- null/empty userId resolves to "anonymous"
- provisioning (`GetUserSettingsRequest`) happens before lock acquisition
- lock is acquired exactly once regardless of tile count
- existing tile is updated in place
- missing tile is appended
- empty tile list still persists settings (LastModified update)
- all touched/appended tiles and settings share the same timestamp

## How to verify
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~UserDashboardSettingsMutatorTests
```
Result: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8`.

A broader `--filter "FullyQualifiedName~Features.Dashboard"` regression run was also
attempted but could not be completed within a reasonable time in this environment (the
solution-wide build competes for CPU with a second, concurrently-running pipeline
worker on the same 4-vCPU host, and pulls in the API project's access-matrix code-gen
target). No compile-breaking change was made to `SaveUserSettingsHandler.cs` or its
existing test file in this task, so this is not expected to regress; that file is
rewritten by the follow-up task `refactor-save-user-settings-handler`, which is where
`SaveUserSettingsHandlerTests` will actually change.

## Notes
No deviations from the task context. Implementation and test file are exactly as
specified in `task-context/implement-mutate-bulk-async.md`.
