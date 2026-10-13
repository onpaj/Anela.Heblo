# Implementation: add-detail-handler-error-code-tests

## What was implemented
Added the unit test file for `GetGiftPackageDetailHandler`, covering the three
scenarios called out in the task: the success path, the `ArgumentException` ->
`ErrorCodes.ValidationError` mapping, and the generic `Exception` ->
`ErrorCodes.InternalServerError` mapping. No production code was touched, per
NFR-2 in `spec.r1.md` — this is a coverage-gap ticket, not a bug fix.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs` — new test class with 3 `[Fact]` tests, mirroring the sibling `DisassembleGiftPackageHandlerTests` style. Written exactly as specified in the task context (verified against the current `GetGiftPackageDetailHandler.cs`, `GetGiftPackageDetailRequest.cs`, `GetGiftPackageDetailResponse.cs`, `IGiftPackageQueryService.cs` and `GiftPackageDto.cs` before writing, to confirm signatures match).

## Tests
- `Handle_ReturnsSuccessWithGiftPackage_WhenServiceSucceeds` — pins the mock setup to the exact request values and verifies the handler forwards them unchanged, asserting `Success=true`, `ErrorCode=null`, and the returned DTO.
- `Handle_ReturnsValidationError_WhenServiceThrowsArgumentException` — asserts `ErrorCode == ErrorCodes.ValidationError` (and `!= InternalServerError`).
- `Handle_ReturnsInternalServerError_WhenServiceThrowsUnexpectedException` — asserts `ErrorCode == ErrorCodes.InternalServerError` (and `!= ValidationError`).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetGiftPackageDetailHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`.

Also ran, from the repo root (the solution file lives there, not under `backend/`):
- `dotnet build Anela.Heblo.sln` — 0 errors, 91 pre-existing warnings unrelated to this change.
- `dotnet format Anela.Heblo.sln --verify-no-changes` — exit code 0, no formatting changes needed.
- Full test suite (`dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`): 7127 passed, 110 failed, 4 skipped, total 7241. All 110 failures are pre-existing Testcontainers/PostgreSQL-backed integration tests failing because no Docker daemon is available in this sandbox (`failed to connect to the docker API at unix:///var/run/docker.sock ... no such file or directory`) — confirmed unrelated to this change: none of the 110 failing tests are in the `GiftPackageManufacture` area, and the 3 new tests (pure unit tests with mocks, no Testcontainers) pass. This is a pre-existing environment limitation, not a regression.

## Notes
No deviations from the task context — the test file was created verbatim as specified. No production code changes were made.

## PR Summary
Adds unit test coverage for `GetGiftPackageDetailHandler`'s exception-to-error-code mapping, closing the coverage gap identified in the tech-debt ticket. Covers the success path plus the `ArgumentException` -> `ValidationError` and generic `Exception` -> `InternalServerError` branches, each asserting the specific error code and that it isn't the other one — so a future swap of the catch-block bodies would be caught by these tests.

### Changes
- `backend/test/Anela.Heblo.Tests/Application/GiftPackageManufacture/GetGiftPackageDetailHandlerTests.cs` — new test file, 3 tests

## Status
DONE
