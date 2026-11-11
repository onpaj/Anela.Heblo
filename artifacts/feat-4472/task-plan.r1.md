# Implementation Plan: DashboardController HandleResponse (#4472)

**Goal:** EnableTile/DisableTile return HTTP status derived from handler `BaseResponse`.
**Architecture:** Use `BaseApiController.HandleResponse<T>()`; return types `ActionResult<EnableTileResponse>`/`ActionResult<DisableTileResponse>`.
**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions; React/TS generated client.

### task: controller-tests
**Files:** Modify `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs`

- [ ] In the test constructor, set `_controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = <ServiceCollection with ILoggerFactory -> NullLoggerFactory.Instance>.BuildServiceProvider() } }` (needed because `HandleResponse` uses `Logger`).
- [ ] Change `EnableTile_ShouldReturnOk` / `DisableTile_ShouldReturnOk` to assert `result.Result.Should().BeOfType<OkObjectResult>()` and that value is the handler response.
- [ ] Add `EnableTile_WhenHandlerReturnsRequiredFieldMissing_ShouldReturnBadRequest` and the `DisableTile` equivalent: mock returns `new EnableTileResponse(ErrorCodes.RequiredFieldMissing)`; assert `BadRequestObjectResult` whose value is the response (verify `RequiredFieldMissing` maps to 400 in `ErrorCodes`).
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter DashboardControllerTests`; the failure tests must FAIL before task `controller-fix`, then pass.

### task: controller-fix
**Files:** Modify `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs`

- [ ] Change `EnableTile` to `public async Task<ActionResult<EnableTileResponse>>`, capture `var response = await _mediator.Send(request);` and `return HandleResponse(response);`.
- [ ] Same for `DisableTile` with `DisableTileResponse`.
- [ ] Leave all other actions untouched.
- [ ] `dotnet build` and `dotnet format` on the solution; run `dotnet test --filter "DashboardController"` (including `DashboardControllerAuthorizationTests`) and all tests under `Features/Dashboard`.

### task: frontend-client
**Files:** `frontend/src/api/generated/api-client.ts` (regenerated), `frontend/src/api/hooks/useDashboard.ts` (only if needed)

- [ ] Build backend (regenerates the OpenAPI TS client per `docs/development/api-client-generation.md`); confirm `dashboard_EnableTile`/`dashboard_DisableTile` now return typed responses instead of `FileResponse`, and commit the regenerated file if the repo tracks it.
- [ ] Check `useEnableTile`/`useDisableTile` and `DashboardSettings.tsx` do not depend on `FileResponse`; adjust only if the build fails.
- [ ] Run `cd frontend && npm run build && npm run lint` and the Dashboard-related jest tests.

### task: process-doc
**Files:** Modify `docs/processes/module-dashboard.md`

- [ ] Add a note (Known quirks / behavior section) that enable/disable tile endpoints return 400 with a `BaseResponse` error body when `TileId` is empty and 200 with the response otherwise; bump `verified_at`.
- [ ] Run `python3 scripts/process-docs/check.py index` and fix any reported issue (the doc owns `DashboardController.cs`, so CI requires this).
