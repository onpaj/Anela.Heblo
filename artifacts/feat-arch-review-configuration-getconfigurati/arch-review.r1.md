# Architecture Review: Replace Direct Environment Variable Access with IConfiguration in GetConfigurationHandler

## Skip Design: true

## Architectural Fit Assessment

This is a pure surgical refactor inside a single Vertical Slice (`Features/Configuration`). The change strengthens existing patterns rather than introducing new ones:

- The handler already depends on `IConfiguration` (constructor at `GetConfigurationHandler.cs:19`) and uses it correctly for `UseMockAuth` (`GetConfigurationHandler.cs:66`). Routing the `APP_VERSION` read through the same injected abstraction removes a one-off deviation.
- The .NET host’s default `AddEnvironmentVariables()` registration ensures `_configuration[ConfigurationConstants.APP_VERSION]` returns the same value as the current `System.Environment.GetEnvironmentVariable(...)` call in every environment (Development, Staging, Production, Test). Behavioral parity holds.
- The repo already has a precedent for `ConfigurationBuilder().AddInMemoryCollection(...)` in unit tests (`backend/test/Anela.Heblo.Tests/Xcc/BackgroundRefresh/RefreshTaskConfigurationTests.cs:13-23`), so the new tests will mirror an established convention.
- No domain-model, MediatR contract, controller, or DI registration changes. `ApplicationConfiguration.CreateWithDefaults` (`backend/src/Anela.Heblo.Domain/Features/Configuration/ApplicationConfiguration.cs:24`) and `ConfigurationConstants.APP_VERSION` (`backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs:9`) stay untouched.

Integration points are minimal: only `GetConfigurationHandler.GetVersionFromSources()` changes.

## Proposed Architecture

### Component Overview

```
GetConfigurationRequest (MediatR)
        │
        ▼
GetConfigurationHandler  ── depends on ──► IConfiguration  ◄── env vars (APP_VERSION),
        │                                       ▲              appsettings, KeyVault
        │                                       │
        │                                       └── registered by host (Program.cs)
        ▼
GetVersionFromSources()    ── reads ──► _configuration[APP_VERSION]   (was: System.Environment.GetEnvironmentVariable)
        │                  ── reflects ──► Assembly.GetExecutingAssembly() informational/version attrs
        ▼
ApplicationConfiguration.CreateWithDefaults(version, env, useMockAuth)
        │
        ▼
GetConfigurationResponse   (unchanged DTO)
```

No new components, services, or registrations. The only edge that changes is the configuration-read edge inside `GetVersionFromSources()`.

### Key Design Decisions

#### Decision 1: Read APP_VERSION via indexer (`_configuration[KEY]`) rather than `GetValue<string>`
**Options considered:**
- A. `_configuration[ConfigurationConstants.APP_VERSION]` — returns `string?`, mirrors the direct env-var API the handler is replacing.
- B. `_configuration.GetValue<string?>(ConfigurationConstants.APP_VERSION)` — symmetric with the `UseMockAuth` read at line 66.

**Chosen approach:** Option A (indexer).

**Rationale:** The replaced call already returned `string?`. The indexer is the idiomatic `IConfiguration` accessor for a flat key that is intentionally optional (the fallback chain expects nulls). `GetValue<T>` adds conversion machinery that is not needed here. Both approaches resolve env vars identically — this is purely about readability and minimal diff.

#### Decision 2: Keep the three-step fallback chain intact and in place
**Options considered:**
- A. Leave `GetVersionFromSources()` as a private method; swap only the first read.
- B. Extract a `IVersionProvider` abstraction with a default implementation, register in DI, inject into the handler.

**Chosen approach:** Option A.

**Rationale:** YAGNI. No other consumer needs a version provider, and the spec scopes the change to a single-file refactor (`NFR-4: surgical change`, `NFR-2: no new dependencies`). Introducing a new abstraction expands blast radius for zero current benefit.

#### Decision 3: Add a dedicated unit-test file alongside the existing integration tests
**Options considered:**
- A. New `GetConfigurationHandlerTests.cs` next to `GetConfigurationEndpointTests.cs`.
- B. Augment the existing `GetConfigurationEndpointTests.cs` with handler-level cases.

**Chosen approach:** Option A.

**Rationale:** The existing file is an integration test fixture (`[Collection("WebApp")]`, `IClassFixture<HebloWebApplicationFactory>`) that exercises the live HTTP pipeline. The new tests are handler-level unit tests that construct `GetConfigurationHandler` directly with an in-memory `IConfiguration`. Mixing the two concerns in one file would muddy the test category boundary. The repo already separates unit and integration handler tests (e.g., `Features/Journal/*HandlerTests.cs`).

## Implementation Guidance

### Directory / Module Structure

**Modify** (1 file):
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs`
  - In `GetVersionFromSources()`, replace line 76:
    ```csharp
    var version = Environment.GetEnvironmentVariable(ConfigurationConstants.APP_VERSION);
    ```
    with:
    ```csharp
    var version = _configuration[ConfigurationConstants.APP_VERSION];
    ```
  - Adjust the adjacent log message wording if it references “environment variable” to keep it accurate (e.g., “Version resolved from configuration: {Version}”). This is the only allowed adjacent edit — it’s required for correctness, not cleanup.
  - Do **not** touch `using` directives: the file does not have a `using System;` declaration (`Environment` resolves via SDK `ImplicitUsings`), and `System.Reflection` remains needed by the assembly-version branches.

**Create** (1 file):
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` — handler-level unit tests with in-memory `IConfiguration`.

**Do not touch:**
- `ConfigurationConstants.cs`, `ApplicationConfiguration.cs`, `GetConfigurationRequest.cs`, `GetConfigurationResponse.cs`, `ConfigurationModule.cs`, `Program.cs`, or `GetConfigurationEndpointTests.cs`.

### Interfaces and Contracts

No interface changes. `IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>` contract is unchanged. The handler’s constructor signature, MediatR routing, and HTTP endpoint binding all remain identical.

Internally, the contract of `GetVersionFromSources()` (private, returns `string?`) is preserved: same return type, same null semantics, same precedence order.

### Data Flow

For a typical `GET /api/configuration` request:

1. MediatR dispatches `GetConfigurationRequest` to `GetConfigurationHandler.Handle`.
2. `BuildApplicationConfiguration()` calls `GetVersionFromSources()`.
3. `GetVersionFromSources()` now consults `_configuration[APP_VERSION]`. The `IConfiguration` provider chain (in order of precedence, last wins for the same key): `appsettings.json` → `appsettings.{Environment}.json` → User Secrets (Dev only) → **Environment Variables** → Azure Key Vault.
   - In Production/Staging containers, `APP_VERSION` is set as an env var by CI/CD, so the env-var provider wins — identical to today.
   - In Test (`HebloWebApplicationFactory`), the env var is absent, so the call returns null and the fallback proceeds.
4. If null/empty, fall back to `AssemblyInformationalVersionAttribute`; if still null/empty, fall back to `Assembly.GetExecutingAssembly().GetName().Version`.
5. `ApplicationConfiguration.CreateWithDefaults(version, env, useMockAuth)` applies the `"1.0.0"` default if all three sources returned null.
6. Response DTO is populated and returned. No change to the HTTP response shape.

**Required unit-test cases** (satisfying FR-3):
| # | Configuration setup | Expected `Version` |
|---|---------------------|--------------------|
| 1 | `{ "APP_VERSION": "2.5.1-ci.42" }` in-memory | `"2.5.1-ci.42"` |
| 2 | `{ "APP_VERSION": "" }` in-memory | Assembly informational version (or assembly version) — assert non-null & not equal to default `"1.0.0"` (the test-host assembly always has one) |
| 3 | empty in-memory dictionary | Same as case 2 |
| 4 | `APP_VERSION` not set, `UseMockAuth = true` | sanity check that `UseMockAuth` is still wired correctly (regression guard for the surgical change) |

Use `Microsoft.Extensions.Hosting.HostEnvironment` (or `Moq`/`NSubstitute` for `IHostEnvironment`) and `NullLogger<GetConfigurationHandler>.Instance` for the other constructor dependencies.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `IConfiguration` provider chain in some environment doesn’t actually include env vars, so APP_VERSION silently disappears in Production | HIGH | Verify `Program.cs` calls `AddEnvironmentVariables()` (default for `Host.CreateDefaultBuilder`/`WebApplication.CreateBuilder`); confirm KV-loaded secrets don’t define `APP_VERSION` (they don’t — KV uses prefixed secret names per CLAUDE.md). Add one assertion to the existing `GetConfiguration_ShouldReturnValidVersion` integration test only if non-trivial. |
| Adjacent log-message edit drifts beyond the surgical scope | LOW | Limit the message change to the one log line whose text becomes inaccurate. No formatting passes, no unrelated tidy-ups. |
| Reflection-based fallback branches (assembly informational version / assembly version) remain hard to unit-test deterministically because `Assembly.GetExecutingAssembly()` always returns the Application assembly | LOW | Accept this as a residual limitation outside this finding’s scope. Tests can still observe the *fallthrough* (non-null, non-empty string returned) without pinning the exact assembly value. Document this in the test file with a one-line comment if needed for clarity. |
| New test file inadvertently pulls in `WebApplicationFactory` boot cost via shared `using` namespaces | LOW | Construct the SUT directly: `new GetConfigurationHandler(configuration, hostEnvironment, NullLogger<GetConfigurationHandler>.Instance)`. No `[Collection("WebApp")]`, no `IClassFixture`. |
| Hidden caller relies on `Environment.GetEnvironmentVariable` side effect (e.g., concurrent set/get races) | NEGLIGIBLE | `IConfiguration` reads are thread-safe; no side effects. Confirmed by reading the handler — the call is a pure read. |

## Specification Amendments

The spec is sound and self-consistent. Two minor clarifications worth pinning before implementation:

1. **Log-message update is in-scope.** The current log line at `GetConfigurationHandler.cs:79` reads `"Version found from APP_VERSION environment variable: {Version}"`. After the refactor, the value may come from any configuration source (appsettings, KV, env var). The log message should change to reflect this (suggested: `"Version found from configuration ({Key}): {Version}"` or `"Version resolved from configuration: {Version}"`). Treat this as part of the surgical change, not as adjacent-code cleanup. Update `NFR-4` mentally to permit this one line.

2. **Assembly-fallback testability is *not* a goal of this change.** Spec FR-3 acceptance criterion (c) says “informational version absent — falls back to assembly version.” In practice `Assembly.GetExecutingAssembly()` will always return a non-null informational version when run from the standard test build. Recommended phrasing tweak: the test should assert “when `APP_VERSION` is absent, the returned version is non-null and not equal to the `DEFAULT_VERSION` constant,” which proves the fallback fired without trying to pin which assembly attribute won. This avoids a brittle test tied to MSBuild output.

3. **Test project package dependency.** `Microsoft.Extensions.Configuration` (the package providing `ConfigurationBuilder` and `AddInMemoryCollection`) is **already transitively available** in `Anela.Heblo.Tests` via the project reference to `Anela.Heblo.Application` and the explicit `Microsoft.Extensions.Hosting.Abstractions` reference (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj:29`). The conditional clause in spec **Dependencies** (“add it to the test project only”) will not need to fire. Verified by the existing precedent in `RefreshTaskConfigurationTests.cs:21-23`.

## Prerequisites

None. All required infrastructure is in place:
- `IConfiguration` is already injected into the handler.
- `Program.cs` already wires environment variables into the configuration provider chain via the default host builder.
- `ConfigurationConstants.APP_VERSION` already exists.
- The test project already supports `Microsoft.Extensions.Configuration` patterns (verified above).
- CI/CD pipeline injection of `APP_VERSION` as an env var is unaffected and unchanged.

Implementation can start immediately.