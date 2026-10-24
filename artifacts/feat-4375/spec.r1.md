# Specification: Remove ApplicationConfiguration domain entity and ConfigurationConstants

## Summary
The Configuration module's Domain layer contains `ApplicationConfiguration` (a three-field holder with a null-coalescing factory) and `ConfigurationConstants` (two default strings). The only consumer, `GetConfigurationHandler`, builds the entity and immediately copies it into `GetConfigurationResponse`. This refactor removes both Domain types, inlines the defaults in the handler, and moves test coverage to the handler. External behaviour of `GET /api/configuration` does not change.

## Background
Issue #4375 (arch-review finding, YAGNI). The entity enforces no invariant (its null checks are unreachable because `CreateWithDefaults` always supplies non-null values), has the same fields as the response DTO, and forces a change to the endpoint to touch three files. Affected files:
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs`
- `backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/ApplicationConfigurationTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` (references `ConfigurationConstants.DEFAULT_VERSION`)
- `docs/processes/module-configuration.md` (references `ConfigurationConstants`)

## Functional Requirements

### FR-1: Remove Domain types
Delete `ApplicationConfiguration.cs` and `ConfigurationConstants.cs`; the Domain `Features/Configuration` folder becomes empty and is removed.
**Acceptance criteria:**
- Neither type exists anywhere in `backend/` (grep returns no matches).
- `dotnet build` succeeds.

### FR-2: Inline defaults in handler
`GetConfigurationHandler` builds `GetConfigurationResponse` directly. Defaults `"1.0.0"` (version) and `"Production"` (environment) become private consts in the handler. Resolution order for version (APP_VERSION -> informational version -> assembly version -> default), environment (`ASPNETCORE_ENVIRONMENT` -> default) and `UseMockAuth` (default false) is unchanged. Remove the now-unused `using Anela.Heblo.Domain.Features.Configuration;`.
**Acceptance criteria:**
- Response values are identical to before for every input combination.
- Debug log of the response is preserved.

### FR-3: Move tests to the handler
Delete `ApplicationConfigurationTests`. Add handler tests covering the behaviours it covered: default environment ("Production") when `ASPNETCORE_ENVIRONMENT` is absent; provided environment passes through; `UseMockAuth` default false/true. Version fallback to `"1.0.0"` is not reachable in practice (assembly version always exists) and need not be tested. Update `GetConfigurationHandlerTests` to stop referencing `ConfigurationConstants.DEFAULT_VERSION` (use a literal `"1.0.0"` in the `NotBe` assertions).
**Acceptance criteria:**
- All tests in `Features/Configuration` pass, including `GetConfigurationEndpointTests`.

### FR-4: Update process doc
`docs/processes/module-configuration.md`: replace `ConfigurationConstants.DEFAULT_VERSION` mention with the handler constant, remove the `ConfigurationConstants.cs` code entry point, bump `verified_at`; run `python3 scripts/process-docs/check.py index`.
**Acceptance criteria:** the check script passes.

## Non-Functional Requirements
### NFR-1: Performance
No change.
### NFR-2: Security
No change; endpoint stays unauthenticated and exposes the same three fields.

## Data Model
None. No persistence; no migrations.

## API / Interface Design
`GET /api/configuration` request/response contract (`GetConfigurationResponse`: Version, Environment, UseMockAuth) is unchanged, so the generated OpenAPI/TypeScript clients are unaffected.

## Dependencies
None.

## Out of Scope
- Changing version/environment resolution logic.
- Frontend changes.
- Removing `ConfigurationModule.cs` or the DTOs.

## Open Questions
None.

## Status: COMPLETE
