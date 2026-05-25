## Module
UserManagement

## Finding
The `GetGroupMembers` MediatR use case belongs to the UserManagement module, but its HTTP endpoint is exposed on `ManufactureOrderController` at `GET /api/ManufactureOrder/responsible-persons` (`backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`, lines 152–192).

This creates two concrete violations:

1. **Cross-module controller dependency** — `ManufactureOrderController` imports `GetGroupMembersRequest` and `GetGroupMembersResponse` from the UserManagement module's contracts, coupling the Manufacture controller to UserManagement internals.

2. **Business logic in controller** — The controller reads `ManufactureGroupId` from `IConfiguration`, validates it, and constructs the `GetGroupMembersRequest`. Per project rules, business logic (including config-driven input construction) belongs in MediatR handlers, not controllers. Additionally, the method resolves `ILogger` via the service-locator `HttpContext.RequestServices.GetRequiredService<ILogger<...>>()` (line 155) rather than constructor injection.

## Why it matters
The guideline in `development_guidelines.md` states: "Business logic must be in MediatR handlers, NOT in controllers" and `filesystem.md` defines one controller per feature. Having Manufacture's HTTP surface own a UserManagement operation means: (a) the modules are coupled at the controller layer, (b) if the handler is ever reused for a different group (e.g. OrgChart), the config lookup and validation must be duplicated, and (c) the endpoint URL (`/api/ManufactureOrder/responsible-persons`) gives no indication this is a user-directory operation.

## Suggested fix
1. Create `backend/src/Anela.Heblo.API/Controllers/UserManagementController.cs` with a `GET /api/UserManagement/group-members` endpoint that accepts `groupId` as a query parameter.
2. Move the `ManufactureGroupId` config lookup into `GetGroupMembersHandler`, or (better) have the controller pass the configured group ID and let callers supply it.
3. Remove the `GetGroupMembersRequest`/`Response` imports from `ManufactureOrderController`.

---
_Filed by daily arch-review routine on 2026-05-24._