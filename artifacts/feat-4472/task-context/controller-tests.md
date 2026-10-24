### task: controller-tests
**Files:** Modify `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs`

- [ ] In the test constructor, set `_controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = <ServiceCollection with ILoggerFactory -> NullLoggerFactory.Instance>.BuildServiceProvider() } }` (needed because `HandleResponse` uses `Logger`).
- [ ] Change `EnableTile_ShouldReturnOk` / `DisableTile_ShouldReturnOk` to assert `result.Result.Should().BeOfType<OkObjectResult>()` and that value is the handler response.
- [ ] Add `EnableTile_WhenHandlerReturnsRequiredFieldMissing_ShouldReturnBadRequest` and the `DisableTile` equivalent: mock returns `new EnableTileResponse(ErrorCodes.RequiredFieldMissing)`; assert `BadRequestObjectResult` whose value is the response (verify `RequiredFieldMissing` maps to 400 in `ErrorCodes`).
- [ ] Run `dotnet test backend/test/Anela.Heblo.Tests --filter DashboardControllerTests`; the failure tests must FAIL before task `controller-fix`, then pass.

