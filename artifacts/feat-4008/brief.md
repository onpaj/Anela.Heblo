## Module / File
`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats/GetIssuedInvoiceSyncStatsHandler.cs`

## Coverage
Line coverage: 19.4% (filter threshold: 60%)

## What's not tested
Two branches are uncovered:

1. **Date-range defaulting** — when `request.FromDate` is null, `fromDate` defaults to `DateTime.Now.Date.AddDays(-30)`; when `request.ToDate` is null, `toDate` defaults to `DateTime.Now.Date`. The defaulted values are passed directly to `GetSyncStatsAsync`, so an off-by-one or wrong direction in either default silently returns statistics for the wrong window.
2. **Exception path** — if `GetSyncStatsAsync` throws, the handler returns `Success = false` with `ErrorCodes.Exception`. The error-response shape (including the `Params` entry with the Czech error message) is never asserted.

## Why it matters
The stats page uses this handler to display invoice sync health. If the default date window shifts (e.g. `AddDays(-30)` becomes `AddDays(+30)` or the wrong date object is used), the dashboard silently shows the wrong period — there is no invariant currently enforcing the correct range. The exception path's structured response is also unverified: a contract break there would result in an unhandled error on the frontend.

## Suggested approach
Unit tests mocking `IIssuedInvoiceRepository`. Cover:
- Both dates null → `GetSyncStatsAsync` receives `today.AddDays(-30)` as fromDate and `today` as toDate
- Explicit dates provided → passed through unchanged
- `GetSyncStatsAsync` throws → `Success = false`, `ErrorCode == ErrorCodes.Exception`
- Happy path: response fields mapped correctly from repository stats

Estimated effort: ~1 h.

---
_Filed by weekly coverage-gap routine on 2026-08-31. Based on CI run #33077392747 (ba8f5eef168e0058dae1787bf6bb9f53fdcdf472)._