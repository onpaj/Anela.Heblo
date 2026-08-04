# Plan (v2): MCP tools bypass the per-feature authorization gate their controllers enforce

This supersedes `plan-01.md`. It folds in the concrete design (`design-01.md`) and the
architecture verification pass (`architecture-01.md`), which confirmed every factual claim
against current source and resolved all three of plan-01's open questions. No structural
change from plan-01 — this version removes ambiguity so development can proceed without
further design decisions.

## Summary

Six MCP tool classes (`CatalogMcpTools`'s seven non-margin methods, `ManufactureOrderMcpTools`,
`ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`, `UserManagementMcpTools`) wrap
MediatR handlers whose MVC controllers are gated by `[FeatureAuthorize(Feature.X)]`, but the MCP
surface applies no equivalent check — `/mcp` is mapped with only `.RequireAuthorization()`, and
MCP tool dispatch never goes through MVC routing, so `[FeatureAuthorize]` never fires for it. Fix:
add a single shared helper, `ICurrentUserService.EnsureFeatureAccess(Feature, string, AccessLevel)`,
in `Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs`, and call it as the first statement of all
18 currently-ungated methods plus the 5 already-gated ones (2 hand-rolled checks migrated onto it
for consistency), so exactly one implementation of "resolve role → check → throw" exists.

## Context

`CatalogMcpTools.GetProductMargins` and `MeetingTasksMcpTools` (4 methods) already implement the
correct pattern by hand: `AccessRoles.For(Feature.X, AccessLevel.Read)` →
`ICurrentUserService.IsInRole(role)` → throw `McpException("[FORBIDDEN] ...")`. It's boilerplate
with no shared enforcement point, so five classes never got it. Worst case:
`UserManagementMcpTools.GetGroupMembers` lets any authenticated user enumerate Entra ID group
membership — gated behind `Admin_Administration` in the web UI.

The architecture pass re-read every referenced file (all seven tool classes, `McpModule.cs`,
`AccessRoles.generated.cs`, `FeatureAuthorizeAttribute.cs`, `CurrentUserService.cs`, both existing
test files, and every sibling controller) and found no divergence between the design and current
source. It also caught one gap plan-01 didn't know about: `LeafletTools` has no existing section
in `docs/integrations/mcp-server.md` at all (confirmed via `grep -n "Leaflet"` returning nothing),
so FR-3 must add a new doc group, not edit a nonexistent one.

## Functional requirements

**FR-1 — Add `McpAuthorizationExtensions.EnsureFeatureAccess`, a single shared gate.**

New file `backend/src/Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs`:

```csharp
namespace Anela.Heblo.API.MCP;

public static class McpAuthorizationExtensions
{
    public static void EnsureFeatureAccess(
        this ICurrentUserService currentUserService,
        Feature feature,
        string resourceName,
        AccessLevel level = AccessLevel.Read)
    {
        var requiredRole = AccessRoles.For(feature, level);
        if (!currentUserService.IsInRole(requiredRole))
        {
            throw new McpException(
                $"[FORBIDDEN] You do not have permission to access {resourceName} (requires {requiredRole}).");
        }
    }
}
```

Message format is byte-identical to the two existing hand-rolled checks, so
`Assert.Contains("FORBIDDEN", ...)` / `Assert.Contains(role, ...)` assertions in existing tests
keep passing unmodified.

Acceptance criteria:
- File exists at the path above, in namespace `Anela.Heblo.API.MCP`.
- Signature matches exactly (extension method on `ICurrentUserService`, `AccessLevel` defaults to `Read`).
- No new package dependency — `Feature`, `AccessLevel`, `AccessRoles`, `ICurrentUserService` (Domain) and `McpException` (`ModelContextProtocol` SDK, already referenced by API project) are all already in use.

**FR-2 — Call the guard as the first statement of every listed method, before any `try`.**

| Class | Methods | Guard call |
|---|---|---|
| `CatalogMcpTools` | `GetCatalogList`, `GetCatalogDetail`, `GetProductComposition`, `GetMaterialsForPurchase`, `GetAutocomplete`, `GetProductUsage`, `GetWarehouseStatistics` | `_currentUserService.EnsureFeatureAccess(Feature.Products_Catalog, "Catalog");` |
| `CatalogMcpTools` | `GetProductMargins` (migrate existing inline block, lines 174–179) | `_currentUserService.EnsureFeatureAccess(Feature.Products_ProductMargins, "Product Margins");` |
| `ManufactureOrderMcpTools` | `GetManufactureOrders`, `GetManufactureOrder`, `GetCalendarView` | `_currentUserService.EnsureFeatureAccess(Feature.Manufacture_ManufactureOrders, "Manufacture Orders");` |
| `ManufactureBatchMcpTools` | `GetBatchTemplate`, `CalculateBatchBySize`, `CalculateBatchByIngredient`, `CalculateBatchPlan` | `_currentUserService.EnsureFeatureAccess(Feature.Manufacture_BatchPlanning, "Batch Planning");` |
| `KnowledgeBaseTools` | `SearchKnowledgeBase`, `AskKnowledgeBase` | `_currentUserService.EnsureFeatureAccess(Feature.Customer_KnowledgeBase, "Knowledge Base");` — **before** the existing `try` |
| `LeafletTools` | `GenerateLeaflet` | `_currentUserService.EnsureFeatureAccess(Feature.Marketing_Leaflet, "Leaflet Generator");` — **before** the existing `try` |
| `UserManagementMcpTools` | `GetGroupMembers` | `_currentUserService.EnsureFeatureAccess(Feature.Admin_Administration, "User Management");` |
| `MeetingTasksMcpTools` | `ListMeetings`, `GetMeetingSummary`, `GetMeetingTranscript`, `GetMeetingTasks` (migrate — delete private `EnsureReadAccess()`) | `_currentUserService.EnsureFeatureAccess(Feature.Anela_Meetings, "Meeting Notes");` |

**Verified reason for the before-`try` placement rule:** `KnowledgeBaseTools.SearchKnowledgeBase`
/ `AskKnowledgeBase` (`KnowledgeBaseTools.cs:31-44`, `:54-67`) wrap the body in
`try { ... } catch (Exception ex) { throw new McpException("Failed to ..."); }` with **no**
`catch (McpException) { throw; }` re-throw guard. A guard call placed inside that `try` would have
its `[FORBIDDEN]` exception caught and re-wrapped into `"Failed to search knowledge base: [FORBIDDEN] ..."`,
losing the prefix tests and clients pattern-match on. `LeafletTools.GenerateLeaflet` does have a
`catch (McpException) { throw; }` clause (`LeafletTools.cs:57-60`) and would survive either
placement, but the rule applies uniformly to all classes/methods regardless — no per-class
try/catch audit needed, no exceptions.

**Constructor changes (mechanical, verified against `McpModule.cs`):**
`ManufactureOrderMcpTools`, `ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`,
`UserManagementMcpTools` each need `ICurrentUserService` added as a constructor parameter + field.
`McpModule.cs` registers each tool class via `.WithTools<T>()` with no explicit factory, resolving
constructor params from the ASP.NET Core container — the same mechanism that already
constructor-injects `ICurrentUserService` into `CatalogMcpTools` and `MeetingTasksMcpTools` in
production today. **No `McpModule.cs` edit is needed.** `ICurrentUserService` is already registered
in the container (it backs these five classes' sibling controllers).

Acceptance criteria:
- For every method in the table, a caller whose `IsInRole(requiredRole)` returns `false` gets an
  `McpException` containing `FORBIDDEN` and the required role string, and `_mediator.Send` /
  downstream logic is never invoked (verify via mock `Times.Never`).
- A caller with the role sees unchanged behavior (request mapping, response JSON, downstream error
  handling for non-auth failures).
- Guard call is the literal first statement of every listed method body — before any `try`, with
  no exceptions.
- Exactly one implementation of "resolve role → check → throw McpException" exists in the codebase
  after migration; no class hand-rolls its own version.

**FR-3 — Update `docs/integrations/mcp-server.md` with each tool's required permission.**

| Section | Change |
|---|---|
| Catalog (8) | Add to group heading: all except `GetProductMargins` require `Products_Catalog`; `GetProductMargins` requires `Products_ProductMargins` (existing per-line note kept). |
| Manufacture Orders (3) | Add: requires `Manufacture_ManufactureOrders`. |
| Manufacture Batch (4) | Add: requires `Manufacture_BatchPlanning`. |
| Knowledge Base (2) | Add: requires `Customer_KnowledgeBase`. |
| User Management (1) | Add: requires `Admin_Administration`. |
| Meeting Notes (4) | Unchanged — already documents `anela.meetings.read`. |
| **Leaflet (1)** | **New group** — confirmed no existing section (`grep -n "Leaflet" docs/integrations/mcp-server.md` returns nothing). Add a `**Leaflet (1)**` heading in the same format as the other groups (e.g. matching `**User Management (1)**`), listing `GenerateLeaflet` and noting it requires `Marketing_Leaflet`. |

Acceptance criteria: every one of the 8 tool classes/groups states its required permission in the
doc, consistent with the FR-2 table; Leaflet is a new group, not an edit to a nonexistent entry.

**FR-4 — Extend unit tests proving the gate for every method, without breaking existing happy-path coverage.**

Follow the existing `MeetingTasksMcpToolsTests` pattern for each of the six touched test files
(`CatalogMcpToolsTests`, `ManufactureOrderMcpToolsTests`, `ManufactureBatchMcpToolsTests`,
`KnowledgeBaseToolsTests`, `LeafletToolsTests`, `UserManagementMcpToolsTests`):

1. Add `private static readonly string ReadRole = AccessRoles.For(Feature.X, AccessLevel.Read);` per class.
2. In the constructor, add a default-allow stub: `_currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);` — needed because once the guard exists, every happy-path test that doesn't otherwise stub `IsInRole` would get Moq's bare-mock default (`false`) and throw.
3. Add a `[Theory]`/`[InlineData]` per gated method asserting: `IsInRole(ReadRole)` set to `false` → `McpException` thrown containing `FORBIDDEN` and `ReadRole` → `_mediatorMock.Verify(..., Times.Never)` for that method's request type.
4. For the five previously-ungated classes, add the `ICurrentUserService` mock + constructor wiring (mirroring `CatalogMcpToolsTests`'s existing shape).

**`CatalogMcpToolsTests` special case (Moq ordering verified, not assumed):** it already mocks
`ICurrentUserService` and tests `GetProductMargins`'s forbidden path with a per-test
`IsInRole(It.IsAny<string>()) → false` stub set up *inside* the test method (after the
constructor runs). Add the new constructor-level default
`_currentUserServiceMock.Setup(s => s.IsInRole(AccessRoles.ProductsCatalogRead)).Returns(true);`
alongside it, not instead of it. Moq resolves the most-recently-configured *matching* setup per
call — the constructor's exact-string setup for `products_catalog_read` doesn't match a call for
`products_product_margins_read`, so the existing `GetProductMargins` forbidden test is unaffected;
only the 7 newly-gated happy-path tests pick up the new default-allow stub.

**`MeetingTasksMcpToolsTests` change:** none beyond what the `EnsureReadAccess` → shared-helper
migration naturally preserves — existing `ReadRole`, constructor stub, and
`Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole` theory already assert the exact
behavior the shared helper reproduces. Run these as a regression check on the migration, not as
new coverage.

Acceptance criteria: for every method in FR-2's table, a forbidden-path test exists and passes;
all pre-existing happy-path tests continue to pass unmodified in behavior (constructor defaults
added, no test logic changed).

## Non-functional requirements

- **Security** — this is the fix itself: no user-visible functional change for permitted users;
  must not weaken any existing check (`MeetingTasksMcpTools`'s per-meeting `IMeetingAccessGuard`
  visibility logic and `GetProductMargins`'s gate stay exactly as strict as today, migration-only).
- **No new dependencies** — reuses `ICurrentUserService`, `AccessRoles`, `Feature`, `AccessLevel`,
  `McpException`, all already referenced by the API project.
- **Consistency** — the role a tool requires must equal the Read role its sibling controller
  enforces; verified 1:1 against each controller's `[FeatureAuthorize]` attribute during the
  architecture pass (`CatalogController.cs:22`, `ManufactureOrderController.cs:19`,
  `ManufactureBatchController.cs:11`, `KnowledgeBaseController.cs:17`, `LeafletController.cs:19`,
  `UserManagementController.cs:10`) — no divergence found.

## Data model

No schema or entity changes. Only touches authorization checks against the existing `Feature`
enum / `AccessRoles` role-string mapping already used by `FeatureAuthorizeAttribute` and
`ICurrentUserService.IsInRole`.

## Interfaces

- No change to MCP tool method signatures, parameters, or response shapes — only a guard clause
  (or, for `GetProductMargins`/`MeetingTasksMcpTools`, a like-for-like replacement of an existing
  guard clause) added as the first statement of each method body.
- No change to the `/mcp` endpoint mapping (`ApplicationBuilderExtensions.cs:136`) —
  `.RequireAuthorization()` for authentication stays; per-feature authorization stays tool-level,
  matching the two classes that already do this correctly. A global SDK-level filter/interceptor
  was considered and explicitly deferred (see Open Questions) — not needed for this fix.
- Wire-level contract change: for a caller lacking the role, the response changes from
  "handler runs and returns data/downstream error" to
  `McpException("[FORBIDDEN] You do not have permission to access {resourceName} (requires {role}).")`,
  fast-failed before `_mediator.Send` is called.

## Dependencies and scope

**In scope:**
- `McpAuthorizationExtensions.EnsureFeatureAccess` (FR-1).
- Guard call sites for all 18 previously-ungated methods across 6 classes, plus migration of the
  5 already-gated methods (`GetProductMargins` + 4 `MeetingTasksMcpTools` methods) onto the shared
  helper (FR-2).
- Constructor changes (`ICurrentUserService` injection) for `ManufactureOrderMcpTools`,
  `ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`, `UserManagementMcpTools`.
- Doc update including new `Leaflet (1)` section (FR-3).
- Test additions/extensions across 6 test files, migration regression-check on
  `MeetingTasksMcpToolsTests` (FR-4).

**Out of scope:**
- Changing `[FeatureAuthorize]` / MVC authorization itself.
- Adopting a `ModelContextProtocol.AspNetCore` SDK-level tool-invocation filter/middleware —
  architecture pass confirmed the per-method + shared-helper approach is sufficient and lower-risk
  than introducing a new enforcement mechanism inside a security fix; a filter-based refactor is
  legitimate future cleanup, not part of this change.
- `MeetingTasksMcpTools`'s per-meeting `IMeetingAccessGuard` visibility logic — unrelated, already
  correct, untouched beyond the role-check migration.
- Any change to what data underlying MediatR handlers return.
- `AccessLevel.Write`-level checks — all seven tool classes are read-only; only `AccessLevel.Read`
  is ever used.
- `McpModule.cs` — no change needed; DI resolves new constructor params automatically.

## Rough plan

1. Add `McpAuthorizationExtensions.EnsureFeatureAccess` (FR-1) in
   `Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs`.
2. Migrate `CatalogMcpTools.GetProductMargins`'s inline block and
   `MeetingTasksMcpTools.EnsureReadAccess` (delete the private method, update its 4 call sites)
   onto the new helper — proves it behaves identically before it's relied on elsewhere; run the
   two existing test files to confirm no regression.
3. Add the guard call to each of the 18 previously-ungated methods, one class at a time, guard
   before any `try`:
   `CatalogMcpTools` (7 methods) → `ManufactureOrderMcpTools` (3, + constructor change) →
   `ManufactureBatchMcpTools` (4, + constructor change) → `KnowledgeBaseTools` (2, + constructor
   change, guard-before-try) → `LeafletTools` (1, + constructor change, guard-before-try) →
   `UserManagementMcpTools` (1, + constructor change).
4. Extend the six test files per FR-4: constructor default-allow stubs, per-method forbidden-path
   theories, `CatalogMcpToolsTests`'s two-stub-coexistence case handled explicitly.
5. Update `docs/integrations/mcp-server.md` per FR-3, including the new `Leaflet (1)` group.
6. Run `dotnet build` + `dotnet format` + full backend test suite; confirm all new and existing
   MCP tests pass, and existing `MeetingTasksMcpToolsTests`/`CatalogMcpToolsTests` cases are
   unaffected by the migration.

## Open questions

All three open questions from plan-01.md are resolved by the design/architecture passes:

- ~~Does the SDK expose a tool-call filter/interceptor?~~ **Resolved: not pursued.** Per-method +
  shared-helper is confirmed sufficient, lower-risk, and consistent with the two already-correct
  classes; a filter-based refactor is optional future cleanup, not part of this change.
- ~~Where should the shared helper live — API layer or Domain?~~ **Resolved: API layer**
  (`Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs`), matching the existing `*Extensions.cs`
  convention in that project (`ApplicationBuilderExtensions.cs`, `AuthenticationExtensions.cs`,
  etc.) and because it depends on `McpException` from the `ModelContextProtocol` SDK, which
  `Anela.Heblo.Domain` should not reference.
- ~~Constructor changes needed for five classes~~ **Resolved: mechanical, no DI registration
  change needed** — confirmed via reading `McpModule.cs`, which resolves tool class constructors
  from the container with no explicit factory.

No new open questions were introduced by the design/architecture passes. One scope item was
added (not a question, already resolved with a default): `LeafletTools` gets a **new** doc group
in FR-3 rather than an edit, since no existing section names it.
