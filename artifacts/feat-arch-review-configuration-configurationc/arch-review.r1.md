I have enough context. Writing the architecture review now.

# Architecture Review: Move Infrastructure Constants out of Domain Layer (Configuration Module)

## Skip Design: true

## Architectural Fit Assessment

The refactor is straightforward and **strongly aligned** with the project's stated Clean Architecture + Vertical Slice conventions (`docs/architecture/filesystem.md`, `docs/architecture/development_guidelines.md`). Every moved constant is consumed exclusively in `Anela.Heblo.API` composition-root code (`Extensions/` + `Infrastructure/`); none are referenced from `Domain` or `Application`. The Domain project today owns infrastructure host concerns it shouldn't know about — moving them restores the dependency direction without touching public types referenced cross-layer.

Integration points (all internal):
- 5 files listed in the spec plus one consumer the spec **missed**: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` uses `ConfigurationConstants.MOCK_AUTH_SCHEME` at line 113 (and also `USE_MOCK_AUTH` / `BYPASS_JWT_VALIDATION`, which stay in Domain). It must be updated to import `InfrastructureConstants` for the moved auth-scheme constant.
- `Application/Features/Configuration/GetConfigurationHandler.cs`, `Application/Features/UserManagement/UserManagementModule.cs`, and `Tests/Features/Configuration/GetConfigurationHandlerTests.cs` only touch the constants that **stay** in Domain (`USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`, `APP_VERSION`) — they are unaffected.

The Domain file also currently declares three constants the spec is **silent on**: `ASPNETCORE_ENVIRONMENT`, `DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`. A repo-wide search shows none of them are referenced via the constant symbol — only as raw strings elsewhere (e.g. `DiagnosticsController.cs:31` uses the literal `"ASPNETCORE_ENVIRONMENT"`). They are effectively dead code. The spec's "Open Questions: None" contradicts FR-1's "see Open Questions" note. This must be resolved before implementation.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain (Features/Configuration/)
└── ConfigurationConstants.cs           ← retains app-level keys only
    • APP_VERSION
    • USE_MOCK_AUTH
    • BYPASS_JWT_VALIDATION

Anela.Heblo.API (Infrastructure/)
└── InfrastructureConstants.cs          ← NEW: host/composition-root keys
    • APPLICATION_INSIGHTS_CONNECTION_STRING
    • APPINSIGHTS_INSTRUMENTATION_KEY
    • APPLICATIONINSIGHTS_CONNECTION_STRING
    • DEFAULT_CONNECTION
    • CORS_ALLOWED_ORIGINS
    • CORS_POLICY_NAME
    • DB_TAG, POSTGRESQL_TAG, DATABASE_HEALTH_CHECK
    • MOCK_AUTH_SCHEME

Consumers (Anela.Heblo.API):
    Extensions/ServiceCollectionExtensions.cs        → InfrastructureConstants
    Extensions/AuthenticationExtensions.cs           → both (MOCK_AUTH_SCHEME from Infra; USE_MOCK_AUTH/BYPASS from Domain)
    Extensions/ApplicationBuilderExtensions.cs       → InfrastructureConstants
    Extensions/LoggingExtensions.cs                  → InfrastructureConstants
    Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs   → Domain only (no change to constants used)
    Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs → both (MOCK_AUTH_SCHEME from Infra)
```

Dependency direction post-refactor: `API → Domain` (one-way; Domain no longer needs rebuild when CORS/health/auth names change).

### Key Design Decisions

#### Decision 1: Location of `InfrastructureConstants.cs`
**Options considered:**
- (a) `backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs` (root of `Infrastructure/`).
- (b) Split across topical files (e.g. `Infrastructure/Cors/CorsConstants.cs`, `Infrastructure/Telemetry/TelemetryConstants.cs`, etc.).
- (c) `Extensions/` folder alongside the consumers.

**Chosen approach:** (a) — single file at `Infrastructure/InfrastructureConstants.cs`, exactly as the spec requires.

**Rationale:** The brief and spec are explicit and the file is small (10 constants). The existing `Infrastructure/` folder already hosts cross-cutting hosting concerns (`ErrorResponseHelper.cs` sits at its root as precedent). Splitting per topic is YAGNI for a structural refactor. Keep churn minimal.

#### Decision 2: Static class vs. typed `IOptions<T>` migration
**Options considered:**
- (a) Move as-is — keep `public static class` with `public const string` members.
- (b) Take this opportunity to migrate selected keys (e.g. CORS, AppInsights) to strongly typed options classes.

**Chosen approach:** (a). The spec's Out-of-Scope section forbids (b).

**Rationale:** Behavior preservation is the explicit goal. Introducing `IOptions<T>` would change registration order, default-value semantics, and configuration-binding behavior. Out of scope; do not expand.

#### Decision 3: How to handle the three undocumented Domain constants (`ASPNETCORE_ENVIRONMENT`, `DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`)
**Options considered:**
- (a) Leave them in `Domain/ConfigurationConstants.cs`.
- (b) Delete them as dead code (zero symbol references repo-wide).
- (c) Move them to `InfrastructureConstants` since `ASPNETCORE_ENVIRONMENT` is an infrastructure-level concept.

**Chosen approach:** (a) leave them in place — out of scope for this task.

**Rationale:** The spec's FR-5 forbids changing Domain's public surface beyond removing the listed members; FR-4 mandates behavior preservation. Deleting unused publics is a separate refactor that warrants its own review. **Flag the dead code in PR description**; do not act on it here. (See "Specification Amendments" below — the contradiction between FR-1 and "Open Questions: None" must be resolved by the planner so future readers don't reopen this.)

#### Decision 4: `const string` vs. `static readonly string`
**Chosen approach:** Preserve original `public const string` declarations.

**Rationale:** Cross-assembly `const` baking is harmless here because the values are configuration-key *names* (not values), and they are now consumed only within `Anela.Heblo.API` (their declaring assembly) — `const` baking concerns don't apply. Matching the original keeps the diff structural.

## Implementation Guidance

### Directory / Module Structure

**Create:**
```
backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs
```

**Modify (constant references only):**
```
backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs
backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs
backend/src/Anela.Heblo.API/Extensions/LoggingExtensions.cs
backend/src/Anela.Heblo.API/Infrastructure/Authentication/HangfireAuthenticationMiddleware.cs   ← only `using` cleanup if needed (uses only Domain constants)
backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs ← MOCK_AUTH_SCHEME usage at :113
backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs                  ← strip moved members
```

The `using Anela.Heblo.Domain.Features.Configuration;` directive should be retained in files that still reference the remaining Domain constants (`AuthenticationExtensions.cs`, `HangfireAuthenticationMiddleware.cs`, `HangfireDashboardTokenAuthorizationFilter.cs`) and removed from files that no longer need it (`ServiceCollectionExtensions.cs`'s `AddCorsServices`/`AddHealthCheckServices`/`AddApplicationInsightsServices` use only moved constants — but the file still imports it; confirm whether any remaining usage exists before removing). `LoggingExtensions.cs` should drop the Domain `using`. `ApplicationBuilderExtensions.cs` should drop the Domain `using`.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs
namespace Anela.Heblo.API.Infrastructure;

public static class InfrastructureConstants
{
    // Application Insights configuration keys
    public const string APPLICATION_INSIGHTS_CONNECTION_STRING = "ApplicationInsights:ConnectionString";
    public const string APPINSIGHTS_INSTRUMENTATION_KEY = "APPINSIGHTS_INSTRUMENTATIONKEY";
    public const string APPLICATIONINSIGHTS_CONNECTION_STRING = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    // Database configuration keys
    public const string DEFAULT_CONNECTION = "DefaultConnection";

    // CORS configuration keys
    public const string CORS_ALLOWED_ORIGINS = "Cors:AllowedOrigins";

    // Policy / scheme names
    public const string CORS_POLICY_NAME = "AllowFrontend";
    public const string MOCK_AUTH_SCHEME = "Mock";

    // Health check tags
    public const string DB_TAG = "db";
    public const string POSTGRESQL_TAG = "postgresql";

    // Health check names
    public const string DATABASE_HEALTH_CHECK = "database";
}
```

String literal values must be **byte-identical** to the originals.

### Data Flow

No runtime data flow changes. Only the source-level type/namespace path of 10 string constants changes. Configuration resolution at runtime (`IConfiguration` lookups, CORS policy lookup by name, health-check tag filtering at `/health/ready`, `AddAuthentication(scheme)` registration) all operate on the same string values.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed consumer (`HangfireDashboardTokenAuthorizationFilter.cs:113`) leaves a dangling `ConfigurationConstants.MOCK_AUTH_SCHEME` reference, causing build failure. | High | Update the file together with the five listed in FR-3; treat the spec's consumer list as incomplete. Verify with a final repo-wide grep for each moved constant name returning hits only in `InfrastructureConstants.cs` (definition) + API consumers. |
| Stale `using Anela.Heblo.Domain.Features.Configuration;` directives left behind, or new `using Anela.Heblo.API.Infrastructure;` directives missed. | Low | Rely on `dotnet build` to surface unresolved symbols; run `dotnet format` to remove unused usings. |
| Editor/IDE inadvertently moves an APP-level constant (e.g. `USE_MOCK_AUTH`) during a "move to file" refactor, breaking Application/Tests consumers. | Medium | Do not use IDE "move members" tooling; create the new file by hand and remove specific members from the original. Verify with grep that `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`, and `APP_VERSION` remain in Domain. |
| Authentication scheme name change breaks runtime — `AddAuthentication("Mock")` and `AddScheme<>("Mock", ...)` must match `ClaimsIdentity(..., "Mock")` in `HangfireDashboardTokenAuthorizationFilter.cs:113`. | High | All four `MOCK_AUTH_SCHEME` references must point to the **same** `InfrastructureConstants.MOCK_AUTH_SCHEME` constant after the move; value `"Mock"` preserved exactly. |
| Reviewer assumes Domain still owns all constants because the file remains. | Low | PR description should explicitly call out the dependency-direction motivation and link the brief. |

## Specification Amendments

1. **FR-3 consumer list is incomplete.** Add `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` to the list. It references `ConfigurationConstants.MOCK_AUTH_SCHEME` at line 113 and must be updated to `InfrastructureConstants.MOCK_AUTH_SCHEME`. (It also reads `USE_MOCK_AUTH` and `BYPASS_JWT_VALIDATION`, which stay in Domain — no change needed for those.)

2. **FR-1 / Open Questions contradiction.** FR-1 says Domain retains "APP_VERSION, USE_MOCK_AUTH, BYPASS_JWT_VALIDATION (and any other constants in the original file that are not infrastructure-specific — see Open Questions)" but Open Questions reads "None". The three orphan constants (`ASPNETCORE_ENVIRONMENT`, `DEFAULT_VERSION`, `DEFAULT_ENVIRONMENT`) currently in Domain are unreferenced by symbol anywhere in the codebase. **Resolution to adopt:** leave them in Domain untouched for this task (out of scope; behavior-preserving), and either remove the "see Open Questions" parenthetical from FR-1 or add an explicit "These three constants remain unchanged" line. Flag the dead code in the PR description for a follow-up cleanup.

3. **FR-3 acceptance check refinement.** The repo-wide search should include the `docs/superpowers/plans/` historical planning files — these contain example code referencing `ConfigurationConstants.USE_MOCK_AUTH`. These are *documentation* (not compiled) and use the **kept** constant; they are valid and require no edit. Acceptance should specify "no occurrences remain in `backend/src/` Domain or in non-API consumers."

## Prerequisites

- `backend/src/Anela.Heblo.API/Infrastructure/` already exists (verified).
- No configuration, environment variable, secret, database migration, or infrastructure-as-code change is required — only `const string` *names* move; the string *values* (actual configuration keys read at runtime) are preserved exactly.
- Validation per `CLAUDE.md`: `dotnet build` + `dotnet format` for the solution, all touched tests pass, manual smoke check that the app starts and (CORS policy registered, DB health check tagged, mock auth scheme resolvable, AppInsights connection string read) all behave identically.