# Design: Slim UpdateManufactureOrderStatusHandler

## Component Design

### UpdateManufactureOrderStatusHandler (modified)
Constructor: `IManufactureOrderRepository`, `TimeProvider`, `ILogger<...>`, `ICurrentUserService`, `IManufactureOrderStatusSideEffects`.
Flow inside the existing try/catch (error handling and returned error codes unchanged):
1. `GetOrderByIdAsync`; null -> `ResourceNotFound` with `id` param.
2. `CanTransitionTo` false -> `InvalidOperation` with `oldState`/`newState` params.
3. `var now = _timeProvider.GetUtcNow().DateTime; order.ChangeState(request.NewState, now, userName);`
4. `ManualActionRequired`, `WeightWithinTolerance`, `WeightDifference` assigned if present (unchanged).
5. `order.ApplyErpDocuments(now, semiProductOrderCode, productOrderCode, flexiDocMaterialIssueForSemiProduct, flexiDocSemiProductReceipt, flexiDocSemiProductIssueForProduct, flexiDocMaterialIssueForProduct, flexiDocProductReceipt)`.
6. Note appended if non-null (unchanged).
7. `await _sideEffects.BeforePersistAsync(order, request.NewState, userName, ct)`.
8. `await _repository.UpdateOrderAsync(order, ct)`.
9. `await _sideEffects.AfterPersistAsync(order, ct)`.
10. Return response (unchanged shape).

### ManufactureOrder.ApplyErpDocuments (new domain method)
Nullable string parameters; for each non-null value sets the property and its `*Date` companion to `nowUtc`; null leaves both untouched. Mapping: semiProductOrderCode -> `ErpOrderNumberSemiproduct`/`...Date`; productOrderCode -> `ErpOrderNumberProduct`/`...Date`; materialIssueForSemiProduct -> `DocMaterialIssueForSemiProduct`/`...Date`; semiProductReceipt -> `DocSemiProductReceipt`/`...Date`; semiProductIssueForProduct -> `DocSemiProductIssueForProduct`/`...Date`; materialIssueForProduct -> `DocMaterialIssueForProduct`/`...Date`; productReceipt -> `DocProductReceipt`/`...Date`.

### ManufactureOrderStatusSideEffects (new, scoped)
- `BeforePersistAsync`: if `newState is SemiProductManufactured or Completed` and no reading for that stage -> `CaptureAsync` and add to `order.ConditionsReadings`; then if `Completed` -> `WriteDownAsync(order, changedByUser, ct)`. Exceptions propagate (handler catch returns InternalServerError, nothing persisted).
- `AfterPersistAsync`: `_catalogSource.RefreshPlannedDataSafelyAsync(_logger, order.Id, ct)`; carries the existing comment explaining why the refresh is needed.

## Data Schemas
No database, API request or response changes. `UpdateManufactureOrderStatusRequest` and `UpdateManufactureOrderStatusResponse` are unchanged classes (OpenAPI client unaffected). DI: `services.AddScoped<IManufactureOrderStatusSideEffects, ManufactureOrderStatusSideEffects>();` in `ManufactureModule.cs`.
