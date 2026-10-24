# Architecture Review: DashboardController HandleResponse

## Skip Design: true
Backend-only controller change; no UI work.

## Architectural Fit Assessment
Aligns with the established convention: controllers derive from `BaseApiController` and return `HandleResponse(response)` (verified in `PackagingController`, `GiftSettingsController`). `HandleResponse<T>` returns `ActionResult<T>` and uses `Logger` (resolved from `HttpContext.RequestServices`), mapping `ErrorCodes` via `HttpStatusCodeAttribute`.

## Proposed Architecture

### Component Overview
Controller -> MediatR -> Handler -> `XxxResponse : BaseResponse` -> `HandleResponse` -> HTTP status.

### Key Design Decisions

#### Decision 1: Return type
**Options considered:** (a) keep `ActionResult` and unwrap `.Result`; (b) change to `ActionResult<EnableTileResponse>` / `ActionResult<DisableTileResponse>`.
**Chosen approach:** (b).
**Rationale:** Matches existing controllers; `HandleResponse` returns `ActionResult<T>`, which does not convert implicitly to `ActionResult`. The OpenAPI client changes from `FileResponse` to a typed response; this is an improvement and the frontend hooks do not consume the return value (verify in `useDashboard.ts` that nothing relies on `FileResponse`).

#### Decision 2: Scope
Only EnableTile/DisableTile change, per issue. `SaveUserSettings` is left as is.

## Implementation Guidance

### Directory / Module Structure
- `backend/src/Anela.Heblo.API/Controllers/DashboardController.cs` (edit)
- `backend/test/Anela.Heblo.Tests/Controllers/DashboardControllerTests.cs` (edit)
- Generated TS client regenerates on frontend build (`frontend/src/api/generated/api-client.ts`); commit if the repo tracks it.
- `docs/processes/module-dashboard.md` owns the controller path via `owns:`; touch it in the same PR (bump `verified_at`, note that tile enable/disable now return 400 on empty TileId) and run `python3 scripts/process-docs/check.py index`.

### Interfaces and Contracts
```csharp
public async Task<ActionResult<EnableTileResponse>> EnableTile(string tileId)
public async Task<ActionResult<DisableTileResponse>> DisableTile(string tileId)
```
Add `using` for the response types (namespaces already imported for the UseCases).

### Data Flow
Success: 200 + body `{success:true}`. Failure: 400 + body with `errorCode`, `params`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Controller unit tests have no HttpContext, so `Logger` throws on failure path | Medium | Set `ControllerContext.HttpContext` with a ServiceProvider providing `ILoggerFactory` in test setup |
| Generated client type change breaks FE build | Low | Run `npm run build`; fix hook typing if needed |
| Existing tests assert `OkResult` | Low | Update to `OkObjectResult` |
| Process-docs CI gate | Medium | Update module-dashboard.md in same PR |

## Specification Amendments
None.

## Prerequisites
None.
