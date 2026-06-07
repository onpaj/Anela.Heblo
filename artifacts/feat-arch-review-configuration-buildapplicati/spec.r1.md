# Specification: Remove Speculative Async from `BuildApplicationConfigurationAsync`

## Summary
Refactor `GetConfigurationHandler.BuildApplicationConfigurationAsync()` to be synchronous because it performs no I/O. The method currently terminates with `await Task.CompletedTask` as a placeholder for speculative future async work, which violates YAGNI and imposes runtime cost (async state machine allocation) on every configuration request without providing any current benefit.

## Background
The arch-review routine (filed 2026-05-31) flagged that `BuildApplicationConfigurationAsync()` at `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs:53–73` is declared `async Task<ApplicationConfiguration>` but only:

1. Reads from `IConfiguration` (sync)
2. Reads `IHostEnvironment.EnvironmentName` (sync)
3. Reads `Environment.GetEnvironmentVariable` via the helper `GetVersionFromSources()` (sync)
4. Calls `ApplicationConfiguration.CreateWithDefaults(...)` (sync)
5. Awaits `Task.CompletedTask` as an explicit placeholder for "potential async operations"

This violates the YAGNI principle from the project's coding standards by designing for a speculative future requirement. Real costs incurred today:

- Unnecessary async state machine allocation on every `/configuration` request.
- Misleading code that signals I/O to readers auditing slow paths or allocation pressure.
- The `await Task.CompletedTask` line is itself dead code with no behavioral effect.

The outer `Handle` method on the MediatR `IRequestHandler` must remain `async Task<...>` per the framework contract, so the refactor is local to the private helper and its single call site.

## Functional Requirements

### FR-1: Rename and de-async the private helper
Convert the private helper from `BuildApplicationConfigurationAsync()` returning `Task<ApplicationConfiguration>` to `BuildApplicationConfiguration()` returning `ApplicationConfiguration`. Remove the `await Task.CompletedTask` placeholder line and its trailing comment.

**Acceptance criteria:**
- The method signature is `private ApplicationConfiguration BuildApplicationConfiguration()`.
- The method body contains no `async`, `await`, or `Task`-returning constructs.
- The placeholder line `await Task.CompletedTask;` and the associated `// Placeholder for potential async operations` comment are removed.
- The method continues to read version, environment name, and the `UseMockAuth` flag exactly as before and returns the result of `ApplicationConfiguration.CreateWithDefaults(...)`.

### FR-2: Update the single call site
Update the call in `Handle()` (currently `var appConfig = await BuildApplicationConfigurationAsync();` at line 32) to call the synchronous method directly.

**Acceptance criteria:**
- Line 32 becomes `var appConfig = BuildApplicationConfiguration();` (no `await`).
- No other call sites exist in the codebase (verified via search). If any are found, they must be updated accordingly.
- The enclosing `Handle()` method retains its `async Task<GetConfigurationResponse>` signature for MediatR compatibility.

### FR-3: Preserve external behavior
The public behavior of `GetConfigurationHandler.Handle` must be functionally identical before and after the change.

**Acceptance criteria:**
- The returned `GetConfigurationResponse` has identical `Version`, `Environment`, `UseMockAuth`, and `Timestamp` values for any given input/configuration state compared with the pre-change implementation.
- Existing logging calls (`LogDebug` for "Handling GetConfiguration request" and "Configuration retrieved successfully") remain unchanged in placement, level, and message template.
- Exception handling in `Handle()` (the try/catch that logs and rethrows) remains intact.

### FR-4: Existing tests continue to pass
All existing unit/integration tests covering `GetConfigurationHandler` must pass without modification of their assertions. Test code that referenced the old async helper name (if any) may be updated mechanically.

**Acceptance criteria:**
- `dotnet build` succeeds with no warnings introduced by this change.
- `dotnet test` for the configuration handler test project passes.
- If a test directly references `BuildApplicationConfigurationAsync` by name (e.g., via reflection), it is updated to the new name; no behavioral test assertions are weakened or removed.

## Non-Functional Requirements

### NFR-1: Performance
The change must remove (not add) overhead. After the refactor, calling `Handle` should no longer allocate an async state machine for the helper, eliminating a small but measurable per-request allocation.

- No new allocations introduced.
- No additional synchronous blocking calls introduced (the work was already synchronous; we simply stop pretending otherwise).

### NFR-2: Security
No security surface change. The method still reads only safe configuration sources (`IConfiguration`, `IHostEnvironment`, `APP_VERSION` env var). No new inputs, no new outputs, no logging of sensitive values.

### NFR-3: Maintainability
The refactor reduces cognitive load:
- The method's signature accurately reflects what it does.
- Readers auditing async/I/O hotspots will no longer be misled.
- Code aligns with the project's YAGNI principle from `coding-standards`.

### NFR-4: Code style
Must satisfy the project validation gates:
- `dotnet build` clean.
- `dotnet format` clean (no diff).
- Nullable reference type annotations preserved.

## Data Model
No changes. `ApplicationConfiguration` and `GetConfigurationResponse` are unaffected.

## API / Interface Design

### Internal (private) interface change
- **Before:** `private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()`
- **After:** `private ApplicationConfiguration BuildApplicationConfiguration()`

### Public interface
No change. `GetConfigurationHandler.Handle(GetConfigurationRequest, CancellationToken)` retains its MediatR-required `async Task<GetConfigurationResponse>` signature. No change to `GetConfigurationRequest`, `GetConfigurationResponse`, the route, or response shape.

### MediatR contract
`IRequestHandler<GetConfigurationRequest, GetConfigurationResponse>` is unchanged.

## Dependencies
None added or removed.
- `IConfiguration` — unchanged usage
- `IHostEnvironment` — unchanged usage
- `ILogger<GetConfigurationHandler>` — unchanged usage
- MediatR `IRequestHandler<,>` — unchanged contract
- `Anela.Heblo.Domain.Features.Configuration.ApplicationConfiguration` — unchanged

## Out of Scope
- Any change to `GetVersionFromSources()` (it is already synchronous and correct).
- Any change to `ApplicationConfiguration.CreateWithDefaults` or domain types.
- Any change to the `Handle` method's async signature, logging, or error handling.
- Any change to the `GetConfigurationResponse` DTO or its consumers (frontend, controllers).
- Any change to other handlers or unrelated configuration loading code.
- Performance benchmarks or load testing — the change is a pure refactor and project does not require microbenchmarks for it.
- Adding new tests beyond what is needed to keep coverage; this is a refactor, not new behavior.

## Open Questions
None.

## Status: COMPLETE