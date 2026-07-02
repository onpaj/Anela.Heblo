# Code Review: feat-3430

## Summary

The refactoring correctly creates `InfrastructureConfigurationKeys` in `Domain/Shared`, removes the three constants from `ConfigurationConstants`, and updates all callers. All string values are identical to the originals, `dotnet build` is clean, `dotnet format --verify-no-changes` exits 0, and all 52 Configuration tests pass.

Note: F-1 below was initially flagged as blocking but is a false positive — `using Anela.Heblo.Domain.Features.Configuration` in `GetConfigurationHandler.cs` is legitimately required for `ApplicationConfiguration` (defined in that namespace, used on lines 54 and 69). `dotnet format --verify-no-changes` confirms no unused import warning.

## Result: CLEAN

## Findings

No blocking issues found.

## Advisory (non-blocking)

**A-1: `USE_MOCK_AUTH` constant is not fully adopted by consumer files touched in this diff**

Several files updated in this diff to use `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION` still read `UseMockAuth` via a raw string literal on the very next or nearby line:

- `CatalogDocumentsModule.cs` line 27
- `KnowledgeBaseModule.cs` line 61
- `MeetingTasksModule.cs` line 23
- `PhotobankModule.cs` line 42
- `E2ETestAuthenticationMiddleware.cs` line 130
- `ServiceCollectionExtensions.cs` line 190
- `ApplicationBuilderExtensions.cs` line 27

These were pre-existing literals, not introduced by this diff, so they are out of scope for FR-3. However, the new constant exists precisely to eliminate these raw strings. A follow-up task to swap all remaining `"UseMockAuth"` literals to `InfrastructureConfigurationKeys.USE_MOCK_AUTH` would complete the intent of the refactoring.

**A-2: `GetConfigurationHandlerTests.cs` retains `using Anela.Heblo.Domain.Features.Configuration`**

This import is legitimately needed — `ConfigurationConstants.DEFAULT_VERSION` is referenced on lines 56 and 70. No change required.
