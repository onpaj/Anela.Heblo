# Specification: DashboardController honours handler BaseResponse for EnableTile/DisableTile

## Summary
`DashboardController.EnableTile` and `DisableTile` discard the MediatR handler response and always return `200 OK`. They must translate the handler's `BaseResponse` (success / `ErrorCodes`) into the proper HTTP status via `BaseApiController.HandleResponse<T>()`, as other controllers already do.

## Background
`EnableTileHandler` / `DisableTileHandler` return `ErrorCodes.RequiredFieldMissing` for an empty `TileId`. The controller ignores this, so clients always see 200. Future error paths (TileNotFound, Unauthorized) would be swallowed too.

## Functional Requirements

### FR-1: EnableTile maps handler response to HTTP status
`POST api/Dashboard/tiles/{tileId}/enable` sends `EnableTileRequest` and returns `HandleResponse(response)`.
**Acceptance criteria:**
- Successful response -> 200 OK with the response body.
- Response with `ErrorCodes.RequiredFieldMissing` -> status from the code's `HttpStatusCodeAttribute` (400 BadRequest) with the response as body; a warning is logged.

### FR-2: DisableTile maps handler response to HTTP status
Same as FR-1 for `POST api/Dashboard/tiles/{tileId}/disable` / `DisableTileRequest`.

### FR-3: Tests
`DashboardControllerTests` cover success and failure for both actions.

### FR-4: Frontend contract stays working
Regenerated OpenAPI TS client (`dashboard_EnableTile` / `dashboard_DisableTile`) must still build; `useEnableTile`/`useDisableTile` hooks and `DashboardSettings.tsx` must keep working.

## Non-Functional Requirements
### NFR-1: Performance
No change.
### NFR-2: Security
No change to authorization; error bodies contain only the standard `BaseResponse` (error code and params).

## Data Model
None.

## API / Interface Design
Both actions return `ActionResult<EnableTileResponse>` / `ActionResult<DisableTileResponse>` (same pattern as `PackagingController`), so success bodies now carry the `BaseResponse` JSON and the OpenAPI schema is typed. Routes and request shapes unchanged.

## Dependencies
`BaseApiController.HandleResponse<T>`, `ErrorCodes` with `HttpStatusCodeAttribute`.

## Out of Scope
- Handler changes.
- Other Dashboard actions (`SaveUserSettings` also discards its response; noted, not changed, per surgical-changes rule).
- New error codes.

## Open Questions
None.

## Status: COMPLETE
