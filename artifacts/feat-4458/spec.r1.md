# Specification: CalculateBatchPlanHandler must not mutate the MediatR request

## Summary
`CalculateBatchPlanHandler.Handle` overwrites `request.ManufactureType` and (for multi-phase products) `request.ProductCode` on the incoming MediatR request. This hidden side effect makes pipeline behaviors and loggers observe inconsistent request values. The handler must treat the request as read-only and pass server-enriched values to `IBatchPlanningService` without touching the original object.

## Background
Source: arch-review issue #4458 (Manufacture module). Handler: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`. `BatchPlanningService.CalculateBatchPlan(request, salesRanges, ct)` reads `request.ManufactureType` (to choose single- vs multi-phase) and `request.ProductCode` (catalog lookup / semiproduct code) plus many other request fields (control mode, multipliers, constraints, direct semiproduct amount). `ManufactureType` is documented on the request as "set internally by handler".

## Functional Requirements

### FR-1: Request object is never mutated
The handler must not assign to any property of the `CalculateBatchPlanRequest` it receives.
**Acceptance criteria:**
- After `Handle` returns (or throws), the original request's `ProductCode` and `ManufactureType` equal their values before the call, in the Product + MultiPhase, Product + SinglePhase and non-Product scenarios.
- A unit test asserts this for the multi-phase case (original `ProductCode` retained).

### FR-2: Service receives enriched values
The service must receive the same effective inputs as today.
**Acceptance criteria:**
- Product kind + MultiPhase template with a SemiProduct ingredient: service gets `ProductCode` = semiproduct code, `ManufactureType` = MultiPhase.
- Product kind + MultiPhase without a SemiProduct ingredient: service gets original `ProductCode`, `ManufactureType` = MultiPhase.
- Product kind + SinglePhase: service gets original `ProductCode`, `ManufactureType` = SinglePhase.
- Non-Product kind (or catalog item not found): service gets the request values unchanged (`ManufactureType` as supplied, typically null).
- All other request fields (FromDate, ToDate, TimePeriod, SalesMultiplier, ControlMode, MmqMultiplier, TotalWeightToUse, TargetDaysCoverage, ProductConstraints, DirectSemiproductAmount) are forwarded with identical values.

### FR-3: Behavior preserved
Sales range resolution (`ResolveSalesRanges`) and response are unchanged. The template lookup keeps using the original product code.

## Non-Functional Requirements

### NFR-1: Performance
No additional I/O; one shallow object copy per call.

### NFR-2: Security
No change. Public API / OpenAPI contract of `CalculateBatchPlanRequest` stays unchanged (no property additions or removals) so the generated clients are unaffected.

## Data Model
No persistence changes. `CalculateBatchPlanRequest` shape unchanged.

## API / Interface Design
`IBatchPlanningService.CalculateBatchPlan` signature unchanged. Handler builds a shallow copy of the request carrying the enriched `ProductCode`/`ManufactureType` and passes the copy to the service. (Note: the suggested fix in the issue passes "local vars" but the service consumes a request object, so a copy is the minimal way without changing the service contract.)

## Dependencies
`IManufactureCatalogSource`, `IManufactureClient`, `IBatchPlanningService`, `ITimePeriodResolver`, `TimeProvider` (all unchanged).

## Out of Scope
- Changing `IBatchPlanningService` signature or introducing a separate internal input model.
- Removing `ManufactureType` from the public request.
- Other handlers' mutation patterns.
- Process docs: no behavior change, so no `docs/processes` edit beyond optionally bumping `verified_at` if CI demands.

## Open Questions
None.

## Status: COMPLETE
