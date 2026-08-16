# Specification: Move `InfrastructureConfigurationKeys` out of Domain layer

## Summary
`InfrastructureConfigurationKeys.cs` currently lives in `Anela.Heblo.Domain/Shared/` even though it holds purely operational/deployment constants (an env var name and two auth-bypass flag keys) with no domain meaning. This is a pure move/rename refactor: relocate the class to `Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs` under namespace `Anela.Heblo.Application.Shared`, and update the `using` directive in every one of the 10 confirmed consumer files. No constant values, method signatures, or runtime behavior change.

## Background
An automated architecture-review pass (GitHub issue #3749) flagged that Domain — the innermost ring of this Clean Architecture solution — must have zero knowledge of infrastructure/deployment concerns. `InfrastructureConfigurationKeys` defines three `const string` env-var/config-key names (`APP_VERSION`, `UseMockAuth`, `BypassJwtValidation`) that exist solely to let Application- and API-layer code read configuration/environment values consistently. These are not domain concepts, so the class does not belong in `Domain.Shared`.

Investigation of the actual codebase (not just the issue body) confirms:
- The class currently sits at `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`, namespace `Anela.Heblo.Domain.Shared`, containing exactly:
  ```csharp
  public static class InfrastructureConfigurationKeys
  {
      public const string APP_VERSION = "APP_VERSION";
      public const string USE_MOCK_AUTH = "UseMockAuth";
      public const string BYPASS_JWT_VALIDATION = "BypassJwtValidation";
  }
  ```
- `Anela.Heblo.Application/Shared/` already exists and already hosts several sibling classes (`ErrorCodes.cs`, `BaseResponse.cs`, `ListResponse.cs`, `HttpStatusCodeAttribute.cs`, etc.), all under namespace `Anela.Heblo.Application.Shared`. Moving `InfrastructureConfigurationKeys` there is consistent with this existing pattern — no new convention is introduced.
- Project-reference direction supports the move without any csproj changes:
  - `Anela.Heblo.Application.csproj` references `Anela.Heblo.Domain.csproj` (unaffected — Application already can see Domain, and after the move no longer needs to for this class).
  - `Anela.Heblo.API.csproj` references `Anela.Heblo.Application.csproj` (already present) — API layer can consume `Application.Shared` types.
  - `Anela.Heblo.Adapters.Microsoft365.csproj` references `Anela.Heblo.Application.csproj` (already present, confirmed by inspection) — this adapter project can also consume `Application.Shared` types without a new reference.
  - No project needs a new `<ProjectReference>` added; the move is purely namespace/using-directive surgery.
- Each of the 10 consumer files has **exactly one** `using Anela.Heblo.Domain.Shared;` line, and in every case it exists solely to resolve `InfrastructureConfigurationKeys` (confirmed no other symbols from `Domain.Shared`, e.g. `Cooling`, `CurrencyCode`, `Result`, are referenced in these files). This means each file's fix is a straight one-line `using` replacement, not a partial removal.

## Functional Requirements

### FR-1: Relocate the class file
Move `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs` to `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs`, changing its namespace declaration from `Anela.Heblo.Domain.Shared` to `Anela.Heblo.Application.Shared`. The class body (three `const string` members, their names, and their string values) must be byte-for-byte unchanged.

**Acceptance criteria:**
- File no longer exists at `backend/src/Anela.Heblo.Domain/Shared/InfrastructureConfigurationKeys.cs`.
- File exists at `backend/src/Anela.Heblo.Application/Shared/InfrastructureConfigurationKeys.cs` with namespace `Anela.Heblo.Application.Shared`.
- `APP_VERSION == "APP_VERSION"`, `USE_MOCK_AUTH == "UseMockAuth"`, `BYPASS_JWT_VALIDATION == "BypassJwtValidation"` remain unchanged.
- No other file in `Anela.Heblo.Domain/Shared/` (`Cooling.cs`, `CurrencyCode.cs`, `Result.cs`, `Rag/`) is touched.

### FR-2: Update every consumer's `using` directive
Update the `using Anela.Heblo.Domain.Shared;` line to `using Anela.Heblo.Application.Shared;` in each of the following 10 files — the authoritative, exhaustive list found by grepping the entire backend tree (source + tests) for `InfrastructureConfigurationKeys`:

1. `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/Microsoft365AdapterServiceCollectionExtensions.cs`
2. `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs`
3. `backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs`
4. `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs`
5. `backend/src/Anela.Heblo.Application/Features/CatalogDocuments/CatalogDocumentsModule.cs`
6. `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
7. `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
8. `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankModule.cs`
9. `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs`
10. `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`

No changes to any other line in these files (no reordering of surrounding `using` blocks beyond the one-line text substitution required to keep them syntactically valid; if the repo's `dotnet format`/StyleCop enforces `using` ordering, re-sort per that convention only — do not otherwise touch these files).

**Acceptance criteria:**
- None of the 10 files contains `using Anela.Heblo.Domain.Shared;` anymore.
- Each of the 10 files contains `using Anela.Heblo.Application.Shared;`.
- No other symbol resolution breaks: a full-tree grep confirms none of these 10 files reference any other `Anela.Heblo.Domain.Shared` type (`Cooling`, `CurrencyCode`, `Result`), so removing the old `using` is safe in every case.
- All references to `InfrastructureConfigurationKeys.APP_VERSION`, `.USE_MOCK_AUTH`, `.BYPASS_JWT_VALIDATION` in these files remain textually unchanged (only the `using` line changes).

### FR-3: No project-file (`.csproj`) changes required
Confirm and preserve as-is: `Anela.Heblo.Application.csproj`, `Anela.Heblo.API.csproj`, and `Anela.Heblo.Adapters.Microsoft365.csproj` already reference the projects needed to compile after the move (Application already references Domain; API and the Microsoft365 adapter already reference Application). No `<ProjectReference>` additions, removals, or version bumps are in scope.

**Acceptance criteria:**
- No `.csproj` file is modified as part of this change.
- Solution builds without introducing new project references.

### FR-4: No behavioral change
This is a structural refactor only. Constant values, configuration key names read from environment/appsettings, authentication bypass behavior, Hangfire dashboard auth behavior, and the `/config` endpoint's version/mock-auth reporting must all behave identically before and after the change.

**Acceptance criteria:**
- `GetConfigurationHandlerTests` (existing test file, updated only for its `using` directive per FR-2) passes unmodified in its assertions/logic.
- Manual/CI verification: application starts, `UseMockAuth`/`BypassJwtValidation` driven behavior in `AuthenticationExtensions`, `HangfireAuthenticationMiddleware`, `HangfireDashboardTokenAuthorizationFilter`, `CatalogDocumentsModule`, `MeetingTasksModule`, `PhotobankModule`, `SharedRagModule`, and `Microsoft365AdapterServiceCollectionExtensions` is unaffected.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this change has no runtime performance impact (compile-time namespace relocation only).

### NFR-2: Security
Not applicable — no change to which values are read, how auth-bypass flags are evaluated, or where secrets/config originate. The two flags (`UseMockAuth`, `BypassJwtValidation`) continue to be read the same way from `IConfiguration`; this refactor does not alter their semantics or trust boundary, only which layer owns the string-constant definitions.

## Data Model
Not applicable — no persisted or transmitted data model is involved. This concerns compile-time C# constants only.

## API / Interface Design
Not applicable — no public API, DTO, or endpoint contract changes. The `/config` endpoint (backed by `GetConfigurationHandler`) continues to return the same shape and values.

## Dependencies
- None beyond the existing project references already in place (see FR-3). No new NuGet packages, no new project references, no external services.

## Out of Scope
- Any change to the constant values themselves (`APP_VERSION`, `UseMockAuth`, `BypassJwtValidation`).
- Any change to how/where `UseMockAuth` or `BypassJwtValidation` are evaluated (i.e., no logic changes in `AuthenticationExtensions`, `HangfireAuthenticationMiddleware`, `HangfireDashboardTokenAuthorizationFilter`, module files, or `GetConfigurationHandler` beyond the `using` directive).
- Splitting the class further (e.g., separate API-local constants as one alternative the original issue floated) — the brief's chosen fix is a single relocation to `Application/Shared`, and this spec follows that directive rather than the alternative "duplicate into API/Infrastructure" option also mentioned in the issue.
- Any other file in `Domain/Shared/` (`Cooling.cs`, `CurrencyCode.cs`, `Result.cs`, `Rag/`) — untouched.
- Renaming the class itself, or renaming its three constants — unchanged.
- `.csproj` / project-reference changes (confirmed unnecessary per FR-3).

## Open Questions
None.

## Status: COMPLETE
