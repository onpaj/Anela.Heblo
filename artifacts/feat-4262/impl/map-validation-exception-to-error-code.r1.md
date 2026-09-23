# Implementation: map-validation-exception-to-error-code

## What was implemented

`ShipmentCreationService.CreateAndPersistAsync` now catches `ShoptetShipmentValidationException`
(thrown by `ShoptetShipmentClient` when Shoptet returns HTTP 422 for `CreateShipmentAsync`)
separately from the generic catch-all. On this specific exception it returns a
`ShipmentCreationResult` with `ErrorCode = ErrorCodes.ShipmentValidationFailed` and a new
`Params` dictionary carrying the raw Shoptet validation message under the fixed key
`"ShoptetMessage"` (per the cross-stack contract fixed in the task context, also consumed
verbatim by `surface-validation-message-in-frontend`). The failure is logged at `Warning`
(not `Error`), since this is now an expected, actionable data-quality condition rather than
an infrastructure anomaly, while still remaining visible in Application Insights.

The generic `catch (Exception ex)` block is unchanged and still handles every other failure
mode (mapped to `ErrorCodes.ShipmentCreationFailed`), so no existing behavior for other
exception types was altered.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs` —
  added a nullable `Dictionary<string, string>? Params` property, set only on the new
  validation-failure path; `null` for every other failure so existing callers that don't
  read it are unaffected.
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` —
  added a `catch (ShoptetShipmentValidationException vex)` block before the existing
  catch-all in `CreateAndPersistAsync`, returning `ErrorCode = ErrorCodes.ShipmentValidationFailed`
  with `Params["ShoptetMessage"] = vex.Message`, logged at `LogWarning`.
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs` —
  added `CreateAndPersistAsync_CreateShipmentThrowsValidationException_ReturnsShipmentValidationFailedWithMessage`,
  asserting `IsSuccess == false`, `ErrorCode == ErrorCodes.ShipmentValidationFailed`, and
  `Params["ShoptetMessage"]` equals the exception's message.

## Tests

- `ShipmentCreationServiceTests` — new test above, plus all 16 pre-existing tests in this
  file (including `CreateAndPersistAsync_CreateShipmentThrows_ReturnsShipmentCreationFailed`,
  confirming the existing catch-all path for other exception types is untouched).

## How to verify

```bash
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ShipmentCreationServiceTests"
```

Result: 17/17 passed (verified in this run).

## Notes

Code changes (`ShipmentCreationResult.cs`, `ShipmentCreationService.cs`, and the test file)
were already present on the branch from a prior interrupted session (commit `df8e5227`,
"WIP ... tests pending verification"), implemented exactly per the task-context steps 3–4.
This pass verified the implementation against the task context (matches steps 3–4 verbatim,
including the fixed `"ShoptetMessage"` params key), confirmed no other required steps were
outstanding, and ran the full targeted test suite to completion (previously left
unverified). No code changes were needed beyond what was already committed.

## PR Summary
Maps the `ShoptetShipmentValidationException` (Shoptet HTTP 422 on shipment creation) to a
new `ShipmentValidationFailed` error code with the Shoptet-provided validation message
attached, so a permanent, non-retryable data-quality rejection is now distinguishable from
a transient/infra failure and can surface the actual reason to the caller instead of a
generic "shipment creation failed."

### Changes
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationResult.cs` — added `Params` dictionary
- `backend/src/Anela.Heblo.Application/Features/Packaging/Services/ShipmentCreationService.cs` — added dedicated catch block for `ShoptetShipmentValidationException`
- `backend/test/Anela.Heblo.Tests/Application/Packaging/ShipmentCreationServiceTests.cs` — added coverage for the new path

## Status
DONE
