# Implementation: update-di-registrations

## What was implemented

Verified both DI registration files against the spec requirements. The previous task (`move-graph-service-files`) had already done the primary work correctly:

- `Microsoft365AdapterServiceCollectionExtensions.cs` — all spec requirements met. Uses `ConfigurationConstants.USE_MOCK_AUTH` instead of the string literal `"UseMockAuth"` shown in the spec (equivalent, and better). The condition is inverted (`if (useMockAuth || bypassJwt)` vs spec's `if (!useMockAuth && !bypassJwt)`) but functionally identical. All required registrations present in the correct branches.
- `UserManagementModule.cs` — all spec requirements met. No `GraphService`/`MockGraphService`/`AddHttpClient`/`using Microsoft.Graph`. Contains `AddScoped<IArticleUserResolver, GraphArticleUserResolver>()` plus validator and pipeline behavior registrations.

**Gap found and fixed:** Moving `GraphService` and `MockGraphService` to `Anela.Heblo.Adapters.Microsoft365.UserManagement` left 5 test files broken — they still referenced those types via the old `Anela.Heblo.Application.Features.UserManagement.Services` namespace. Additionally, `ParseMembersFromJson` is `internal static` on the new `GraphService` but `Anela.Heblo.Adapters.Microsoft365` had no `InternalsVisibleTo` grant for the test project.

Changes made:
1. Added `using Anela.Heblo.Adapters.Microsoft365.UserManagement;` to 5 test files.
2. Added `<InternalsVisibleTo Include="Anela.Heblo.Tests" />` to `Anela.Heblo.Adapters.Microsoft365.csproj`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetAppRoleMembersTests.cs` — added adapter namespace using directive
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs` — added adapter namespace using directive
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — added adapter namespace using directive
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/MockGraphServiceTests.cs` — added adapter namespace using directive
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs` — added adapter namespace using directive
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj` — added `InternalsVisibleTo` for `Anela.Heblo.Tests`

## Tests

All existing `GraphService` / `MockGraphService` unit tests are preserved and now compile correctly under the new namespace. `ParseMembersFromJsonTests` exercises the internal static parsing helper via `InternalsVisibleTo`.

## How to verify

```bash
cd /home/user/worktrees/feature-3298-Arch-Review-Usermanagement-Graphservice-Belongs-In
dotnet build Anela.Heblo.sln
# Expected: Build succeeded. 0 Error(s)
```

## Notes

- The spec shows `configuration.GetValue<bool>("UseMockAuth", false)` but the code uses `ConfigurationConstants.USE_MOCK_AUTH` (= `"UseMockAuth"`). The constant was deliberately kept — it is more maintainable and consistent with all other usages in the codebase.
- The spec shows the production branch first (`if (!useMockAuth && !bypassJwt)`); current code has mock branch first (`if (useMockAuth || bypassJwt)`). Semantically identical; no change made.
- The MSB3073 warning about `AccessMatrixGen` exiting with code 134 is a pre-existing issue unrelated to this task.

## PR Summary

Fixes broken test compilation introduced when `GraphService` and `MockGraphService` were moved from the application layer to `Anela.Heblo.Adapters.Microsoft365.UserManagement`. Adds the correct namespace `using` to affected test files and grants `InternalsVisibleTo` so tests can access the `internal static ParseMembersFromJson` helper.

### Changes

- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetAppRoleMembersTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceSearchTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/MockGraphServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/ParseMembersFromJsonTests.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Anela.Heblo.Adapters.Microsoft365.csproj`

## Status

DONE
