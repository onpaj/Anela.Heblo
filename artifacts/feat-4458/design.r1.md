# Design: CalculateBatchPlanHandler request immutability

## Component Design
- `CalculateBatchPlanHandler.Handle`: reads from `request`, computes local `productCode` and `manufactureType`, then calls `CreateEffectiveRequest(request, productCode, manufactureType)` and forwards the result to `IBatchPlanningService`. `ResolveSalesRanges` continues to read the original request.
- `private static CalculateBatchPlanRequest CreateEffectiveRequest(CalculateBatchPlanRequest source, string productCode, ManufactureType? manufactureType)`: object initializer copying ProductCode (overridden), FromDate, ToDate, TimePeriod, SalesMultiplier, ControlMode, MmqMultiplier, TotalWeightToUse, TargetDaysCoverage, ProductConstraints, DirectSemiproductAmount, ManufactureType (overridden).
- No change to `IBatchPlanningService`, controller, MCP tool, validator or request DTO.

## Data Schemas
No schema, API or OpenAPI changes.
