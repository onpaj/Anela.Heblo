## Telemetry

App Insights (last 24h vs 7-day daily average):

| Metric | 24h count | 7d daily avg | Ratio |
|--------|-----------|--------------|-------|
| `POST ManufactureOrder/ConfirmSemiProductManufacture [id]` — HTTP 400 | 2 | 0.29 | **~7x** |

The 400 is not a client validation error — it is returned by the controller when `result.Success == false` from `ConfirmSemiProductManufactureWorkflow.ExecuteAsync`.

## Root Cause

`ManufactureOrderController.ConfirmSemiProductManufacture` (line ~128) returns `BadRequest` whenever the workflow's `ConfirmSemiProductManufactureResult.Success` is false. This path is hit in three workflow scenarios:

1. **Quantity update failure** — `UpdateSemiProductQuantityAsync` fails (MediatR handler error)
2. **Status change failure** — `UpdateStatusAsync` fails after ERP submission
3. **Unexpected exception** — caught at the bottom of `ExecuteAsync`, returns `false` with a generic message

The spike coincides with new FlexiBee `sarze-expirace` (lot-expiry lookup) `Canceled` failures seen today for product codes `AKL039` and `OLE058`, suggesting the ERP call in step 2 (`SubmitToErpAsync`) is timing out during stock-movement creation for lot-specific materials, causing the workflow to return `Success = false` → HTTP 400 rather than 500.

## Suggested Fix

1. **Distinguish 400 vs 502/503**: Business logic failures (e.g. invalid state transitions) should return 422 Unprocessable Entity; FlexiBee timeout/Canceled failures should return 502 Bad Gateway so monitoring can differentiate client mistakes from infrastructure failures.
2. **Correlate with FlexiBee timeout issue**: The `sarze-expirace` Canceled failures (see related issue) may be the proximate trigger — if the lot-stock lookup times out, the manufacture submission fails silently and gets wrapped as `Success = false`.
3. **Add structured logging**: Log the `result.Message` and triggering sub-step so the exact failure reason is visible in App Insights without requiring a code change.

## Relevant Code

- Controller: `backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs:106-139`
- Workflow: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`
- Lot stock filter: `backend/src/Anela.Heblo.Application/Features/Manufacture/ErrorFilters/Filters/InsufficientLotStockFilter.cs`