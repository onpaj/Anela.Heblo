Final review: **READY_TO_MERGE**. All checks pass — consistency, scope, coverage, security, architecture alignment.

---

# Implementation: Consistent Invalid-Input Handling in `GetExpeditionListsByDateHandler`

## What was implemented
Fixed `GetExpeditionListsByDateHandler` so that an invalid `Date` input returns a canonical failure response (`Success=false`, `ErrorCode=InvalidFormat`, `Params={Field:Date, ExpectedFormat:yyyy-MM-dd}`) instead of a misleading `200 OK` with empty items. The controller's `GetByDate` action was switched from `Ok(response)` to `HandleResponse(response)` so the existing `[HttpStatusCode(BadRequest)]` on `ErrorCodes.InvalidFormat` automatically maps the failure to HTTP 400. Tests cover all invalid-date edge cases and confirm blob storage is never called on bad input.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — added `using Anela.Heblo.Application.Shared`; replaced silent-empty early return with canonical `Success=false, ErrorCode=InvalidFormat, Params` response
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` — changed `return Ok(response)` to `return HandleResponse(response)` in `GetByDate` (single line)
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` — added `Handle_ReturnsFailure_WhenDateIsInvalid` `[Theory]` with 5 `[InlineData]` cases; added `using Anela.Heblo.Application.Shared`

## Tests
- `GetExpeditionListsByDateHandlerTests.Handle_ReturnsFailure_WhenDateIsInvalid` — 5 cases: `"not-a-date"`, `"2026/03/25"`, `"25-03-2026"`, `""`, `null` — all assert `Success=false`, `ErrorCode=InvalidFormat`, correct `Params` keys, empty `Items`, and `ListBlobsAsync` never invoked
- `Handle_ReturnsItemsForDate` and `Handle_FiltersPdfFilesOnly` — pre-existing; both continue passing (22 total ExpeditionListArchive tests, 4378 total backend tests)

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ExpeditionListArchive" --nologo
dotnet build --nologo
```

## Notes
- Adopted the canonical `ErrorCode`+`Params` pattern (used by 35+ handlers codebase-wide) rather than mirroring the local `Fail(string)/ErrorMessage` outlier on the sibling `Download`/`Reprint` handlers. The arch-review amended the spec to direct this approach.
- 38 pre-existing Docker-dependent integration test failures exist in the full suite (PostgreSQL testcontainers requiring Docker); these are unrelated to this change.
- Frontend reconnaissance confirmed `useExpeditionListsByDate` throws on non-2xx and the global handler displays a structured-error toast — no frontend changes needed, no regression risk.
- The sibling handlers (`Download`, `Reprint`) still use the outlier `Fail(string)` pattern; their migration is explicitly out of scope per the spec amendments.

## PR Summary
Fixed a correctness bug where `GetExpeditionListsByDateHandler` silently returned `200 OK` with empty items on an invalid date string, making it indistinguishable from a valid day with no archived files. The fix adopts the canonical error pattern already used by 35+ handlers across the codebase: populate `Success=false`, `ErrorCode=InvalidFormat`, and `Params` on the response DTO, then route the controller action through `BaseApiController.HandleResponse<T>()` which maps the error code to HTTP 400 via its `[HttpStatusCode]` attribute. No new DTO properties or factory methods are needed.

### Changes
- `GetExpeditionListsByDateHandler.cs` — replaced invalid-date early return with canonical failure response; added `using Anela.Heblo.Application.Shared`
- `ExpeditionListArchiveController.cs` — switched `GetByDate` from `Ok(response)` to `HandleResponse(response)` (one line)
- `GetExpeditionListsByDateHandlerTests.cs` — added `[Theory]` covering 5 invalid-date inputs; verifies `ErrorCode=InvalidFormat`, correct `Params`, and that blob storage is never called on bad input

## Status
DONE