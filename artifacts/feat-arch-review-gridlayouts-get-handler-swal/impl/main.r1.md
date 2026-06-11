# Implementation: GetGridLayout DB Error Surfacing

## What was implemented

Fixed a behavioural inconsistency where `GetGridLayoutHandler` silently swallowed `GridLayoutPersistenceException` and returned HTTP 200 with null body, indistinguishable from the "no saved layout" case. The handler now returns `BaseResponse(ErrorCodes.DatabaseError)` and the controller returns HTTP 500, so the React frontend preserves the user's visible column layout instead of silently resetting it during a DB outage.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutResponse.cs` — added parameterless constructor and `GetGridLayoutResponse(ErrorCodes errorCode) : base(errorCode)` constructor mirroring SaveGridLayoutResponse
- `backend/src/Anela.Heblo.Application/Features/GridLayouts/UseCases/GetGridLayout/GetGridLayoutHandler.cs` — changed `catch (GridLayoutPersistenceException)` return from `new GetGridLayoutResponse { Layout = null }` to `new GetGridLayoutResponse(ErrorCodes.DatabaseError)`; preserved `_logger.LogError` verbatim
- `backend/src/Anela.Heblo.API/Controllers/GridLayoutsController.cs` — added `if (!response.Success) return StatusCode(500, response);` before `return Ok(response.Layout)`, matching the existing Save/Reset pattern
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GetGridLayoutHandlerTests.cs` — renamed `Handle_WhenDatabaseThrows_ReturnsNullLayoutAndLogsError` to `Handle_WhenDatabaseThrows_ReturnsErrorResponseAndLogsError`; updated assertions to `Assert.False(response.Success)` + `Assert.Equal(ErrorCodes.DatabaseError, response.ErrorCode)`
- `backend/test/Anela.Heblo.Tests/Features/GridLayouts/GridLayoutsControllerTests.cs` — new file with 3 controller tests: 500 on DB error, 200+null on empty, 200+dto on success
- `frontend/src/features/grid-layout/useGridLayout.ts` — replaced unconditional `setColumnState(buildDefaultState(...))` in catch branch with functional updater `setColumnState((prev) => prev.length > 0 ? prev : buildDefaultState(columnsRef.current))`
- `frontend/src/features/grid-layout/__tests__/useGridLayout.test.ts` — appended `describe('useGridLayout — DB-error preservation')` with two tests: first-mount fallback and state-preservation on re-load failure

## Tests

**Backend — handler tests** (`GetGridLayoutHandlerTests.cs`): 7 tests, all pass. Renamed DB-error test now asserts `Success=false` and `ErrorCode=DatabaseError`.

**Backend — controller tests** (`GridLayoutsControllerTests.cs`): 3 new tests covering DB error → 500, no layout → 200+null, saved layout → 200+dto.

**Frontend — hook tests** (`useGridLayout.test.ts`): 10 tests total (8 pre-existing + 2 new). New tests verify: (1) first-mount failure falls back to defaults; (2) re-load failure preserves non-empty state.

**Total GridLayouts backend tests:** 37 pass.

## How to verify

Backend:
```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~GridLayouts"
# Expected: 37 passed
```

Frontend:
```bash
cd frontend
npm run build
npm test -- --watchAll=false --testPathPattern="useGridLayout.test.ts" --no-coverage
# Expected: 10 passed (use npm test / react-scripts test, NOT npx jest directly)
```

## Notes

- **HTTP 500 used, not 503**: The arch review amended the spec — `ErrorCodes.DatabaseError` has `[HttpStatusCode(InternalServerError)]` and Save/Reset already return 500. Using 503 would fragment the module's error convention.
- **Toast deferred**: Adding `useToast()` to the hook would require wrapping all existing `renderHook` calls with `ToastProvider`, violating the surgical-change constraint. Tracked as a follow-up.
- **`npm test` required for frontend tests**: `npx jest` bypasses react-scripts Babel/TypeScript config and fails on `as jest.Mock` syntax. Always run via `npm test`.
- **Commits on branch**: 7 commits added — constructor, RED handler test, GREEN handler fix, RED controller test, GREEN controller fix, RED frontend test, GREEN frontend fix.

## PR Summary

Fixed `GET /api/GridLayouts/{gridKey}` so a database error returns HTTP 500 with `BaseResponse(ErrorCodes.DatabaseError)` instead of HTTP 200 with null body. This aligns Get with the existing Save/Reset behaviour and lets the React frontend's existing non-2xx catch branch preserve the user's visible column layout during a transient DB outage rather than silently resetting it to defaults.

The frontend `useGridLayout` catch branch was also corrected: it previously called `buildDefaultState` unconditionally on any error, which wiped out the user's column customisations on every reload failure. It now uses a functional updater that preserves non-empty state and only falls back to defaults on first mount.

### Changes
- `GetGridLayoutResponse.cs` — added error-aware constructor mirroring SaveGridLayoutResponse
- `GetGridLayoutHandler.cs` — catch branch returns `new GetGridLayoutResponse(ErrorCodes.DatabaseError)` instead of success-with-null
- `GridLayoutsController.cs` — Get action checks `response.Success` and returns `StatusCode(500, response)` on failure
- `GetGridLayoutHandlerTests.cs` — updated DB-error test to assert `Success=false` + `ErrorCode=DatabaseError`
- `GridLayoutsControllerTests.cs` — new test class covering all three controller response paths
- `useGridLayout.ts` — catch branch uses conditional functional updater to preserve non-empty state
- `useGridLayout.test.ts` — two new tests locking in the DB-error preservation behaviour

## Status
DONE
