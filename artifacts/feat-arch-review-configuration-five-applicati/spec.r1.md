# Specification: Replace `BypassJwtValidation` magic strings with `ConfigurationConstants.BYPASS_JWT_VALIDATION`

## Summary
Five Application-layer module files use the raw string literal `"BypassJwtValidation"` when reading configuration, instead of the existing `ConfigurationConstants.BYPASS_JWT_VALIDATION` constant. This change replaces those literals with the constant reference so that any future rename is centralized and compile-time safe.

## Background
`ConfigurationConstants.BYPASS_JWT_VALIDATION` is defined at `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:14` specifically to provide a single, rename-safe reference for this configuration key. The API layer (`HangfireDashboardTokenAuthorizationFilter.cs`, `AuthenticationExtensions.cs`, `HangfireAuthenticationMiddleware.cs`) and `UserManagementModule.cs` already use the constant correctly.

However, five Application-layer module files still pass the raw string `"BypassJwtValidation"` to `IConfiguration.GetValue<bool>(...)`. If the config key is ever renamed during an appsettings restructure, a search-and-replace on the constant will miss these five sites. They would silently fall back to the `GetValue<bool>` default (`false`), disabling the JWT-bypass branch without any compile-time or test-time signal. This defeats the purpose of the central constants file and is a latent maintenance hazard.

The fix is purely a refactor — runtime behavior must remain identical because the literal value `"BypassJwtValidation"` matches the constant's value exactly.

## Functional Requirements

### FR-1: Replace magic string with constant reference in five module files
In each of the files listed below, replace the literal `"BypassJwtValidation"` with `ConfigurationConstants.BYPASS_JWT_VALIDATION` and add the required `using Anela.Heblo.Domain.Features.Configuration;` directive if it is not already present.

Target files and lines (as identified at audit time — verify before editing):

| File | Line |
|------|------|
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` | 21 |
| `backend/src/Anela.Heblo.Application/Features/Marketing/MarketingModule.cs` | 38 |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | 58 |
| `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` | 27 |
| `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` | 41 |

**Acceptance criteria:**
- In each of the five files, the literal `"BypassJwtValidation"` no longer appears.
- Each file references `ConfigurationConstants.BYPASS_JWT_VALIDATION` in its place.
- Each file has a `using Anela.Heblo.Domain.Features.Configuration;` directive (only added when not already imported transitively or directly).
- A repo-wide search for `"BypassJwtValidation"` (the quoted string literal, case-sensitive) returns zero results outside `ConfigurationConstants.cs` itself.
- No other code changes are introduced in these files (no formatting, no adjacent refactors, no comment cleanup).

### FR-2: Preserve runtime behavior
The refactor must not alter how the bypass flag is read, defaulted, or interpreted.

**Acceptance criteria:**
- The `IConfiguration.GetValue<bool>(...)` call signature in each file is otherwise unchanged (same overload, same default behavior).
- The boolean returned for any given `appsettings.json` configuration is identical before and after the change.
- The conditional branch that consumes the value (registering bypass vs. real JWT auth wiring) is unchanged.

### FR-3: Build and tests must remain green
The refactor must compile cleanly and not break any existing tests.

**Acceptance criteria:**
- `dotnet build` succeeds with no new warnings introduced by this change.
- `dotnet format` reports no required changes after the edit.
- Any existing unit/integration tests that touch these five modules continue to pass.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. Constant references compile to the same string literal in IL; runtime behavior is identical.

### NFR-2: Security
No change to the security posture. The bypass flag continues to be read from configuration and continues to default to `false` when absent. This refactor reduces the risk of a future rename silently disabling JWT validation enforcement — a latent security benefit, not a regression vector.

### NFR-3: Maintainability
After this change, the configuration key `"BypassJwtValidation"` exists as a string literal in exactly one place (`ConfigurationConstants.cs`). All consumers go through the constant.

## Data Model
No data model changes.

## API / Interface Design
No API or interface changes. This is a pure internal refactor of module registration code.

## Dependencies
- The Application-layer projects already reference `Anela.Heblo.Domain` (where `ConfigurationConstants` lives), as evidenced by other Domain types being consumed throughout. No new project references are required.

## Out of Scope
- Renaming the constant or its underlying configuration key.
- Changing the default value or semantics of the bypass flag.
- Refactoring the surrounding module-registration logic, error handling, or DI wiring in the five files.
- Auditing other magic-string configuration keys elsewhere in the codebase (a separate concern).
- Adding new tests beyond verifying that existing tests still pass.
- Formatting or style changes unrelated to the literal replacement and the `using` directive.

## Open Questions
None.

## Status: COMPLETE