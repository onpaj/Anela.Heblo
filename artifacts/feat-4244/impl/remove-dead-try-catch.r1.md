# Implementation: remove-dead-try-catch

## What was implemented

Removed the dead try-catch-log-rethrow wrapper from `GetConfigurationHandler.Handle()`.
The catch block only logged the exception via `_logger.LogError` and then rethrew it
unchanged, duplicating the global exception-handling middleware (which already logs
unhandled exceptions before translating them to an HTTP response) and risking
double-logging of any exception. `BuildApplicationConfiguration()` has no realistic
failure mode — it only reads `IConfiguration` values and assembly metadata — so the
catch block added no recovery value.

All statements that were inside the try block (both `LogDebug` calls, the call to
`BuildApplicationConfiguration()`, and the response construction/return) were kept
unchanged, just de-indented one level. Nothing else in the file — constructor,
fields, usings, namespace, `BuildApplicationConfiguration()`, `GetVersionFromSources()`
— was touched.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — removed the try/catch wrapper around the body of `Handle()`; exceptions now propagate unwrapped to the global exception-handling middleware.

## Tests

No test files were created or modified (per the task plan — this is a pure refactor
with no behavior change that the existing tests assert on). Used as regression guards:

- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Configuration/GetConfigurationEndpointTests.cs`

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Configuration.GetConfigurationHandlerTests|FullyQualifiedName~Features.Configuration.GetConfigurationEndpointTests"
```

Results: 10/10 passed both before (baseline) and after the edit — identical to
baseline, confirming no regression.

```bash
dotnet build   # run from repo root (Anela.Heblo.sln lives at repo root, not backend/)
dotnet format --verify-no-changes
```

Both succeeded: build produced 0 errors (pre-existing warnings only, none new/related
to this change), and `dotnet format --verify-no-changes` reported no diff.

```bash
dotnet test    # full solution, from repo root
```

Full suite: `Anela.Heblo.Tests.dll` had 111 failures out of 7442 (7327 passed), plus
pre-existing failures in `Anela.Heblo.Adapters.Flexi.Tests.dll` (72/347) and
`Anela.Heblo.Adapters.Shoptet.Tests.dll` (13/99). All of these are **pre-existing
environmental failures unrelated to this change**:
- The `Anela.Heblo.Tests.dll` failures are Testcontainers-based integration tests
  (`KnowledgeBaseRepositoryIntegrationTests`, `LeafletRepositoryIntegrationTests`,
  `TransportBoxRepositoryCodeOccupancySqlShapeTests`,
  `SmartsuppPresenceRepositoryIntegrationTests`, etc.) that fail with
  `System.ArgumentException: Docker is either not running or misconfigured` — Docker
  is not available in this sandbox.
- The Flexi/Shoptet failures are live-API integration tests failing on missing
  user-secret configuration (e.g. `Missing Shoptet:StatusId:EXP in configuration`).

None of the 111+72+13 failures are in `GetConfigurationHandlerTests` or
`GetConfigurationEndpointTests`, and none reference `GetConfigurationHandler` or
`Configuration` in a way connected to this change (the two `Configuration`-named
Shoptet failures, `ListAsync_WithValidConfiguration_*`, are unrelated Shoptet CSV
parsing tests, not this handler). No test outside the Configuration slice regressed
because of this change.

## Notes

- The task-context's literal `cd backend && dotnet build` / `dotnet test` commands do
  not work as written in this repo layout — `Anela.Heblo.sln` lives at the repo root,
  not inside `backend/`, so `dotnet build`/`dotnet test` with no project argument must
  be run from the repo root (this matches `docs/development/setup.md`). The
  project-scoped `dotnet test <path>.csproj` commands worked fine from `backend/` as
  written.
- No pre-existing `CS1998` "async method lacks await" warning was observed on this
  method before or after the change (method still awaits nothing internally but is
  unchanged in that respect — out of scope, not touched).
- No documentation changes needed: this is a pure internal refactor of one handler,
  no public API, CLI, or environment-variable behavior changed.

## PR Summary

Removed a dead try-catch-log-rethrow wrapper from `GetConfigurationHandler.Handle()`,
flagged by an architecture review. The catch block only logged and rethrew the
exception unchanged, duplicating the global exception-handling middleware and risking
double-logging. `BuildApplicationConfiguration()` has no realistic failure mode, so
the catch added no recovery value. All statements inside the try block are preserved
unchanged; only the wrapper was removed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationHandler.cs` — removed the try/catch around `Handle()`'s body

## Status
DONE
