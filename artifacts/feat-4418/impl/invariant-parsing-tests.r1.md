# Implementation: invariant-parsing-tests

## What was implemented
Failing-first tests for culture-invariant amount parsing in `AmountClassificationRule`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/AmountClassificationRuleTests.cs` - `WithCulture` helper (sets CurrentCulture/CurrentUICulture, restores in finally); cs-CZ theory (16 cases, all six operators, both sides of boundary); cs-CZ fact `>=1000,50` returns false; whitespace fact `>= 100` matches 100.

## Tests
`dotnet test --no-build --filter AmountClassificationRuleTests`: 9 of 44 fail before the fix (the cs-CZ dot-pattern cases that depend on a matching/ordering of the dot, plus the comma case); whitespace fact and all pre-existing tests pass.

## How to verify
Run the filter above; after `invariant-parsing-fix` all 44 must pass.

## Notes
Culture is process-global; tests mutate it only within try/finally. xUnit runs tests within a class serially, but other classes could run in parallel and observe the culture change - low risk for this suite.

## PR Summary
Add cs-CZ culture regression tests for amount rule parsing.
