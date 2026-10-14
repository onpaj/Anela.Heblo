# Implementation: analytics-error-code-and-message

## What was implemented
Added the `InvalidTimeWindow` error code (1706) to the Analytics module error code block in `ErrorCodes.cs`, mapped to HTTP 400 (Bad Request), and added the corresponding validation message constant `INVALID_TIME_WINDOW` to `AnalyticsConstants.ValidationMessages`. These are the building blocks the next task (`product-margin-summary-validator`) uses to turn an invalid `TimeWindow` string into a proper 400 response instead of an unhandled `ArgumentException` (500).

## Files created/modified
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `InvalidTimeWindow = 1706` immediately after `InvalidReportPeriod = 1705`, inside the `// Analytics module errors (17XX)` block, annotated with `[HttpStatusCode(HttpStatusCode.BadRequest)]`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs` — added `public const string INVALID_TIME_WINDOW = "Invalid time window: '{0}'. Supported values: {1}";` to the `ValidationMessages` nested class, after `MAX_PRODUCTS_MINIMUM`.

## Tests
No dedicated test for this task per the task-context instructions (enum members and string constants are exercised indirectly by the validator test in the next task, `product-margin-summary-validator`). `backend/test/Anela.Heblo.Tests/Shared/ErrorCodesTests.cs` does not exist, so Steps 1/2/4 of the original task template were skipped as instructed and the edit was applied directly.

## How to verify
Run `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — build succeeds with 0 errors (135 pre-existing warnings unrelated to this change).

## Notes
No deviations from the task spec. This is a small, additive change (new enum member + new string constant) with no behavioral effect until the next task wires validation to use them.

## PR Summary
Added the `InvalidTimeWindow` (1706) error code and its associated validation message template to the Analytics module's shared error infrastructure, laying the groundwork for returning HTTP 400 instead of an unhandled `ArgumentException` (500) when an Analytics endpoint receives an invalid `TimeWindow` string.

### Changes
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `InvalidTimeWindow = 1706` error code
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs` — added `INVALID_TIME_WINDOW` validation message constant

## Status
DONE
