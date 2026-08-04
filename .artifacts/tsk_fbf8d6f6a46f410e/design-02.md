# Design (v2): MCP tools bypass the per-feature authorization gate their controllers enforce

This supersedes `design-01.md`. Structurally unchanged — the architecture pass (`architecture-01.md`)
approved the design as-is after re-reading every referenced file against current source. This
version resolves design-01's one open hedge (the Leaflet doc entry, "confirm during
implementation") into a settled instruction, and states DI/test details as verified facts rather
than proposals, per `plan-02.md`. All source excerpts below were re-verified directly against the
current tree at the time of this writing.

No UI is involved — this is a backend-only authorization fix inside the MCP tool layer
(`backend/src/Anela.Heblo.API/MCP/Tools/`). The UX/UI section is omitted.

## Component design

### New component: `McpAuthorizationExtensions`

**File:** `backend/src/Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs` (new)
**Namespace:** `Anela.Heblo.API.MCP`

A single static extension method on `ICurrentUserService`, replacing the two hand-rolled copies of
the same sequence — verified present today at `CatalogMcpTools.cs:174-179` (inline block inside
`GetProductMargins`) and `MeetingTasksMcpTools.cs:184` (private `EnsureReadAccess()`, called from
lines 52, 91, 130, 160):

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

This is byte-for-byte the message shape already produced by the two existing checks
(`"[FORBIDDEN] You do not have permission to access Product Margins (requires {role})."` /
`"... Meeting Notes (requires {role})."`), just with `resourceName` and `feature` parameterized —
so `MeetingTasksMcpToolsTests` and `CatalogMcpToolsTests`'s existing `Assert.Contains("FORBIDDEN", ...)`
/ `Assert.Contains(role, ...)` assertions keep working unmodified.

**Why an extension method, not a standalone static helper class taking `ICurrentUserService` as a
parameter:** it reads at the call site exactly like the existing `_currentUserService.IsInRole(role)`
calls it replaces (`_currentUserService.EnsureFeatureAccess(Feature.X, "Y")`), so the diff at each
of the 18 call sites is a one-line insertion, not a restructure.

**Why `Anela.Heblo.API/MCP/` and not `Anela.Heblo.Domain/Features/Authorization/`:** the helper
throws `McpException` from the `ModelContextProtocol` SDK package, which the Domain layer does not
(and should not) reference. This mirrors where `McpException` is already thrown throughout the
tool classes. It also matches the established `Anela.Heblo.API/*Extensions.cs` naming/placement
convention already in the API project (`ApplicationBuilderExtensions.cs`,
`AuthenticationExtensions.cs`, `ServiceCollectionExtensions.cs`).

**Placement rule inside each tool method (verified against the actual try/catch shape of every
affected class, not assumed):** the call must be the **first statement in the method body, before
any `try`**.

- `KnowledgeBaseTools.SearchKnowledgeBase` / `AskKnowledgeBase` wrap their body in
  `try { ... } catch (Exception ex) { throw new McpException($"Failed to ..."); }` with **no**
  `catch (McpException) { throw; }` re-throw guard (confirmed at `KnowledgeBaseTools.cs:24-45` and
  `:47-68`). A guard call placed inside that `try` would have its `[FORBIDDEN]` `McpException`
  caught by the generic `catch (Exception ex)` and re-wrapped into
  `"Failed to search knowledge base: [FORBIDDEN] ..."`, losing the prefix tests and any client
  pattern-match on.
- `LeafletTools.GenerateLeaflet` (`LeafletTools.cs:24-65`) **does** have
  `catch (McpException) { throw; }` (`:56-59`) ahead of its generic catch, so it would technically
  survive placement inside the `try` — but the rule is applied uniformly to all six classes
  regardless, so no per-class try/catch audit is needed and the rule stays correct even if a
  class's catch structure changes later.

### Modified components — one guard call site per method, one constructor change per class

Constructor shapes below are read directly from source, not inferred:

| Class | File | Current constructor | Constructor change | Guard call (first line of every listed method) |
|---|---|---|---|---|
| `CatalogMcpTools` | `MCP/Tools/CatalogMcpTools.cs` | `CatalogMcpTools(IMediator mediator, ICurrentUserService currentUserService)` (`:29`) | none — already injected | `GetCatalogList`, `GetCatalogDetail`, `GetProductComposition`, `GetMaterialsForPurchase`, `GetAutocomplete`, `GetProductUsage`, `GetWarehouseStatistics` → `_currentUserService.EnsureFeatureAccess(Feature.Products_Catalog, "Catalog");` |
| `CatalogMcpTools.GetProductMargins` | same | same | none | replace the existing inline block at lines 174–179 with `_currentUserService.EnsureFeatureAccess(Feature.Products_ProductMargins, "Product Margins");` |
| `ManufactureOrderMcpTools` | `MCP/Tools/ManufactureOrderMcpTools.cs` | `ManufactureOrderMcpTools(IMediator mediator)` (`:21`), one field `_mediator` | add `ICurrentUserService currentUserService` param + `_currentUserService` field | `GetManufactureOrders`, `GetManufactureOrder`, `GetCalendarView` → `_currentUserService.EnsureFeatureAccess(Feature.Manufacture_ManufactureOrders, "Manufacture Orders");` |
| `ManufactureBatchMcpTools` | `MCP/Tools/ManufactureBatchMcpTools.cs` | `ManufactureBatchMcpTools(IMediator mediator)` (`:21`), one field `_mediator` | add `ICurrentUserService currentUserService` param + field | `GetBatchTemplate`, `CalculateBatchBySize`, `CalculateBatchByIngredient`, `CalculateBatchPlan` → `_currentUserService.EnsureFeatureAccess(Feature.Manufacture_BatchPlanning, "Batch Planning");` |
| `KnowledgeBaseTools` | `MCP/Tools/KnowledgeBaseTools.cs` | `KnowledgeBaseTools(IMediator mediator, ILogger<KnowledgeBaseTools> logger)` (`:18`) | add `ICurrentUserService currentUserService` param + field, additive alongside the existing `ILogger` param | `SearchKnowledgeBase`, `AskKnowledgeBase` → `_currentUserService.EnsureFeatureAccess(Feature.Customer_KnowledgeBase, "Knowledge Base");` — **before** the existing `try` |
| `LeafletTools` | `MCP/Tools/LeafletTools.cs` | `LeafletTools(IMediator mediator, ILogger<LeafletTools> logger)` (`:18`) | add `ICurrentUserService currentUserService` param + field, additive alongside the existing `ILogger` param | `GenerateLeaflet` → `_currentUserService.EnsureFeatureAccess(Feature.Marketing_Leaflet, "Leaflet Generator");` — **before** the existing `try` |
| `UserManagementMcpTools` | `MCP/Tools/UserManagementMcpTools.cs` | `UserManagementMcpTools(IMediator mediator)` (`:20`), one field `_mediator` | add `ICurrentUserService currentUserService` param + field | `GetGroupMembers` → `_currentUserService.EnsureFeatureAccess(Feature.Admin_Administration, "User Management");` |
| `MeetingTasksMcpTools` | `MCP/Tools/MeetingTasksMcpTools.cs` | already has `ICurrentUserService` (verified: `EnsureReadAccess()` at `:184` already calls `_currentUserService.IsInRole`) | none | delete the private `EnsureReadAccess()` method; replace its 4 call sites (`ListMeetings`, `GetMeetingSummary`, `GetMeetingTranscript`, `GetMeetingTasks` — lines 52, 91, 130, 160) with `_currentUserService.EnsureFeatureAccess(Feature.Anela_Meetings, "Meeting Notes");` |

**DI wiring (verified against `McpModule.cs` directly, reproduced here in full):**

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<CatalogMcpTools>()
    .WithTools<ManufactureOrderMcpTools>()
    .WithTools<ManufactureBatchMcpTools>()
    .WithTools<KnowledgeBaseTools>()
    .WithTools<LeafletTools>()
    .WithTools<UserManagementMcpTools>()
    .WithTools<MeetingTasksMcpTools>();
```

`.WithTools<T>()` takes no explicit factory — `T` is constructed by the ASP.NET Core container,
resolving constructor parameters the normal DI way. This is the exact mechanism that already
constructor-injects `ICurrentUserService` into `CatalogMcpTools` and `MeetingTasksMcpTools` in
production today over the live `/mcp` HTTP transport. Adding the parameter to the other five
classes requires **no change to `McpModule.cs`** — it is a mechanical constructor edit only.
`ICurrentUserService` is already registered in the container (it backs these five classes' sibling
controllers), so no new DI registration is needed either.

**Resource-name strings** (`"Catalog"`, `"Manufacture Orders"`, `"Batch Planning"`,
`"Knowledge Base"`, `"Leaflet Generator"`, `"User Management"`, `"Meeting Notes"`,
`"Product Margins"`) are plain English labels for the `McpException` message, matching the style of
the two existing ones (`"Product Margins"`, `"Meeting Notes"`) — not the Czech
`FeatureDefinition.Name` values in the generated access-matrix, which are UI-facing and localized
for a different audience.

## Data / contract schemas

No persisted data schema changes. Two wire-level contracts are affected:

**1. `McpException` message shape (unchanged format, now emitted uniformly by one code path)**

```
[FORBIDDEN] You do not have permission to access {resourceName} (requires {role}).
```

- `{resourceName}` — one of the 8 labels above.
- `{role}` — the exact string `AccessRoles.For(feature, AccessLevel.Read)` returns (e.g.
  `products_catalog_read`), identical to what the sibling controller's `[FeatureAuthorize]`
  requires (verified against `AccessRoles.generated.cs:66`'s `For(Feature, AccessLevel)` switch).

This is the only observable behavior change for a caller without the role: previously
`_mediator.Send(...)` ran and returned data (or a downstream `McpException` for unrelated errors);
now the call fails fast with the message above and the mediator is never invoked (a caller with the
role sees no change — same request mapping, same response JSON, same downstream-error handling).

**2. `docs/integrations/mcp-server.md` — permission note per group (FR-3)**

| Section | Line to add/change |
|---|---|
| Catalog (8) | Add to the group heading: all except `GetProductMargins` require `Products_Catalog`; `GetProductMargins` requires `Products_ProductMargins` (keep the existing per-line note on `GetProductMargins`). |
| Manufacture Orders (3) | Add: requires `Manufacture_ManufactureOrders`. |
| Manufacture Batch (4) | Add: requires `Manufacture_BatchPlanning`. |
| Knowledge Base (2) | Add: requires `Customer_KnowledgeBase`. |
| User Management (1) | Add: requires `Admin_Administration`. |
| Meeting Notes (4) | Unchanged — already documents `anela.meetings.read`. |
| **Leaflet (1)** | **Settled, not a hedge:** confirmed via direct read of `docs/integrations/mcp-server.md` that no `Leaflet` section exists anywhere in the file (a prior grep for `Leaflet` returned nothing). Add a new `**Leaflet (1)**` heading, in the same format as the other groups (e.g. matching `**User Management (1)**`), listing `GenerateLeaflet` and noting it requires `Marketing_Leaflet`. This is an addition, not an edit to an existing entry. |

Acceptance: every one of the 8 tool classes/groups states its required permission in the doc,
consistent with the component-design table above.

## Test design

No new test *files*; extend the six existing suites (`CatalogMcpToolsTests`,
`ManufactureOrderMcpToolsTests`, `ManufactureBatchMcpToolsTests`, `KnowledgeBaseToolsTests`,
`LeafletToolsTests`, `UserManagementMcpToolsTests`) plus a small migration edit to
`MeetingTasksMcpToolsTests` (no new cases — it already covers the forbidden path).
`McpAuthorizationExtensions` itself gets no dedicated unit test file: it's a 4-line pure function
exercised by every forbidden-path test across all seven classes, matching the project's existing
convention of not unit-testing the inline check in isolation.

**Per-class change, following the `MeetingTasksMcpToolsTests` pattern exactly:**

1. Add `private static readonly string ReadRole = AccessRoles.For(Feature.X, AccessLevel.Read);`
   (already present in `MeetingTasksMcpToolsTests`; new in the other five).
2. In the constructor, stub the default:
   `_currentUserServiceMock.Setup(s => s.IsInRole(ReadRole)).Returns(true);` — so all existing
   happy-path tests for the newly-gated methods keep passing without modification. Without this,
   Moq's bare-mock default for an unstubbed `IsInRole` call is `false`, which would make every
   happy-path test throw `McpException` once the guard exists.
3. Add a `[Theory]` with one `[InlineData("MethodName")]` per gated method in that class, mirroring
   `MeetingTasksMcpToolsTests.Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole`: set
   `IsInRole(ReadRole)` to `false`, assert `McpException` is thrown containing `"FORBIDDEN"` and
   `ReadRole`, and assert `_mediatorMock.Verify(..., Times.Never)` for that method's request type.
4. Add/adjust the constructor for the five previously-ungated classes' test fixtures to accept and
   store a new `Mock<ICurrentUserService>`, matching `CatalogMcpToolsTests`'s existing constructor
   shape.

**`CatalogMcpToolsTests` special case — Moq resolution order verified, not assumed.** This fixture
already has an `ICurrentUserService` mock and already tests `GetProductMargins`'s forbidden path
with a per-test `IsInRole(It.IsAny<string>()) → false` stub configured *inside* the test method
(i.e., after the constructor runs). Add a constructor-level default stub for the new
`Products_Catalog` role:

```csharp
_currentUserServiceMock.Setup(s => s.IsInRole(AccessRoles.ProductsCatalogRead)).Returns(true);
```

added alongside — not instead of — the existing `GetProductMargins` test's own stub. Moq resolves
the most-recently-configured *matching* setup for a given call. For a call to
`IsInRole("products_product_margins_read")`, the constructor's exact-string setup for
`products_catalog_read` does not match, so only the test-level `It.IsAny<string>() → false` setup
applies — the existing `GetProductMargins_ThrowsMcpException_WhenUserLacksFeature` test is
unaffected. Only the 7 newly-gated happy-path tests pick up the new default-allow stub.

**`MeetingTasksMcpToolsTests` change:** none beyond what the `EnsureReadAccess` → shared-helper
migration naturally preserves — its existing `ReadRole`, constructor stub, and
`Tools_ThrowForbidden_AndSkipMediator_WhenUserLacksReadRole` theory already assert the exact
message/behavior the shared helper reproduces. Run these as a regression check on the migration,
not as new coverage.

## Sequencing (informs, does not replace, the development step's task breakdown)

Confirmed low-risk based on this design pass plus the architecture verification — no open technical
questions remain that would change the shape of the change:

- No `McpModule.cs` edit needed (DI resolves the new constructor params automatically — verified
  against its literal contents above).
- No `ModelContextProtocol.AspNetCore` SDK filter/interceptor hook assumed or required — the
  per-method guard is confirmed sufficient and consistent with the two already-correct classes; a
  filter-based refactor is legitimate future cleanup, explicitly out of scope for this change.
- The guard-before-`try` placement rule is the one non-obvious implementation detail that must be
  respected in `KnowledgeBaseTools` and `LeafletTools` to avoid the forbidden message being
  silently re-wrapped by their generic exception handlers — verified necessary specifically for
  `KnowledgeBaseTools` (no `catch (McpException)` guard), applied uniformly to all six classes to
  avoid a per-class catch-clause audit.
- The `Leaflet (1)` doc section is a new addition, settled by direct verification — not an open
  item for the development step to investigate.
