# Specification: Decouple Manufacture confirmation workflows from UpdateManufactureOrder response DTO

## Summary
`ConfirmProductCompletionWorkflow` and `ConfirmSemiProductManufactureWorkflow` currently pass `UpdateManufactureOrderDto` — the HTTP response shape of the `UpdateManufactureOrder` use case — into internal business-logic helpers (`SubmitToErpAsync`, `IResidueDistributionCalculator.CalculateAsync`, `IManufactureNameBuilder.Build`, and BoM-ingredient updates). This couples domain workflow logic to an API response contract that is free to change shape for presentation reasons. This spec replaces that DTO with the `ManufactureOrder` domain entity, fetched directly from `IManufactureOrderRepository` after the update use case persists its change.

## Background
Both workflows follow the same pattern: they call `_mediator.Send(new UpdateManufactureOrderRequest {...})`, which returns `UpdateManufactureOrderResponse.Order` (an `UpdateManufactureOrderDto`). That DTO is then threaded through several private helper methods that read `SemiProduct`, `Products`, `ManufactureType`, and `OrderNumber` to make ERP-submission and residue/BoM decisions.

`UpdateManufactureOrderDto` lives in `Application/Features/Manufacture/UseCases/UpdateManufactureOrder/` and is built by `UpdateManufactureOrderHandler.MapToDto(ManufactureOrder order)` purely to shape the HTTP response for that use case's own callers (e.g. an MVC controller). It is not meant to be a general-purpose internal data carrier.

Investigation of the current code found the coupling is **broader than the two call sites named in the issue**: the DTO also flows into `IResidueDistributionCalculator.CalculateAsync(UpdateManufactureOrderDto, ...)` and `IManufactureNameBuilder.Build(UpdateManufactureOrderDto, ...)`. Both interfaces are defined and consumed only inside the Manufacture module (confirmed via repo-wide search — no other production callers, only these two workflows and their unit tests), so both interfaces are in scope for this change alongside the two workflow classes.

`UpdateManufactureOrderHandler` already obtains the domain entity itself: it loads it via `_repository.GetOrderByIdAsync`, mutates it, persists it via `_repository.UpdateOrderAsync`, and only then maps it to the DTO for the response. The domain entity `ManufactureOrder` already carries every field the workflows currently read off the DTO (`SemiProduct`, `Products`, `ManufactureType`, `OrderNumber`), so no new domain data needs to be introduced — only the type flowing through the workflow's internal call chain changes.

Per `docs/architecture/development_guidelines.md` and the repo's `CLAUDE.md`, DTOs must be classes (never C# records — OpenAPI client generators mishandle record parameter order), and any module-boundary/DTO change must consult that guide before implementation. This spec keeps `UpdateManufactureOrderDto` itself unchanged in shape and ownership; it only stops two internal workflow services from depending on it. `ManufactureOrder` is an internal domain entity (already a class), never serialized to an OpenAPI contract, so the "DTOs are classes" rule is not itself in tension with this change — it is called out here because the architect/designer must confirm no part of this refactor turns `ManufactureOrder`, or any new type introduced, into something exposed over HTTP as a record.

## Functional Requirements

### FR-1: `ConfirmProductCompletionWorkflow` reads the domain entity from the repository, not the update response DTO
After `_mediator.Send(new UpdateManufactureOrderRequest {...})` persists the product-quantity update, the workflow must fetch the current `ManufactureOrder` domain entity via `IManufactureOrderRepository.GetOrderByIdAsync(orderId, cancellationToken)` and use that entity — not `UpdateManufactureOrderResponse.Order` — for every subsequent business-logic step: `IResidueDistributionCalculator.CalculateAsync`, `SubmitToErpAsync` (ERP item selection, direct-output totals, manufacture naming), and `UpdateBoMIngredientsAsync`.

**Acceptance criteria:**
- `ConfirmProductCompletionWorkflow` takes `IManufactureOrderRepository` as a new constructor dependency.
- The workflow no longer reads `updateResult.Order` for business logic; `UpdateManufactureOrderResponse` is used only to check `.Success` / `.ErrorCode` for the "did the update persist" branch.
- If `GetOrderByIdAsync` returns `null` after a successful update (should not happen in practice, but the method's signature is nullable), the workflow returns the same class of error it currently returns for a failed update (do not let a `NullReferenceException` propagate).
- All existing behavior (residue-threshold confirmation gate, ERP submission fields, BoM ingredient updates, status transition notes) is preserved byte-for-byte — this is a refactor of the *data source*, not the business rules.
- Existing unit tests in `ConfirmProductCompletionWorkflowTests.cs` are updated to mock `IManufactureOrderRepository.GetOrderByIdAsync` returning a `ManufactureOrder` domain object (instead of relying on `UpdateManufactureOrderResponse.Order`'s DTO shape) and continue to pass.

### FR-2: `ConfirmSemiProductManufactureWorkflow` reads the domain entity from the repository, not the update response DTO
Same change, applied to the semi-product confirmation workflow: after `_mediator.Send(new UpdateManufactureOrderRequest {...})` for the semi-product quantity update, fetch `ManufactureOrder` via `IManufactureOrderRepository.GetOrderByIdAsync` and pass it into `SubmitToErpAsync` (via `IManufactureNameBuilder.Build` and the ERP item construction) instead of `updateResult.Order`.

**Acceptance criteria:**
- `ConfirmSemiProductManufactureWorkflow` takes `IManufactureOrderRepository` as a new constructor dependency.
- Same null-after-successful-update handling as FR-1.
- Existing behavior preserved byte-for-byte.
- Existing unit tests in `ConfirmSemiProductManufactureWorkflowTests.cs` updated accordingly and continue to pass.

### FR-3: `IResidueDistributionCalculator` operates on the domain entity
`IResidueDistributionCalculator.CalculateAsync` currently accepts `UpdateManufactureOrderDto order`. Change its signature to accept `ManufactureOrder order` (the domain entity), and update `ResidueDistributionCalculator`'s implementation to read the equivalent fields off the domain type. This interface has no other production consumers.

**Acceptance criteria:**
- `IResidueDistributionCalculator.CalculateAsync(ManufactureOrder order, CancellationToken ct)` replaces the DTO-typed overload (no dual overload kept — there is exactly one call site).
- `ResidueDistributionCalculator`'s existing unit tests are updated to construct/pass a `ManufactureOrder` instead of `UpdateManufactureOrderDto` and continue to pass, with identical calculated results for equivalent input data.
- The calculation logic itself is unchanged — only the input type.

### FR-4: `IManufactureNameBuilder` operates on the domain entity
`IManufactureNameBuilder.Build` currently accepts `UpdateManufactureOrderDto order`. Change its signature to accept `ManufactureOrder order`. This interface has no other production consumers.

**Acceptance criteria:**
- `IManufactureNameBuilder.Build(ManufactureOrder order, ErpManufactureType type)` replaces the DTO-typed overload.
- `ManufactureNameBuilder`'s existing unit tests are updated to construct/pass a `ManufactureOrder` and continue to pass, with identical naming output for equivalent input data.
- Naming logic itself is unchanged — only the input type.

### FR-5: `UpdateManufactureOrderDto` and the `UpdateManufactureOrder` use case are otherwise untouched
`UpdateManufactureOrderDto`, `UpdateManufactureOrderResponse`, `UpdateManufactureOrderHandler`, and the HTTP contract of the `UpdateManufactureOrder` use case are not to change shape, naming, or nullability as part of this fix. The use case keeps returning the DTO to its own (HTTP) callers exactly as before; this fix only removes the two workflow services (and the two shared helper interfaces) as *internal* consumers of that DTO.

**Acceptance criteria:**
- No changes to `UpdateManufactureOrderDto.cs`, `UpdateManufactureOrderResponse.cs`, `UpdateManufactureOrderProductDto.cs`, `UpdateManufactureOrderSemiProductDto.cs`, `UpdateManufactureOrderNoteDto.cs`, or `UpdateManufactureOrderHandler.cs`'s public shape.
- Any existing controller/API consumer of `UpdateManufactureOrderResponse` (outside the two workflows) is unaffected.

## Non-Functional Requirements

### NFR-1: Performance
The change adds exactly one additional `IManufactureOrderRepository.GetOrderByIdAsync` read per workflow execution (a single-order lookup by primary key, already used elsewhere in the module, e.g. inside `UpdateManufactureOrderHandler` itself). This is a low-cardinality, already-indexed lookup; no measurable latency or throughput regression is expected for the confirmation flows (these are already multi-step, multi-mediator-call workflows, not hot paths).

### NFR-2: Security
No change. No new data is exposed; no authorization boundary changes. The domain entity carries the same information as the DTO already did — this is strictly an internal data-shape change with no external contract impact.

### NFR-3: Testability / regression protection
This fix directly addresses a testability gap named in the issue: workflow unit tests currently mock `IMediator` and are therefore blind to structural changes in `UpdateManufactureOrderDto`. After this change, the workflow tests must mock `IManufactureOrderRepository.GetOrderByIdAsync` to supply the domain entity, which:
- Makes the workflow's dependency on the *domain* shape explicit and independently testable.
- Ensures a future structural change to `UpdateManufactureOrderDto` (an HTTP-boundary concern) can no longer silently break these workflows, because the workflows no longer reference that type in their internal logic path.

## Data Model
No schema or persistence changes. Relevant existing types (unchanged in shape, only in who consumes them):

- **`ManufactureOrder`** (domain entity, `Domain/Features/Manufacture/ManufactureOrder.cs`) — already carries `Id`, `OrderNumber`, `ManufactureType`, `SemiProduct` (`ManufactureOrderSemiProduct?`), `Products` (`List<ManufactureOrderProduct>`), `Notes`, state fields. This becomes the type both workflows and both helper interfaces (`IResidueDistributionCalculator`, `IManufactureNameBuilder`) operate on internally.
- **`UpdateManufactureOrderDto`** (`Application/Features/Manufacture/UseCases/UpdateManufactureOrder/UpdateManufactureOrderDto.cs`) — unchanged; remains the HTTP response shape for the `UpdateManufactureOrder` use case only.
- **`IManufactureOrderRepository`** (`Domain/Features/Manufacture/IManufactureOrderRepository.cs`) — unchanged interface; `GetOrderByIdAsync(int id, CancellationToken)` is the method both workflows will now call directly (it is already used by `UpdateManufactureOrderHandler` for the same order).

## API / Interface Design

Signature changes (internal, non-HTTP-facing interfaces only):

```csharp
// Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs
public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
    // was: CalculateAsync(UpdateManufactureOrderDto order, ...)
}

// Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs
public interface IManufactureNameBuilder
{
    string Build(ManufactureOrder order, ErpManufactureType type);
    // was: Build(UpdateManufactureOrderDto order, ErpManufactureType type)
}
```

Both `ConfirmProductCompletionWorkflow` and `ConfirmSemiProductManufactureWorkflow` gain a new constructor parameter:

```csharp
private readonly IManufactureOrderRepository _repository;
// constructor: ..., IManufactureOrderRepository repository, ...
```

Illustrative flow (`ConfirmProductCompletionWorkflow.ExecuteAsync`, mirrors the issue's suggested fix):

```csharp
var updateResult = await UpdateProductsQuantityAsync(orderId, productActualQuantities, cancellationToken);
if (!updateResult.Success) { /* unchanged error handling */ }

var order = await _repository.GetOrderByIdAsync(orderId, cancellationToken);
if (order == null)
{
    // treat as the same class of failure as a failed update — see FR-1 acceptance criteria
    return new ConfirmProductCompletionResult(
        string.Format(ManufactureMessages.ProductQuantityUpdateErrorFormat, ErrorCodes.ResourceNotFound));
}

var distribution = await _residueCalculator.CalculateAsync(order, cancellationToken);
...
var submitResult = await SubmitToErpAsync(orderId, order, distribution, cancellationToken);
```

`SubmitToErpAsync`, `UpdateBoMIngredientsAsync` (in `ConfirmProductCompletionWorkflow`) and `SubmitToErpAsync` (in `ConfirmSemiProductManufactureWorkflow`) change their `order` parameter type from `UpdateManufactureOrderDto` to `ManufactureOrder`; their bodies read the same property names (`order.SemiProduct`, `order.Products`, `order.ManufactureType`, `order.OrderNumber`) since both types expose them identically.

No public HTTP endpoint, request/response contract, or OpenAPI-generated client changes as a result of this fix.

## Dependencies
- `IManufactureOrderRepository` — already registered in DI (`ManufactureModule.cs`) and already injected into `UpdateManufactureOrderHandler`; no new registration needed, only new constructor parameters on the two workflow classes.
- `docs/architecture/development_guidelines.md` — must be consulted by the architect/designer before finalizing the module-boundary decision (see Background); no deviation from its DTO/persistence rules is anticipated, but the architect phase should explicitly confirm this.
- Existing unit test suites: `ConfirmProductCompletionWorkflowTests.cs`, `ConfirmSemiProductManufactureWorkflowTests.cs`, and the (currently DTO-typed) tests for `ResidueDistributionCalculator` and `ManufactureNameBuilder` — all four require updates as part of this change and are not optional cleanup.

## Out of Scope
- Any change to `UpdateManufactureOrderDto`'s shape, the `UpdateManufactureOrder` HTTP use case's request/response contract, or its OpenAPI-generated client.
- Any change to the business logic/decision rules inside `SubmitToErpAsync`, `CalculateAsync`, `Build`, or the BoM update loop — only their input *type* changes.
- Broader repository-wide DTO-as-domain-carrier audits beyond the two workflows and two helper interfaces named here (the brief and this investigation scope the fix to the Manufacture confirmation workflows only).
- Introducing a new dedicated "workflow order" projection/type distinct from both `ManufactureOrder` and `UpdateManufactureOrderDto` — the domain entity is reused directly, per the issue's suggested fix.

## Open Questions

None.

## Status: COMPLETE
