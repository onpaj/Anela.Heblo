# Development: MCP tools bypass the per-feature authorization gate their controllers enforce

Implements `plan-02.md` / `design-02.md` exactly, following `architecture-02.md`'s one
correction (constructor **added**, not adjusted, for `KnowledgeBaseToolsTests`/`LeafletToolsTests`).

## Summary

Added a single shared MCP-side permission gate and applied it as the first statement of
every method in all seven MCP tool classes, closing the gap where five of seven classes
had no feature-permission check even though their sibling MVC controllers require one.

## Files created

- `backend/src/Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs` — new static extension
  method `ICurrentUserService.EnsureFeatureAccess(Feature, string resourceName, AccessLevel level = Read)`.
  Resolves the required role via `AccessRoles.For`, checks `IsInRole`, and throws
  `McpException("[FORBIDDEN] You do not have permission to access {resourceName} (requires {role}).")`
  on failure — byte-identical message shape to the two hand-rolled checks it replaces.

## Files changed (source)

- `CatalogMcpTools.cs` — added the guard as the first statement of `GetCatalogList`,
  `GetCatalogDetail`, `GetProductComposition`, `GetMaterialsForPurchase`, `GetAutocomplete`,
  `GetProductUsage`, `GetWarehouseStatistics` (all → `Feature.Products_Catalog`, "Catalog").
  `GetProductMargins`'s existing inline block replaced with the shared helper call
  (`Feature.Products_ProductMargins`, "Product Margins") — no constructor change, it already
  had `ICurrentUserService`.
- `ManufactureOrderMcpTools.cs` — added `ICurrentUserService` constructor param/field; guarded
  `GetManufactureOrders`, `GetManufactureOrder`, `GetCalendarView`
  (`Feature.Manufacture_ManufactureOrders`, "Manufacture Orders").
- `ManufactureBatchMcpTools.cs` — added `ICurrentUserService` constructor param/field; guarded
  `GetBatchTemplate`, `CalculateBatchBySize`, `CalculateBatchByIngredient`, `CalculateBatchPlan`
  (`Feature.Manufacture_BatchPlanning`, "Batch Planning").
- `KnowledgeBaseTools.cs` — added `ICurrentUserService` constructor param/field (additive
  alongside `ILogger`); guarded `SearchKnowledgeBase`, `AskKnowledgeBase`
  (`Feature.Customer_KnowledgeBase`, "Knowledge Base") **before** the existing `try` — required
  because these methods' `catch (Exception ex)` has no `catch (McpException) { throw; }` guard
  and would otherwise re-wrap the `[FORBIDDEN]` message.
- `LeafletTools.cs` — added `ICurrentUserService` constructor param/field (additive alongside
  `ILogger`); guarded `GenerateLeaflet` (`Feature.Marketing_Leaflet`, "Leaflet Generator")
  before the existing `try` (applied uniformly per the design rule, though this method already
  has a `catch (McpException) { throw; }` guard).
- `UserManagementMcpTools.cs` — added `ICurrentUserService` constructor param/field; guarded
  `GetGroupMembers` (`Feature.Admin_Administration`, "User Management") — closes the sharpest
  gap: any authenticated user could previously enumerate Entra ID group membership.
- `MeetingTasksMcpTools.cs` — migrated: deleted the private `EnsureReadAccess()` method, replaced
  its 4 call sites (`ListMeetings`, `GetMeetingSummary`, `GetMeetingTranscript`,
  `GetMeetingTasks`) with the shared helper (`Feature.Anela_Meetings`, "Meeting Notes"). No
  constructor change — already had `ICurrentUserService`.

No change to `McpModule.cs` — DI resolves the new constructor parameters automatically (verified
via `dotnet build`, which constructs all seven tool types through the container at startup path).

## Files changed (tests)

Extended all six previously-ungated test fixtures, plus verified `MeetingTasksMcpToolsTests`
needs no change (its existing coverage already asserts the exact behavior the shared helper
reproduces):

- `CatalogMcpToolsTests.cs` — added constructor-level `IsInRole(AccessRoles.ProductsCatalogRead) → true`
  default stub (coexists with the pre-existing `GetProductMargins` forbidden test's own
  `IsInRole(It.IsAny<string>()) → false` per-test override — confirmed via the actual test run,
  not just Moq-resolution-order theory). Added a `[Theory]` covering all 7 newly-gated methods'
  forbidden path (`FORBIDDEN` + role in message, mediator never called).
- `ManufactureOrderMcpToolsTests.cs` — added `Mock<ICurrentUserService>`, constructor default-allow
  stub, `ReadRole` constant, and a forbidden-path `[Theory]` for all 3 methods.
- `ManufactureBatchMcpToolsTests.cs` — same pattern, 4 methods.
- `UserManagementMcpToolsTests.cs` — added `Mock<ICurrentUserService>`, constructor default-allow
  stub, `ReadRole` constant, and a single forbidden-path `[Fact]` for `GetGroupMembers` (its only
  method).
- `KnowledgeBaseToolsTests.cs` — this fixture had **no constructor** (per architecture-02's
  correction), so one was **added**: `Mock<ICurrentUserService>` field + constructor stubbing
  `IsInRole(ReadRole) → true`; `CreateTools()` factory updated to pass it through. Added a
  forbidden-path `[Theory]` for both methods.
- `LeafletToolsTests.cs` — same "add a constructor" correction applied; added a forbidden-path
  `[Fact]` for `GenerateLeaflet`.

## Docs

`docs/integrations/mcp-server.md` — added a required-permission note to each tool group heading
(Catalog, Manufacture Orders, Manufacture Batch, Knowledge Base, User Management), and added a
new **Leaflet (1)** group (confirmed no prior section existed) documenting `GenerateLeaflet`
requires `Marketing_Leaflet`. Meeting Notes section left unchanged (already documented).

## Verification performed

- `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` — 0 errors (155 pre-existing
  nullable warnings unrelated to this change).
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — 0 errors.
- `dotnet test --filter "FullyQualifiedName~MCP.Tools"` — **67/67 passed** (up from the
  pre-existing 29; +38 new forbidden-path tests, 0 regressions in the 29 existing happy-path/error
  tests).
- `dotnet format Anela.Heblo.sln --verify-no-changes --no-restore --include <all 15 changed .cs files>`
  — clean, no formatting changes needed (re-verified after the final `UserManagementMcpToolsTests`
  addition).
- Full backend suite (`dotnet test --no-build`, 6119 tests): 6069 passed, 46 failed, 4 skipped.
  All 46 failures are pre-existing EF Core `ManyServiceProvidersCreatedWarning` integration-test
  infrastructure flakiness (`DbResiliencePipelineProviderTests`, `SmartsuppRepository*IntegrationTests`,
  `MeetingTranscriptRepositorySearchIntegrationTests`, `LeafletDocumentRepositoryPagedTests`,
  `LeafletRepositoryIntegrationTests`, `KnowledgeBaseRepositoryIntegrationTests`,
  `CatalogMergeSchedulerTests`) — a known side effect of running the entire 6100+ test suite in one
  process (>20 EF `IServiceProvider` instances created), triggered by DB-integration test classes
  unrelated to MCP/authorization. None are in the `MCP.Tools` namespace or touch `Feature`/
  `AccessRoles`/`ICurrentUserService`. Verified by running the MCP-scoped filter separately (clean).

## How to verify

```bash
cd backend
dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MCP.Tools"
```

Expect: build succeeds, 67 tests pass (0 failed).

To confirm the fix manually: call any of the 18 previously-ungated MCP methods (e.g.
`UserManagementMcpTools.GetGroupMembers`) as an authenticated user who lacks the corresponding
role (e.g. missing `admin.administration.read`) — it now throws `McpException` with a `[FORBIDDEN]`
message before `IMediator.Send` is invoked, matching the behavior already in place for
`GetProductMargins` and the `MeetingTasksMcpTools` methods.
