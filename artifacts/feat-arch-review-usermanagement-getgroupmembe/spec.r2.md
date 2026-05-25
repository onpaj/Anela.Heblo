# Specification: Move `GetGroupMembers` Endpoint Out of `ManufactureOrderController`

## Summary
The `GET /api/ManufactureOrder/responsible-persons` endpoint currently owns a UserManagement use case (`GetGroupMembers`) inside `ManufactureOrderController`, performs business logic (config lookup, validation, request construction) at the controller layer, and uses service-locator-based logger resolution. Relocate this endpoint to a new `UserManagementController`, eliminate cross-module imports from `ManufactureOrderController`, align logger resolution with constructor injection conventions, expose the `ManufactureGroupId` to the frontend via `ConfigurationController`, and update all callers (frontend hook, MCP tool, MCP docs) accordingly.

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

### FR-4: Expose `ManufactureGroupId` via `ConfigurationController`
Add a nullable string property `manufactureGroupId` to `GetConfigurationResponse` and populate it in `GetConfigurationHandler` by reading `IConfiguration["ManufactureGroupId"]` — the same key `ManufactureOrderController` reads today. This becomes the single server-side source of truth for the manufacture group ID exposed to the frontend.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.Application/Features/Configuration/GetConfigurationResponse.cs` has a new property: `public string? ManufactureGroupId { get; set; }`.
- `GetConfigurationHandler` reads the value from `IConfiguration["ManufactureGroupId"]` and assigns it to the response. No throw on missing — the field is nullable.
- Existing tests for `GetConfigurationHandler` are extended to cover the new field (configured value and null/missing-config cases).
- The OpenAPI-generated TypeScript client picks up the new field automatically on build (`npm run build`).
- No new endpoint is introduced; the value flows through the existing configuration query already consumed by the frontend.

### FR-5: Update frontend caller (`useResponsiblePersonsQuery`)
The hook at `frontend/src/api/hooks/useUserManagement.ts` calls the old URL with no parameters. Update it to call the new endpoint with the manufacture group ID supplied by the caller. The hook is **not** renamed — only its signature changes.

**Acceptance criteria:**
- Hook calls `${apiClient.baseUrl}/api/UserManagement/group-members?groupId={encoded}` (URL construction follows the project rule for absolute URLs — `${apiClient.baseUrl}${relativeUrl}`, see `docs/development/api-client-generation.md`).
- Hook signature becomes `useResponsiblePersonsQuery(groupId: string)`; query is `enabled` only when `groupId` is a non-empty string.
- `queryKey` includes `groupId` so changes invalidate the cache correctly.
- The hook file remains `frontend/src/api/hooks/useUserManagement.ts` and the exported name remains `useResponsiblePersonsQuery`.
- Existing `staleTime` (15 min) and `retry: 2` configuration are preserved.
- Frontend `npm run build` and `npm run lint` succeed.

### FR-6: Thread `groupId` through manufacture callers
`ResponsiblePersonCombobox` is a generic shared component (`frontend/src/components/common/`). It must receive `groupId` from each manufacture caller rather than reading configuration itself, so the component remains domain-agnostic.

**Acceptance criteria:**
- `ResponsiblePersonCombobox` accepts a new required `groupId: string` prop and forwards it to `useResponsiblePersonsQuery(groupId)`.
- The combobox is disabled (and the underlying query skipped) until `groupId` is a non-empty string.
- All callers — at minimum `CreateManufactureOrderModal.tsx`, `BasicInfoSection.tsx`, `ManufactureOrderFilters.tsx` — read `manufactureGroupId` from the configuration hook/context and pass it to the combobox.
- A central configuration consumer hook (e.g. `useConfiguration()` or the existing app-config context provider) is used; do not re-fetch `/api/Configuration` per call site.
- TypeScript compilation surfaces any missed call site as a hard error (the prop is required), preventing accidental omissions.
- Frontend `npm run build` and `npm run lint` succeed.

### FR-7: Move and rename the MCP tool
`ManufactureOrderMcpTools.GetResponsiblePersons` (`backend/src/Anela.Heblo.API/MCP/Tools/ManufactureOrderMcpTools.cs:73-89`) is a UserManagement operation living in a Manufacture MCP tool class. Move it to a new `UserManagementMcpTools` class and rename for clarity.

**Acceptance criteria:**
- New file `backend/src/Anela.Heblo.API/MCP/Tools/UserManagementMcpTools.cs` containing a `GetGroupMembers(string groupId, CancellationToken)` MCP tool that delegates to `GetGroupMembersRequest` via `IMediator`.
- The `using Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;` import is removed from `ManufactureOrderMcpTools.cs`.
- The new tool class is registered with the MCP server in the same place existing tool classes are registered (search for `WithTools<ManufactureOrderMcpTools>` or equivalent in `Program.cs`/`Startup` and mirror the pattern).

### FR-8: Update MCP documentation
`docs/integrations/mcp-server.md` enumerates every MCP tool by name and groups them by tool-class. The doc must be updated in the same PR so the inventory does not go stale.

**Acceptance criteria:**
- The "Manufacture Orders (4)" section header is updated to **"Manufacture Orders (3)"** and the `GetResponsiblePersons` bullet is removed.
- A new section **"User Management (1)"** is added containing exactly one bullet: `` `GetGroupMembers` — Entra ID group members by group ID ``.
- The intro phrase "MCP tools for AI assistants to query catalog data, manufacturing orders, and perform batch calculations" is extended to mention user-directory lookups, e.g. "...batch calculations, and user-directory lookups."
- No other sections (Implementation, Tool Pattern, Endpoints, Client Setup) are modified.

## Non-Functional Requirements

### NFR-1: Performance
- Endpoint response time and resource usage must be unchanged vs. the current implementation (this is a relocation, not a behavior change). No additional network hops or DB calls.
- React Query caching: preserve the existing `staleTime: 15 * 60 * 1000` (15 min) and `retry: 2` configuration.
- The configuration query is already fetched once at app start; exposing `manufactureGroupId` through it does not introduce a per-render network call.

### NFR-2: Security
- New endpoint requires the same authentication as the rest of the controllers: `[Authorize]` attribute at controller level, identical to `ManufactureOrderController` and surrounding controllers.
- `groupId` is a single string accepted via query string; it is forwarded to `IGraphService` which uses the Microsoft Graph SDK (parameterized SDK call — no string concatenation into a query/URL on the Graph side). No additional input sanitization required, but the controller must validate non-empty.
- Logging in the handler already logs `groupId` at Information level. Keep as-is; group IDs are not secrets.
- `ManufactureGroupId` exposed via `ConfigurationController` is a non-sensitive Entra group identifier; the configuration response is already returned to authenticated clients and follows the existing pattern for safe-to-expose config values.
- Error responses must not leak stack traces or internal details (the handler already returns a `BaseResponse` with `ErrorCode`; `HandleResponse` mapping preserves this).

### NFR-3: Backwards compatibility
- The old URL `/api/ManufactureOrder/responsible-persons` is **removed**, not aliased. All in-repo callers are updated in the same PR (frontend hook, MCP tool, MCP doc, tests). No external public consumers exist (solo developer project, see `CLAUDE.md`).

### NFR-4: Testing
- Existing controller tests for `GetResponsiblePersons` in `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs` must be deleted or migrated.
- New unit tests for `UserManagementController.GetGroupMembers` covering: success path, missing `groupId` returns 400, handler failure (`Success = false`) flows through `HandleResponse`.
- Existing handler tests in `backend/test/Anela.Heblo.Tests/Features/UserManagement/GetGroupMembersHandlerTests.cs` must continue to pass unchanged.
- `GetConfigurationHandler` tests extended to cover the new `manufactureGroupId` field (configured and missing).
- Coverage for the touched area stays at or above current levels and meets the project 80% bar (`~/.claude/rules/testing.md`).

## Data Model
No persisted data model changes. The transported types are unchanged:
- `GetGroupMembersRequest { string GroupId }`
- `GetGroupMembersResponse : BaseResponse { List<UserDto> Members }`
- `UserDto { string Id, string DisplayName, string Email }`

`GetConfigurationResponse` gains one nullable field:
- `string? ManufactureGroupId`

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

### Configuration endpoint payload extension
```
GET /api/Configuration
{
  ...existing fields...,
  "manufactureGroupId": "00000000-0000-0000-0000-000000000000" | null
}
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

### Frontend component prop change
```
// Before
<ResponsiblePersonCombobox value={...} onChange={...} />

// After
<ResponsiblePersonCombobox groupId={manufactureGroupId} value={...} onChange={...} />
// Disabled until groupId is non-empty.
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
- Existing `ConfigurationController`, `GetConfigurationHandler`, `GetConfigurationResponse` — extended, not replaced.
- OpenAPI TypeScript client regeneration on `npm run build` (existing pipeline).
- No new NuGet packages, no new external services.

## Out of Scope
- Changes to `GetGroupMembersHandler` business logic (caching, batching, etc.).
- Changes to `IGraphService` implementation or `MockGraphService`.
- Renaming `useResponsiblePersonsQuery`, `ResponsiblePersonCombobox`, or their files.
- Introducing a generic `IConfiguration`-driven mapping of "named" groups (e.g. `manufacture`, `orgChart`) to Entra IDs server-side.
- Removing or refactoring other cross-module issues elsewhere in `ManufactureOrderController` or other controllers.
- Database migrations (none required).
- Adjusting the React Query cache strategy beyond preserving the current `staleTime` and `retry` values.

## Open Questions
None.

## Status: COMPLETE