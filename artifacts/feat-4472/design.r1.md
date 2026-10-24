# Design: DashboardController HandleResponse

## Component Design
`DashboardController` (derives `BaseApiController`):
- `EnableTile(string tileId)`: build `EnableTileRequest { TileId = tileId }`, `var response = await _mediator.Send(request); return HandleResponse(response);`
- `DisableTile(string tileId)`: same with `DisableTileRequest`.
Return types `ActionResult<EnableTileResponse>` and `ActionResult<DisableTileResponse>`. No handler changes.

Tests (`DashboardControllerTests`): give the controller a `ControllerContext` whose `HttpContext.RequestServices` resolves `ILoggerFactory` (e.g. `NullLoggerFactory.Instance`). Cases: success -> `OkObjectResult` with response; `RequiredFieldMissing` -> `BadRequestObjectResult` wrapping the response; for both actions.

## Data Schemas
Response (`BaseResponse`): `success: bool`, `errorCode?: ErrorCodes`, `params?: Dictionary<string,string>`. Request unchanged (route param `tileId`).
