# Specification: Configuration version fallback reads API entry-point assembly

## Summary
`GetConfigurationHandler.GetVersionFromSources()` falls back to `Assembly.GetExecutingAssembly()`, which is `Anela.Heblo.Application`, not the deployed host `Anela.Heblo.API`. Change the fallback to prefer the entry assembly and strengthen tests so the mismatch is detectable.

## Background
Version priority: (1) `APP_VERSION` configuration, (2) assembly informational version, (3) assembly version, (4) default `1.0.0`. Steps 2-3 read the Application assembly. If CI stamps only the API csproj, the fallback reports the wrong version where `APP_VERSION` is unset (local dev, ad-hoc environments). Issue #4423 (arch-review).

## Functional Requirements

### FR-1: Fallback reads the entry-point assembly
Steps 2 and 3 use `Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()`, resolved once and used for both steps.
**Acceptance criteria:**
- With `APP_VERSION` set, behaviour is unchanged.
- With `APP_VERSION` absent/empty, version is taken from the entry assembly when one exists, else from the Application assembly.
- Priority order and the `1.0.0` default are unchanged.

### FR-2: Testable assembly selection
Assembly resolution must be verifiable by tests without depending on the test host identity.
**Acceptance criteria:**
- A test proves the entry assembly is preferred over the executing assembly (e.g. an internal/overridable assembly provider or optional constructor seam, keeping the existing public constructor `(IConfiguration, ILogger)` working for DI and existing tests).
- A test proves fallback to the executing assembly when the entry assembly is null.
- Existing tests continue to pass.

## Non-Functional Requirements
### NFR-1: Performance
Negligible; reflection on a single assembly per request, as today.
### NFR-2: Security
No new surface; version string is already exposed.

## Data Model
None. `GetConfigurationResponse` unchanged.

## API / Interface Design
No API/contract change; OpenAPI client unaffected.

## Dependencies
None new. Related: #4374 (IConfiguration injection) is not addressed here.

## Out of Scope
- Injecting version via `IOptions<ApplicationVersionOptions>` (the stricter alternative), and removing `IConfiguration` from the handler.
- CI version stamping changes.
- Frontend changes.

## Open Questions
None.

## Status: COMPLETE
