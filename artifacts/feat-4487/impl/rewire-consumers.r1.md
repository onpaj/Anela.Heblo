# Implementation: rewire-consumers

## What was implemented
- GetUserSettingsHandler now depends only on IUserDashboardSettingsReader + ICurrentUserService ("anonymous" fallback), returns reader result in GetUserSettingsResponse.
- GetTileDataHandler uses the reader (same user-id resolution) instead of IMediator.
- UserDashboardSettingsMutator uses IUserDashboardSettingsProvisioner.EnsureProvisionedAsync(resolvedUserId) before the lock in MutateAsync and MutateBulkAsync.
- IUserDashboardSettingsMutator remarks updated.
- DashboardModule registers provisioner and reader (scoped).

## How to verify
`dotnet build backend/src/Anela.Heblo.Application` -> 0 errors.

## Notes
Existing tests (GetUserSettingsHandlerTests, GetTileDataHandlerTests, mutator/Enable/Disable tests) construct the old dependencies and will not compile until the update-existing-tests task; deliberately left for that task.

## Status
DONE
