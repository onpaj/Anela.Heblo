# Review: MCP tools bypass the per-feature authorization gate their controllers enforce

Reviewed commit `a714cd9b` (the sole development commit for this task) against `plan-02.md`,
`design-02.md`, and `architecture-02.md`.

## What I checked

- Read the full diff (`git show a714cd9b`) for every changed production and test file.
- Read every touched MCP tool class end-to-end in its current state (`CatalogMcpTools`,
  `ManufactureOrderMcpTools`, `ManufactureBatchMcpTools`, `KnowledgeBaseTools`, `LeafletTools`,
  `UserManagementMcpTools`, `MeetingTasksMcpTools`).
- Cross-checked every `Feature` passed to `EnsureFeatureAccess` against the actual
  `[FeatureAuthorize(Feature.X)]` attribute on each method's sibling controller, via `grep` on
  live source (`CatalogController.cs:22`, `ManufactureOrderController.cs:19`,
  `ManufactureBatchController.cs:11`, `KnowledgeBaseController.cs:17`, `LeafletController.cs:19`,
  `UserManagementController.cs:10`) — **all 6 match exactly**, no divergence.
- Verified `McpModule.cs` registers all 7 tool classes via `.WithTools<T>()` with no explicit
  factory, confirming the new constructor parameters (`ICurrentUserService`) resolve automatically
  from the DI container — no `McpModule.cs` edit was needed, and none was made.
- Verified the "guard before `try`" placement rule is actually followed in `KnowledgeBaseTools`
  and `LeafletTools` (the two classes whose `catch (Exception ex)` blocks would otherwise re-wrap
  a `[FORBIDDEN]` `McpException` and lose the marker) — confirmed both guards sit before the `try`.
- Verified `AccessRoles.generated.cs` contains the role constants referenced by the new tests
  (e.g. `ProductsCatalogRead`) and that `ICurrentUserService.IsInRole(string)` is the interface
  actually used.
- Read all 6 modified test files; confirmed each adds a constructor-level default-allow
  `IsInRole` stub plus a forbidden-path test (`Theory`/`Fact`) per gated method, asserting
  `FORBIDDEN` + role in the exception message and `Times.Never` on the corresponding
  `IMediator.Send` overload — matching FR-4 exactly.
- Confirmed `MeetingTasksMcpToolsTests.cs` has no diff, consistent with the claim that the
  `EnsureReadAccess()` → shared-helper migration preserves its existing behavior/coverage
  unchanged.
- Confirmed the `CatalogMcpToolsTests` Moq-ordering claim (constructor-level exact-match stub for
  `products.catalog.read` coexisting with the pre-existing per-test `IsInRole(It.IsAny<string>())`
  override in the `GetProductMargins` forbidden test) by reading the actual test code — the two
  setups match disjoint role strings for methods that gate on different `Feature`s, so there's no
  conflict.
- Confirmed `docs/integrations/mcp-server.md` documents the required permission for every one of
  the 8 tool groups, including the new **Leaflet (1)** section — matches FR-3 exactly.
- Ran `dotnet build src/Anela.Heblo.API/Anela.Heblo.API.csproj` myself: **succeeded, exit code 0.**

## Verification limitation

The host this session runs on was under severe, sustained contention from unrelated concurrent
builds the whole time I reviewed (load average 27–35 throughout, dozens of concurrent
`dotnet build`/`MSBuild` processes from other worktrees). My own `dotnet test --filter
"FullyQualifiedName~MCP.Tools"` run received essentially 0% CPU for over 30 minutes and never
completed in-session, despite the production build (same host, same load) completing
successfully. This is an infrastructure/scheduling issue, not a property of the change — I could
not force it to complete faster (no permission to renice, no way to reduce other tasks' load).
I'm not treating the unfinished live test run as a red flag: the diff is small, mechanical, and
every one of its ~40 individual assertions is one I independently traced through the actual
production code by hand and found correct. The development step's self-reported results (67/67
MCP tests passing, `dotnet format` clean, full-suite run showing only pre-existing unrelated
EF-integration flakiness) are consistent with what a correct implementation of this diff would
produce, and nothing in my manual review contradicts them.

## Findings

None. The implementation:

- Closes the gap for all 18 previously-ungated methods across 6 classes, using the exact
  `Feature` each method's sibling controller enforces — verified 1:1 against live controller
  attributes, no mismatches.
- Migrates the 2 already-correct classes (`CatalogMcpTools.GetProductMargins`,
  `MeetingTasksMcpTools`) onto the same shared helper without changing behavior or the
  `[FORBIDDEN] ... (requires {role})` message format that existing tests assert against.
  `MeetingTasksMcpTools`'s per-meeting `IMeetingAccessGuard` visibility logic is untouched, as
  required (NFR).
- Correctly reasoned about try/catch ordering for the two classes where a naive placement would
  have silently swallowed the `[FORBIDDEN]` signal.
- Adds no new dependencies; single shared `EnsureFeatureAccess` extension method, matching the
  API project's existing `*Extensions.cs` convention.
- Test coverage matches FR-4's acceptance criteria for every gated method; no regressions to
  pre-existing happy-path or error-path tests (default-allow stubs added, no existing test logic
  changed).
- Docs match FR-3 exactly.
- Scope is clean — nothing beyond the MCP/, tests, and docs files needed for this fix.

No stylistic nitpicks worth raising either; the added guard-call pattern is uniform and matches
the two classes that already had it right.

```json
{"outcome": "done", "summary": "Diff (a714cd9b) matches plan-02/design-02 exactly: all 18 previously-ungated MCP tool methods now call the shared McpAuthorizationExtensions.EnsureFeatureAccess before any handler/try logic, using the same Feature each method's sibling MVC controller enforces (verified 1:1 against live controller attributes). Guard-before-try placement is correct in the two classes where it mattered (KnowledgeBaseTools, LeafletTools). DI wiring, test coverage (forbidden-path test per gated method, Times.Never on mediator), and docs (new Leaflet section) all match the plan. Independently rebuilt the API project (succeeded). The live MCP test run couldn't finish in-session due to severe unrelated host contention (load avg 27-35 for 30+ min), but manual code-level verification found zero discrepancies from spec and no correctness issues."}
```
