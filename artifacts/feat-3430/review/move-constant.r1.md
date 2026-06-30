# Code Review: Move Cross-Cutting Configuration Keys to Domain/Shared

## Summary
The implementation correctly moves `APP_VERSION`, `USE_MOCK_AUTH`, and `BYPASS_JWT_VALIDATION` from `ConfigurationConstants` into the new `InfrastructureConfigurationKeys` class in `Domain/Shared`. All files in scope per the task specification have been updated. The three moved constants no longer exist on `ConfigurationConstants`, and `ConfigurationConstants` retains only `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`. No file outside `Domain/Features/Configuration/` imports `Anela.Heblo.Domain.Features.Configuration` for the moved constants. All acceptance criteria are satisfied.

## Review Result: PASS

### task: move-constant
**Status:** PASS

## Overall Notes
The implementation is surgically correct within its stated scope. A few observations that are out of scope for this task but worth noting for future cleanup:

- Several files (`CatalogDocumentsModule`, `KnowledgeBaseModule`, `PhotobankModule`, `MeetingTasksModule`, `E2ETestAuthenticationMiddleware`, `ServiceCollectionExtensions.AddSwaggerServices`, `ApplicationBuilderExtensions`) still read `"UseMockAuth"` via a raw string literal rather than `InfrastructureConfigurationKeys.USE_MOCK_AUTH`. The task spec explicitly limited Application module files to only switching the `BYPASS_JWT_VALIDATION` reference and listed `E2ETestAuthenticationMiddleware` and `ServiceCollectionExtensions` as "remove stale using only" changes, so these raw strings are pre-existing and were correctly left untouched. They represent an incremental cleanup opportunity once this change is merged.
- `GetConfigurationHandlerTests.cs` correctly retains `using Anela.Heblo.Domain.Features.Configuration` for the `ConfigurationConstants.DEFAULT_VERSION` reference and the new `using Anela.Heblo.Domain.Shared` for the moved constants — both usings are live.
- `GetConfigurationHandler.cs` correctly retains `using Anela.Heblo.Domain.Features.Configuration` because `ApplicationConfiguration` lives in that namespace.
- No stray raw `"BypassJwtValidation"` strings were found outside the new constant definition itself.
