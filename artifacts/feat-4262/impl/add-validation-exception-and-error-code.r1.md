# Implementation: add-validation-exception-and-error-code

## What was implemented
Added the two purely-additive pieces needed to distinguish a permanent Shoptet
shipment-validation failure from a transient error, with no behavior wired up yet:
a new exception type carrying the carrier's order code / error code / message /
instance, and a new `ErrorCodes` enum value mapped to HTTP 422.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShoptetShipmentValidationException.cs` — new exception type `ShoptetShipmentValidationException` (OrderCode, ShoptetErrorCode, Instance properties)
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `ShipmentValidationFailed = 2910` (HttpStatusCode.UnprocessableEntity) immediately after `ShipmentOrderWeightUnavailable = 2909`

## Tests
None required for this task — purely additive types with no callers yet, per task context.

## How to verify
`cd backend && dotnet build` — succeeds with 0 errors (244 pre-existing warnings, unrelated to this change).

## Notes
No deviations from the task context. Implemented directly in this session (no
Task-tool/subagent dispatch available in this environment), following
`.claude/agents/developer.md`'s constraints directly instead of delegating to
implementer/reviewer subagents.

## PR Summary
Added `ShoptetShipmentValidationException` and the `ShipmentValidationFailed`
error code as the foundation for mapping Shoptet's 422 `shipment-validation-failed`
response into an actionable error instead of a generic 503. No behavior changes
yet — these are wired up in later tasks.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ShipmentLabels/ShoptetShipmentValidationException.cs` — new exception type
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — new `ShipmentValidationFailed = 2910` error code

## Status
DONE
