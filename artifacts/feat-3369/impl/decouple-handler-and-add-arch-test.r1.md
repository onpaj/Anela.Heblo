# Implementation: decouple-handler-and-add-arch-test

## What was implemented

Removed the direct SDK type references (`MsalException`, `ODataError`) from `GetGroupMembersHandler` and replaced them with the application-level wrapper types (`GraphServiceAuthException`, `GraphServiceException`) introduced in task 1. Added a new `[Fact]` — `Application_types_should_not_catch_SDK_exception_types_directly` — to `ModuleBoundariesTests` that enforces the Application layer does not surface `Microsoft.Identity.Client` or `Microsoft.Graph.Models.ODataErrors` types in signatures, with a justified allowlist for pre-existing violations.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/UseCases/GetGroupMembers/GetGroupMembersHandler.cs` — removed `using Microsoft.Identity.Client;`, replaced `catch (MsalException ex)` with `catch (GraphServiceAuthException ex)` (mapped to `ErrorCodes.ConfigurationError`) and `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` with `catch (GraphServiceException ex)` (mapped to `ErrorCodes.ExternalServiceError`); all other catch blocks unchanged.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — added `SdkExceptionAllowlist` static field with three allowlist entries (GraphArticleUserResolver, GraphPlannerService, GraphCatalogDocumentsStorage) and new `[Fact]` `Application_types_should_not_catch_SDK_exception_types_directly`.

## How to verify

```bash
# Build
cd /home/user/worktrees/feature-3369-Arch-Review-Usermanagement-Application-Layer-Handl
dotnet build Anela.Heblo.sln

# New arch test only
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "Application_types_should_not_catch_SDK_exception_types_directly" --no-build

# All ModuleBoundariesTests (regression check)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests" --no-build
```

Expected: 1 passed for the targeted test; 27 passed for all `ModuleBoundariesTests`.

## Notes

- `EnumerateReferencedTypes` inspects fields, properties, constructor parameters, and method signatures but NOT method bodies. Catch-clause exception types that appear only in method bodies are therefore not directly detected by the helper. However, compiler-generated async state machines (e.g. `<AcquireDelegatedTokenAsync>d__11`) expose their captured locals as fields, which IS visible. This is why `GraphPlannerService` and `GraphCatalogDocumentsStorage` appeared as violations even though the test is nominally signature-only — their async state machines held the caught `MsalUiRequiredException` as a `<ex>5__N` field.
- Two pre-existing violations were discovered during test execution that were not mentioned in the task context: `GraphPlannerService` and `GraphCatalogDocumentsStorage`. Both were added to the allowlist with follow-up comments.
- `GraphArticleUserResolver` (the type mentioned in the task spec as needing an allowlist entry) did not actually produce a violation with the current `EnumerateReferencedTypes` helper (its SDK references appear only in catch blocks in the method body, not in any field/property/signature). The allowlist entry was added defensively as specified.

## Status
DONE
