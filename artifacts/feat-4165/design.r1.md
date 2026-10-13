# Design: Stop exception swallowing in PackingMaterials daily-consumption handlers

## Component Design

### `ProcessDailyConsumptionHandler` (`Application/Features/PackingMaterials/UseCases/ProcessDailyConsumption/ProcessDailyConsumptionHandler.cs`)

**Responsibility, after this change:** orchestrate one call to `IConsumptionCalculationService.ProcessDailyConsumptionAsync`, translate its two legitimate outcomes (already processed / processed with N materials updated / processed with zero invoices found) into `ProcessDailyConsumptionResponse`, and otherwise stay out of the way — any exception from the service call is the caller's problem, not this handler's.

**Interface:** unchanged — `IRequestHandler<ProcessDailyConsumptionRequest, ProcessDailyConsumptionResponse>`, same constructor (`IConsumptionCalculationService`, `ILogger<ProcessDailyConsumptionHandler>`).

**Behavior contract change:** `Handle` may now throw. Previously it never threw (all paths returned a `ProcessDailyConsumptionResponse`). The one remaining `_logger.LogInformation` calls before/after the service call are unchanged; the `_logger.LogError` call that existed only inside the removed catch block is deleted along with it — nothing in this handler logs the failure path any more (that responsibility now belongs entirely to whoever observes the propagated exception, per Data Schemas below).

### `DailyConsumptionJob` (`Application/Features/PackingMaterials/Infrastructure/Jobs/DailyConsumptionJob.cs`)

**Responsibility:** unchanged. No code changes. Its existing `try { ... } catch (Exception ex) { _logger.LogError(...); throw; }` around the `IMediator.Send` call becomes reachable for handler-internal failures for the first time — this is the whole point of the fix, achieved with zero lines changed in this file.

### `GetDailyConsumptionBreakdownHandler` (`Application/Features/PackingMaterials/UseCases/GetDailyConsumptionBreakdown/GetDailyConsumptionBreakdownHandler.cs`)

**Responsibility, after this change:** load consumption rows and materials, build the requested grouping, return `GetDailyConsumptionBreakdownResponse`. Any exception from the repository calls, or from the switch statement's `_ => throw new ArgumentOutOfRangeException(...)` default arm, propagates.

**Interface:** unchanged — `IRequestHandler<GetDailyConsumptionBreakdownRequest, GetDailyConsumptionBreakdownResponse>`, same constructor. The three private `BuildGroupBy*` static helpers are untouched.

**Behavior contract change:** `Handle` may now throw. The `_logger.LogError` call inside the removed catch block is deleted along with it, for the same reason as above.

### `PackingMaterialsController` (`API/Controllers/PackingMaterialsController.cs`)

**No change.** `ProcessDailyConsumption` (line 133) and `GetDailyConsumptionBreakdown` (line 149) are both left exactly as they are. An exception thrown out of either handler now propagates out of the action method before either action's own return statement executes, so the app's already-registered `app.UseExceptionHandler()` pipeline (see arch-review.r1.md, "Architectural Fit Assessment") handles it — the controller code has nothing to do.

## Data Schemas

No schema, DTO, or database changes. For clarity on what does *not* change:

- `ProcessDailyConsumptionResponse` — `Success`, `ProcessedDate`, `MaterialsProcessed`, `Message` (inherited: `ErrorCode`, `Params` from `BaseResponse`, both still unused by this handler). Still populated exactly as today for the two legitimate outcomes (already-processed / processed).
- `GetDailyConsumptionBreakdownResponse` — `Success`, `Error`, `Date`, `GroupBy`, `Groups` (inherited: `ErrorCode`, `Params`, still unused). `Error` is no longer ever set by this handler after the change (its only writer was the removed catch block) — it remains on the DTO per the spec's Out of Scope decision not to touch DTO shape, but is now dead in practice for this handler; a future cleanup could remove it, out of scope here.

### Failure-path data flow (replaces the old "catch and build a Success=false DTO" data flow)

```
Exception thrown in IConsumptionCalculationService.ProcessDailyConsumptionAsync
  or in IPackingMaterialRepository.{GetConsumptionsByDateAsync,GetAllWithAllocationsAsync}
  or ArgumentOutOfRangeException from GetDailyConsumptionBreakdownHandler's switch default
                                │
                                ▼
                    propagates out of Handle() unhandled
                                │
                ┌───────────────┴───────────────┐
                ▼ (job path)                     ▼ (HTTP path)
   DailyConsumptionJob's own catch:       app.UseExceptionHandler() pipeline:
   LogError(ex, ...); throw;              typed handlers (Unauthorized/Validation/
                │                          Argument) try first, first match wins;
                ▼                          ArgumentOutOfRangeException matches
   Hangfire marks the run Failed,         ArgumentExceptionHandler → 400 ProblemDetails
   applies its configured retry           with the exception's own message (safe —
   policy on daily-consumption-           ArgumentException messages are developer-
   calculation.                           authored, not secrets); anything else falls
                                           through to AddProblemDetails()'s default
                                           writer → 500 ProblemDetails, generic body,
                                           no exception message/stack trace included.
```

No new payload shapes are introduced by this flow — both terminal responses (Hangfire's failure record; the ProblemDetails HTTP body) are produced entirely by existing framework/infrastructure code, not by anything this change adds.
