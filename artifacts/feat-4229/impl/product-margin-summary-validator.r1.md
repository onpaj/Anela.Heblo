# Implementation: product-margin-summary-validator

## What was implemented
Added `GetProductMarginSummaryRequestValidator`, a FluentValidation validator for `GetProductMarginSummaryRequest` that rejects any `TimeWindow` value not present in `TimeWindowParser.SupportedTimeWindows`. On failure it attaches the `InvalidTimeWindow` (1706) error code, carries the offending value in `CustomState` under the `timeWindow` key, and produces a message via the `INVALID_TIME_WINDOW` template — all building blocks added by the two prerequisite tasks (`time-window-shared-allowlist`, `analytics-error-code-and-message`), which were already completed and committed on this branch.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs` (new) — the validator, following the exact structure specified in the task context.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginSummaryRequestValidatorTests.cs` (new) — 9 test cases (5 supported-value theory cases with no validation error, 3 unsupported-value theory cases asserting the `InvalidTimeWindow` error code, 1 fact asserting the offending value is carried in `CustomState`).

## Tests
`GetProductMarginSummaryRequestValidatorTests` — covers all 5 supported `TimeWindow` values (no error), 3 unsupported values (`"bogus"`, `""`, `"Current-Year"` — case sensitivity), and the `CustomState` dictionary carrying the offending value.

Followed TDD per the task context:
1. Wrote the test file first. Ran `dotnet test --filter "FullyQualifiedName~GetProductMarginSummaryRequestValidatorTests"` — confirmed it failed with a build error (`CS0246: The type or namespace name 'GetProductMarginSummaryRequestValidator' could not be found`), i.e. the expected red step.
2. Wrote the validator implementation exactly as specified in the task context.
3. Re-ran the same filtered test command — all 9 cases passed (`Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9`).

## How to verify
Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginSummaryRequestValidatorTests"` — expect 9/9 passing.

## Notes
No deviations from the task spec. The implementation is a verbatim match of the code given in the task-context file, and the dependency types (`TimeWindowParser.SupportedTimeWindows`, `ErrorCodes.InvalidTimeWindow`, `AnalyticsConstants.ValidationMessages.INVALID_TIME_WINDOW`, `GetProductMarginSummaryRequest.TimeWindow`) were verified to already exist on the branch with the exact names/shapes the task context assumes, since both prerequisite tasks were already completed.

## PR Summary
Added `GetProductMarginSummaryRequestValidator`, which rejects an unsupported `TimeWindow` string on the `GetProductMarginSummary` request with a proper validation error (error code 1706, `InvalidTimeWindow`) instead of allowing it to reach `TimeWindowParser.ParseTimeWindow`, which would otherwise throw an unhandled `ArgumentException` (500). The validator also attaches the offending value to the failure's `CustomState` so downstream error-response mapping can surface it to the caller.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs` — new FluentValidation validator for `GetProductMarginSummaryRequest.TimeWindow`
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Validators/GetProductMarginSummaryRequestValidatorTests.cs` — new test suite, 9 cases

## Status
DONE
