# Architecture assessment (v2): MCP tools bypass the per-feature authorization gate

## Verdict

**Approved as designed — no changes required.** This pass re-verifies `design-02.md` against the
current tree from scratch (not by trusting `architecture-01.md`'s prior sign-off): every tool class
constructor, every try/catch shape, `McpModule.cs`'s DI registration list, every controller's
`[FeatureAuthorize]` attribute, `AccessRoles.generated.cs`'s `For(Feature, AccessLevel)` switch,
`ICurrentUserService.IsInRole`'s signature, both reference test fixtures
(`CatalogMcpToolsTests`, `MeetingTasksMcpToolsTests`), the five untouched test fixtures, and
`docs/integrations/mcp-server.md` in full. Every factual claim in design-02 checked out byte-for-byte
against source. One small, non-blocking correction below (test-fixture shape for two of the five
newly-touched suites); no structural objection.

## Alignment with existing patterns

Re-read directly, not assumed from the prior pass:

- **`CatalogMcpTools.cs`** — constructor `(IMediator, ICurrentUserService)` confirmed at line 29;
  `GetProductMargins`'s inline guard confirmed at lines 174–179, byte-identical to the design's quote.
- **`MeetingTasksMcpTools.cs`** — `EnsureReadAccess()` confirmed at lines 184–192; its 4 call sites
  confirmed at lines 52, 91, 130, 160, each as the first statement of its method, before the method's
  own `try`.
- **`KnowledgeBaseTools.cs`** — constructor `(IMediator, ILogger<KnowledgeBaseTools>)` confirmed at
  line 18; both methods' `try { ... } catch (Exception ex) { throw new McpException(...) }` confirmed
  with **no** `catch (McpException)` guard — the design's placement-rule rationale is exactly right.
- **`LeafletTools.cs`** — constructor `(IMediator, ILogger<LeafletTools>)` confirmed at line 18;
  `GenerateLeaflet`'s `catch (McpException) { throw; }` confirmed at lines 57–59, ahead of the generic
  catch — the design's "would survive inside the try, but apply the rule uniformly anyway" reasoning
  holds.
- **`ManufactureOrderMcpTools.cs`**, **`ManufactureBatchMcpTools.cs`**, **`UserManagementMcpTools.cs`**
  — each confirmed single-field `(IMediator _mediator)` constructor, no `ICurrentUserService` present,
  matching the design's "add param + field" instruction exactly.
- **`McpModule.cs`** — the `.WithTools<T>()` chain matches the design's reproduction verbatim, all 7
  classes, no explicit factories.
- **Feature/role mapping** — every controller's class-level `[FeatureAuthorize(Feature.X)]` re-read
  directly: `CatalogController` → `Products_Catalog` (`:22`), `ManufactureOrderController` →
  `Manufacture_ManufactureOrders` (`:19`), `ManufactureBatchController` →
  `Manufacture_BatchPlanning` (`:11`), `KnowledgeBaseController` → `Customer_KnowledgeBase` (`:17`),
  `LeafletController` → `Marketing_Leaflet` (`:19`), `UserManagementController` →
  `Admin_Administration` (`:10`) — all Read level (method-level overrides on these controllers are all
  `AccessLevel.Write`, confirming Read is the correct, and only, level relevant to the Get/Search/Ask/
  Calculate methods MCP wraps). `AccessRoles.generated.cs`'s switch confirms every `(Feature, Read)`
  pair the design cites resolves to the exact role string claimed (e.g.
  `Products_Catalog, Read → "products.catalog.read"`).
- **`FeatureAuthorizeAttribute`** — single-feature constructor confirmed to default
  `AccessLevel.Read` (`:11`), consistent with the design's basis for treating Read as the correct
  level everywhere in scope.
- **`ICurrentUserService.IsInRole(string role)`** — signature confirmed; nothing async, nothing
  requiring `HttpContext` access beyond what `CurrentUserService` already does for the two working
  gated tools.
- **`docs/integrations/mcp-server.md`** — read in full. Confirmed: no `Leaflet` heading exists
  anywhere in the file (grep for `Leaflet` returns zero matches); every other group heading
  (`Catalog (8)`, `Manufacture Orders (3)`, `Manufacture Batch (4)`, `User Management (1)`,
  `Knowledge Base (2)`, `Meeting Notes (4)`) exists exactly as the design's table assumes. The
  "settled, not a hedge" framing for the new Leaflet section is correct.

## Proposed architecture

Unchanged from architecture-01's approval: a single static extension method,
`McpAuthorizationExtensions.EnsureFeatureAccess(this ICurrentUserService, Feature, string
resourceName, AccessLevel level = Read)`, in `Anela.Heblo.API/MCP/McpAuthorizationExtensions.cs`
(confirmed **not yet created** — this is genuinely new code, not a rename of something existing),
called as the first statement of all 18 currently-ungated tool methods plus the 5 migrated call sites.
This remains the right shape: it closes the authorization gap with the minimum structural change,
reuses the exact mechanism already proven in production for 2 of 7 classes, and requires no SDK-level
filter/interceptor, no MVC involvement, and no change to `/mcp`'s `.RequireAuthorization()` mapping.
Nothing found in this pass changes that conclusion.

## Implementation guidance

**Placement rule** — confirmed correct and necessary, not just plausible. Verified directly:
guard call must be the first statement of every method body, before any `try`, in all seven classes.
This is strictly required for `KnowledgeBaseTools` (no `catch (McpException)` guard — a
`[FORBIDDEN]` thrown inside the `try` would be caught by `catch (Exception ex)` and re-wrapped as
`"Failed to search knowledge base: [FORBIDDEN] ..."`), and safe-but-unnecessary for the other six
(either no `try` exists yet, or a `catch (McpException) { throw; }` guard already exists) — apply it
uniformly regardless, exactly as design-02 states.

**DI wiring** — no `McpModule.cs` edit needed. Confirmed the file's `.WithTools<T>()` calls take no
explicit factory; adding a constructor parameter to `ManufactureOrderMcpTools`,
`ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`, `UserManagementMcpTools` is
sufficient, resolved by the container the same way it already resolves `CatalogMcpTools` and
`MeetingTasksMcpTools` in production.

**Test-fixture shape — one correction to design-02's test-design section.** The design states (test
design, point 4): *"Add/adjust the constructor for the five previously-ungated classes' test
fixtures to accept and store a new `Mock<ICurrentUserService>`, matching `CatalogMcpToolsTests`'s
existing constructor shape."* Reading all five fixtures directly:

- `ManufactureOrderMcpToolsTests`, `ManufactureBatchMcpToolsTests`, `UserManagementMcpToolsTests` —
  each **does** have an explicit constructor building a stored `_tools` field (matching
  `CatalogMcpToolsTests`'s shape exactly). The design's instruction applies to these three verbatim.
- `KnowledgeBaseToolsTests`, `LeafletToolsTests` — **do not** have a constructor at all today. Both
  use field initializers (`private readonly Mock<IMediator> _mediator = new();`) plus a private
  `CreateTools()` factory method called fresh inside each `[Fact]`, with no stored `_tools` field.
  There is no existing constructor to "adjust" — implementers must **add** one (or set the default
  stub via a field initializer / at the top of `CreateTools()`), not modify one that matches
  `CatalogMcpToolsTests`'s pattern, because that pattern isn't present in these two files.

This is a documentation nuance, not a design defect — the intended behavior (constructor-level
default-allow stub, test-level override to deny) is unaffected; only the mechanical "which existing
constructor do I edit" instruction is inaccurate for these two files. No structural risk: adding a
constructor to a class that currently has none is a normal, low-risk edit. Flagging so the
development step doesn't go looking for a constructor that isn't there.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Newly-gated method's happy-path test throws `FORBIDDEN` because its fixture never stubs `IsInRole` | Constructor-level default-allow stub per fixture; confirmed compatible with Moq's setup-resolution order in `CatalogMcpToolsTests` (constructor-level exact-string setup for a different role string does not shadow the test-level `It.IsAny<string>() → false` override used by `GetProductMargins_ThrowsMcpException_WhenUserLacksFeature`) |
| Guard placed inside a `try`/generic-`catch` gets re-wrapped, hiding `FORBIDDEN` | Guard always first statement, before any `try` — verified necessary specifically for `KnowledgeBaseTools`, applied uniformly |
| Feature/role mismatch between an MCP tool and its sibling controller | Every mapping re-verified directly against the controller's `[FeatureAuthorize]` attribute in this pass — no divergence found |
| Doc update misses `LeafletTools` because no existing anchor exists | Confirmed no `Leaflet` heading exists — add a new group, not an edit |
| Test-fixture edit for `KnowledgeBaseToolsTests`/`LeafletToolsTests` written as if a matching constructor already exists | Called out above — these two need a constructor **added**, not adjusted; everything else about the stub pattern is unaffected |

## Prerequisites before implementation begins

None outstanding. `McpAuthorizationExtensions.cs` confirmed not to exist yet (clean new-file add, no
conflict). `ICurrentUserService`, `AccessRoles`, `Feature`, `AccessLevel`, `McpException` are already
in use in this exact combination in production code. No new package, no schema change, no MVC/routing
change, no `McpModule.cs` change. Implementation can proceed directly per `plan-02.md`'s sequence,
with the one test-fixture clarification above.
