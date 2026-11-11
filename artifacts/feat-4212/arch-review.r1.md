# Architecture Review: Decouple Manufacture confirmation workflows from UpdateManufactureOrder response DTO

## Skip Design: true
Pure backend refactor inside the Manufacture module: two internal interfaces change their parameter type from a use-case-scoped DTO to the existing domain entity, and two workflow services gain a repository dependency. No new or changed UI components, screens, endpoints, or visual design decisions.

## Architectural Fit Assessment
This aligns cleanly with `docs/architecture/development_guidelines.md`'s Contracts and DTOs rules: DTOs are use-case-scoped, never shared/global, and modules communicate through explicit contracts — not through leaking one use case's response shape into unrelated internal services. `UpdateManufactureOrderDto` is exactly the kind of thing that section forbids treating as a general carrier ("DTOs are never shared or global"; the API/response layer "never owns" internal business logic's data needs). The fix restores the intended boundary rather than introducing a new one.

The change is entirely intra-module (Manufacture only) — confirmed by repo-wide search: `IResidueDistributionCalculator` and `IManufactureNameBuilder` have no consumers outside `ConfirmProductCompletionWorkflow`, `ConfirmSemiProductManufactureWorkflow`, and their four unit test files. No cross-module contract, no `Contracts/` folder, no ADR-004-style repository-registration concern is implicated: `IManufactureOrderRepository` is already registered in `ManufactureModule.cs` and already consumed by `UpdateManufactureOrderHandler` in the same module — the two workflows simply become additional consumers of an existing, already-scoped repository interface. No new DI registration is required.

`ManufactureOrder` (the domain entity) is a plain class already used as the aggregate root throughout the module (`UpdateManufactureOrderHandler`, `ManufactureOrderRepository`, etc.) — reusing it as the internal data carrier does not create a new type, does not touch the OpenAPI surface, and is not itself a "DTO" under the CLAUDE.md class-vs-record rule (that rule governs `Request`/`Response` contracts; `ManufactureOrder` is, and remains, an internal domain entity, never serialized to a client contract). No new class/record decision is introduced by this fix.

## Proposed Architecture

### Component Overview
```
Before:
  ConfirmProductCompletionWorkflow
    --send--> UpdateManufactureOrderRequest (mediator)
    <--response-- UpdateManufactureOrderResponse { Order: UpdateManufactureOrderDto }
                                                        |
                     +----------------------------------+----------------------------+
                     v                                  v                            v
        IResidueDistributionCalculator      SubmitToErpAsync(order: Dto)   UpdateBoMIngredientsAsync(order: Dto)
             .CalculateAsync(Dto)

After:
  ConfirmProductCompletionWorkflow
    --send--> UpdateManufactureOrderRequest (mediator)     [persists change, response used only for Success/ErrorCode]
    --read--> IManufactureOrderRepository.GetOrderByIdAsync(orderId)  -->  ManufactureOrder (domain entity)
                                                        |
                     +----------------------------------+----------------------------+
                     v                                  v                            v
        IResidueDistributionCalculator      SubmitToErpAsync(order: ManufactureOrder)  UpdateBoMIngredientsAsync(order: ManufactureOrder)
             .CalculateAsync(ManufactureOrder)

  ConfirmSemiProductManufactureWorkflow — same shape, one fewer branch (no residue calc / BoM step).
```
`UpdateManufactureOrderHandler` is untouched: it still builds and returns `UpdateManufactureOrderDto` to its own (HTTP) callers exactly as before. The workflows simply stop being one of those callers for internal-logic purposes.

### Key Design Decisions

#### Decision 1: Re-fetch via repository vs. widen the mediator response
**Options considered:**
- (a) Add the domain entity to `UpdateManufactureOrderResponse` alongside the DTO.
- (b) Have the workflow call `IManufactureOrderRepository.GetOrderByIdAsync` directly after the update, as the issue suggests.
- (c) Introduce a third, workflow-owned projection type distinct from both the DTO and the entity.

**Chosen approach:** (b), matching the issue's suggested fix exactly.

**Rationale:** (a) keeps `UpdateManufactureOrderResponse` — an HTTP contract — carrying a domain entity, which is the same class of coupling in the opposite direction and would leak `ManufactureOrder` into the use-case's public response shape. (c) adds a type with no independent reason to exist; the domain entity already has every field the workflows need, so a third projection is pure ceremony. (b) is the smallest change that removes the coupling: it uses an interface (`IManufactureOrderRepository`) already registered in this module and already proven to load the same order inside `UpdateManufactureOrderHandler.Handle` in the same request. It also matches the existing idiom elsewhere in the module (handlers already fetch-then-mutate-then-persist through this repository).

#### Decision 2: Change `IResidueDistributionCalculator` / `IManufactureNameBuilder` in place vs. add an overload
**Options considered:**
- (a) Add a second `CalculateAsync(ManufactureOrder, ...)` / `Build(ManufactureOrder, ...)` overload, deprecate the DTO-typed one.
- (b) Replace the DTO-typed signature outright.

**Chosen approach:** (b).

**Rationale:** Both interfaces have exactly one production call site each (the two workflows), confirmed by search. Keeping a DTO-typed overload "for compatibility" would leave dead code with no caller and re-introduce the exact ambiguity this fix removes — a future author could reach for the wrong overload. A clean signature replacement is safer and matches "surgical, no unnecessary options" guidance; the four existing unit test files for these two interfaces are updated in the same change (not incidental — they are load-bearing regression coverage per NFR-3 in the spec).

#### Decision 3: Null handling when the repository read fails after a successful update
**Options considered:**
- (a) Let a `NullReferenceException` propagate (fails loudly but ungracefully mid-workflow, after the persist already happened).
- (b) Treat `GetOrderByIdAsync` returning `null` as an error, mapped to the same result path the workflow already uses for a failed update.

**Chosen approach:** (b).

**Rationale:** `GetOrderByIdAsync` returns `ManufactureOrder?` by contract, so the compiler forces a null check regardless (with nullable reference types enabled, `order.SemiProduct` on a possibly-null `order` is a warning/error). This case should be practically unreachable (the same order ID was just successfully updated a moment earlier in the same request), so it is defensive, not a new business rule — reuse the existing `ProductQuantityUpdateErrorFormat` / equivalent error path with `ErrorCodes.ResourceNotFound` (as the spec's illustrative snippet shows) rather than inventing a new error code or message.

## Implementation Guidance

### Directory / Module Structure
No new files or folders. All changes are edits to existing files, all inside `backend/src/Anela.Heblo.Application/Features/Manufacture/`:

- `Services/Workflows/ConfirmProductCompletionWorkflow.cs` — add `IManufactureOrderRepository` constructor param; replace `updateResult.Order!` reads with a `GetOrderByIdAsync` call and null-guard; update `SubmitToErpAsync` / `UpdateBoMIngredientsAsync` parameter types to `ManufactureOrder`.
- `Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` — same shape of change (no BoM/residue step, so only `SubmitToErpAsync`'s parameter type and the fetch call are affected).
- `Services/IResidueDistributionCalculator.cs` and `Services/ResidueDistributionCalculator.cs` — change `CalculateAsync`'s parameter type from `UpdateManufactureOrderDto` to `Anela.Heblo.Domain.Features.Manufacture.ManufactureOrder`; update field reads if property names differ in nullability annotations (they don't for the fields read today — verify during implementation, not just from this review).
- `Services/Workflows/ManufactureNameBuilder.cs` — same signature change for `IManufactureNameBuilder.Build`.

Test files (all under `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/`, mirroring the above):
- `Workflows/ConfirmProductCompletionWorkflowTests.cs`
- `Workflows/ConfirmSemiProductManufactureWorkflowTests.cs`
- `Workflows/ManufactureNameBuilderTests.cs`
- `ResidueDistributionCalculatorTests.cs`

`Application/Features/Manufacture/UseCases/UpdateManufactureOrder/*` — **no edits**. Do not touch `UpdateManufactureOrderDto.cs`, `UpdateManufactureOrderResponse.cs`, or `UpdateManufactureOrderHandler.cs`. This is the boundary the fix restores; touching these files would be scope creep and risks the HTTP contract / OpenAPI client (see CLAUDE.md's DTO-as-class rule and `docs/development/api-client-generation.md` — any shape change there regenerates the TypeScript client and is out of scope for this issue).

### Interfaces and Contracts
```csharp
// Domain/Features/Manufacture/IManufactureOrderRepository.cs — UNCHANGED, just a new consumer
Task<ManufactureOrder?> GetOrderByIdAsync(int id, CancellationToken cancellationToken = default);

// Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs — CHANGED
Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);

// Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs — CHANGED
string Build(ManufactureOrder order, ErpManufactureType type);
```
Both workflow constructors add:
```csharp
private readonly IManufactureOrderRepository _repository;
```
No change to `IConfirmProductCompletionWorkflow` / `IConfirmSemiProductManufactureWorkflow` public method signatures (`ExecuteAsync(...)` is unaffected — the DTO never appeared in these interfaces' own signatures, only inside their implementations' private helpers).

### Data Flow
1. Controller/API caller → `IConfirmProductCompletionWorkflow.ExecuteAsync` / `IConfirmSemiProductManufactureWorkflow.ExecuteAsync` (unchanged entry point).
2. Workflow sends `UpdateManufactureOrderRequest` via `IMediator` → `UpdateManufactureOrderHandler` loads, mutates, persists `ManufactureOrder` via `IManufactureOrderRepository`, maps to `UpdateManufactureOrderDto`, returns `UpdateManufactureOrderResponse`. Workflow now uses this response **only** for `.Success` / `.ErrorCode`.
3. Workflow calls `IManufactureOrderRepository.GetOrderByIdAsync(orderId, ct)` directly → gets the freshly-persisted `ManufactureOrder`.
4. Workflow passes that `ManufactureOrder` into `IResidueDistributionCalculator.CalculateAsync`, `IManufactureNameBuilder.Build`, `SubmitToErpAsync`, `UpdateBoMIngredientsAsync` — all business-logic steps, now reading domain data directly instead of through the HTTP-response DTO.
5. Remaining steps (`SubmitManufactureRequest`, `UpdateManufactureOrderStatusRequest` via mediator) are unaffected by this change.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Extra `GetOrderByIdAsync` round-trip reads a slightly different snapshot than what `UpdateManufactureOrderHandler` just wrote, if EF Core doesn't flush/track consistently within the same request scope | Low | Same `DbContext`/scope as the handler (single `ApplicationDbContext`, ADR-001, scoped DI) within one HTTP request — the read will see the just-committed write. Cover with an integration test if one doesn't already exist for these workflows; unit tests with mocked repository won't catch this class of issue. |
| `GetOrderByIdAsync` returns `null` on the re-fetch despite a successful update (should be unreachable, but the signature is nullable) | Low | Explicit null-check mapped to the existing failure result path, per Decision 3 — no silent NRE. |
| Property-level drift between `UpdateManufactureOrderDto` and `ManufactureOrder` (e.g. a field present on one but not the other, or differing nullability) causes a compile error or subtle behavior change when switching the four call sites | Low | Verified in this review: `SemiProduct`, `Products`, `ManufactureType`, `OrderNumber` — the only fields read by `SubmitToErpAsync`/`CalculateAsync`/`Build` today — exist on `ManufactureOrder` with matching nullability (`SemiProduct` is `ManufactureOrderSemiProduct?` on both sides). Developer must still diff each read site during implementation, not just trust this review. |
| Test rewrites (4 files) understate coverage if mocks are set up loosely (e.g. `It.IsAny<ManufactureOrder>()` instead of asserting on real field values) | Medium | Spec's FR-1–FR-4 acceptance criteria explicitly require the rewritten tests to construct a real `ManufactureOrder` with the same field values the old DTO-based fixtures used, and to keep the existing Verify/Assert assertions — this is a like-for-like test port, not a coverage reduction. Code review should check for this specifically. |
| Scope creep into `UpdateManufactureOrderDto`/handler while "in the area" | Low | Explicitly called out as out-of-scope in both the spec and this review; enforced by review, not by tooling. |

## Specification Amendments
None. The spec (`spec.r1.md`) already correctly scopes the fix to the two workflows plus the two shared helper interfaces, correctly identifies `ManufactureOrder` as the replacement type, and correctly excludes the `UpdateManufactureOrder` use case itself from changes. This review's only additions are: (1) explicit confirmation that no cross-module contract or DI-registration rule from `development_guidelines.md` is implicated, (2) the decision record for *why* re-fetch-via-repository beats the two alternatives, and (3) the field-level nullability check in the Risks table — the planner should turn that into an explicit "diff each read site" checklist item per task rather than assuming it from this review alone.

## Prerequisites
None. No migrations, no config, no new infrastructure, no new DI registrations — `IManufactureOrderRepository` is already registered in `ManufactureModule.cs` (`services.AddScoped<IManufactureOrderRepository, ManufactureOrderRepository>();`). Implementation can start immediately from `spec.r1.md` and this review.
