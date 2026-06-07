All 6 implementation commits are on the branch. Here is the output summary:

---

# Implementation: Align ExpeditionListArchive responses with BaseResponse error contract

## What was implemented

Removed the ad-hoc `string? ErrorMessage` parallel error channel from `DownloadExpeditionListResponse` and `ReprintExpeditionListResponse`. Replaced it with the typed `BaseResponse.ErrorCode` pattern (`ErrorCodes.InvalidBlobPath = 1808`) used everywhere else in the codebase. Propagated the change atomically through handlers, controller, frontend hook, and all affected tests.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — Added `InvalidBlobPath = 1808` in the FileStorage range with `[HttpStatusCode(BadRequest)]`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs` — Removed `ErrorMessage`, parameterless `Fail()` sets `ErrorCode = InvalidBlobPath`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` — Calls `Fail()` without arguments
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs` — Same as Download response
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — Calls `Fail()` without arguments
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` — Download: `BadRequest(response)`, Reprint: `HandleResponse(response)`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs` — Asserts `result.ErrorCode == ErrorCodes.InvalidBlobPath`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — Asserts `result.ErrorCode == ErrorCodes.InvalidBlobPath`
- `frontend/src/api/hooks/useExpeditionListArchive.ts` — Typed `ReprintExpeditionListResponse` interface, `REPRINT_ERROR_MESSAGES` Czech lookup, updated error handlers
- `frontend/src/pages/__tests__/ExpeditionListArchivePage.test.tsx` — Updated mock to `{ success: true, errorCode: null, params: null }`

## Tests

- `DownloadExpeditionListHandlerTests.cs` — 6 cases: 1 success + 5-theory invalid paths, all assert `ErrorCode == InvalidBlobPath`
- `ReprintExpeditionListHandlerTests.cs` — 4 cases: success, invalid-path (with ErrorCode assertion), temp-file cleanup, blob-failure cleanup
- `ExpeditionListArchivePage.test.tsx` — 4 UI tests, all passing with updated mock shape

**Verification:** `dotnet test --filter "FullyQualifiedName~ExpeditionListArchive"` → 17/17 pass. `npm test -- --watchAll=false --testPathPattern=ExpeditionListArchive` → all pass. Full `dotnet build` and `npm run build` clean.

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionListArchive"
cd frontend && npm run build && npm run lint
cd frontend && npm test -- --watchAll=false --testPathPattern=ExpeditionListArchive
```

## Notes

- The controller `Reprint` action now uses the inherited `BaseApiController.HandleResponse(response)`, matching all other modules. `Download` keeps manual `BadRequest(response)` because its success path returns a binary file stream.
- `useRunExpeditionListPrintFix` keeps a defensive `errorMessage` fallback because its backing endpoint (`/api/expedition-list/run-fix`) is on an out-of-scope controller — cleanup is a follow-up.
- During Task 4, a subagent preemptively touched the controller (setting `BadRequest(response.ErrorCode)` — wrong). Task 5 corrected this to `BadRequest(response)` and added `HandleResponse(response)` on Reprint.

## PR Summary

Removes the parallel `ErrorMessage` error channel from the two `ExpeditionListArchive` response classes and replaces it with the typed `BaseResponse.ErrorCode` contract (`InvalidBlobPath = 1808`) used by every other module in the codebase. The frontend hook is updated in lockstep, mapping the new error code to a Czech user-facing string instead of relaying the server-supplied English string.

### Changes
- `backend/.../Shared/ErrorCodes.cs` — new `InvalidBlobPath = 1808` enum value, FileStorage range
- `backend/.../DownloadExpeditionListResponse.cs` — removed `ErrorMessage`, parameterless `Fail()` with typed error code
- `backend/.../DownloadExpeditionListHandler.cs` — call site updated to `Fail()`
- `backend/.../ReprintExpeditionListResponse.cs` — same as Download
- `backend/.../ReprintExpeditionListHandler.cs` — call site updated to `Fail()`
- `backend/.../ExpeditionListArchiveController.cs` — Download: `BadRequest(response)`; Reprint: `HandleResponse(response)`
- `backend/test/.../DownloadExpeditionListHandlerTests.cs` — added `ErrorCode == InvalidBlobPath` assertion
- `backend/test/.../ReprintExpeditionListHandlerTests.cs` — added `ErrorCode == InvalidBlobPath` assertion
- `frontend/.../useExpeditionListArchive.ts` — typed `ReprintExpeditionListResponse`, Czech error lookup, updated error handlers
- `frontend/.../ExpeditionListArchivePage.test.tsx` — mock updated to new `BaseResponse` shape

## Status
DONE