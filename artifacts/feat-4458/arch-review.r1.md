# Architecture Review: CalculateBatchPlanHandler request immutability

## Skip Design: true
Backend-only refactor, no UI.

## Architectural Fit Assessment
Fits the Vertical Slice / MediatR layout (`Features/Manufacture/UseCases/CalculateBatchPlan`). Only the handler and its tests change. `BatchPlanningService` reads `request.ProductCode` and `request.ManufactureType` in many private methods (single/multi-phase internals), so the issue's "just pass local variables" cannot be applied literally without a service signature change spreading across ~10 private methods.

## Proposed Architecture

### Component Overview
Controller/MCP tool -> MediatR -> `CalculateBatchPlanHandler` -> builds enriched copy -> `IBatchPlanningService.CalculateBatchPlan(copy, ranges, ct)`. Original request untouched.

### Key Design Decisions

#### Decision 1: Enriched shallow copy vs. new service parameters
**Options considered:** (a) add `productCode`/`manufactureType` parameters to the service interface; (b) introduce an internal input record; (c) shallow-copy the request in the handler.
**Chosen approach:** (c). A private static helper in the handler creates a new `CalculateBatchPlanRequest` copying every property, with overridden `ProductCode` and `ManufactureType`.
**Rationale:** Smallest diff, no interface or generated-client change, honors CLAUDE.md "surgical changes". `ProductConstraints` list is copied by reference (service only reads it); acceptable and noted.

#### Decision 2: Keep behavior for non-Product items
Local variables start as `request.ProductCode` / `request.ManufactureType`; only overwritten inside the Product branch, exactly mirroring current semantics.

## Implementation Guidance

### Directory / Module Structure
- Edit `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`.
- Edit `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs`.
- Add a comment on `CalculateBatchPlanRequest.ManufactureType` is not required; leave the request class untouched.

### Interfaces and Contracts
Handler body:
```
var productCode = request.ProductCode; var manufactureType = request.ManufactureType;
if (product?.Type == ProductKind.Product) { template lookup by request.ProductCode; manufactureType = template.ManufactureType; if MultiPhase -> productCode = semiproduct ?? productCode; }
var effective = CreateEffectiveRequest(request, productCode, manufactureType);
ResolveSalesRanges(request) (unchanged); service.CalculateBatchPlan(effective, ranges, ct)
```
Note the existing code looks up the template with `request.ProductCode` before mutation; the same original code is used.

### Data Flow
Unchanged except the object identity passed to the service differs from the incoming request.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future property added to the request is forgotten in the copy | Medium | Unit test using reflection that asserts all public properties other than the two overridden ones are equal on the forwarded copy |
| Existing tests match the service call by request reference | Low | Update them to match by value (`It.Is<...>`) |
| `ProductConstraints` shared by reference | Low | Service only reads; documented |

## Specification Amendments
Spec FR-2 already reflects the copy approach; no further amendments.

## Prerequisites
None.
