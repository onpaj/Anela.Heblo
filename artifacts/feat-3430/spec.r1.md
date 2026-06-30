# Specification: Move Cross-Cutting Configuration Keys to Domain/Shared

## Summary

`ConfigurationConstants` in `Domain/Features/Configuration/` currently acts as a de-facto shared global imported by 10 files across 4 projects (Application feature modules, API infrastructure, and the Microsoft365 adapter), violating the module-boundary rule that forbids direct references between feature modules. This refactor extracts the infrastructure configuration key constants (`BYPASS_JWT_VALIDATION`, `USE_MOCK_AUTH`, `APP_VERSION`) into a new cross-cutting file under `Domain/Shared/`, eliminating the illegal coupling with a mechanical rename and namespace swap.

## Background

The development guidelines state: "No direct references between feature modules — communication only through contracts/interfaces." `ConfigurationConstants` lives in `Anela.Heblo.Domain.Features.Configuration`, a feature module namespace. Ten consumers across CatalogDocuments, KnowledgeBase, Photobank, MeetingTasks, Marketing (via NoOpOutlookCalendarSync registration), the API infrastructure layer, and the Microsoft365 adapter all import `Anela.Heblo.Domain.Features.Configuration` solely to access these string keys. This creates a hard compile-time dependency from unrelated modules into the Configuration feature's domain layer. Any rename or namespace reorganisation in the Configuration module would break all ten consumers silently at compile time.

The fix is purely structural: move the three cross-cutting constants to `Domain/Shared/`, which is already the accepted home for cross-module domain utilities (`Result`, `CurrencyCode`, `Cooling`, etc.). `GetConfigurationHandler`, which legitimately owns the Configuration feature, will keep using the same constants — they just come from a neutral namespace.

## Functional Requirements

### FR-1: Create InfrastructureConfigurationKeys in Domain/Shared

Create a new static class `InfrastructureConfigurationKeys` at `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` in namespace `Anela.Heblo.Domain.Shared`. It must contain the following constants with identical string values to those currently in `ConfigurationConstants`:

```csharp
public const string APP_VERSION = "APP_VERSION";
public const string USE_MOCK_AUTH = "UseMockAuth";
public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
```

**Acceptance criteria:**
- File exists at the specified path.
- Namespace is `Anela.Heblo.Domain.Shared`.
- All three constants are present with exactly the same string values as they have today in `ConfigurationConstants`.
- Class is `public static`.

### FR-2: Remove the three constants from ConfigurationConstants

Remove `APP_VERSION`, `USE_MOCK_AUTH`, and `BYPASS_JWT_VALIDATION` from `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`. The remaining constants (`DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`) stay in place — they are owned by the Configuration feature and are not cross-cutting.

**Acceptance criteria:**
- `ConfigurationConstants` no longer declares `APP_VERSION`, `USE_MOCK_AUTH`, or `BYPASS_JWT_VALIDATION`.
- `ConfigurationConstants` still declares `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`.
- The file compiles cleanly.

### FR-3: Update all consumers to reference InfrastructureConfigurationKeys

Replace every occurrence of `ConfigurationConstants.BYPASS_JWT_VALIDATION`, `ConfigurationConstants.USE_MOCK_AUTH`, and `ConfigurationConstants.APP_VERSION` in the following files with `InfrastructureConfigurationKeys.<CONSTANT_NAME>`. Add `using Anela.Heblo.Domain.Shared;` where not already present. Remove `using Anela.Heblo.Domain.Features.Configuration;` from each file where it is no longer needed.

Files to update (all confirmed to import `Anela.Heblo.Domain.Features.Configuration` and use the constants being moved):

| File | Constants used |
|------|---------------|
| `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` | `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` | `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` | `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` | `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs` | `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs` | (uses namespace, verify which constants) |
| `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` | `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION` |
| `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` | (uses namespace, verify which constants) |
| `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` | `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION` |

The `GetConfigurationHandler` (`backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`) also uses these constants. It must be updated in the same pass: switch its `using` to `Anela.Heblo.Domain.Shared` for the moved constants while retaining the `using Anela.Heblo.Domain.Features.Configuration;` import for `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT` if it uses those.

**Acceptance criteria:**
- No file outside `Domain/Features/Configuration/` imports `Anela.Heblo.Domain.Features.Configuration` for the sole purpose of using the three moved constants.
- `using Anela.Heblo.Domain.Features.Configuration;` is removed from any file where it was only present to reference the moved constants.
- All listed files compile cleanly.

### FR-4: Full solution compiles and tests pass

After the above changes, the entire backend solution must build without errors or warnings introduced by this change.

**Acceptance criteria:**
- `dotnet build` exits 0 with no new errors or warnings.
- `dotnet format --verify-no-changes` exits 0 (or formatting is applied if the project uses auto-format).
- All existing unit and integration tests pass.

## Non-Functional Requirements

### NFR-1: No behavioral change

This is a pure structural refactor. The string values of all constants are identical before and after. No runtime behavior changes — environment variable names, `appsettings.json` keys, and application logic are unaffected.

**Acceptance criteria:**
- The constant values `"APP_VERSION"`, `"UseMockAuth"`, and `"BypassJwtValidation"` are unchanged.
- No logic is altered in any consumer file beyond changing the class name and namespace import.

### NFR-2: No new inter-module dependencies introduced

After the change, `Domain/Shared` is the only shared import consumers need. No consumer should gain a new dependency on any feature module's namespace as a side-effect.

### NFR-3: Surgical scope

Only the files listed in FR-3 (plus the new file from FR-1 and `ConfigurationConstants.cs` from FR-2) are modified. No reformatting, comment edits, or unrelated cleanup.

## Data Model

N/A — this is a pure code structural refactor with no database or entity changes.

## API / Interface Design

N/A — no API surface, DTOs, or contracts are modified. The key strings are identical, so no configuration file or environment variable changes are needed.

## Dependencies

- `Anela.Heblo.Domain` project — new file is added here; no new project references are introduced.
- All consumer projects (`Application`, `API`, `Adapters.Microsoft365`) already reference `Anela.Heblo.Domain`, so no `.csproj` changes are required.

## Out of Scope

- Moving `DEFAULT_VERSION` or `DEFAULT_ENVIRONMENT` — these are used only within the Configuration feature and should remain in `ConfigurationConstants`.
- Introducing a typed options class or `IOptions<T>` binding to replace the raw `IConfiguration.GetValue<bool>` calls — that is a separate architectural improvement.
- Changing the string values of any constant.
- Modifying any frontend code or API contracts.
- Updating the `Configuration` module's query handler logic beyond the namespace import change.
- Deleting `ConfigurationConstants` entirely — it retains two constants that legitimately belong to the Configuration feature.
- Any E2E or integration test changes — runtime behavior is identical.

## Open Questions

None.

## Status: COMPLETE
