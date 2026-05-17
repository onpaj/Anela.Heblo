# Implementation: ManufactureOrder Confirm Endpoints MediatR Refactor

## What was implemented

Refactored `POST /{id}/confirm-semi-product` and `POST /{id}/confirm-products` endpoints in `ManufactureOrderController` to dispatch through MediatR (matching the eight sibling endpoints). Created two MediatR handlers, moved `ResidueDistribution → ResidueDistributionDto` mapping into AutoMapper profile, deleted the now-unused `IManufactureOrderApplicationService`, and updated all test files. Zero observable behavior change.

## Files created/modified

**Created (4 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandler.cs` — MediatR handler delegating to `IConfirmSemiProductManufactureWorkflow` with try/catch + logger
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs` — MediatR handler delegating to `IConfirmProductCompletionWorkflow` with IMapper, try/catch + logger
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmSemiProductManufactureHandlerTests.cs` — 4 unit tests (success, failure with code, failure without code, exception)
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ConfirmProductCompletionHandlerTests.cs` — 5 unit tests (success, RequiresConfirmation with mapped distribution, failure, exception, parameter passing)

**Modified (5 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmSemiProductManufactureRequest.cs` — added `IRequest<ConfirmSemiProductManufactureResponse>` marker
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Contracts/ConfirmProductCompletionRequest.cs` — added `IRequest<ConfirmProductCompletionResponse>` marker
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs` — added `CreateMap<ResidueDistribution, ResidueDistributionDto>()` and `CreateMap<ProductConsumptionDistribution, ProductConsumptionDistributionDto>()`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — removed DI registration for `IManufactureOrderApplicationService`
- `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs` — removed service field/ctor param, replaced both endpoint bodies with `return HandleResponse(await _mediator.Send(request))`, deleted `MapResidueDistributionToDto`

**Deleted (3 files):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IManufactureOrderApplicationService.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureOrderApplicationService.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureOrderApplicationServiceTests.cs`

**Updated test files (2 files):**
- `backend/test/Anela.Heblo.Tests/Controllers/ManufactureOrderControllerTests.cs` — dropped `_applicationServiceMock`, switched to 2-arg controller ctor, replaced confirm tests to use `_mediatorMock`, added new `ConfirmProductCompletion Tests` region with 5 status-code-pinning tests
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderControllerProtocolTests.cs` — dropped `serviceMock`, updated to 2-arg controller ctor

**Created test file (1 file):**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/ManufactureOrderMappingProfileTests.cs` — 2 tests validating ResidueDistribution → ResidueDistributionDto mapping (multi-item, empty list)

## Tests

- `ConfirmSemiProductManufactureHandlerTests.cs` — 4 tests: success, failure with error code, failure without error code (defaults InvalidOperation), exception → InternalServerError
- `ConfirmProductCompletionHandlerTests.cs` — 5 tests: success, RequiresConfirmation with mapped distribution, workflow failure → InvalidOperation, exception → InternalServerError, parameter passing verification
- `ManufactureOrderMappingProfileTests.cs` — 2 tests: full field round-trip (multi-item + zero-quantity edge case), empty product list
- `ManufactureOrderControllerTests.cs` — 9 new tests pinning HTTP status codes: 200/500/502/400(mismatch) for confirm-semi-product; 200/200(RequiresConfirmation)/400/500/400(mismatch) for confirm-products
- Full suite: 3,640 tests pass (0 failures)

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConfirmSemiProductManufactureHandlerTests|FullyQualifiedName~ConfirmProductCompletionHandlerTests|FullyQualifiedName~ManufactureOrderMappingProfileTests|FullyQualifiedName~ManufactureOrderControllerTests"
```

All 6 commits in order:
- `64e624a3` refactor: mark Confirm request classes as IRequest<TResponse>
- `e82b7ecb` feat: add ResidueDistribution and ProductConsumptionDistribution AutoMapper profile mappings
- `00b57679` feat: add ConfirmSemiProductManufactureHandler
- `71354246` feat: add ConfirmProductCompletionHandler
- `2ae09b46` refactor: route confirm-semi-product and confirm-products through MediatR
- `3dc2ddfe` refactor: remove unused IManufactureOrderApplicationService

## Notes

- **No global MediatR exception pipeline exists** in this codebase (arch-review finding). Both handlers implement `try/catch (Exception)` mirroring `ResolveManualActionHandler` — this preserves the current error-response shape (typed response with `ErrorCodes.InternalServerError`, not unstructured ASP.NET 500).
- **Request/Response classes stay in `Contracts/`** per spec FR-1/FR-2 (not moved to `UseCases/<X>/` like handlers). The Vertical Slice inconsistency is noted as a future cleanup ticket.
- **`HandleResponse` used** (not bare `Ok(...)`) so `HttpStatusCodeAttribute` on `ErrorCodes` drives correct status codes — 400 for InvalidOperation, 500 for InternalServerError, 502 for ErpGatewayError.
- One cosmetic stale comment remains in `ManufactureOrderStateTransitionTests.cs:74` referencing the old service name — harmless, not a functional reference.

## PR Summary

Refactored the two remaining endpoints in `ManufactureOrderController` that bypassed MediatR and called `IManufactureOrderApplicationService` directly. Both endpoints now route through handlers (`ConfirmSemiProductManufactureHandler`, `ConfirmProductCompletionHandler`) matching the eight sibling endpoints. The `ResidueDistribution → ResidueDistributionDto` mapping moved from a private controller method into `ManufactureOrderMappingProfile`. The application service had no other consumers and was deleted in full.

The key architectural correction vs. the original spec: the codebase has no global MediatR exception pipeline, so error handling moved into the handlers (try/catch returning a typed `XxxResponse(ErrorCodes.InternalServerError)`) rather than being dropped entirely — preserving identical observable behavior for the exception path.

### Changes
- `Contracts/ConfirmSemiProductManufactureRequest.cs` — added `IRequest<TResponse>` marker
- `Contracts/ConfirmProductCompletionRequest.cs` — added `IRequest<TResponse>` marker
- `ManufactureOrderMappingProfile.cs` — added 2 CreateMap calls for ResidueDistribution types
- `ManufactureModule.cs` — removed application service DI registration
- `ManufactureOrderController.cs` — removed service dependency, both endpoints now one-liner
- `UseCases/ConfirmSemiProductManufacture/ConfirmSemiProductManufactureHandler.cs` — new handler (created)
- `UseCases/ConfirmProductCompletion/ConfirmProductCompletionHandler.cs` — new handler (created)
- `Services/IManufactureOrderApplicationService.cs` — deleted
- `Services/ManufactureOrderApplicationService.cs` — deleted
- `Services/ManufactureOrderApplicationServiceTests.cs` — deleted
- `ManufactureOrderControllerTests.cs` — updated to 2-arg ctor, added status-code-pinning tests
- `ManufactureOrderControllerProtocolTests.cs` — updated to 2-arg ctor

## Status
DONE
