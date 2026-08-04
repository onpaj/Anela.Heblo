# Plan: MCP tools bypass the per-feature authorization gate their controllers enforce

## Summary

Five of seven MCP tool classes (`CatalogMcpTools` non-margin methods, `ManufactureOrderMcpTools`, `ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`, `UserManagementMcpTools`) wrap MediatR handlers whose MVC controllers are gated by `[FeatureAuthorize(Feature.X)]`, but apply no equivalent check themselves. Because `/mcp` is mapped with only `.RequireAuthorization()` and MCP tool dispatch bypasses MVC routing entirely, `[FeatureAuthorize]` never fires for these calls — any authenticated Entra user can invoke them regardless of role. The fix is to add the same per-feature Read-level check already used by `CatalogMcpTools.GetProductMargins` and `MeetingTasksMcpTools` to every remaining tool method, factored into one shared helper so the pattern can't silently regress per class.

## Context

`CatalogMcpTools.GetProductMargins` and all of `MeetingTasksMcpTools` already implement the correct pattern: resolve the required role via `AccessRoles.For(Feature.X, AccessLevel.Read)`, check it with `ICurrentUserService.IsInRole(role)`, and throw `McpException("[FORBIDDEN] ...")` if it fails. This is manual, per-method boilerplate — there is no shared enforcement point, so each new tool author must remember to add it. Five classes didn't. The starkest exposure is `UserManagementMcpTools.GetGroupMembers`, which lets any authenticated user enumerate Entra ID group membership — data gated behind `Admin_Administration` in the web UI.

## Functional requirements

**FR-1 — Add a per-feature access check to every currently-ungated MCP tool method.**
Each method below must throw `McpException` with a `[FORBIDDEN]` message (matching the existing wording style) before calling `_mediator.Send`, when the caller lacks the corresponding Read role.

| Class | Methods | Required feature (Read) |
|---|---|---|
| `CatalogMcpTools` | `GetCatalogList`, `GetCatalogDetail`, `GetProductComposition`, `GetMaterialsForPurchase`, `GetAutocomplete`, `GetProductUsage`, `GetWarehouseStatistics` | `Feature.Products_Catalog` |
| `ManufactureOrderMcpTools` | `GetManufactureOrders`, `GetManufactureOrder`, `GetCalendarView` | `Feature.Manufacture_ManufactureOrders` |
| `ManufactureBatchMcpTools` | `GetBatchTemplate`, `CalculateBatchBySize`, `CalculateBatchByIngredient`, `CalculateBatchPlan` | `Feature.Manufacture_BatchPlanning` |
| `KnowledgeBaseTools` | `SearchKnowledgeBase`, `AskKnowledgeBase` | `Feature.Customer_KnowledgeBase` |
| `LeafletTools` | `GenerateLeaflet` | `Feature.Marketing_Leaflet` |
| `UserManagementMcpTools` | `GetGroupMembers` | `Feature.Admin_Administration` |

`CatalogMcpTools.GetProductMargins` (already gated on `Feature.Products_ProductMargins`) and all of `MeetingTasksMcpTools` (already gated on `Feature.Anela_Meetings`) are unaffected — verified as controls, not touched except to migrate onto the new shared helper for consistency (FR-2).

Acceptance criteria:
- For every method above, a caller whose `ICurrentUserService.IsInRole(requiredRole)` returns `false` gets an `McpException` containing `FORBIDDEN` and the required role name, and `_mediator.Send` is never invoked (verify via mock `Times.Never`).
- A caller with the role gets the existing behavior unchanged (request shape, response JSON, error handling for downstream failures).
- The required role/feature for each tool matches the Read role its sibling controller enforces (table above) — no tool ends up stricter or laxer than its controller.

**FR-2 — Factor the check into one shared, reusable helper so the pattern can't be re-omitted.**
Replace the duplicated `EnsureReadAccess`/inline block pattern (currently hand-rolled in `MeetingTasksMcpTools` and `CatalogMcpTools.GetProductMargins`) with a single reusable piece — e.g. a static helper or extension method on `ICurrentUserService` such as `EnsureFeatureAccess(Feature feature, AccessLevel level = AccessLevel.Read)` that throws the standard `McpException`. All eight tool classes call the same helper; no class hand-rolls its own forbidden-message string.

Acceptance criteria:
- Exactly one implementation of the "resolve role → check → throw McpException" sequence exists in the codebase; all tool classes call it.
- Message format is identical across all tools (so tests and clients can pattern-match on it consistently) and preserves the existing `[FORBIDDEN] ... (requires {role})` shape already relied upon by `MeetingTasksMcpToolsTests` and `CatalogMcpToolsTests`.

**FR-3 — Update `docs/integrations/mcp-server.md` to document the permission each tool requires.**
Currently only `GetProductMargins` and Meeting Notes tools document their required permission. Add the same note to Catalog, Manufacture Orders, Manufacture Batch, Knowledge Base, Leaflet, and User Management sections, naming the required feature/role.

Acceptance criteria: every tool listed in the doc states its required permission, consistent with the table in FR-1.

**FR-4 — Add/extend tests proving the gate for every previously-ungated method.**
Follow the existing pattern in `MeetingTasksMcpToolsTests` (parameterized "throws forbidden and skips mediator" test) and `CatalogMcpToolsTests` (`GetProductMargins` forbidden test). Add equivalent tests to `CatalogMcpToolsTests` (for the 7 newly-gated methods), `ManufactureOrderMcpToolsTests`, `ManufactureBatchMcpToolsTests`, `KnowledgeBaseToolsTests`, `LeafletToolsTests`, `UserManagementMcpToolsTests`.

Acceptance criteria: for every method in the FR-1 table, a test exists asserting `McpException` with `FORBIDDEN` is thrown and `IMediator.Send` is never called when `IsInRole` returns `false`, and existing "happy path" tests continue to pass with `IsInRole` mocked to `true` (test constructors currently don't set this up for the five ungated classes — will need a default `true` stub added, mirroring `MeetingTasksMcpToolsTests`'s constructor pattern).

## Non-functional requirements

- **Security**: this is the core of the fix — no user-visible functional change for permitted users; the change must not weaken any existing check (Meeting Notes' per-meeting visibility guard and Catalog's product-margins gate stay exactly as strict as today).
- **No new dependencies**: reuse `ICurrentUserService`, `AccessRoles`, `Feature`, `AccessLevel`, `McpException` — all already in use.
- **Consistency**: role required by a tool must equal the Read role of its sibling controller (see table) — don't introduce a divergent permission model for MCP vs. web.

## Data model

No schema/entity changes. This only touches authorization checks against the existing `Feature` enum / `AccessRoles` role-string mapping already used by `FeatureAuthorizeAttribute` and `ICurrentUserService.IsInRole`.

## Interfaces

- No change to MCP tool method signatures, parameters, or response shapes — only a guard clause added at the top of each method body (or centralized via the FR-2 helper, invoked as the first line of each method).
- No change to the `/mcp` endpoint mapping (`ApplicationBuilderExtensions.cs:136`) — authentication stays `.RequireAuthorization()`; per-feature authorization stays tool-level, matching the pattern the two already-correct classes established (issue explicitly does not prescribe a transport-level mechanism).

## Dependencies and scope

**In scope:**
- The 6 tool classes / 18 methods listed in FR-1.
- The new shared helper (FR-2).
- Doc update (FR-3).
- Test additions (FR-4).

**Out of scope:**
- Changing `[FeatureAuthorize]` / MVC authorization itself.
- Investigating whether the ModelContextProtocol SDK (`ModelContextProtocol.AspNetCore` 1.0.0) offers a global tool-invocation filter/middleware hook that could enforce this at one choke point instead of per-method — worth a quick spike during implementation (see Open Questions), but the fallback (per-method checks via the shared helper) is proven, low-risk, and matches the existing two correct classes, so it's the default plan.
- `MeetingTasksMcpTools`'s per-meeting `IMeetingAccessGuard` visibility logic — unrelated, already correct, not touched beyond migrating its role-check onto the shared helper.
- Any change to what data each underlying MediatR handler returns.
- Write-level MCP tools — none currently exist; all seven tool classes are read-only, so this plan only ever checks `AccessLevel.Read`.

## Rough plan

1. Add the shared access-check helper (FR-2) — e.g. `AuthorizationExtensions.EnsureFeatureAccess` in `Anela.Heblo.API/MCP` or `Anela.Heblo.Domain/Features/Authorization`, matching the existing `McpException` message format.
2. Migrate `MeetingTasksMcpTools.EnsureReadAccess` and `CatalogMcpTools.GetProductMargins`'s inline check onto the new helper (proves it behaves identically before it's relied on elsewhere).
3. Add the guard call to each of the 18 methods in the FR-1 table, one class at a time: `CatalogMcpTools` (7 methods), `ManufactureOrderMcpTools` (3), `ManufactureBatchMcpTools` (4), `KnowledgeBaseTools` (2), `LeafletTools` (1), `UserManagementMcpTools` (1).
4. Add/extend unit tests per class (FR-4), including a default `IsInRole → true` stub in each test fixture's constructor so existing happy-path tests keep passing, plus new forbidden-path tests.
5. Update `docs/integrations/mcp-server.md` per FR-3.
6. Run `dotnet build` + `dotnet format` + full backend test suite; confirm all new and existing MCP tests pass.

## Open questions

- **Does `ModelContextProtocol.AspNetCore` 1.0.0 expose a tool-call filter/interceptor?** If yes, a single choke point might be preferable to 18 per-method call sites even with the shared helper. Default: proceed with the per-method + shared-helper approach (proven pattern, matches existing code) and treat a filter-based refactor as a possible follow-up, not a blocker for this fix.
- **Should the shared helper live in `Anela.Heblo.API.MCP` (API-layer, MCP-specific) or `Anela.Heblo.Domain.Features.Authorization` (alongside `AccessRoles`/`FeatureAuthorizeAttribute`)?** Default: API layer (`Anela.Heblo.API/MCP/`), since it depends on `McpException` from the `ModelContextProtocol` package and is MCP-specific; the domain layer shouldn't take a dependency on that SDK.
- **`CatalogMcpTools` constructor already takes `ICurrentUserService`; the other five ungated classes don't.** Adding it is a required, mechanical constructor change to each (`ManufactureOrderMcpTools`, `ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`, `UserManagementMcpTools`) and their test fixtures — noted here so it isn't a surprise during implementation, not treated as an open design question.
