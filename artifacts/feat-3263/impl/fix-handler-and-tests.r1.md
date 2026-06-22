# Implementation: fix-handler-and-tests

## What was implemented
1. Removed `using Microsoft.Identity.Client;` from `BackfillArticleRequestedByHandler.cs`
2. Replaced `catch (MsalException ex)` with `catch (ArticleUserResolverAuthException ex)` — same log message, same return code (`ConfigurationError`)
3. Replaced `catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)` with `catch (ArticleUserResolverServiceException ex)` — same log message, same return code (`ExternalServiceError`)
4. Removed `using Microsoft.Identity.Client;` from `BackfillArticleRequestedByHandlerTests.cs`
5. Renamed `Handle_WhenResolverThrowsMsalException_ReturnsConfigurationError` → `Handle_WhenResolverThrowsAuthException_ReturnsConfigurationError`; mock throws `ArticleUserResolverAuthException` instead of `MsalUiRequiredException`
6. Renamed `Handle_WhenResolverThrowsODataError_ReturnsExternalServiceError` → `Handle_WhenResolverThrowsServiceException_ReturnsExternalServiceError`; mock throws `ArticleUserResolverServiceException` instead of `ODataError`

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — removed SDK using and catch types
- `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — removed SDK using, updated two test methods

## Tests
All 11 tests in `BackfillArticleRequestedByHandlerTests` pass. Test count unchanged.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BackfillArticleRequestedBy" --no-restore
```
Result: All tests pass (exit code 0).

## Notes
- `BackfillArticleRequestedByHandler.cs` now has zero SDK-specific references in the Article feature namespace
- `UnauthorizedAccessException` and generic `Exception` catch blocks in the handler are unchanged
- The csproj package references for `Microsoft.Graph` and `Microsoft.Identity.Web` remain — they are needed by `GraphService`, `GraphPlannerService`, and `GraphCatalogDocumentsStorage` in the Application project. The architectural violation (SDK catch in handler) is resolved; removing those package references is not possible without a larger refactor.

## PR Summary
Fixed a Clean Architecture violation where `BackfillArticleRequestedByHandler` caught `MsalException` and `ODataError` (infrastructure SDK types) directly in the Application layer. The fix introduces two domain exceptions (`ArticleUserResolverAuthException`, `ArticleUserResolverServiceException`) in the Article contracts namespace and moves exception translation to `GraphArticleUserResolver`, the adapter that already has legitimate access to those SDK types. The handler now catches only domain exceptions, making it fully testable without SDK stubs.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/ArticleUserResolverAuthException.cs` — new domain exception for auth failures
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/ArticleUserResolverServiceException.cs` — new domain exception for service failures
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs` — added XML exception contract docs
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — catches MsalException and ODataError, translates to domain exceptions
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — catches domain exceptions only; removed `using Microsoft.Identity.Client`
- `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — updated two tests to throw domain exceptions

## Status
DONE
