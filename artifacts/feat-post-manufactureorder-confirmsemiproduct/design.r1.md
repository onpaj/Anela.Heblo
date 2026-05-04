Design document written to `artifacts/feat-post-manufactureorder-confirmsemiproduct/design.md`.

Key decisions reflected from the arch review:

**Backend**
- `ConfirmSemiProductManufactureResult` uses factory methods + `ErrorCode` field (no parallel outcome enum) — the `[HttpStatusCode]` attribute on `ErrorCodes` drives HTTP status via the existing `HandleResponse` pattern
- ERP soft-failure → HTTP 503 + `manualErpActionRequired: true` (not HTTP 200 as the spec proposed) — this is the operationally critical change that makes 5xx alerting actually fire
- Business rule failures → existing codes/status (400, or 422 via a new `ManufactureOrderStateTransitionInvalid` code if PM confirms)
- New `HttpContextExtensions.SetFailureCategory` sets `Activity.Current?.AddTag` — a thin static helper, no DI

**Frontend**
- The spec marked UI changes optional, but the arch review's 200→503 reclassification makes them mandatory: `useConfirmSemiProductManufacture` gets an `onManualErpRequired` callback option so the page can show a `showWarning` toast and keep the optimistic state (the DB write succeeded — rollback would be wrong)

**One decision still needs PM confirmation** before implementation: whether HTTP 503 (arch review) or HTTP 200 (spec) is the right code for the ERP soft-failure case.