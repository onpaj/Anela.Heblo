## Module / File
`backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBase.cs`

## Coverage
Line coverage: 20.0% (filter threshold: 60%)

## What's not tested
The job has three distinct execution paths that are all unexercised: (1) the job-disabled early return — when the feature flag is off, the job should do nothing and return without calling the import service; (2) the partial-failure warning branch — when `result.Failed.Count > 0`, a warning is logged and execution continues; (3) the exception-rethrow path — an unhandled exception from the import service is caught, logged, and rethrown. No test verifies that disabled → no-op, partial failure → log + continue, or exception → rethrow.

## Why it matters
A regression in the disabled-job guard could trigger live Shoptet API calls in environments where the job should be turned off. A broken exception-rethrow would silently swallow import errors and prevent Hangfire from marking the job as failed.

## Suggested approach
Unit tests for each derived job class (or the base class directly with a mock import service): verify the short-circuit on disabled, verify that a warning is logged and result returned on partial failure, and verify that an exception propagates to the caller. Estimated effort: ~2h.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._