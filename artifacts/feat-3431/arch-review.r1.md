# Architecture Review: Remove IHostEnvironment from GetConfigurationHandler

## Skip Design: true

## Architectural Fit Assessment

This is a two-file cleanup that eliminates a genuine architectural violation. `GetConfigurationHandler`
lives in `Anela.Heblo.Application` but injects `IHostEnvironment` from `Microsoft.Extensions.Hosting`
— a hosting-layer contract that belongs in the outer ring (`Anela.Heblo.API`), not in the Application
layer. The project's own guidelines explicitly forbid this class of dependency: the development
guidelines describe each layer's boundary, and the pattern for host-environment reads is well
established elsewhere in the codebase (`EnvironmentTelemetryInitializer`, `E2ETestAuthenticationMiddleware`
all live in the API project where `IHostEnvironment` legitimately belongs).

The violation is also the *only* reason the test suite needs an `IHostEnvironment` mock in
`GetConfigurationHandlerTests`. Removing it eliminates dead scaffolding and makes the test factory
`CreateHandler` a clean in-memory-config builder with no substitutes at all.

`IHostEnvironment` is used in other places in the codebase, but legitimately: `FileStorageModule`
(Application layer bootstrap — module registration, not a handler), `PersistenceModule.cs` (infra
layer), and `ApplicationModule.AddApplicationServices` (bootstrap method). Those uses are at
wiring-time, not inside a MediatR handler, and are out of scope.

The proposed fix — reading `ASPNETCORE_ENVIRONMENT` directly from `IConfiguration` with a fallback
to `ConfigurationConstants.DEFAULT_ENVIRONMENT` — is idiomatic .NET. ASP.NET Core itself populates
this key from the environment variable before the application starts; reading it via `IConfiguration`
is exactly what the runtime does internally when no hosting abstraction is available. The fallback
constant `"Production"` matches what `IHostEnvironment.EnvironmentName` returns when the variable
is unset, so runtime behaviour is preserved exactly.

There is no interface change, no DI registration change, no database touch, no frontend impact.
This review rates the spec accurate and complete with one amendment noted below.

## Proposed Architecture

### Component Overview

| Component | Change | Location |
|-----------|--------|----------|
| `GetConfigurationHandler` | Remove `IHostEnvironment` field + constructor param; replace `_environment.EnvironmentName` with `_configuration["ASPNETCORE_ENVIRONMENT"] ?? ConfigurationConstants.DEFAULT_ENVIRONMENT`; add comment; remove `using Microsoft.Extensions.Hosting` | `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` |
| `GetConfigurationHandlerTests` | Remove `NSubstitute` mock of `IHostEnvironment`; pass environment via `configData["ASPNETCORE_ENVIRONMENT"]` in `CreateHandler`; add `ASPNETCORE_ENVIRONMENT` to any test that asserts `response.Environment` | `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs` |

No new files. No DI change. No module registration change. No migration.

### Key Design Decisions

#### Decision 1: Read environment from IConfiguration["ASPNETCORE_ENVIRONMENT"], not a wrapper abstraction

**Options considered:**
- (a) Read `_configuration["ASPNETCORE_ENVIRONMENT"] ?? ConfigurationConstants.DEFAULT_ENVIRONMENT` directly (spec proposal).
- (b) Introduce a thin `IEnvironmentNameProvider` domain interface, implemented by an adapter in the API layer that delegates to `IHostEnvironment`.

**Chosen approach:** (a) Direct `IConfiguration` read.

**Rationale:** Option (b) is engineering overhead for a value that is already present in
`IConfiguration`. ASP.NET Core's hosting system writes `ASPNETCORE_ENVIRONMENT` into the
configuration pipeline via `ChainedConfigurationSource` before any module runs, so it is always
available to `IConfiguration` at handler execution time. The fallback to `DEFAULT_ENVIRONMENT`
handles the rare case where the key is absent (e.g. isolated unit tests that do not set it), which
is the same safety net the deleted `IHostEnvironment` mock was providing in tests. Option (b) adds
an interface, an adapter class, a DI registration, and a second abstraction to mock in tests — all
to read a string that is already in the dictionary. Reject it.

#### Decision 2: Fallback value is ConfigurationConstants.DEFAULT_ENVIRONMENT ("Production")

**Options considered:**
- (a) `ConfigurationConstants.DEFAULT_ENVIRONMENT` ("Production") — spec proposal.
- (b) A different default (e.g. `"Development"`, `"Unknown"`).

**Chosen approach:** (a).

**Rationale:** `ConfigurationConstants.DEFAULT_ENVIRONMENT` is defined in the Domain layer and is
already the value that `ApplicationConfiguration.CreateWithDefaults` uses when `environment` is
`null`. "Production" is also the ASP.NET Core runtime default when `ASPNETCORE_ENVIRONMENT` is
unset. Consistency with the existing constant is correct. Do not introduce a second literal.

#### Decision 3: Existing test coverage is sufficient — no new test cases required

**Options considered:**
- (a) Rely on the four existing tests after adapting `CreateHandler`.
- (b) Add a fifth test that explicitly verifies the "no ASPNETCORE_ENVIRONMENT key → falls back to Production" case.

**Chosen approach:** (a) for now; (b) is desirable but not blocking.

**Rationale:** The four existing tests cover version resolution and mock-auth wiring, which are the
business concerns of the handler. None of the four tests currently asserts on `response.Environment`,
meaning environment propagation is untested in the current suite regardless. A targeted test for the
fallback behaviour (missing key → "Production") would be a net improvement and takes three lines —
but the spec makes this optional (FR-3's comment, not a new test). The implementation is complete
without it. Add it if time permits; do not block merge on it.

## Implementation Guidance

### Directory / Module Structure

No structural change. All edits are in-place on the two files listed in the spec.

### Interfaces and Contracts

The MediatR contract (`GetConfigurationRequest` → `GetConfigurationResponse`) is unchanged.
`GetConfigurationHandler`'s public constructor signature changes from three parameters to two:

```csharp
// Before
public GetConfigurationHandler(IConfiguration configuration, IHostEnvironment environment, ILogger<GetConfigurationHandler> logger)

// After
public GetConfigurationHandler(IConfiguration configuration, ILogger<GetConfigurationHandler> logger)
```

MediatR resolves handlers from the DI container by type — the constructor parameter reduction is
transparent to MediatR and to `AddConfigurationModule()`. No call site outside the DI container
instantiates `GetConfigurationHandler` directly. Verify `AddConfigurationModule()` (or the
`AddApplicationServices` call that registers it via MediatR's assembly scan) does not manually
register `GetConfigurationHandler` with an explicit factory — if it does, update that factory. Based
on inspection, MediatR auto-registration via `RegisterServicesFromAssembly` is used
(`ApplicationModule.cs` line 64), so no factory needs updating.

### Data Flow

1. `IConfiguration["ASPNETCORE_ENVIRONMENT"]` is populated by the hosting layer before the
   application starts, via environment variable → configuration pipeline.
2. `BuildApplicationConfiguration()` reads the key and coalesces to `DEFAULT_ENVIRONMENT` if absent.
3. The value is passed into `ApplicationConfiguration.CreateWithDefaults(version, environment, useMockAuth)`.
4. `GetConfigurationResponse.Environment` carries it to the controller and the frontend.
5. In tests, `CreateHandler` supplies the value via `configData["ASPNETCORE_ENVIRONMENT"] = "Test"`.

No other data flow is affected.

### Inline comment placement

The comment must be adjacent to the assignment line, not above it in a separate block:

```csharp
// Falls back to ConfigurationConstants.DEFAULT_ENVIRONMENT ("Production") if not set
var environment = _configuration["ASPNETCORE_ENVIRONMENT"]
    ?? ConfigurationConstants.DEFAULT_ENVIRONMENT;
```

Keep this exact wording — the spec mandates it in FR-3 and it is self-documenting for the next
reader.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ASPNETCORE_ENVIRONMENT` not present in the configuration pipeline at handler execution time | Low | ASP.NET Core's `WebApplication.CreateBuilder` always writes this into the configuration pipeline from the environment variable. The `?? DEFAULT_ENVIRONMENT` fallback handles the only remaining gap (unit tests that do not set it). |
| `using Microsoft.Extensions.Hosting` left in place after removing `IHostEnvironment` usage | Low | Compiler will warn on unused `using` with `dotnet build`; `dotnet format` removes it. Verify explicitly after edit. |
| DI fails at startup because the two-parameter constructor conflicts with a manually-registered factory | Very low | `GetConfigurationHandler` is registered via MediatR's assembly scan, not a factory. Confirm by checking that `AddConfigurationModule()` contains no `services.AddScoped<GetConfigurationHandler>` call. |
| Test `CreateHandler` builds a handler where `ASPNETCORE_ENVIRONMENT` is absent (no key set) — causes `response.Environment` to be "Production" instead of "Test" | Medium | Any test that asserts `response.Environment == "Test"` will fail unless `configData["ASPNETCORE_ENVIRONMENT"] = "Test"` is added. Inspect all four tests; currently none asserts on `response.Environment`, so the risk is contained to future tests. Set `"ASPNETCORE_ENVIRONMENT"` in `CreateHandler`'s default `configData` to make it explicit. |

## Specification Amendments

**FR-2 / Test update scope:** The spec's `CreateHandler` fix (supply environment via
`configData["ASPNETCORE_ENVIRONMENT"]`) is correct. The amendment is: set this key in the shared
`CreateHandler` factory for all tests, not only in tests that happen to assert on environment. This
avoids a silent asymmetry where the default `CreateHandler` call produces `response.Environment ==
"Production"` while the real application produces `response.Environment == "Staging"` (or
`"Development"`). Concretely, add `["ASPNETCORE_ENVIRONMENT"] = "Test"` to the dictionary inside
`CreateHandler` so every test invocation gets a deterministic environment without callers having to
opt in. Callers that need a different value can still override it.

**No other amendments.** The two-file scope, the fallback constant choice, and the comment wording
are all correct as written.

## Prerequisites

None blocking. All facts verified against live source:

- `GetConfigurationHandler.cs` confirmed: uses `_environment.EnvironmentName` on line 63; has
  `using Microsoft.Extensions.Hosting` on line 3; `IHostEnvironment` field on line 16 and constructor
  param on line 19.
- `GetConfigurationHandlerTests.cs` confirmed: `Substitute.For<IHostEnvironment>()` on line 19;
  `environment.EnvironmentName.Returns("Test")` on line 20; no test asserts on `response.Environment`.
- `ConfigurationConstants.DEFAULT_ENVIRONMENT` confirmed: `= "Production"` in Domain layer
  (`backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs`).
- `ApplicationConfiguration.CreateWithDefaults` confirmed: accepts nullable `environment`; falls
  back to `"Production"` literal on the `new ApplicationConfiguration(...)` call — consistent with
  the constant.
- MediatR registration confirmed: `RegisterServicesFromAssembly` in `ApplicationModule.cs` line 64;
  no manual `GetConfigurationHandler` factory found.
- No other Application-layer handler injects `IHostEnvironment` — the grep result confirms this is
  the sole instance in the handler layer.

Validation gate: `dotnet build` (zero errors/warnings) + `dotnet test --filter
"GetConfigurationHandlerTests"` (all four tests green) + `dotnet format`.
