# Design: MCP tools bypass the per-feature authorization gate their controllers enforce

No UI is involved — this is a backend-only authorization fix inside the MCP tool layer (`backend/src/Anela.Heblo.API/MCP/Tools/`). The UX/UI section is omitted.

## Component design

### New component: `McpAuthorizationExtensions`

**File:** `backend/src/Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs` (new)
**Namespace:** `Anela.Heblo.API.MCP`

A single static extension method on `ICurrentUserService`, replacing the two hand-rolled copies of the same sequence (`CatalogMcpTools.GetProductMargins`'s inline block, `MeetingTasksMcpTools.EnsureReadAccess`):

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using ModelContextProtocol;

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

This is byte-for-byte the message shape already produced by the two existing checks (`"[FORBIDDEN] You do not have permission to access Product Margins (requires {role})."` / `"... Meeting Notes (requires {role})."`), just with `resourceName` and `feature` parameterized — so `MeetingTasksMcpToolsTests` and `CatalogMcpToolsTests`'s existing `Assert.Contains("FORBIDDEN", ...)` / `Assert.Contains(role, ...)` assertions keep working unmodified.

**Why an extension method, not a standalone static helper class taking `ICurrentUserService` as a parameter:** it reads at the call site exactly like the existing `_currentUserService.IsInRole(role)` calls it replaces (`_currentUserService.EnsureFeatureAccess(Feature.X, "Y")`), so the diff at each of the 18 call sites is a one-line insertion, not a restructure.

**Why `Anela.Heblo.API/MCP/` and not `Anela.Heblo.Domain/Features/Authorization/`:** the helper throws `McpException` from the `ModelContextProtocol` SDK package, which the Domain layer does not (and should not) reference. This mirrors where `McpException` is already thrown throughout the tool classes.

**Placement rule inside each tool method (important — verified against two existing bugs-in-waiting):**
The call must be the **first statement in the method body, before any `try`**. Three of the five ungated classes wrap their body in `try { ... } catch (Exception ex) { throw new McpException("Failed to ..."); }` with no `catch (McpException) { throw; }` re-throw guard (`KnowledgeBaseTools`, and effectively `LeafletTools` for its outer generic catch — `LeafletTools` does have a `catch (McpException) { throw; }` clause, but `KnowledgeBaseTools` does not). If the guard call were placed inside the `try`, the `McpException` it throws would be caught by the generic `catch (Exception ex)` and re-wrapped into a *different* message (`"Failed to search knowledge base: ..."`), losing the `[FORBIDDEN]`/role text the tests and any calling client pattern-match on. Placing the guard before the `try` sidesteps this for every class uniformly, so no class needs a `catch (McpException) { throw; }` audit.

### Modified components — one guard call site per method, one constructor change per class

| Class | File | Constructor change | Guard call (first line of every listed method) |
|---|---|---|---|
| `CatalogMcpTools` | `MCP/Tools/CatalogMcpTools.cs` | none (`ICurrentUserService` already injected) | `GetCatalogList`, `GetCatalogDetail`, `GetProductComposition`, `GetMaterialsForPurchase`, `GetAutocomplete`, `GetProductUsage`, `GetWarehouseStatistics` → `_currentUserService.EnsureFeatureAccess(Feature.Products_Catalog, "Catalog");` |
| `CatalogMcpTools.GetProductMargins` | same | none | replace the existing 6-line inline block (lines 174–179) with `_currentUserService.EnsureFeatureAccess(Feature.Products_ProductMargins, "Product Margins");` |
| `ManufactureOrderMcpTools` | `MCP/Tools/ManufactureOrderMcpTools.cs` | add `ICurrentUserService currentUserService` param + field | `GetManufactureOrders`, `GetManufactureOrder`, `GetCalendarView` → `_currentUserService.EnsureFeatureAccess(Feature.Manufacture_ManufactureOrders, "Manufacture Orders");` |
| `ManufactureBatchMcpTools` | `MCP/Tools/ManufactureBatchMcpTools.cs` | add `ICurrentUserService currentUserService` param + field | `GetBatchTemplate`, `CalculateBatchBySize`, `CalculateBatchByIngredient`, `CalculateBatchPlan` → `_currentUserService.EnsureFeatureAccess(Feature.Manufacture_BatchPlanning, "Batch Planning");` |
| `KnowledgeBaseTools` | `MCP/Tools/KnowledgeBaseTools.cs` | add `ICurrentUserService currentUserService` param + field | `SearchKnowledgeBase`, `AskKnowledgeBase` → `_currentUserService.EnsureFeatureAccess(Feature.Customer_KnowledgeBase, "Knowledge Base");` (inserted before the existing `try`) |
| `LeafletTools` | `MCP/Tools/LeafletTools.cs` | add `ICurrentUserService currentUserService` param + field | `GenerateLeaflet` → `_currentUserService.EnsureFeatureAccess(Feature.Marketing_Leaflet, "Leaflet Generator");` (inserted before the existing `try`) |
| `UserManagementMcpTools` | `MCP/Tools/UserManagementMcpTools.cs` | add `ICurrentUserService currentUserService` param + field | `GetGroupMembers` → `_currentUserService.EnsureFeatureAccess(Feature.Admin_Administration, "User Management");` |
| `MeetingTasksMcpTools` | `MCP/Tools/MeetingTasksMcpTools.cs` | none | delete the private `EnsureReadAccess()` method; replace its 4 call sites (`ListMeetings`, `GetMeetingSummary`, `GetMeetingTranscript`, `GetMeetingTasks`) with `_currentUserService.EnsureFeatureAccess(Feature.Anela_Meetings, "Meeting Notes");` |

**DI wiring:** `McpModule.cs` registers tool classes via `.WithTools<T>()`, which resolves `T` from the ASP.NET Core container — the same mechanism that already constructor-injects `ICurrentUserService` into `CatalogMcpTools` and `MeetingTasksMcpTools` today. Adding the parameter to the other five classes requires **no change to `McpModule.cs`**; it's a mechanical constructor edit only. `ICurrentUserService` is already registered in the container (it backs the five classes' sibling controllers), so no new DI registration is needed.

**Resource-name strings** (`"Catalog"`, `"Manufacture Orders"`, `"Batch Planning"`, `"Knowledge Base"`, `"Leaflet Generator"`, `"User Management"`, `"Meeting Notes"`, `"Product Margins"`) are plain English labels for the `McpException` message, matching the style of the two existing ones (`"Product Margins"`, `"Meeting Notes"`) — not the Czech `FeatureDefinition.Name` values in `AccessMatrix.generated.cs`, which are UI-facing and localized for a different audience.

## Data / contract schemas

No persisted data schema changes. Two wire-level contracts are affected:

**1. `McpException` message shape (unchanged format, now emitted uniformly)**

```
[FORBIDDEN] You do not have permission to access {resourceName} (requires {role}).
```

- `{resourceName}` — one of the 8 labels above.
- `{role}` — the exact string `AccessRoles.For(feature, AccessLevel.Read)` returns (e.g. `products_catalog_read`), identical to what the sibling controller's `[FeatureAuthorize]` requires.

This is the only observable behavior change for a caller without the role: previously `_mediator.Send(...)` ran and returned data (or a downstream `McpException` for unrelated errors); now the call fails fast with the message above and the mediator is never invoked. Callers *with* the role see no change — same request mapping, same response JSON, same downstream-error handling.

**2. `docs/integrations/mcp-server.md` — permission column (FR-3)**

Update the "Available Tools" section so every group states its required permission, matching the existing style used for Catalog's `GetProductMargins` line and the Meeting Notes group heading:

| Section | Line to add/change |
|---|---|
| Catalog (8) | Add to the group heading: `**Catalog (8)** — all except \`GetProductMargins\` require the Products_Catalog permission; \`GetProductMargins\` requires Products_ProductMargins.` (keep the existing per-line note on `GetProductMargins`) |
| Manufacture Orders (3) | `**Manufacture Orders (3)** — requires the Manufacture_ManufactureOrders permission.` |
| Manufacture Batch (4) | `**Manufacture Batch (4)** — requires the Manufacture_BatchPlanning permission.` |
| User Management (1) | `**User Management (1)** — requires the Admin_Administration permission.` |
| Knowledge Base (2) | `**Knowledge Base (2)** — requires the Customer_KnowledgeBase permission.` |
| Meeting Notes (4) | unchanged — already documents `anela.meetings.read` |

(`LeafletTools.GenerateLeaflet` is not currently listed as its own group in the doc — confirm during implementation whether it has an existing section; if so, add `— requires the Marketing_Leaflet permission.` to its heading using the same phrasing.)

## Test design

No new test *files*; extend the six existing suites (`CatalogMcpToolsTests`, `ManufactureOrderMcpToolsTests`, `ManufactureBatchMcpToolsTests`, `KnowledgeBaseToolsTests`, `LeafletToolsTests`, `UserManagementMcpToolsTests`) plus a small edit to `MeetingTasksMcpToolsTests` (migration only, no new cases — it already covers the forbidden path). `McpAuthorizationExtensions` itself is not given a dedicated unit test file: it's a 4-line pure function exercised by every forbidden-path test across all seven classes, matching the project's existing convention of not unit-testing the inline check in isolation.

**Per-class change, following the `MeetingTasksMcpToolsTests` pattern exactly:**

1. Add `private static readonly string ReadRole = AccessRoles.For(Feature.X, AccessLevel.Read);` (already present in `MeetingTasksMcpToolsTests`; new in the other five).
2. In the constructor, stub the default: `_currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);` — so all existing happy-path tests for the newly-gated methods keep passing without modification.
3. Add a `[Theory]` with one `[InlineData("MethodName")]` per gated method in that class, mirroring `MeetingTasksMcpToolsTests.Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole`: set `IsInRole(ReadRole)` to `false`, assert `McpException` is thrown containing `"FORBIDDEN"` and `ReadRole`, and assert `_mediatorMock.Verify(..., Times.Never)` for that method's request type.
4. Add/adjust the constructor for the five previously-unauthenticated classes to accept and store the new `Mock<ICurrentUserService>` (new field + constructor arg to the class under test), matching `CatalogMcpToolsTests`'s existing constructor shape.

**`CatalogMcpToolsTests` special case:** this class already has an `ICurrentUserService` mock and already tests `GetProductMargins`'s forbidden path with its own per-test `IsInRole` stubs (not a constructor default). Add a *second* default stub for the new `Products_Catalog` role — `_currentUserServiceMock.Setup(s => s.IsInRole(AccessRoles.ProductsCatalogRead)).Returns(true);` — alongside (not instead of) the existing `GetProductMargins` tests' own explicit stubs, since Moq resolves a later, more specific test-level setup over an earlier constructor-level one for the same test run. Verified: the existing `GetProductMargins_ThrowsMcpException_WhenUserLacksFeature` test's `IsInRole(It.IsAny<string>()) → false` setup is added after the constructor's, so it still wins for that test — no regression.

**`MeetingTasksMcpToolsTests` change:** none required beyond what the migration naturally preserves — its existing `ReadRole`, constructor stub, and `Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole` theory already assert the exact message/behavior the shared helper reproduces. Run as a regression check that the migration didn't change behavior, not as new coverage.

## Sequencing (informs, does not replace, the development step's task breakdown)

Confirmed low-risk based on this design pass — no open technical questions remain that would change the shape of the change:
- No `McpModule.cs` edit needed (DI resolves the new constructor params automatically).
- No `ModelContextProtocol.AspNetCore` SDK filter/interceptor hook was assumed or required — the per-method guard, as scoped in the plan, is confirmed sufficient and consistent with the two already-correct classes.
- The guard-before-`try` placement rule above is the one non-obvious implementation detail that must be respected in `KnowledgeBaseTools` and `LeafletTools` to avoid the forbidden message being silently re-wrapped by their generic exception handlers.
