# Specification: Refactor ManufactureOrder Confirm Endpoints to MediatR Pattern

## Summary
Two endpoints in `ManufactureOrderController` (`POST /{id}/confirm-semi-product` and `POST /{id}/confirm-products`) bypass MediatR and call `IManufactureOrderApplicationService` directly, violating the project's controller → MediatR pattern. This refactor introduces MediatR handlers for both operations, moves DTO mapping out of the controller, and removes the application-service dependency from the API layer. No behavioral change is expected.

## Background
The project's `development_guidelines.md` mandates: **"Business logic must be in MediatR handlers, NOT in controllers."** Eight of ten endpoints in `ManufactureOrderController` already follow this rule via `_mediator.Send(request)`. The two `confirm-*` endpoints were implemented against the application service directly, which:

1. Bypasses cross-cutting MediatR pipeline behaviours (exception handling, validation, future logging/tracing).
2. Forces the controller to manually catch generic `Exception` and return a hardcoded 500 — duplicating the global pipeline's responsibility.
3. Couples the API layer to the domain model via a private `MapResidueDistributionToDto` mapper that belongs in the Application layer.
4. Creates inconsistency within a single controller, complicating reasoning and testing (service mocking instead of handler mocking in integration tests).

The underlying workflows (`IConfirmSemiProductManufactureWorkflow`, `IConfirmProductCompletionWorkflow`) are correctly designed; only the dispatch path is wrong.

## Functional Requirements

### FR-1: Convert `ConfirmSemiProductManufactureRequest` to a MediatR request
Modify `ConfirmSemiProductManufactureRequest` in `Application/Features/Manufacture/Contracts/` to implement `IRequest<ConfirmSemiProductManufactureResponse>`. The existing `[Required]` and validation attributes must be preserved. The request must remain a class (not a record) per project DTO rules.

**Acceptance criteria:**
- `ConfirmSemiProductManufactureRequest : IRequest<ConfirmSemiProductManufactureResponse>` compiles.
- All existing properties, validation attributes, and serialization shape are unchanged.
- OpenAPI client generation produces the same TypeScript types as before.

### FR-2: Convert `ConfirmProductCompletionRequest` to a MediatR request
Modify `ConfirmProductCompletionRequest` analogously to implement `IRequest<ConfirmProductCompletionResponse>`.

**Acceptance criteria:**
- `ConfirmProductCompletionRequest : IRequest<ConfirmProductCompletionResponse>` compiles.
- All existing properties, validation attributes, and serialization shape are unchanged.
- OpenAPI client generation produces the same TypeScript types as before.

### FR-3: Implement `ConfirmSemiProductManufactureHandler`
Create a handler class in the same vertical slice (`Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/` or the matching existing folder) that:
- Implements `IRequestHandler<ConfirmSemiProductManufactureRequest, ConfirmSemiProductManufactureResponse>`.
- Receives `IConfirmSemiProductManufactureWorkflow` (and any other current dependencies of the application service method) via constructor injection.
- Delegates execution to the workflow with the same arguments the application service currently passes.
- Returns the same response shape currently produced.

**Acceptance criteria:**
- Handler is registered automatically via existing MediatR assembly scan.
- Calling the handler produces identical results (response payload, side effects, persisted state) to the current `_manufacturingApplicationService.ConfirmSemiProductManufactureAsync(...)` call for the same input.
- Handler does not contain business logic beyond translating request → workflow input and workflow output → response.

### FR-4: Implement `ConfirmProductCompletionHandler`
Create a handler analogous to FR-3 for `ConfirmProductCompletionRequest`, delegating to `IConfirmProductCompletionWorkflow`.

**Acceptance criteria:**
- Handler is registered automatically via existing MediatR assembly scan.
- Calling the handler produces identical results to the current `_manufacturingApplicationService.ConfirmProductCompletionAsync(...)` call.
- Handler contains no business logic beyond delegation.

### FR-5: Relocate `ResidueDistribution` → `ResidueDistributionDto` mapping
Move the `MapResidueDistributionToDto` logic out of `ManufactureOrderController` and into the existing AutoMapper profile `ManufactureOrderMappingProfile.cs` as a `CreateMap<ResidueDistribution, ResidueDistributionDto>()` (with member mappings as needed to preserve exact output).

**Acceptance criteria:**
- A unit test (or assertion within the handler test) confirms `ResidueDistribution → ResidueDistributionDto` produces the same DTO shape and values as the current controller-private mapper for representative inputs (including empty residue, multi-item residue, and zero-quantity edge cases if present today).
- No mapping code remains in the controller.
- The handler in FR-4 uses `IMapper` (or the workflow output already shaped via the profile) to produce the response.

### FR-6: Refactor `ManufactureOrderController` to use MediatR for both endpoints
In `ManufactureOrderController.cs`:
- Remove `IManufactureOrderApplicationService _manufacturingApplicationService` from the constructor and field declarations.
- Replace the body of `POST /{id}/confirm-semi-product` with `var response = await _mediator.Send(request); return Ok(response);` (matching the style of the other 8 endpoints in the same controller).
- Replace the body of `POST /{id}/confirm-products` analogously.
- Remove the `try/catch (Exception)` blocks and hardcoded 500 responses from both endpoints — let the global MediatR exception pipeline / middleware handle errors as it does for the other 8 endpoints.
- Remove the now-unused `MapResidueDistributionToDto` private method.

**Acceptance criteria:**
- `IManufactureOrderApplicationService` no longer appears in `ManufactureOrderController.cs`.
- Both endpoints are structurally identical to the other MediatR-based endpoints in the same controller (constructor injection of `IMediator` only, single `await _mediator.Send(request)` line, no manual exception handling).
- Endpoint routes, HTTP verbs, request DTOs, response DTOs, and status codes are unchanged.
- All existing integration tests for these endpoints continue to pass; tests that mocked `IManufactureOrderApplicationService` are updated to mock the handlers or the underlying workflow as appropriate.

### FR-7: Remove or retain `IManufactureOrderApplicationService` based on remaining usage
After FR-6, audit usages of `IManufactureOrderApplicationService` and `ManufactureOrderApplicationService`. If the `ConfirmSemiProductManufactureAsync` and `ConfirmProductCompletionAsync` methods are no longer called anywhere, remove them. If the entire service is no longer used, delete the interface, implementation, and DI registration.

**Acceptance criteria:**
- No production code references `IManufactureOrderApplicationService.ConfirmSemiProductManufactureAsync` or `ConfirmProductCompletionAsync` after the refactor.
- If the service still has other consumers, only the two methods are removed (or kept if shared internal helpers — but the controller never depends on the service).
- Build succeeds and no dead code remains specifically introduced by the bypass.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence
The refactor must produce identical observable behavior: same HTTP status codes for success and error paths, same response payloads, same persisted state, same side effects (events emitted, logs written by the workflow, etc.). The only intentional change is that uncaught exceptions are now handled by the global MediatR pipeline rather than the controller's manual catch block — error responses must remain semantically equivalent (e.g., still 500 for unhandled exceptions, unchanged response body shape from the perspective of API consumers).

### NFR-2: Performance
No measurable change in p50/p95 latency for either endpoint. The added MediatR dispatch adds negligible overhead (<1 ms) and must not introduce extra database round-trips or allocations beyond what the existing application service already incurs.

### NFR-3: Test coverage
Unit tests for each new handler must cover: happy path, validation failure path (if applicable), and workflow exception propagation. Existing controller integration tests must be updated to reflect the new dispatch path but must continue to assert the same end-to-end behavior. Total test coverage for the two endpoints must not decrease.

### NFR-4: Consistency
Both new handlers must follow the same structural conventions as existing MediatR handlers in `Application/Features/Manufacture/` (folder layout, file naming, constructor patterns, response construction).

## Data Model
No schema changes. The relevant types:

- **`ConfirmSemiProductManufactureRequest`** (Application/Features/Manufacture/Contracts/) — gains `IRequest<ConfirmSemiProductManufactureResponse>` marker.
- **`ConfirmProductCompletionRequest`** (Application/Features/Manufacture/Contracts/) — gains `IRequest<ConfirmProductCompletionResponse>` marker.
- **`ConfirmSemiProductManufactureResponse`** — unchanged.
- **`ConfirmProductCompletionResponse`** — unchanged.
- **`ResidueDistribution`** (domain) — unchanged.
- **`ResidueDistributionDto`** (Application contract) — unchanged shape; mapping now lives in `ManufactureOrderMappingProfile`.

## API / Interface Design

### Endpoints (unchanged contract)
- `POST /api/manufacture-orders/{id}/confirm-semi-product`
  - Request body: `ConfirmSemiProductManufactureRequest`
  - Success: `200 OK` with `ConfirmSemiProductManufactureResponse`
  - Error: handled by global pipeline (was: explicit 500 in controller)
- `POST /api/manufacture-orders/{id}/confirm-products`
  - Request body: `ConfirmProductCompletionRequest`
  - Success: `200 OK` with `ConfirmProductCompletionResponse`
  - Error: handled by global pipeline

### Internal dispatch
Both endpoints become a one-liner: `return Ok(await _mediator.Send(request));`, matching the eight other endpoints in `ManufactureOrderController`.

### Handler signatures
```csharp
public class ConfirmSemiProductManufactureHandler
    : IRequestHandler<ConfirmSemiProductManufactureRequest, ConfirmSemiProductManufactureResponse>

public class ConfirmProductCompletionHandler
    : IRequestHandler<ConfirmProductCompletionRequest, ConfirmProductCompletionResponse>
```

## Dependencies
- **MediatR** — already in use across the codebase; no version change.
- **AutoMapper** — already in use; `ManufactureOrderMappingProfile` already exists and will gain one additional `CreateMap`.
- **`IConfirmSemiProductManufactureWorkflow`** — existing application-layer workflow, used unchanged.
- **`IConfirmProductCompletionWorkflow`** — existing application-layer workflow, used unchanged.
- **Global exception middleware / MediatR exception pipeline** — must already convert unhandled exceptions to 500 responses for the other eight endpoints; if not, that is out of scope (see Open Questions).

## Out of Scope
- Changes to the workflows `IConfirmSemiProductManufactureWorkflow` / `IConfirmProductCompletionWorkflow` themselves.
- Changes to the underlying domain logic, persistence, or business rules of either confirmation operation.
- Introducing new MediatR pipeline behaviours (logging, tracing) — only existing ones apply after the refactor.
- Refactoring other controllers or application services.
- Frontend changes — the OpenAPI-generated TypeScript client will regenerate with no shape changes; no manual frontend work expected.
- Changing endpoint routes, HTTP verbs, or request/response payload shapes.

## Open Questions
None.

## Status: COMPLETE