### task: refactor-handler
**Files:** `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`

- [ ] In `Handle`, declare `var effectiveRequest = request;`.
- [ ] Inside the `ProductKind.Product` branch, fetch the template with `request.ProductCode`; compute local `productCode = request.ProductCode` and, if template type is `MultiPhase`, `productCode = semiproduct?.ProductCode ?? productCode`; then `effectiveRequest = CreateEffectiveRequest(request, productCode, manufactureTemplate.ManufactureType);`. Remove all assignments to `request.*`.
- [ ] Add `private static CalculateBatchPlanRequest CreateEffectiveRequest(CalculateBatchPlanRequest source, string productCode, ManufactureType? manufactureType)` copying all properties (ProductCode, FromDate, ToDate, TimePeriod, SalesMultiplier, ControlMode, MmqMultiplier, TotalWeightToUse, TargetDaysCoverage, ProductConstraints, DirectSemiproductAmount, ManufactureType) with the two overrides.
- [ ] Keep `ResolveSalesRanges(request)` on the original; call `_batchPlanningService.CalculateBatchPlan(effectiveRequest, salesRanges, cancellationToken)`.
- [ ] Verify: `cd backend && dotnet build` succeeds; `dotnet format --verify-no-changes` clean for the touched file.

