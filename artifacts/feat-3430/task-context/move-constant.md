# Task Plan: feat-3430

### task: move-constant

**Goal:** Create InfrastructureConfigurationKeys, remove constants from ConfigurationConstants, update all consumers, verify build.

**Context:**
Three configuration key constants (`BYPASS_JWT_VALIDATION`, `USE_MOCK_AUTH`, `APP_VERSION`) are defined in `Anela.Heblo.Domain.Features.Configuration.ConfigurationConstants` but are used across infrastructure, API, and adapter layers — far outside the Configuration feature boundary. The refactor moves them to `Anela.Heblo.Domain.Shared.InfrastructureConfigurationKeys` so their namespace reflects their cross-cutting nature. The remaining constants in `ConfigurationConstants` (`DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`) are domain/Configuration-specific and stay put.

Key constraints:
- String values of all three constants must remain unchanged: `"APP_VERSION"`, `"UseMockAuth"`, `"BypassJwtValidation"`.
- `GetConfigurationHandler` and its test both need `Anela.Heblo.Domain.Features.Configuration` retained (for `DEFAULT_VERSION`/`DEFAULT_ENVIRONMENT`) but must also add `Anela.Heblo.Domain.Shared` for the moved constants.
- Several module files (`CatalogDocumentsModule`, `KnowledgeBaseModule`, `PhotobankModule`, `MeetingTasksModule`) already use raw string `"UseMockAuth"` (not the constant) for `useMockAuth` — the spec asks only that their `BYPASS_JWT_VALIDATION` reference is switched; the raw string `"UseMockAuth"` lines are left as-is since they do not reference the constant.
- `E2ETestAuthenticationMiddleware` and `ServiceCollectionExtensions` import `Anela.Heblo.Domain.Features.Configuration` but do not reference any of the three moved constants. Their stale using directives should be removed.

**Files to create/modify:**

- `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` (create)
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` (modify)
- `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs` (modify)
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` (modify)
- `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs` (modify)
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` (modify)
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` (modify)
- `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs` (modify)
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs` (modify)
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/E2ETestAuthenticationMiddleware.cs` (modify — remove stale using only)
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` (modify)
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (modify — remove stale using only)
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs` (modify)
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` (modify)

**Implementation steps:**

1. **Create `InfrastructureConfigurationKeys.cs`**

   Create `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`:
   ```csharp
   namespace Anela.Heblo.Domain.Shared;

   public static class InfrastructureConfigurationKeys
   {
       public const string APP_VERSION = "APP_VERSION";
       public const string USE_MOCK_AUTH = "UseMockAuth";
       public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
   }
   ```

2. **Remove three constants from `ConfigurationConstants.cs`**

   Remove the `APP_VERSION`, `USE_MOCK_AUTH`, and `BYPASS_JWT_VALIDATION` lines. The resulting file keeps only:
   ```csharp
   namespace Anela.Heblo.Domain.Features.Configuration;

   /// <summary>
   /// Configuration constants and keys
   /// </summary>
   public static class ConfigurationConstants
   {
       // Default values
       public const string DEFAULT_VERSION = "1.0.0";
       public const string DEFAULT_ENVIRONMENT = "Production";
   }
   ```

3. **Update `CatalogDocumentsModule.cs`**

   - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`
   - The `"UseMockAuth"` raw string literal on line 27 is left unchanged (it does not use the constant).

4. **Update `KnowledgeBaseModule.cs`**

   - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`
   - The `"UseMockAuth"` raw string literal is left unchanged.

5. **Update `PhotobankModule.cs`**

   - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`
   - The `"UseMockAuth"` raw string literal is left unchanged.

6. **Update `MeetingTasksModule.cs`**

   - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`
   - The `"UseMockAuth"` raw string literal is left unchanged.

7. **Update `GetConfigurationHandler.cs`**

   - Keep `using Anela.Heblo.Domain.Features.Configuration;` (needed for `DEFAULT_VERSION`/`DEFAULT_ENVIRONMENT` via `ApplicationConfiguration.CreateWithDefaults`).
   - Add `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.USE_MOCK_AUTH` → `InfrastructureConfigurationKeys.USE_MOCK_AUTH`
   - Change `ConfigurationConstants.APP_VERSION` → `InfrastructureConfigurationKeys.APP_VERSION`

8. **Update `AuthenticationExtensions.cs`**

   - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.USE_MOCK_AUTH` → `InfrastructureConfigurationKeys.USE_MOCK_AUTH`
   - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`

9. **Update `HangfireAuthenticationMiddleware.cs`**

   - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
   - Change `ConfigurationConstants.USE_MOCK_AUTH` → `InfrastructureConfigurationKeys.USE_MOCK_AUTH`
   - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`

10. **Update `E2ETestAuthenticationMiddleware.cs`**

    - Remove the stale `using Anela.Heblo.Domain.Features.Configuration;` line. The file uses only a raw string `"UseMockAuth"` on line 131 inside `ShouldBeRegistered`, which is not a constant reference and does not need changing.

11. **Update `HangfireDashboardTokenAuthorizationFilter.cs`**

    - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
    - Change `ConfigurationConstants.USE_MOCK_AUTH` → `InfrastructureConfigurationKeys.USE_MOCK_AUTH`
    - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`

12. **Update `ServiceCollectionExtensions.cs`**

    - Remove the stale `using Anela.Heblo.Domain.Features.Configuration;` line. No constant from that namespace is actually referenced in the file body (the `"UseMockAuth"` on line 191 inside `AddSwaggerServices` is a raw string literal).

13. **Update `Microsoft365AdapterServiceCollectionExtensions.cs`**

    - Replace `using Anela.Heblo.Domain.Features.Configuration;` with `using Anela.Heblo.Domain.Shared;`
    - Change `ConfigurationConstants.USE_MOCK_AUTH` → `InfrastructureConfigurationKeys.USE_MOCK_AUTH`
    - Change `ConfigurationConstants.BYPASS_JWT_VALIDATION` → `InfrastructureConfigurationKeys.BYPASS_JWT_VALIDATION`

14. **Update `GetConfigurationHandlerTests.cs`**

    - Keep `using Anela.Heblo.Domain.Features.Configuration;` (still needed for `ConfigurationConstants.DEFAULT_VERSION` used in two test assertions).
    - Add `using Anela.Heblo.Domain.Shared;`
    - Change `ConfigurationConstants.APP_VERSION` → `InfrastructureConfigurationKeys.APP_VERSION` (appears in two test cases as dictionary key).
    - Change `ConfigurationConstants.USE_MOCK_AUTH` → `InfrastructureConfigurationKeys.USE_MOCK_AUTH` (appears in one test case as dictionary key).
    - Leave `ConfigurationConstants.DEFAULT_VERSION` references unchanged.

15. **Run `dotnet build`**

    ```
    dotnet build backend/Anela.Heblo.sln
    ```
    Fix any remaining `CS0117` (missing member) or `CS8019` (unused using) errors before declaring done.

**Tests to write/update:**

- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — update existing tests as described in step 14. No new tests are required; behaviour is unchanged.

**Acceptance criteria:**

- `dotnet build` exits 0 with no errors or warnings introduced by this change.
- No file outside `Domain/Features/Configuration/` imports `Anela.Heblo.Domain.Features.Configuration` for the three moved constants (`APP_VERSION`, `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`).
- `ConfigurationConstants` retains only `DEFAULT_VERSION` and `DEFAULT_ENVIRONMENT`.
- `InfrastructureConfigurationKeys` contains exactly `APP_VERSION = "APP_VERSION"`, `USE_MOCK_AUTH = "UseMockAuth"`, `BYPASS_JWT_VALIDATION = "BypassJwtValidation"` — string values identical to the originals.
- All existing tests pass (`dotnet test`).
