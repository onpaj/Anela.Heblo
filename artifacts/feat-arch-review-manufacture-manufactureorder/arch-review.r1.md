I have enough context. Critical finding: the spec assumes a "global MediatR exception pipeline" that does not exist in this codebase — handlers explicitly catch exceptions and return error-coded responses (see `ResolveManualActionHandler.cs:79-83`). Writing the review now.

```markdown
# Architecture Review: Refactor ManufactureOrder Confirm Endpoints to MediatR Pattern

## Skip Design: true

Pure backend refactor. No UI/UX changes — request/response shapes preserved, generated TypeScript client unaffected.

## Architectural Fit Assessment

The proposal aligns perfectly with the project's documented controller → MediatR convention (`docs/architecture/development_guidelines.md` ADR-003) and with the eight sibling endpoints in `ManufactureOrderController`. The Manufacture module already uses Vertical Slice organization under `Application/Features/Manufacture/UseCases/<UseCase>/` (e.g., `DuplicateManufactureOrder`, `ResolveManualAction`, `CreateManufactureOrder`), all of which implement `IRequest<TResponse>` and pair with a sibling `Handler`.

**Critical correction to the spec assumption (NFR-1):** The spec states "let the global MediatR exception pipeline / middleware handle errors." **This pipeline does not exist.** A repo-wide search for `UseExceptionHandler`, `IExceptionHandler`, `ExceptionMiddleware`, or any catch-all `IPipelineBehavior` returned no results. The actual project pattern is:

- Each handler wraps its body in `try/catch (Exception ex)`, logs, and returns `new XxxResponse(ErrorCodes.InternalServerError)` (see `ResolveManualActionHandler.cs:79-83`, plus identical patterns already inside the two target workflows).
- Controllers call `HandleResponse(response)` (`BaseApiController.cs:28-59`), which uses `BaseResponse.Success` + `HttpStatusCodeAttribute` on `ErrorCodes` to translate to the correct HTTP status (200 / 400 / 404 / 500 / …).
- `ValidationBehavior` is the only `IPipelineBehavior` and is **opt-in per request type** in each module's `AddXxxModule()`.

If the refactor naively removes the controller `try/catch` without giving the handler one, uncaught exceptions will produce ASP.NET's default unstructured 500 response (empty body / dev exception page) instead of today's `ConfirmXxxResponse(ErrorCodes.InternalServerError)` JSON payload — a real, observable behavior change that breaks NFR-1.

The workflows themselves (`ConfirmSemiProductManufactureWorkflow.cs:85-94`, `ConfirmProductCompletionWorkflow.cs:113-118`) **already catch `Exception` and return `Result` objects with `Success=false`** — so in practice the controller's `catch` is reachable only for exceptions thrown by the workflow's own catch handler (vanishingly rare). The handler's own catch becomes belt-and-braces, matching `ResolveManualActionHandler`.

## Proposed Architecture

### Component Overview

```
HTTP POST /api/manufacture-orders/{id}/confirm-semi-product
                  │
                  ▼
       ManufactureOrderController            (Anela.Heblo.API)
       (id-match guard → Mediator.Send → HandleResponse)
                  │
                  ▼
       ConfirmSemiProductManufactureHandler  (Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture)
       (try/catch → workflow.ExecuteAsync → map Result → Response)
                  │
                  ▼
       IConfirmSemiProductManufactureWorkflow (unchanged)
                  │
                  ▼
       MediatR fan-out to UpdateManufactureOrder / SubmitManufacture / UpdateManufactureOrderStatus handlers


HTTP POST /api/manufacture-orders/{id}/confirm-products
                  │
                  ▼
       ManufactureOrderController            (Anela.Heblo.API)
                  │
                  ▼
       ConfirmProductCompletionHandler       (Application/Features/Manufacture/UseCases/ConfirmProductCompletion)
       (try/catch → workflow.ExecuteAsync → IMapper → Response)
                  │              │
                  │              └── IMapper (AutoMapper) for ResidueDistribution → ResidueDistributionDto
                  ▼
       IConfirmProductCompletionWorkflow      (unchanged)
```

### Key Design Decisions

#### Decision 1: Where Request/Response classes live

**Options considered:**
- (A) Keep `ConfirmSemiProductManufactureRequest/Response` and `ConfirmProductCompletionRequest/Response` in `Application/Features/Manufacture/Contracts/`, add `IRequest<T>` marker in place. Create handler in `UseCases/<X>/`.
- (B) Move all four files into the new `UseCases/<X>/` folders to match the prevailing Vertical Slice pattern (every other Manufacture use case co-locates Request/Response/Handler).

**Chosen approach:** **(A)** — keep them in `Contracts/` per the spec (FR-1, FR-2). 

**Rationale:** The spec explicitly says "Modify in `Contracts/`". Option B is architecturally cleaner but expands scope, churns the OpenAPI input file paths, and risks subtle NSwag schema-naming changes. The Vertical Slice purity payoff is small relative to the diff cost. Note this inconsistency in the follow-up backlog instead of fixing it here.

#### Decision 2: Where exceptions are caught

**Options considered:**
- (A) Remove controller `try/catch`, rely on a "global MediatR exception pipeline" (per spec wording).
- (B) Move the `try/catch (Exception)` from the controller into the handler. Handler returns `new XxxResponse(ErrorCodes.InternalServerError)` with the same user-facing message currently produced.
- (C) Introduce a new generic `ExceptionHandlingBehavior : IPipelineBehavior<,>` and register it for the two requests.

**Chosen approach:** **(B)** — handler-local catch.

**Rationale:** Matches the established codebase pattern (`ResolveManualActionHandler` and every workflow already do this). (A) is impossible — the pipeline doesn't exist. (C) is overengineering for two endpoints and would still need to construct the typed `XxxResponse(ErrorCodes.InternalServerError)`, which requires reflection or per-type handling. (B) preserves NFR-1 exactly: same 500 status, same structured body, same Czech error message.

#### Decision 3: ResidueDistribution → ResidueDistributionDto mapping

**Options considered:**
- (A) Inline mapping in `ConfirmProductCompletionHandler` (a private method, same shape as today's controller mapper).
- (B) Add `CreateMap<ResidueDistribution, ResidueDistributionDto>()` and `CreateMap<ProductConsumptionDistribution, ProductConsumptionDistributionDto>()` to existing `ManufactureOrderMappingProfile.cs`; inject `IMapper` into the handler.

**Chosen approach:** **(B)** — AutoMapper profile.

**Rationale:** Spec FR-5 prescribes this, and `ManufactureOrderMappingProfile` already exists with five sibling `CreateMap` calls — adding two more is idiomatic. AutoMapper convention-based property mapping covers all 7 `ResidueDistributionDto` fields and all 8 `ProductConsumptionDistributionDto` fields without explicit `.ForMember(...)` calls (names and types match). Test the profile via a unit test that asserts member equality against a representative `ResidueDistribution` fixture, including the null-distribution guard (the handler — not the profile — must throw `InvalidOperationException` if `result.Distribution` is null when `RequiresConfirmation` is true, preserving the controller's current invariant at `ManufactureOrderController.cs:199`).

#### Decision 4: How the controller maps handler responses

**Chosen approach:** Use `return HandleResponse(await _mediator.Send(request))` — the same one-liner used by every other endpoint in this controller.

**Rationale:** The handler returns a `BaseResponse`-derived object whose `Success`/`ErrorCode` fields drive `HandleResponse`'s switch in `BaseApiController.cs:42-54`. **Verify before merge** that `HttpStatusCodeAttribute` on `ErrorCodes.InvalidOperation` resolves to **400 BadRequest** (today the `confirm-products` workflow-failure path hard-codes `BadRequest(response)` at `ManufactureOrderController.cs:184`) and that `ErrorCodes.InternalServerError` resolves to **500** (today hard-coded `StatusCode(500, response)`). If either mapping diverges, NFR-1 is violated and either the attribute or the handler's chosen `ErrorCodes` value must be adjusted before merging.

#### Decision 5: Handle the `RequiresConfirmation = true` path

The workflow returns `ConfirmProductCompletionResult.NeedsConfirmation(distribution)` with `Success = false` (`ConfirmProductCompletionResult.cs:31`). The handler must translate this into a response with `Success = true` and `RequiresConfirmation = true` — i.e., construct it with the **parameterless** `ConfirmProductCompletionResponse()` constructor (which sets `Success = true` via `BaseResponse`), then set `RequiresConfirmation = true` and `Distribution = _mapper.Map<ResidueDistributionDto>(...)`. This matches today's controller logic at `ManufactureOrderController.cs:167-172` and makes `HandleResponse` return `200 OK`.

## Implementation Guidance

### Directory / Module Structure

Create two new folders and four new files; modify five existing files:

```
backend/src/Anela.Heblo.Application/Features/Manufacture/
├── Contracts/
│   ├── ConfirmSemiProductManufactureRequest.cs         [MODIFY: add ": IRequest<ConfirmSemiProductManufactureResponse>"]
│   └── ConfirmProductCompletionRequest.cs              [MODIFY: add ": IRequest<ConfirmProductCompletionResponse>"]
├── ManufactureOrderMappingProfile.cs                   [MODIFY: add 2 CreateMap calls]
├── ManufactureModule.cs                                [MODIFY: remove DI registration for IManufactureOrderApplicationService once unused]
├── Services/
│   ├── IManufactureOrderApplicationService.cs          [DELETE per FR-7]
│   └── ManufactureOrderApplicationService.cs           [DELETE per FR-7]
└── UseCases/
    ├── ConfirmSemiProductManufacture/
    │   └── ConfirmSemiProductManufactureHandler.cs     [NEW]
    └── ConfirmProductCompletion/
        └── ConfirmProductCompletionHandler.cs          [NEW]

backend/src/Anela.Heblo.API/Controllers/
└── ManufactureOrderController.cs                        [MODIFY: remove service field/ctor param, replace both endpoint bodies, delete MapResidueDistributionToDto]

backend/test/Anela.Heblo.Tests/
├── Controllers/ManufactureOrderControllerTests.cs       [MODIFY: drop _applicationServiceMock, switch tests to mock IMediator.Send on the two new request types]
└── Features/Manufacture/UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandlerTests.cs   [NEW]
└── Features/Manufacture/UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandlerTests.cs             [NEW]
```

### Interfaces and Contracts

**`ConfirmSemiProductManufactureHandler` signature:**

```csharp
public class ConfirmSemiProductManufactureHandler
    : IRequestHandler<ConfirmSemiProductManufactureRequest, ConfirmSemiProductManufactureResponse>
{
    private readonly IConfirmSemiProductManufactureWorkflow _workflow;
    private readonly ILogger<ConfirmSemiProductManufactureHandler> _logger;
    // ctor + Handle(): try { var result = await _workflow.ExecuteAsync(req.Id, req.ActualQuantity, req.ChangeReason, ct);
    //                       if (result.Success) return new ConfirmSemiProductManufactureResponse { Message = result.Message };
    //                       return new ConfirmSemiProductManufactureResponse(result.ErrorCode ?? ErrorCodes.InvalidOperation) { Message = result.Message }; }
    //                  catch (Exception ex) { _logger.LogError(ex, ...);
    //                       return new ConfirmSemiProductManufactureResponse(ErrorCodes.InternalServerError)
    //                            { Message = "Došlo k neočekávané chybě při potvrzení výroby polotovaru" }; }
}
```

**`ConfirmProductCompletionHandler` signature:**

```csharp
public class ConfirmProductCompletionHandler
    : IRequestHandler<ConfirmProductCompletionRequest, ConfirmProductCompletionResponse>
{
    private readonly IConfirmProductCompletionWorkflow _workflow;
    private readonly IMapper _mapper;
    private readonly ILogger<ConfirmProductCompletionHandler> _logger;
    // ctor + Handle():
    //   - Translate request.Products -> Dictionary<int, decimal>
    //   - try {
    //       var result = await _workflow.ExecuteAsync(req.Id, dict, req.OverrideConfirmed, req.ChangeReason, ct);
    //       if (result.RequiresConfirmation) {
    //           if (result.Distribution is null) throw new InvalidOperationException("Distribution cannot be null when mapping to DTO");
    //           return new ConfirmProductCompletionResponse {
    //               RequiresConfirmation = true,
    //               Distribution = _mapper.Map<ResidueDistributionDto>(result.Distribution) };
    //       }
    //       if (result.Success) return new ConfirmProductCompletionResponse();
    //       return new ConfirmProductCompletionResponse(ErrorCodes.InvalidOperation) { Message = result.ErrorMessage };
    //     }
    //     catch (Exception ex) { _logger.LogError(ex, ...);
    //       return new ConfirmProductCompletionResponse(ErrorCodes.InternalServerError)
    //            { Message = "Došlo k neočekávané chybě při dokončení výroby produktů" }; }
}
```

**Controller signature (both endpoints):**

```csharp
[HttpPost("{id}/confirm-semi-product")]
public async Task<ActionResult<ConfirmSemiProductManufactureResponse>> ConfirmSemiProductManufacture(
    int id, [FromBody] ConfirmSemiProductManufactureRequest request)
{
    if (id != request.Id) return BadRequest("ID in URL does not match ID in request body.");
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

(The `id != request.Id` guard exists on six of the eight existing endpoints — keep it, it is the project convention.)

### Data Flow

**Happy-path `confirm-semi-product`:**
1. Controller validates `id == request.Id`, dispatches `request` via `IMediator.Send`.
2. MediatR resolves `ConfirmSemiProductManufactureHandler`, runs `ValidationBehavior` only if explicitly registered (it isn't — leave it unregistered to match status quo; spec NFR-1 says no behavior change).
3. Handler invokes `IConfirmSemiProductManufactureWorkflow.ExecuteAsync(...)`, which internally MediatR-dispatches `UpdateManufactureOrderRequest`, `SubmitManufactureRequest`, and `UpdateManufactureOrderStatusRequest`.
4. Workflow returns `ConfirmSemiProductManufactureResult(success=true, message=...)`.
5. Handler builds `ConfirmSemiProductManufactureResponse { Message = ... }` (defaults `Success = true`).
6. Controller's `HandleResponse(response)` returns `Ok(response)`.

**Confirmation-required path `confirm-products`:**
1. Controller → handler → workflow as above.
2. Workflow returns `ConfirmProductCompletionResult.NeedsConfirmation(distribution)` (Success=false, RequiresConfirmation=true, Distribution=non-null).
3. Handler maps `Distribution` via `IMapper`, builds `ConfirmProductCompletionResponse { RequiresConfirmation = true, Distribution = dto }` (Success=true via default ctor).
4. `HandleResponse` returns `Ok(response)` — frontend sees same `RequiresConfirmation=true` payload.

**Exception path (either endpoint):**
1. Handler's `try` block hits an exception not caught by the workflow (extremely rare since the workflow itself catches `Exception`).
2. Handler logs and returns `new XxxResponse(ErrorCodes.InternalServerError) { Message = "Došlo k neočekávané chybě …" }`.
3. `HandleResponse` consults `HttpStatusCodeAttribute` on `ErrorCodes.InternalServerError`, returns `StatusCode(500, response)`. Body shape identical to today.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec's "global MediatR exception pipeline" doesn't exist → removing the controller `try/catch` without a handler-local catch returns unstructured 500. | **HIGH** | Decision 2 above: handler-local `try/catch` returning `ErrorCodes.InternalServerError` response. Mirror `ResolveManualActionHandler.cs:79-83` exactly. |
| `HttpStatusCodeAttribute` on `ErrorCodes.InvalidOperation` may not be 400 → workflow-failure path on `confirm-products` may return a different status than today's hard-coded `BadRequest`. | MEDIUM | Add an integration test that asserts the exact status code for the workflow-error path before and after the refactor. If divergent, either fix the attribute or pick a different `ErrorCodes` value that maps to 400. |
| Existing controller tests mock `IManufactureOrderApplicationService` → removing the field breaks ctor signature and all setup code. | MEDIUM | Update `ManufactureOrderControllerTests.cs:35-43` to drop the third constructor parameter and remove `_applicationServiceMock`. Replace any test that exercises the two endpoints with `_mediatorMock.Setup(m => m.Send(It.IsAny<ConfirmXxxRequest>(), ...))`. Add new unit tests directly on each handler with mocked `IConfirmXxxWorkflow`. |
| AutoMapper convention mapping silently drops a field if a future property is added to `ResidueDistribution` but not to `ResidueDistributionDto` (or vice versa). | LOW | The unit test required by FR-5 (representative-input round-trip) covers today's fields. Add `Profile.AssertConfigurationIsValid()` to an existing AutoMapper test if one exists; otherwise add a one-line test. |
| Pre-existing partial Vertical-Slice violation: Request/Response stay in `Contracts/` while every other Manufacture use case co-locates them in `UseCases/<X>/`. | LOW | Out of scope per spec FR-1/FR-2. Note as a future cleanup ticket. |
| The two `confirm-*` endpoints become discoverable by `IPipelineBehavior<,>` registrations in other modules if any are registered generically (open-generic). | LOW | Inspect `ApplicationModule.cs:54` and confirm no open-generic behaviors are registered. Currently only `ValidationBehavior` is registered per-closed-type — safe. |

## Specification Amendments

1. **NFR-1 (Behavioral equivalence) — REWORD:** Replace the phrase "uncaught exceptions are now handled by the global MediatR pipeline rather than the controller's manual catch block" with: *"Uncaught exceptions are now caught inside the MediatR handler (mirroring the project's established handler pattern, e.g., `ResolveManualActionHandler`), which returns the same `XxxResponse(ErrorCodes.InternalServerError)` payload the controller's `catch` block produces today."* This is the actual project convention — there is no global exception pipeline.

2. **FR-6 — ADD acceptance criterion:** "Both endpoints use `return HandleResponse(await _mediator.Send(request));` — not bare `Ok(...)` — to preserve correct status-code mapping driven by `BaseResponse.ErrorCode` + `HttpStatusCodeAttribute`."

3. **FR-3 / FR-4 — ADD acceptance criterion:** "Handlers `try/catch (Exception)`, log via `ILogger<THandler>`, and return `new XxxResponse(ErrorCodes.InternalServerError) { Message = "Došlo k neočekávané chybě …" }` (Czech text from the current controller catch blocks preserved verbatim)."

4. **FR-5 — CLARIFY:** "The handler must throw `InvalidOperationException("Distribution cannot be null when mapping to DTO")` if `result.RequiresConfirmation` is true but `result.Distribution` is null — preserving the current invariant from `ManufactureOrderController.cs:197-200`. AutoMapper handles non-null distributions; the null-guard is the handler's responsibility, not the profile's."

5. **FR-7 — CONFIRM:** Repo-wide grep for `IManufactureOrderApplicationService` returns only the controller, the interface/impl pair, `ManufactureModule.cs:51`, and two test files (`ManufactureOrderControllerProtocolTests.cs`, `ManufactureOrderControllerTests.cs`). After the controller stops depending on it, the service and DI registration **can be fully deleted** — there are no other production consumers. Update the two test files accordingly (drop the mock; the protocol test likely just needs its ctor signature updated).

6. **NFR-3 (Test coverage) — ADD:** "An integration/unit test must explicitly verify the HTTP status codes returned for: success (200), workflow-failure on `confirm-products` (must remain 400), `RequiresConfirmation=true` (200), and exception path (500). These four cases pin down the observable behavior so future `HandleResponse` changes can't silently shift status codes."

## Prerequisites

None. All required infrastructure exists:

- **MediatR + assembly-scan registration** — `ApplicationModule.cs:54` (`AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly))`). New handlers will be auto-discovered.
- **AutoMapper profile** — `ManufactureOrderMappingProfile` already wired into the existing `IServiceCollection.AddAutoMapper(...)` setup (verify via `dotnet build` after edits).
- **Both workflows** (`IConfirmSemiProductManufactureWorkflow`, `IConfirmProductCompletionWorkflow`) already registered in `ManufactureModule.cs:54-55`.
- **`BaseApiController.HandleResponse`** + **`BaseResponse`** + **`ErrorCodes`** infrastructure all in place.
- **No database migration, no config change, no infra change.**

Before merging, run: `dotnet build`, `dotnet format`, `dotnet test` (backend), and regenerate the OpenAPI TypeScript client — verify zero diff in `frontend/src/api-client/` for the four affected DTO types.
```