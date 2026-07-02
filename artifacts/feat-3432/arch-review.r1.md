# Architecture Review: Remove Timestamp from ApplicationConfiguration Domain Entity

## Skip Design: true

## Architectural Fit Assessment

This is a surgical refactor within a single vertical slice. The change touches three files, all within the `Configuration` module boundary, and requires no cross-module coordination.

The current design violates SRP: `ApplicationConfiguration` is a domain value object that encapsulates application-wide settings (version, environment, auth mode). Capturing `DateTime.UtcNow` in its constructor adds a transport-layer concern to the domain and introduces non-determinism, making the class impossible to assert on in unit tests without time tolerance. The existing test suite for `GetConfigurationHandler` has no assertion on `Timestamp` at all — a direct symptom of this untestability.

The fix is architecturally correct and consistent with how other domain entities in this codebase are structured (see `AnalyticsProduct`, `Article`, `BankAccountConfiguration` — none capture wall-clock time in their constructors). The domain should be a pure, deterministic representation of configuration state; response metadata belongs in the handler.

Integration points:
- `ApplicationConfiguration` (Domain layer) — property and constructor
- `GetConfigurationHandler` (Application layer) — response construction
- `GetConfigurationHandlerTests` (Test layer) — assertion tightening

No controller changes, no frontend changes, no persistence, no DI wiring changes.

## Proposed Architecture

### Component Overview

```
Domain Layer
  ApplicationConfiguration
    - Version: string
    - Environment: string
    - UseMockAuth: bool
    [- Timestamp: DateTime]  <-- REMOVED

Application Layer
  GetConfigurationHandler.Handle()
    builds ApplicationConfiguration via BuildApplicationConfiguration()
    constructs GetConfigurationResponse:
      .Version    = appConfig.Version
      .Environment = appConfig.Environment
      .UseMockAuth = appConfig.UseMockAuth
      .Timestamp  = DateTime.UtcNow     <-- MOVED HERE (was appConfig.Timestamp)

  GetConfigurationResponse (unchanged shape)
    - Version: string
    - Environment: string
    - UseMockAuth: bool
    - Timestamp: DateTime               <-- stays; only assignment source changes

Test Layer
  GetConfigurationHandlerTests
    new test: Handle_SetsTimestampToUtcNow  <-- add deterministic assertion
```

### Key Design Decisions

#### Decision 1: Assign `DateTime.UtcNow` directly in the handler, no clock abstraction
**Options considered:**
- (A) Move `DateTime.UtcNow` directly into `GetConfigurationHandler.Handle()`.
- (B) Inject `TimeProvider` (or a custom `ISystemClock`) and use it in the handler; mock it in tests.

**Chosen approach:** Option A — direct `DateTime.UtcNow` in the handler.

**Rationale:** The spec explicitly excludes `ISystemClock`/`TimeProvider` injection (Out of Scope). More importantly, `Timestamp` on the response is a response-generation timestamp, not a business value that requires controlled time in tests. Asserting it is "close to UtcNow" with a small tolerance (e.g. `BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5))`) is sufficient and appropriate for a wall-clock value. Introducing a clock abstraction for this single low-stakes field would add unjustified complexity. If a future use case requires deterministic time control elsewhere, `TimeProvider` can be adopted system-wide at that point.

#### Decision 2: Test assertion strategy
**Options considered:**
- (A) Assert `Timestamp` using a small `BeCloseTo` tolerance.
- (B) Assert that `Timestamp` is after a captured `before` time and before a captured `after` time.
- (C) Do not assert `Timestamp` at all (status quo).

**Chosen approach:** Option A with `FluentAssertions` `BeCloseTo`.

**Rationale:** The spec calls for tightening the assertion (FR-3). Option C is the broken status quo. Option B (bracket with before/after captures) is the most rigorous but verbose; `BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5))` is idiomatic in this codebase (FluentAssertions already in use), communicates intent clearly, and tolerates CI timing variability without being loose enough to miss a stale timestamp.

## Implementation Guidance

### Directory / Module Structure

No new files. Changes are confined to:

```
backend/src/Anela.Heblo.Domain/Features/Configuration/
  ApplicationConfiguration.cs          -- remove Timestamp property + assignment

backend/src/Anela.Heblo.Application/Features/Configuration/
  GetConfigurationHandler.cs            -- assign DateTime.UtcNow directly in Handle()

backend/test/Anela.Heblo.Tests/Features/Configuration/
  GetConfigurationHandlerTests.cs       -- add Timestamp assertion test
```

### Interfaces and Contracts

`GetConfigurationResponse.Timestamp` (in `GetConfigurationResponse.cs`) remains unchanged — no contract break, no OpenAPI schema change, no frontend impact.

`ApplicationConfiguration` constructor signature is unchanged: `(string version, string environment, bool useMockAuth)`. The only observable change is removal of the `Timestamp` property from the class.

### Data Flow

**Before:**
```
Handle()
  -> BuildApplicationConfiguration()
       -> new ApplicationConfiguration(version, env, useMockAuth)
            -> this.Timestamp = DateTime.UtcNow   [side effect in domain]
  -> response.Timestamp = appConfig.Timestamp
```

**After:**
```
Handle()
  -> BuildApplicationConfiguration()
       -> new ApplicationConfiguration(version, env, useMockAuth)
            [no side effect; pure value object]
  -> response.Timestamp = DateTime.UtcNow         [stamped at response construction]
```

The handler already owns the response construction block (lines 34-40 of `GetConfigurationHandler.cs`); adding `Timestamp = DateTime.UtcNow` there requires a one-line change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Timestamp reflects response-construction time, not domain-object-creation time — a semantic difference that could matter if `BuildApplicationConfiguration()` became expensive and was cached | Low | `BuildApplicationConfiguration()` reads from `IConfiguration` and `IHostEnvironment` synchronously and is not cached; the delta is microseconds. If caching is added in future, the handler must re-stamp on each `Handle()` call, which the new design naturally enforces. |
| Any code outside this handler reading `appConfig.Timestamp` would break at compile time | Low | A project-wide grep confirms `Timestamp` on `ApplicationConfiguration` is referenced only in `GetConfigurationHandler.cs` line 39. Compilation will catch any missed reference. |
| `BeCloseTo` tolerance is too tight on a slow CI agent | Low | Use `TimeSpan.FromSeconds(5)` as tolerance; this is standard for wall-clock assertions in this codebase. |

## Specification Amendments

None required. The spec is complete and unambiguous for this scope.

## Prerequisites

None. All three files exist, all dependencies are already wired, and the change requires no migrations, infrastructure configuration, or DI registration changes.
