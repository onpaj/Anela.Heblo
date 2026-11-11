# Implementation Plan: CalculateBatchPlanHandler request immutability

**Goal:** Handler stops mutating the MediatR request; the service receives an enriched copy.
**Architecture:** Local variables in `Handle`, plus private static `CreateEffectiveRequest` shallow-copy helper. When the request is not a `Product` kind (no enrichment), the original request object is forwarded as-is so existing reference-matching tests keep passing.
**Tech Stack:** .NET 8, MediatR, xUnit, Moq.

### task: refactor-handler
**Files:** `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`

- [ ] In `Handle`, declare `var effectiveRequest = request;`.
- [ ] Inside the `ProductKind.Product` branch, fetch the template with `request.ProductCode`; compute local `productCode = request.ProductCode` and, if template type is `MultiPhase`, `productCode = semiproduct?.ProductCode ?? productCode`; then `effectiveRequest = CreateEffectiveRequest(request, productCode, manufactureTemplate.ManufactureType);`. Remove all assignments to `request.*`.
- [ ] Add `private static CalculateBatchPlanRequest CreateEffectiveRequest(CalculateBatchPlanRequest source, string productCode, ManufactureType? manufactureType)` copying all properties (ProductCode, FromDate, ToDate, TimePeriod, SalesMultiplier, ControlMode, MmqMultiplier, TotalWeightToUse, TargetDaysCoverage, ProductConstraints, DirectSemiproductAmount, ManufactureType) with the two overrides.
- [ ] Keep `ResolveSalesRanges(request)` on the original; call `_batchPlanningService.CalculateBatchPlan(effectiveRequest, salesRanges, cancellationToken)`.
- [ ] Verify: `cd backend && dotnet build` succeeds; `dotnet format --verify-no-changes` clean for the touched file.

### task: add-handler-tests
**Files:** `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs`

- [ ] Add test `Handle_ProductMultiPhase_DoesNotMutateRequestAndForwardsSemiproductCode`: catalog returns `ProductKind.Product` snapshot; `_manufactureClientMock.GetManufactureTemplateAsync` returns MultiPhase template with a SemiProduct ingredient `SEMI001`; capture request passed to service via `Callback`/`It.Is`; assert original request `ProductCode`=="PROD001" and `ManufactureType` null, and forwarded `ProductCode`=="SEMI001", `ManufactureType`==MultiPhase.
- [ ] Add test for Product + SinglePhase: forwarded ProductCode unchanged, ManufactureType SinglePhase, original request's ManufactureType still null.
- [ ] Add test for MultiPhase without SemiProduct ingredient: forwarded ProductCode is original.
- [ ] Add a reflection-based test asserting that every public property except `ProductCode` and `ManufactureType` is equal between original and forwarded request (set non-default values on all fields first).
- [ ] Verify existing tests still pass: `cd backend && dotnet test test/Anela.Heblo.Tests --filter CalculateBatchPlanHandlerTests`.

### task: final-validation
**Files:** none

- [ ] Run `cd backend && dotnet build && dotnet format --verify-no-changes` and the Manufacture test filter (`--filter FullyQualifiedName~Manufacture`); all must pass.
- [ ] No process-doc change needed (behavior unchanged); if the Process Docs CI check flags `docs/processes/module-manufacture.md` or the batch-planning doc, bump `verified_at` and run `python3 scripts/process-docs/check.py index`.
