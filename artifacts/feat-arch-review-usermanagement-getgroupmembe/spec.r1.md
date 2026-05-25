I have sufficient context. Writing the spec now.

# Specification: Move `GetGroupMembers` Endpoint Out of `ManufactureOrderController`

## Summary
The `GET /api/ManufactureOrder/responsible-persons` endpoint currently owns a UserManagement use case (`GetGroupMembers`) inside `ManufactureOrderController`, performs business logic (config lookup, validation, request construction) at the controller layer, and uses service-locator-based logger resolution. Relocate this endpoint to a new `UserManagementController`, eliminate cross-module imports from `ManufactureOrderController`, and align logger resolution with constructor injection conventions. Update all callers (frontend hook, MCP tool) accordingly.

## Background
Project architecture rules (`docs/architecture/development_guidelines.md`, `docs/architecture/filesystem.md`) require:
- One controller per feature/module.
- Business logic (including config-driven input construction and validation) lives in MediatR handlers, not controllers.
- Modules must not couple at the controller layer.

Today `ManufactureOrderController` violates all three points for the responsible-persons endpoint:
1. It imports `GetGroupMembersRequest`/`GetGroupMembersResponse` from the UserManagement module (`backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs:12`).
2. It reads `ManufactureGroupId` from `IConfiguration`, validates its presence, then builds the `GetGroupMembersRequest` inside the action method (`ManufactureOrderController.cs:158-169`).
3. It resolves `ILogger<ManufactureOrderController>` via `HttpContext.RequestServices.GetRequiredService<...>()` (line 155) instead of constructor injection.

The endpoint URL `/api/ManufactureOrder/responsible-persons` also misleads consumers — the operation is a generic Microsoft Entra ID group-members lookup, not a manufacture-specific concern.

Filed by the daily arch-review routine on 2026-05-24.

## Functional Requirements

### FR-1: New `UserManagementController` with generic group-members endpoint
Create `backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs` exposing a single endpoint that returns Entra ID group members. The endpoint accepts the group identifier as a query parameter and delegates entirely to the existing `GetGroupMembersHandler`. The controller must depend only on `IMediator` (no `IConfiguration`, no UserManagement service types) and must be decorated with `[Authorize]` and `[ApiController]`, matching surrounding controllers.

**Acceptance criteria:**
- A file `UserManagementController.cs` exists in `backend/src/Anela.Heblo.API/Controllers/` deriving from `BaseApiController`.
- Endpoint signature: `[HttpGet("group-members")] Task<ActionResult<GetGroupMembersResponse>> GetGroupMembers([FromQuery, Required] string groupId, CancellationToken cancellationToken)`.
- Route resolves to `GET /api/UserManagement/group-members?groupId={id}`.
- Controller constructor takes `IMediator` only.
- Missing/empty `groupId` returns HTTP 400 with a clear error message (validation via `[Required]` attribute and/or `ModelState` check — preferred over manual `string.IsNullOrEmpty` block).
- Action body is at most: build `GetGroupMembersRequest`, call `_mediator.Send`, return `HandleResponse(response)`. No logging logic, no config reads, no business validation.

### FR-2: Remove the endpoint and its dependencies from `ManufactureOrderController`
The `GetResponsiblePersons` action (`ManufactureOrderController.cs:149-192`) must be deleted along with all related imports and constructor dependencies that are no longer used by remaining actions.

**Acceptance criteria:**
- `ManufactureOrderController.cs` no longer contains `GetResponsiblePersons`.
- `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;` is removed.
- If `IConfiguration` and `using Microsoft.Extensions.Configuration;` are no longer referenced by any remaining action in this controller, both are removed from the constructor and `using` list (verified by `dotnet build` and a grep for `_configuration` within the file).
- The unused service-locator `ILogger` resolution pattern is no longer present in this file.

### FR-3: Logger resolution via constructor injection
Logging in the new controller (or in the handler, which already does it correctly) must use constructor-injected `ILogger<T>`. The service-locator pattern `HttpContext.RequestServices.GetRequiredService<ILogger<...>>()` must not appear in the new code path.

**Acceptance criteria:**
- `GetGroupMembersHandler` continues to log request and outcome via its existing constructor-injected `ILogger<GetGroupMembersHandler>` — no additional controller-side logging is required (the handler already covers it).
- A grep for `HttpContext.RequestServices.GetRequiredService<ILogger` in `UserManagementController.cs` and the modified `ManufactureOrderController.cs` returns zero matches.

### FR-4: Update frontend caller (`useResponsiblePersonsQuery`)
The hook at `frontend/src/api/hooks/useUserManagement.ts` calls the old URL with no parameters. Update it to call the new endpoint with the manufacture group ID supplied by the caller.

**Acceptance criteria:**
- Hook calls `${apiClient.baseUrl}/api/UserManagement/group-members?groupId={encoded}` (URL construction follows the project rule for absolute URLs — `${apiClient.baseUrl}${relativeUrl}`, see `docs/development/api-client-generation.md`).
- Hook signature becomes `useResponsiblePersonsQuery(groupId: string)`; query is `enabled` only when `groupId` is a non-empty string.
- `queryKey` includes `groupId` so changes invalidate the cache correctly.
- All existing call sites of `useResponsiblePersonsQuery` pass the manufacture group ID (sourced as defined in FR-6).
- Frontend `npm run build` and `npm run lint` succeed.

### FR-5: Update MCP tool location and naming
`ManufactureOrderMcpTools.GetResponsiblePersons` (`backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs:73-89`) is a UserManagement operation living in a Manufacture MCP tool class. Move it to a new (or existing if available) UserManagement MCP tool class and rename for clarity.

**Acceptance criteria:**
- New file `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs` (if no UserManagement MCP tool class exists) containing a `GetGroupMembers(string groupId, CancellationToken)` MCP tool that delegates to `GetGroupMembersRequest` via `IMediator`.
- The `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;` import is removed from `ManufactureOrderMcpTools.cs`.
- The new tool class is registered with the MCP server in the same place existing tool classes are registered (search for `WithTools<ManufactureOrderMcpTools>` or equivalent in `Program.cs`/`Startup` and mirror the pattern).
- `docs/integrations/mcp-server.md` is updated if it enumerates available tools.

### FR-6: Manufacture group ID is supplied by the caller, not read in the new controller
The `ManufactureGroupId` configuration value must continue to drive which group the manufacture UI/MCP queries, but the lookup must not happen inside the new `UserManagementController`. The brief offers two options; this spec selects the second ("better") option: callers supply `groupId`.

**Acceptance criteria:**
- The new `UserManagementController.GetGroupMembers` action does not read `IConfiguration`.
- The `GetGroupMembersHandler` is **not** modified to read `IConfiguration` — it remains group-agnostic.
- The manufacture frontend obtains the `ManufactureGroupId` value by one of the resolution paths chosen in Open Question OQ-1.

## Non-Functional Requirements

### NFR-1: Performance
- Endpoint response time and resource usage must be unchanged vs. the current implementation (this is a relocation, not a behavior change). No additional network hops or DB calls.
- React Query caching: keep the existing `staleTime: 15 * 60 * 1000` (15 min) and `retry: 2` configuration to preserve current caching behavior.

### NFR-2: Security
- New endpoint requires the same authentication as the rest of the controllers: `[Authorize]` attribute at controller level, identical to `ManufactureOrderController` and surrounding controllers.
- `groupId` is a single string accepted via query string; it is forwarded to `IGraphService` which uses the Microsoft Graph SDK (parameterized SDK call — no string concatenation into a query/URL on the Graph side). No additional input sanitization required, but the controller must validate non-empty.
- Logging in the handler already logs `groupId` at Information level. Keep as-is; group IDs are not secrets.
- Error responses must not leak stack traces or internal details (the handler already returns a `BaseResponse` with `ErrorCode`; `HandleResponse` mapping preserves this).

### NFR-3: Backwards compatibility
- The old URL `/api/ManufactureOrder/responsible-persons` is **removed**, not aliased. All in-repo callers are updated in the same PR (frontend hook, MCP tool, tests). No external public consumers exist (solo developer project, see `CLAUDE.md`).

### NFR-4: Testing
- Existing controller tests for `GetResponsiblePersons` in `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs` must be deleted or migrated.
- New unit tests for `UserManagementController.GetGroupMembers` covering: success path, missing `groupId` returns 400, handler failure (`Success = false`) flows through `HandleResponse`.
- Existing handler tests in `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs` must continue to pass unchanged.
- Coverage for the touched area stays at or above current levels and meets the project 80% bar (`~/.claude/rules/testing.md`).

## Data Model
No persisted data model changes. The transported types are unchanged:
- `GetGroupMembersRequest { string GroupId }`
- `GetGroupMembersResponse : BaseResponse { List<UserDto> Members }`
- `UserDto { string Id, string DisplayName, string Email }`

## API / Interface Design

### New endpoint
```
GET /api/UserManagement/group-members?groupId={entraGroupId}
Authorization: Bearer <token>

200 OK
{
  "success": true,
  "members": [
    { "id": "...", "displayName": "...", "email": "..." }
  ],
  "errorCode": null,
  "params": null
}

400 Bad Request   — when groupId is missing/empty
401 Unauthorized  — when no/invalid bearer token
500 Internal      — propagated from handler via BaseResponse mapping
```

### Removed endpoint
```
GET /api/ManufactureOrder/responsible-persons         (REMOVED)
```

### Frontend hook signature change
```
// Before
useResponsiblePersonsQuery(): UseQueryResult<GetGroupMembersResponse>

// After
useResponsiblePersonsQuery(groupId: string): UseQueryResult<GetGroupMembersResponse>
// Query disabled until groupId is a non-empty string.
```

### MCP tool relocation
```
// Before
ManufactureOrderMcpTools.GetResponsiblePersons(string groupId, ...)

// After
UserManagementMcpTools.GetGroupMembers(string groupId, ...)
```

## Dependencies
- `MediatR` (existing).
- `Microsoft.AspNetCore.Authorization` (existing).
- `IGraphService` / Microsoft Graph SDK via existing `UserManagementModule` registration — no changes.
- `BaseApiController` and `HandleResponse` helper — existing.
- No new NuGet packages, no new external services.

## Out of Scope
- Changes to `GetGroupMembersHandler` business logic (caching, batching, etc.).
- Changes to `IGraphService` implementation or `MockGraphService`.
- Introducing a generic `IConfiguration`-driven mapping of "named" groups (e.g. `manufacture`, `orgChart`) to Entra IDs server-side.
- Removing or refactoring other cross-module issues elsewhere in `ManufactureOrderController` or other controllers.
- Database migrations (none required).
- Adjusting the React Query cache strategy beyond preserving the current `staleTime` and `retry` values.

## Open Questions

### OQ-1: How does the frontend obtain the `ManufactureGroupId`?
The current backend reads `IConfiguration["ManufactureGroupId"]` server-side. After the refactor the new generic endpoint will not. The manufacture frontend must therefore obtain this Entra group ID through another channel. Candidate approaches:

1. **Expose via existing `ConfigurationController`** — add a public configuration value `manufactureGroupId` to whatever DTO that controller returns (or a new endpoint), and have the manufacture screen read it before invoking `useResponsiblePersonsQuery(groupId)`. Keeps the value server-side, supports environment-specific values without rebuilds.
2. **Frontend environment variable** — expose as `REACT_APP_MANUFACTURE_GROUP_ID` baked into the bundle. Simpler, but couples the value to build-time.
3. **Thin manufacture-side handler that does the config lookup, then internally invokes `GetGroupMembersHandler` via `IMediator`** — keeps controller pure, but reintroduces the cross-module call at the application layer (UserManagement handler called from Manufacture handler). Per `development_guidelines.md` this likely needs explicit approval since it crosses module boundaries.

A decision is required before implementation begins. Recommendation: **Option 1** (`ConfigurationController`) — preserves the server-side single source of truth, fits the existing pattern, and avoids a hard module dependency at the handler layer.

### OQ-2: Should `useResponsiblePersonsQuery` be renamed?
The hook currently lives in `useUserManagement.ts` and is named in manufacture-domain terms. With a generic backend endpoint, a more accurate hook name could be `useGroupMembersQuery(groupId)`. Renaming touches all call sites. Decision: keep the existing `useResponsiblePersonsQuery` name and signature change only (add `groupId` param) to minimize churn, unless a rename is preferred. Confirm before implementation.

### OQ-3: Should the MCP tool class be added to MCP documentation?
`docs/integrations/mcp-server.md` lists the 15 currently exposed MCP tools. Moving and renaming `GetResponsiblePersons` → `GetGroupMembers` (in a new `UserManagementMcpTools` class) changes the tool inventory. Confirm whether the doc enumerates tool names that must be updated in the same PR.

## Status: HAS_QUESTIONS