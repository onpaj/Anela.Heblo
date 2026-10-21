# Architecture Review: Configuration version fallback

## Skip Design: true
Backend-only, no UI.

## Architectural Fit Assessment
Single handler in `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` (vertical slice). Registered via MediatR assembly scanning; the only direct reference outside tests is a comment in `Program.cs`. Existing tests in `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`.

## Proposed Architecture

### Component Overview
Handler -> `GetVersionFromSources()` -> config `APP_VERSION` -> assembly (entry, else executing) informational version -> assembly version -> default.

### Key Design Decisions

#### Decision 1: Minimal fix vs IOptions injection
**Options considered:** (a) `GetEntryAssembly() ?? GetExecutingAssembly()`; (b) inject `IOptions<ApplicationVersionOptions>` from the API composition root.
**Chosen approach:** (a).
**Rationale:** Matches CLAUDE.md surgical-changes rule; (b) is a broader refactor overlapping #4374 and is left as follow-up.

#### Decision 2: Testing seam
**Options considered:** (a) none, assert non-empty; (b) `internal` second constructor taking a `Func<Assembly?>` entry-assembly provider.
**Chosen approach:** (b), with the public `(IConfiguration, ILogger)` constructor delegating to it using `Assembly.GetEntryAssembly`. Ensure DI resolves unambiguously (single public constructor; internal one is ignored by MS DI) and expose internals to the test project only if not already (check for existing `InternalsVisibleTo`; otherwise make the seam `public` with a clear name).
**Rationale:** Makes the entry-vs-executing choice verifiable; current tests pass against either assembly.

## Implementation Guidance

### Directory / Module Structure
No new files. Edit the handler and its test class.

### Interfaces and Contracts
Handler behaviour and `GetConfigurationResponse` unchanged. Resolve the assembly once per call, reuse for both attribute lookups.

### Data Flow
Unchanged except assembly source.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Under test host / `dotnet-ef`, entry assembly is the test runner or tool, changing fallback version | Low | Fallback only when `APP_VERSION` unset; tests use the seam; existing non-empty assertions still hold |
| Second constructor breaks DI | Low | Keep one public constructor; run DI/integration tests |

## Specification Amendments
Fine as written; FR-2 seam details (internal vs public) decided by presence of `InternalsVisibleTo`.

## Prerequisites
None.
