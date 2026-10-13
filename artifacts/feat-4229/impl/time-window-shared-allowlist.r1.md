# Implementation: time-window-shared-allowlist

## What was implemented
Added a static `SupportedTimeWindows` allow-list to `TimeWindowParser`, giving the
five currently-supported `TimeWindow` literal values (`current-year`,
`current-and-previous-year`, `last-6-months`, `last-12-months`, `last-24-months`)
a single, shared source of truth that a later task's validator will reference,
instead of hardcoding a second copy of the list. `ParseTimeWindow`'s body and its
`ArgumentException` fallback were left unchanged, exactly as the task specified.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — added `public static readonly string[] SupportedTimeWindows` field to `TimeWindowParser`.
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs` — new test file (no prior test file existed for this class).

## Tests
- `TimeWindowParserTests.SupportedTimeWindows_ContainsExactlyTheFiveKnownValues` — asserts the new field contains exactly the five known time-window values.

## How to verify
Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimeWindowParserTests"`
Result: 1 test passed, 0 failed.

## Notes
No behavior change to `ParseTimeWindow`; this is purely additive (a new static
field), so all five existing switch arms and the exception fallback are
untouched. This unblocks the later `product-margin-summary-validator` task,
which references `TimeWindowParser.SupportedTimeWindows` directly.

## PR Summary
Added `TimeWindowParser.SupportedTimeWindows`, a shared static allow-list of the
five valid `TimeWindow` values, so the upcoming FluentValidation validator for
`GetProductMarginSummaryRequest` can validate against the same list the parser's
`switch` uses, instead of duplicating it.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — added `SupportedTimeWindows` static field
- `backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs` — new test covering the field's contents

## Status
DONE
