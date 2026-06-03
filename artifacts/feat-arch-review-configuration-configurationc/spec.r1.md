# Specification: Move Infrastructure Constants out of Domain Layer (Configuration Module)

## Summary
The `ConfigurationConstants.cs` file in the Domain layer currently mixes domain/application-level configuration keys with infrastructure-specific constants (Application Insights, CORS, health check tags, authentication scheme names). This violates Clean Architecture's dependency rule. Split the file so the Domain layer retains only application-level keys, and move infrastructure constants into the API project where they are actually consumed.

## Background
Clean Architecture mandates that the Domain layer be free of infrastructure concerns — it must not know about hosting, middleware, observability providers, or composition-root wiring. The current `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` violates this by holding string constants that describe how the API host wires itself up:

- **Application Insights keys**: `APPLICATION_INSIGHTS_CONNECTION_STRING`, `APPINSIGHTS_INSTRUMENTATION_KEY`, `APPLICATIONINSIGHTS_CONNECTION_STRING`
- **Database connection key**: `DEFAULT_CONNECTION`
- **CORS**: `CORS_ALLOWED_ORIGINS`, `CORS_POLICY_NAME`
- **Health check tags**: `DB_TAG` (`"db"`), `POSTGRESQL_TAG`, `DATABASE_HEALTH_CHECK`
- **Auth scheme name**: `MOCK_AUTH_SCHEME`

Every consumer of these constants is in `Anela.Heblo.API` (`ServiceCollectionExtensions.cs`, `AuthenticationExtensions.cs`, `ApplicationBuilderExtensions.cs`, `LoggingExtensions.cs`, `HangfireAuthenticationMiddleware.cs`). No Domain or Application handler references them. Their physical location in Domain creates an inverted dependency: a rename of the CORS policy or health check tag in the API forces a Domain edit.

This refactor restores the proper dependency direction without changing any runtime behavior.

## Functional Requirements

### FR-1: Retain application-level constants in Domain
The Domain `ConfigurationConstants.cs` file must continue to expose constants that are genuinely cross-layer application configuration keys, used (or potentially used) by domain/application code:

- `APP_VERSION`
- `USE_MOCK_AUTH`
- `BYPASS_JWT_VALIDATION`

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` exists and contains only the three constants above (and any other constants in the original file that are not infrastructure-specific — see Open Questions).
- The class name, namespace, accessibility, and constant string values are unchanged for the retained constants.
- No reference to Application Insights, CORS, database connection strings, health check tags, or authentication schemes remains in the Domain project.

### FR-2: Introduce `InfrastructureConstants` in the API project
Create a new file `backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs` that holds the infrastructure-specific constants previously in Domain.

**Acceptance criteria:**
- The new file exists at `backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs`.
- It declares a public static class `InfrastructureConstants` in namespace `Anela.Heblo.API.Infrastructure`.
- It contains the following constants with their original string values exactly preserved:
  - `APPLICATION_INSIGHTS_CONNECTION_STRING`
  - `APPINSIGHTS_INSTRUMENTATION_KEY`
  - `APPLICATIONINSIGHTS_CONNECTION_STRING`
  - `DEFAULT_CONNECTION`
  - `CORS_ALLOWED_ORIGINS`
  - `CORS_POLICY_NAME`
  - `DB_TAG`
  - `POSTGRESQL_TAG`
  - `DATABASE_HEALTH_CHECK`
  - `MOCK_AUTH_SCHEME`
- Constant names and string literal values are byte-identical to the originals (so configuration keys read from environment variables, CORS policy lookups, and health check tag matching all continue to resolve).

### FR-3: Update API consumers to reference the new location
All references to the moved constants inside the `Anela.Heblo.API` project must be updated to use `InfrastructureConstants` instead of the Domain `ConfigurationConstants` symbol.

**Acceptance criteria:**
- The following files compile against `InfrastructureConstants` (and no longer reference `ConfigurationConstants` for the moved members):
  - `ServiceCollectionExtensions.cs`
  - `AuthenticationExtensions.cs`
  - `ApplicationBuilderExtensions.cs`
  - `LoggingExtensions.cs`
  - `HangfireAuthenticationMiddleware.cs`
- A repository-wide search for each moved constant name returns hits only in `InfrastructureConstants.cs` (definition) and the API consumer files (usages). No occurrences remain in Domain or elsewhere.
- `using` directives are added/removed as needed; no `using Anela.Heblo.Domain.Features.Configuration;` is left dangling.

### FR-4: Behavior preservation
The refactor must be purely structural. No runtime behavior changes.

**Acceptance criteria:**
- `dotnet build` succeeds for the entire solution.
- `dotnet format` reports no violations on changed files.
- All existing backend tests pass with no modification (test code should not need to change; if a test directly referenced a moved constant via Domain, update its `using` and symbol reference to the new location).
- Manual smoke check: the application starts locally and (a) reads `DEFAULT_CONNECTION` from configuration, (b) registers the CORS policy under `CORS_POLICY_NAME`, (c) tags database health checks with `DB_TAG`/`POSTGRESQL_TAG`/`DATABASE_HEALTH_CHECK`, and (d) registers the `MOCK_AUTH_SCHEME` authentication scheme when mock auth is enabled — all unchanged from current behavior.

### FR-5: No new public surface in Domain
The refactor must not introduce any new types, interfaces, or abstractions in the Domain layer to "bridge" the move (e.g., no new `IInfrastructureConfiguration` port created for this task).

**Acceptance criteria:**
- The Domain project's public API surface, aside from the removal of the moved constants from `ConfigurationConstants`, is unchanged.
- No new files are added under `backend/src/Anela.Heblo.Domain/`.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected. Constants remain compile-time `const string` (or equivalent) with identical values; only their declaring assembly changes.

### NFR-2: Security
No security impact. The constants are configuration-key names, not secret values; secret resolution continues to flow through Azure Key Vault and `IConfiguration` exactly as before.

### NFR-3: Maintainability
Post-refactor, a developer changing CORS policy naming, health check tag values, or Application Insights key names must edit only the API project. The Domain project no longer needs to be rebuilt for infrastructure-only changes, and Clean Architecture dependency rules are restored.

### NFR-4: Backward compatibility
Not applicable — these are internal source-level constants. The string literal values they expose (which are the actual configuration keys read at runtime) are preserved exactly, so external configuration (appsettings, environment variables, Key Vault secret names) requires no change.

## Data Model
N/A — this is a code organization refactor with no persistence, entity, or schema changes.

## API / Interface Design
N/A — no HTTP API, MediatR contract, or UI surface is affected. The only "interface" change is the C# namespace/type a few internal files import.

**Before:**
```csharp
using Anela.Heblo.Domain.Features.Configuration;
// ...
ConfigurationConstants.CORS_POLICY_NAME
```

**After:**
```csharp
using Anela.Heblo.API.Infrastructure;
// ...
InfrastructureConstants.CORS_POLICY_NAME
```

## Dependencies
- **Source layout assumption:** `backend/src/Anela.Heblo.API/Infrastructure/` either exists or can be created. (The brief specifies this as the target path.)
- No NuGet, framework, or external service dependencies are added or removed.
- No coordination with frontend, deployment, or infrastructure-as-code is required.

## Out of Scope
- Renaming any constants or changing their string values.
- Introducing typed configuration objects (`IOptions<T>` patterns) for these keys.
- Refactoring how `ServiceCollectionExtensions`, `AuthenticationExtensions`, etc. are structured beyond updating symbol references.
- Reviewing other Domain files for similar Clean Architecture violations — this spec covers only `ConfigurationConstants.cs`.
- Documentation updates beyond what is required to keep references accurate (no design-doc rewrites).
- Moving `APP_VERSION`, `USE_MOCK_AUTH`, or `BYPASS_JWT_VALIDATION` — these remain in Domain.

## Open Questions
None.

## Status: COMPLETE