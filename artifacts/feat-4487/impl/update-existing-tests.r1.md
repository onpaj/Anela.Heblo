# Implementation: update-existing-tests

## What was implemented
- GetUserSettingsHandlerTests rewritten as pure query tests against a mocked IUserDashboardSettingsReader (anonymous fallback for null/empty id, reader result passthrough). Write-path cases live in provisioner tests.
- GetTileDataHandlerTests mock IUserDashboardSettingsReader instead of IMediator; anonymous tests now actually set a null/empty user id and verify "anonymous".
- UserDashboardSettingsMutatorTests, EnableTileHandlerTests, DisableTileHandlerTests mock IUserDashboardSettingsProvisioner; ordering test asserts provisioner before lock and with the resolved user id (incl. "anonymous").

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~Dashboard` -> 203 passed. dotnet format clean.

## Notes
Plain `dotnet test` hung after API build in this sandbox (nested `dotnet run` access-matrix step); set MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 to work around.

## Status
DONE
