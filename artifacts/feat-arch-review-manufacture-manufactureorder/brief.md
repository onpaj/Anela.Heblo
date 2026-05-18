## Module
Manufacture

## Finding
Two endpoints in `ManufactureOrderController` call the application service directly instead of dispatching a MediatR request, breaking the controller → MediatR pattern used by every other endpoint in the same class.

**Affected code in `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs`:**

- Lines 27–38: `IManufactureOrderApplicationService _manufacturingApplicationService` is injected alongside `IMediator`.
- Lines 106–141 (`POST /{id}/confirm-semi-product`): calls `_manufacturingApplicationService.ConfirmSemiProductManufactureAsync(...)` directly.
- Lines 148–193 (`POST /{id}/confirm-products`): calls `_manufacturingApplicationService.ConfirmProductCompletionAsync(...)` directly.

In contrast, all 8 other endpoints in the same controller use `_mediator.Send(request)`.

A secondary symptom of the bypass: lines 195–222 contain a private `MapResidueDistributionToDto` method that maps the domain type `ResidueDistribution` to `ResidueDistributionDto` directly in the controller. This mapping belongs in the Application layer (AutoMapper profile or inside the workflow), not in the API layer.

The `ConfirmSemiProductManufactureRequest` and `ConfirmProductCompletionRequest` in `Application/Features/Manufacture/Contracts/` are plain classes (`[Required]`-annotated DTOs), **not** `IRequest<T>` MediatR types, so there are no handlers for these operations — the multi-step workflow logic is entirely in the service and invoked by the controller.

## Why it matters
`development_guidelines.md` states: **"Business logic must be in MediatR handlers, NOT in controllers."** Bypassing MediatR means:

1. Cross-cutting pipeline behaviours (exception handling middleware, validation pipeline, future logging/tracing MediatR behaviours) do not apply to these two operations.
2. The controller manually catches `Exception` (lines 136–141, 189–193) and returns a hardcoded 500 response — duplicating what the pipeline or a global exception handler should do.
3. Inconsistency within the same controller makes the codebase harder to reason about and harder to test (service must be mocked instead of handler in integration tests).
4. Domain-to-DTO mapping in the API layer couples the controller to the domain model, defeating the purpose of the `Contracts/` layer.

## Suggested fix
Wrap the two workflows in MediatR handlers:

1. Convert `ConfirmSemiProductManufactureRequest` to implement `IRequest<ConfirmSemiProductManufactureResponse>` and create `ConfirmSemiProductManufactureHandler` that delegates to `IConfirmSemiProductManufactureWorkflow`.
2. Convert `ConfirmProductCompletionRequest` to implement `IRequest<ConfirmProductCompletionResponse>` and create `ConfirmProductCompletionHandler` that delegates to `IConfirmProductCompletionWorkflow`.
3. Move `MapResidueDistributionToDto` into the handler (or into an AutoMapper profile — a `ResidueDistribution → ResidueDistributionDto` profile in `ManufactureOrderMappingProfile.cs`).
4. In the controller, replace both direct service calls with `await _mediator.Send(request)` and remove `IManufactureOrderApplicationService` from the constructor.

The workflows themselves need no change — only their entry point moves from controller-direct-call to a thin MediatR handler.

---
_Filed by daily arch-review routine on 2026-05-16._