# Implementation: gate-getenvironmentinfo-by-environment

## What was implemented
Added the same "Staging or Development only" environment guard that `CreateE2ESession`, `GetAuthStatus`, and `GetE2EApp` already carry to `E2ETestController.GetEnvironmentInfo`. Outside Staging/Development it now returns `404 NotFound` with `{ error, currentEnvironment }` instead of leaking `environment`, `isDevelopment`, `isProduction`, `isStaging`, and the raw `ASPNETCORE_ENVIRONMENT` value anonymously. Inside Staging/Development, behavior is byte-for-byte unchanged (still anonymously reachable, same response shape).

## Files created/modified
- `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs` — added the guard clause (copied verbatim from `CreateE2ESession`) at the top of `GetEnvironmentInfo`'s body. No other action, the constructor, the class doc-comment, or any `using` directive was touched.
- `backend/test/Anela.Heblo.Tests/Controllers/E2ETestControllerTests.cs` — new test file (no prior test coverage existed for this controller).

## Tests
`E2ETestControllerTests`:
- `GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldReturnNotFound` — `[Theory]` over `Production`/`Test`/`QA`, asserts `NotFoundObjectResult` with `{ error, currentEnvironment }`.
- `GetEnvironmentInfo_OutsideStagingOrDevelopment_ShouldNotLeakEnvironmentVariables` — asserts the 404 body does not contain `environmentVariables`.
- `GetEnvironmentInfo_InStagingOrDevelopment_ShouldReturnOkWithEnvironmentDetails` — `[Theory]` over `Staging`/`Development`, asserts unchanged `200 OK` behavior (no regression).

## How to verify
- `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~E2ETestControllerTests"` — 6/6 pass.
- `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — full suite: 6426 passed, 102 failed, 4 skipped. All 102 failures are pre-existing `DotNet.Testcontainers` / Postgres integration tests (`System.ArgumentException: Docker is either not running or misconfigured`) across `*IntegrationTests`/`*SqlShapeTests` classes unrelated to this controller — this sandbox has no Docker daemon. None of the failures are in `E2ETestControllerTests` or reference `E2ETestController`.
- `dotnet build` from the repo root (`Anela.Heblo.sln`) — 0 errors, 94 warnings, all pre-existing (none newly introduced by this change; the two CS8602 warnings on lines 164/189 of `E2ETestController.cs` are in the untouched `GetE2EAppHtml`/`GetE2EApp` code).
- `dotnet format` — ran clean against the intended diff. It additionally reformatted an unrelated pre-existing style inconsistency in `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs` (object-initializer line wrapping); that unrelated change was reverted (`git checkout --`) to keep this diff scoped to FR-2 (only `GetEnvironmentInfo` in `E2ETestController.cs` plus the new test file).

## Notes
No deviations from the task-context/spec. `[Authorize]`/`[AllowAnonymous]` attributes intentionally not touched, per spec.r1.md's explicit Out of Scope note and arch-review.r1.md Decision 1.

## PR Summary
Closed the anonymous-in-Production information-disclosure gap in `E2ETestController.GetEnvironmentInfo` (`GET /api/E2ETest/env-info`) — the one action in the file with no environment gate. Added the identical "Staging or Development only" guard already used by the three sibling actions (`CreateE2ESession`, `GetAuthStatus`, `GetE2EApp`), so the endpoint now returns `404 NotFound` outside those environments instead of leaking environment name/flags/`ASPNETCORE_ENVIRONMENT` anonymously. Behavior inside Staging/Development is unchanged. Added the first unit test coverage for this controller.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/E2ETestController.cs` — added environment guard clause to `GetEnvironmentInfo`
- `backend/test/Anela.Heblo.Tests/Controllers/E2ETestControllerTests.cs` — new test file covering the guard and the unchanged in-environment behavior

## Status
DONE
