# Architecture Review: Move Cross-Cutting Configuration Keys to Domain/Shared

## Skip Design: true

## Architectural Fit Assessment

The spec is correct and the refactor is necessary. `ConfigurationConstants.BYPASS_JWT_VALIDATION`, `USE_MOCK_AUTH`, and `APP_VERSION` are consumed by 10 files across 4 projects (`Application`, `API`, `Adapters.Microsoft365`, and the test project) — none of which are the Configuration feature module. Sourcing these constants from `Anela.Heblo.Domain.Features.Configuration` imposes a hard compile-time dependency on the Configuration feature module from unrelated modules, which directly violates the project's "no direct references between feature modules" rule (`development_guidelines.md`, Required Practices).

`Domain/Shared/` is already the established home for exactly this kind of cross-module domain primitive: `Result`, `CurrencyCode`, and `Cooling` all live there and share the same `Anela.Heblo.Domain.Shared` namespace. Moving the three infrastructure config keys there is the correct classification: they are deployment-mode signals consumed by bootstrapping code across the entire solution, not Configuration-feature domain concepts.

The two remaining constants (`DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`) are internal to the Configuration feature and are used only in `GetConfigurationHandler` and its tests as fallback values and test assertions. They stay put.

One real-world nuance the spec captures but does not fully resolve: `ServiceCollectionExtensions.cs` and `E2ETestAuthenticationMiddleware.cs` both carry `using Anela.Heblo.Domain.Features.Configuration;` but do not reference `ConfigurationConstants` by name anywhere in their bodies. The `using` directive in both files is therefore already a dead import (the compiler would surface this as a warning under `dotnet format`). After this refactor those imports disappear entirely without requiring any constant-reference substitution; the implementer just removes the `using`.

The spec's consumer table lists `E2ETestAuthenticationMiddleware.cs` with "(uses namespace, verify which constants)" — the answer, confirmed by inspection: it uses only the literal string `"UseMockAuth"` directly (line 132), never `ConfigurationConstants`. The import is stale. Same finding for `ServiceCollectionExtensions.cs`: uses `"UseMockAuth"` as a raw string literal (line 191), `ConfigurationConstants` is never referenced. Both files need only import removal, not constant substitution.

`MarketingModule.cs` was listed in the brief but is NOT in the spec's file table. Inspection confirms it never uses `ConfigurationConstants` — it registers `NoOpOutlookCalendarSync` unconditionally and does not read auth configuration at all. It does not import `Anela.Heblo.Domain.Features.Configuration`. No change needed.

`GetConfigurationHandlerTests.cs` uses `ConfigurationConstants.APP_VERSION`, `ConfigurationConstants.USE_MOCK_AUTH`, and `ConfigurationConstants.DEFAULT_VERSION`. After the move, the test must switch its `using` to `Anela.Heblo.Domain.Shared` for `APP_VERSION` and `USE_MOCK_AUTH`, while keeping `Anela.Heblo.Domain.Features.Configuration` for `DEFAULT_VERSION` (which remains in `ConfigurationConstants`). This file is absent from the spec's FR-3 file table — it is a gap that must be addressed.

## Proposed Architecture

### Component Overview

```
Domain/Shared/                                  ← already exists
  InfrastructureConfigurationKeys.cs            ← NEW FILE (FR-1)
  Result.cs
  CurrencyCode.cs
  Cooling.cs

Domain/Features/Configuration/
  ConfigurationConstants.cs                     ← MODIFIED: remove 3 constants, keep 2 (FR-2)
  ApplicationConfiguration.cs                   ← unchanged

Application/Features/Configuration/
  GetConfigurationHandler.cs                    ← switch 2 constant refs to Domain.Shared (FR-3)

test/Anela.Heblo.Tests/Features/Configuration/
  GetConfigurationHandlerTests.cs               ← switch 2 constant refs to Domain.Shared (gap, see below)
```

All Application feature modules (`CatalogDocumentsModule`, `KnowledgeBaseModule`, `PhotobankModule`, `MeetingTasksModule`) and all API infrastructure files (`AuthenticationExtensions`, `HangfireAuthenticationMiddleware`, `HangfireDashboardTokenAuthorizationFilter`) switch from `ConfigurationConstants.BYPASS_JWT_VALIDATION` / `USE_MOCK_AUTH` to `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION` / `USE_MOCK_AUTH`. `ServiceCollectionExtensions.cs` and `E2ETestAuthenticationMiddleware.cs` drop their stale `using Anela.Heblo.Domain.Features.Configuration;` lines without any constant substitution. `Microsoft365AdapterServiceCollectionExtensions.cs` replaces both constant references.

No `.csproj` changes are needed: all consumers already reference `Anela.Heblo.Domain`, which is where the new file lands.

### Key Design Decisions

#### Decision 1: Class name — InfrastructureConfigurationKeys vs. alternatives
**Options considered:**
- `InfrastructureConfigurationKeys` (spec proposal)
- `AppConfigurationKeys`
- `EnvironmentConfigurationKeys`
- `SharedConfigurationKeys`

**Chosen approach:** `InfrastructureConfigurationKeys` as proposed by the spec.

**Rationale:** The three constants are read by infrastructure bootstrapping code (authentication setup, DI wiring, adapter selection) — not by business logic or domain entities. The name accurately captures their role: they are configuration keys that control infrastructure-mode switches (`BypassJwtValidation`, `UseMockAuth`) and a deployment metadata value (`APP_VERSION`). `AppConfigurationKeys` would collide conceptually with the existing Configuration feature's purpose. `InfrastructureConfigurationKeys` is unambiguous and consistent with the project's naming style (`InfrastructureConstants` already exists in the API layer for HTTP/CORS constants).

#### Decision 2: Keep ConfigurationConstants or delete it
**Options considered:**
- Delete `ConfigurationConstants` entirely after removing the three cross-cutting constants
- Keep it with the two remaining constants (`DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`)

**Chosen approach:** Keep `ConfigurationConstants` with `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`.

**Rationale:** Both remaining constants are used by `GetConfigurationHandler` and its tests. They represent fallback values for Configuration-feature-owned response fields, not infrastructure mode switches. They belong to the Configuration feature domain and should stay there.

#### Decision 3: Scope of the test file update
**Options considered:**
- Leave `GetConfigurationHandlerTests.cs` untouched (tests use the config key strings as dictionary keys, which still work)
- Update `GetConfigurationHandlerTests.cs` to match the moved constants

**Chosen approach:** Update `GetConfigurationHandlerTests.cs`.

**Rationale:** The test uses `ConfigurationConstants.APP_VERSION` and `ConfigurationConstants.USE_MOCK_AUTH` as dictionary keys in `IConfiguration` setup and as assertions. After the move those constants no longer live in `ConfigurationConstants`, so the file will produce a compilation error unless updated. This is not optional. The spec lists FR-4 ("full solution compiles") as a hard acceptance criterion; this update is a prerequisite. The spec's file table omits this file — the implementer must include it.

## Implementation Guidance

### Directory / Module Structure

1. Create `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` with namespace `Anela.Heblo.Domain.Shared`, class `public static InfrastructureConfigurationKeys`, three `public const string` members with identical string values to today's `ConfigurationConstants`.

2. Edit `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`: remove the three constant declarations. File retains `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`.

3. For each consumer file in the table below, perform the mechanical substitution and `using` update:

| File | Action |
|------|--------|
| `Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` | Replace `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`; swap `using` |
| `Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | Replace `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` |
| `Application/Features/Photobank/PhotobankModule.cs` | Replace `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` |
| `Application/Features/MeetingTasks/MeetingTasksModule.cs` | Replace `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` |
| `Application/Features/Configuration/GetConfigurationHandler.cs` | Replace `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.APP_VERSION`; add `using Anela.Heblo.Domain.Shared;`, retain `using Anela.Heblo.Domain.Features.Configuration;` (for `DEFAULT_VERSION` / `DEFAULT_ENVIRONMENT` via `ApplicationConfiguration.CreateWithDefaults`) |
| `API/Extensions/AuthenticationExtensions.cs` | Replace `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` for the Configuration import (retain the Authorization import) |
| `API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs` | Replace `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` |
| `API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs` | Remove stale `using Anela.Heblo.Domain.Features.Configuration;` (no constant references present) |
| `API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` | Replace `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` (retain `using Anela.Heblo.Domain.Features.Authorization;`) |
| `API/Extensions/ServiceCollectionExtensions.cs` | Remove stale `using Anela.Heblo.Domain.Features.Configuration;` (no constant references present) |
| `Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` | Replace `ConfigurationConstants.USE_MOCK_AUTH` and `ConfigurationConstants.BYPASS_JWT_VALIDATION`; swap `using` |
| `test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` | Replace `ConfigurationConstants.APP_VERSION` and `ConfigurationConstants.USE_MOCK_AUTH` with `InfrastructureConfigurationKeys` equivalents; add `using Anela.Heblo.Domain.Shared;`, retain `using Anela.Heblo.Domain.Features.Configuration;` for `ConfigurationConstants.DEFAULT_VERSION` |

### Interfaces and Contracts

No new interfaces or contracts are introduced. `InfrastructureConfigurationKeys` is a `public static` class with `public const string` members — the same pattern as the existing `ConfigurationConstants` and `InfrastructureConstants` classes in the codebase. No abstraction layer is needed for string constants.

### Data Flow

N/A — pure structural refactor. The string values are unchanged; runtime behavior is identical before and after.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file (`GetConfigurationHandlerTests.cs`) omitted from the spec's file table, causing a compilation error if not updated | High | Implementer must include this file in the change set; it is enumerated explicitly above |
| `ServiceCollectionExtensions.cs` and `E2ETestAuthenticationMiddleware.cs` carry stale `using` imports that are not tracked as constant-reference updates in the spec — risk of leaving dead imports in place | Low | Both files are in the table above with explicit "remove stale using" instructions; `dotnet format` will surface any remaining dead imports |
| `GetConfigurationHandler.cs` uses both moved constants (`APP_VERSION`, `USE_MOCK_AUTH`) and an unmoved one (`DEFAULT_VERSION` is used implicitly via `ApplicationConfiguration.CreateWithDefaults` which has `"1.0.0"` hardcoded — no constant reference) — risk of accidentally removing the Configuration feature `using` entirely | Low | The handler only needs `using Anela.Heblo.Domain.Features.Configuration;` for `ConfigurationConstants.DEFAULT_VERSION` check — confirm there is no direct reference; if absent, the Configuration `using` can be removed from the handler too |
| `ModuleBoundariesTests.cs` does not currently check the Configuration → other-feature boundary; after the move, no new architecture test is needed (the violation is eliminated, not moved) | Low | No test change required; `dotnet build` success is the verification criterion |
| Constant name collision if a future developer adds an `InfrastructureConfigurationKeys` type elsewhere | Very Low | The `Anela.Heblo.Domain.Shared` namespace is unique and not re-declared in other assemblies |

## Specification Amendments

**FA-1 (Gap — required fix):** Add `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` to the FR-3 file table. The file uses `ConfigurationConstants.APP_VERSION` (lines 31, 47, 78) and `ConfigurationConstants.USE_MOCK_AUTH` (line 79) as dictionary keys in in-memory `IConfiguration` setup. After `APP_VERSION` and `USE_MOCK_AUTH` are removed from `ConfigurationConstants`, this file will not compile. The fix is: add `using Anela.Heblo.Domain.Shared;` and replace the two constant references with `InfrastructureConfigurationKeys.APP_VERSION` and `InfrastructureConfigurationKeys.USE_MOCK_AUTH`. The `ConfigurationConstants.DEFAULT_VERSION` references on lines 55 and 69 stay as-is — `DEFAULT_VERSION` remains in `ConfigurationConstants`.

**FA-2 (Clarification — no code change):** The spec lists `API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs` and `API/Extensions/ServiceCollectionExtensions.cs` as using "namespace, verify which constants." Confirmed: neither file references `ConfigurationConstants` by name. Both files' `using Anela.Heblo.Domain.Features.Configuration;` directives are stale dead imports. The correct action for both is import removal only, with no constant substitution.

**FA-3 (Scope confirmation):** `MarketingModule.cs` was mentioned in the brief but correctly excluded from the spec. Confirmed: it does not import `Anela.Heblo.Domain.Features.Configuration` and uses no constants from that namespace. No change required.

## Prerequisites

None. All referenced projects already reference `Anela.Heblo.Domain` via their `.csproj` files. No new project references, NuGet packages, or migration steps are needed before implementation begins.
