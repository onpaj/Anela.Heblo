# Implementation: fix-unit-tests

## What was implemented

Updated the two DI registration tests in `GraphServiceTests.cs` to call `AddMicrosoft365Adapter(configuration)` instead of `AddUserManagement(configuration)`. This is required because `IGraphService` registration was moved from `UserManagementModule.AddUserManagement()` to `Microsoft365AdapterServiceCollectionExtensions.AddMicrosoft365Adapter()` as part of the arch review.

Changes:
- Added `using Anela.Heblo.Adapters.Microsoft365;` import (was missing; only the sub-namespace `UserManagement` was imported).
- Removed unused `using Anela.Heblo.Application.Features.UserManagement;` import.
- `AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService`: changed `services.AddUserManagement(configuration)` to `services.AddMicrosoft365Adapter(configuration)`.
- `AddUserManagement_MockBranch_RegistersMockGraphService`: changed `services.AddUserManagement(configuration)` to `services.AddMicrosoft365Adapter(configuration)`.

The `ITokenAcquisition` singleton mock was already present in the production branch test; no change was needed there.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — updated two DI tests to call `AddMicrosoft365Adapter` and added the required namespace import

## Tests

```
Passed!  - Failed: 0, Passed: 48, Skipped: 0, Total: 48, Duration: 385 ms
```

All 48 UserManagement tests pass, including the two previously-failing DI registration tests.

## How to verify

```bash
cd /home/user/worktrees/feature-3298-Arch-Review-Usermanagement-Graphservice-Belongs-In
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~UserManagement" --no-build
```

## Notes

No deviations from the plan. Formatting was verified clean (`dotnet format Anela.Heblo.sln --verify-no-changes` exited 0).

The `ITokenAcquisition` mock was already in the production test setup, so no additional change was needed for that step.

## PR Summary

Updated DI registration unit tests to reflect that `IGraphService` is now registered by the adapter layer (`AddMicrosoft365Adapter`) rather than the application layer (`AddUserManagement`).

### Changes

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`

## Status

DONE
