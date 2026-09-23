# Code Review: add-validation-exception-and-error-code

## Summary
Purely additive change matching the task context exactly: a new `ShoptetShipmentValidationException` type and a new `ShipmentValidationFailed = 2910` error code mapped to `HttpStatusCode.UnprocessableEntity`, inserted immediately after `ShipmentOrderWeightUnavailable = 2909` as specified. Build succeeds with 0 errors.

## Review Result: PASS

### task: add-validation-exception-and-error-code
**Status:** PASS

## Docs to Update
(None — no public behavior changes yet; these are unwired additive types per the task context.)

## Overall Notes
No callers reference either new symbol yet, consistent with the task's intent to keep this step safe to commit standalone. Diff matches the spec's code snippets verbatim.

**Status:** PASS
