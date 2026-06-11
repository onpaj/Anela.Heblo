Exploration complete. The placeholder is exactly as described: only the registration line and the placeholder class reference it. Existing test infra (`CompositionRootTests`) already validates `ValidateOnBuild + ValidateScopes` against the prod composition root. The handler test uses `Mock<IManufactureProtocolRenderer>`, not the placeholder. No `docs/` reference to `NotImplementedManufactureProtocolRenderer`. Writing the review now.

```markdown
# Architecture Review: Remove Dead `NotImplementedManufactureProtocolRenderer` DI Placeholder

## Skip Design: true

This is a backend-only DI cleanup. No UI, no new visual surface, no design tokens — pure composition-root hygiene.

## Architectural Fit Assessment

The change is fully aligned with the existing Clean Architecture / Vertical Slice setup and **fixes a latent violation** rather than introducing one.

- `IManufactureProtocolRenderer` (Application) defines the contract.
- `QuestPdfManufactureProtocolRenderer` (API/PDFPrints) owns the QuestPDF dependency. Keeping QuestPDF out of the Application layer is an explicit and correct design choice already noted at `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:151` (`"PDF renderer — lives in API layer because QuestPDF is not a dependency of Application layer"`). This rules out moving the registration into `ManufactureModule`.
- The composition root in the API project is the conventional binding site for infrastructure-bound implementations; `ServiceCollectionExtensions.AddApiServices` already binds the sibling `ISemiproductRecipeRenderer` and other infrastructure adapters there. The Manufacture module registers domain/application services only.
- The current placeholder violates this layering by registering an infrastructure stub from the Application module. Removing it restores the invariant.

Integration points touched by this change: exactly one DI registration line and one (deletable) placeholder class. No public interfaces, no consumers, no HTTP contracts.

The pre-existing `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` (`backend/test/Anela.Heblo.Tests/Infrastructure/CompositionRootTests.cs`) already enforces `ValidateOnBuild=true` + `ValidateScopes=true` against the real API composition root — meaning **the production startup path is already guarded** against a missing `IManufactureProtocolRenderer` registration after this cleanup. This is load-bearing context for FR-4.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application/Features/Manufacture/
├─ UseCases/GetManufactureProtocol/
│  ├─ IManufactureProtocolRenderer.cs       [UNCHANGED — Application-owned contract]
│  ├─ GetManufactureProtocolHandler.cs      [UNCHANGED — depends on the interface only]
│  └─ NotImplementedManufactureProtocolRenderer.cs   [DELETE — dead code]
└─ ManufactureModule.cs                     [EDIT — remove lines 73–74]

Anela.Heblo.API/
├─ PDFPrints/
│  └─ QuestPdfManufactureProtocolRenderer.cs [UNCHANGED — the real implementation]
└─ Extensions/
   └─ ServiceCollectionExtensions.cs        [UNCHANGED — sole registration site, line 152]

Anela.Heblo.Tests/
├─ Features/Manufacture/
│  └─ GetManufactureProtocolHandlerTests.cs [UNCHANGED — uses Mock<IManufactureProtocolRenderer>]
└─ Infrastructure/
   └─ CompositionRootTests.cs               [UNCHANGED — already covers production fail-fast]
```

Post-change DI binding flow for `/api/manufacture-order/{id}/protocol.pdf`:

```
Program.Main
 └─ services.AddApiServices()
     └─ services.AddScoped<IManufactureProtocolRenderer, QuestPdfManufactureProtocolRenderer>()   ← only registration
 └─ services.AddManufactureModule(configuration)
     └─ (no IManufactureProtocolRenderer registration)

GetManufactureProtocolHandler  ──depends on──▶  IManufactureProtocolRenderer
                                                       │
                                                       ▼
                                            QuestPdfManufactureProtocolRenderer (Scoped)
```

### Key Design Decisions

#### Decision 1: Delete the placeholder class outright (not move it to the test project)

**Options considered:**
- (a) Delete `ManufactureModule.cs:73–74` only; leave `NotImplementedManufactureProtocolRenderer.cs` in place.
- (b) Delete both the registration **and** `NotImplementedManufactureProtocolRenderer.cs`.
- (c) Delete the registration, move the placeholder to the test project as a reusable stub fixture.

**Chosen approach:** (b). Delete the file entirely.

**Rationale:** The only reference to `NotImplementedManufactureProtocolRenderer` outside its own definition is the registration line being removed. The single handler test (`GetManufactureProtocolHandlerTests`) uses `Mock<IManufactureProtocolRenderer>` — confirmed at `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs:14`. (a) leaves dead code; (c) introduces a "useful stub" abstraction nobody needs (YAGNI) and would create a test-project file that contradicts its own type's stated purpose. Deletion is cleanest.

#### Decision 2: Do **not** add the FR-4 isolated-host negative test

**Options considered:**
- (a) Add a new test that calls `services.AddManufactureModule()` against an empty `ServiceCollection`, builds the provider with `ValidateOnBuild=true`, and asserts `InvalidOperationException`.
- (b) Rely on the existing `CompositionRootTests` to cover the prod host, and skip the negative-side test.

**Chosen approach:** (b). Treat FR-4 as already satisfied in effect; document the rationale in the spec amendment.

**Rationale:** FR-4 is asking us to write a test that asserts a built-in DI behavior (`ValidateOnBuild` throws on missing bindings). That is a framework guarantee, not a guarantee of our code. Worse, the test would pin a *coincidental* set of dependencies — adding any new dependency anywhere in the Manufacture module would change which type the container complains about first, producing brittle, hard-to-maintain failure modes. The *production-relevant* invariant ("the assembled API container has exactly one valid `IManufactureProtocolRenderer` registration") is already enforced by `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices`. Removing the placeholder is itself the architectural fix; the test does not add value commensurate with the maintenance cost.

If the user/spec owner disagrees and wants the negative test anyway, it should be scoped narrowly — see Specification Amendments below.

#### Decision 3: Do not change registration lifetime

**Options considered:** Preserve `Scoped`; change to `Transient` or `Singleton`.

**Chosen approach:** Preserve `Scoped` (unchanged from `ServiceCollectionExtensions.cs:152`).

**Rationale:** The renderer is invoked synchronously inside an HTTP-request-scoped MediatR handler; `Scoped` matches the handler's lifetime, avoids accidental statefulness across requests, and matches the sibling `ISemiproductRecipeRenderer` registration. NFR-1 (behavior preservation) explicitly forbids lifetime drift.

#### Decision 4: Leave the Application module silent about the requirement (no XML doc / no throwing-factory guard)

**Options considered:**
- (a) Add an XML doc comment on `AddManufactureModule` stating "callers must also register `IManufactureProtocolRenderer`".
- (b) Replace the placeholder with `services.AddScoped<IManufactureProtocolRenderer>(_ => throw new InvalidOperationException(...))` so missing binding surfaces with a friendly message.
- (c) Do nothing; trust DI validation to surface the missing binding.

**Chosen approach:** (c).

**Rationale:** (b) recreates the exact anti-pattern this cleanup is removing — a "valid" registration that throws at request time — defeating the point. (a) couples Application-layer XML docs to API-layer composition decisions and goes stale silently; future host writers won't read the XML doc. (c) is the cleanest signal: missing binding → DI fails at startup via `ValidateOnBuild`, which is already enabled in the prod path's validation test. Brief code comments are not needed.

## Implementation Guidance

### Directory / Module Structure

**Files to delete:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/NotImplementedManufactureProtocolRenderer.cs`

**Files to edit:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — remove lines 73–74 (the comment **and** the `AddScoped` call). No replacement; leave a blank line or close ranks to the preceding `RegisterTile` block.

**Files explicitly NOT to touch:**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/IManufactureProtocolRenderer.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs`
- `backend/src/Anela.Heblo.API/PDFPrints/QuestPdfManufactureProtocolRenderer.cs`
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (line 152 is the keep-as-is registration)
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureProtocolHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Infrastructure/CompositionRootTests.cs`
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

### Interfaces and Contracts

No interface changes.
- `IManufactureProtocolRenderer` (Application) — unchanged signature, unchanged location.
- `AddManufactureModule(this IServiceCollection, IConfiguration)` — unchanged signature, body shrinks by two lines (one comment + one registration).
- HTTP contract `/api/manufacture-order/{id}/protocol.pdf` — unchanged.

**Implicit contract for hosts:** any future host that calls `AddManufactureModule()` must also register an `IManufactureProtocolRenderer`. We rely on DI startup validation (already enabled in `CompositionRootTests`) to surface a missing registration as `InvalidOperationException` at provider build, not as `NotImplementedException` at HTTP request time. This is intentionally left implicit — see Decision 4.

### Data Flow

For `GET /api/manufacture-order/{id}/protocol.pdf` (unchanged from current production behavior):

```
HTTP request → MediatR dispatch → GetManufactureProtocolHandler.Handle
  ├─ IManufactureOrderRepository.GetOrderByIdAsync
  ├─ IManufactureClient.GetErpDocumentItemsAsync   (per FlexiDoc code)
  ├─ Build ManufactureProtocolData
  └─ IManufactureProtocolRenderer.Render(data) → QuestPdfManufactureProtocolRenderer
                                                  → byte[] PDF
```

Before this change, the placeholder was instantiated first by `ManufactureModule.AddManufactureModule`, then the binding was overwritten by `AddApiServices` (the last registration wins in `IServiceCollection`). After this change, only the QuestPDF registration exists — same runtime outcome, simpler graph.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A non-API host (test, worker, alternate composition root) silently relied on the placeholder | Low | Codebase grep confirms only the production API host registers Manufacture services. `CompositionRootTests` exercises the prod host. Handler unit tests use `Mock<IManufactureProtocolRenderer>` and do not call `AddManufactureModule`. |
| Removing the registration changes effective binding order in some edge case | Low | "Last registration wins" semantics of `IServiceCollection`: removing a now-shadowed earlier registration cannot change the resolved type. `CompositionRootTests` re-verifies after the change. |
| Deleting the placeholder file breaks a hidden internal type reference | Low | Class is `internal sealed`; cross-assembly references are impossible. Only intra-assembly references exist (none after registration is removed). Compiler will surface any miss. |
| Future contributor adds a new module/host that registers Manufacture but forgets the renderer | Low (and intentional) | This is exactly the failure mode the cleanup wants to surface. DI validation will throw `InvalidOperationException` at provider build — the desired fail-fast behavior. |
| `dotnet format` re-emits whitespace diffs on `ManufactureModule.cs` | Low | NFR-2 requires `dotnet format` to produce no diff on the affected files. After the edit, run `dotnet format` and stage the formatted result. |
| Spec's FR-4 is interpreted as mandatory "write a new test", contradicting Decision 2 | Medium | See Specification Amendments below — restate FR-4 as already-satisfied by the existing `CompositionRootTests` rather than mandating a new test. |
| Hidden documentation reference outside `docs/` | Low | `grep` of the worktree found `NotImplementedManufactureProtocolRenderer` only in the two source files. No README, ADR, or feature spec mentions it. FR-5 is essentially satisfied. |

## Specification Amendments

1. **FR-4 (validate behavior under missing registration) — restate as satisfied by existing infrastructure, not as a new test.**
   The spec asks for "an isolated test host that calls `AddManufactureModule()` but does not register `QuestPdfManufactureProtocolRenderer`" and asserts an `InvalidOperationException` from DI. Per Decision 2, this test tests a framework guarantee, is brittle to unrelated dependency changes in the Manufacture module, and duplicates the production-side invariant already covered by `backend/test/Anela.Heblo.Tests/Infrastructure/CompositionRootTests.cs`. **Amend FR-4 to:** "The production API host's container validation (already enforced by `CompositionRootTests.ServiceContainer_ValidateOnBuild_NoLifetimeMismatchesOrUnresolvableServices` with `ValidateOnBuild=true` + `ValidateScopes=true`) must continue to pass after the cleanup. No new negative test is required; the cleanup itself shifts failure surface from request time to startup time via this existing gate."

2. **FR-2 (delete the class if unused) — make the deletion unconditional.**
   Active exploration confirms the class is referenced only by the registration line being removed (tests use `Mock<IManufactureProtocolRenderer>`). The conditional ("if tests reference it as a stub fixture, leave the class but move it") is dead-on-arrival here. **Amend FR-2 to:** "Delete `NotImplementedManufactureProtocolRenderer.cs` entirely after FR-1. No test-project relocation is required."

3. **FR-5 (documentation cleanup) — narrow the scope.**
   `grep` of `docs/` produced zero matches for `NotImplementedManufactureProtocolRenderer`. The remaining "Phase 6" references in `docs/` belong to *other* features (RAG, terminal scan, gift package, async stockup, etc.) and are out of scope. **Amend FR-5 to:** "Remove the inline comment on `ManufactureModule.cs:73` and the XML `<summary>` on the deleted placeholder file. No documentation under `docs/` requires changes (verified by repository-wide grep)."

4. **NFR-2 wording — clarify expected scope of test runs.**
   The wording "All existing unit, integration, and module-boundary tests must pass" should explicitly call out `CompositionRootTests` and `GetManufactureProtocolHandlerTests` as the critical gates, since they validate the two behavioral invariants of this change (prod fail-fast + handler behavior). **No semantic change; clarification only.**

## Prerequisites

None. This is a self-contained dead-code removal.

- No migrations.
- No new configuration keys.
- No new infrastructure (Azure resources, secrets, Key Vault entries) needed.
- No package additions or version bumps.
- No `feature-flag` gating — change is unconditionally safe per Decision 4.
- Branch is already on a clean tree (per `git status` at session start). The two-line edit + one file delete can be made in a single commit. Validation gates from `CLAUDE.md` (`dotnet build` + `dotnet format` + relevant tests) close the loop.
```