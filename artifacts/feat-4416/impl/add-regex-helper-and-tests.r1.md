# Implementation: add-regex-helper-and-tests

## What was implemented
Added `ClassificationRegex.IsMatchOrContains` (200 ms regex timeout; timeout -> non-match; invalid pattern -> case-insensitive Contains) and routed the CompanyName, Description and ItemDescription classification rules through it.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/ClassificationRegex.cs` — new helper
- `.../Rules/CompanyNameClassificationRule.cs`, `DescriptionClassificationRule.cs`, `ItemDescriptionClassificationRule.cs` — delegate to helper
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/Rules/*RuleTests.cs` (3 files) — catastrophic-backtracking and invalid-pattern tests
- `docs/processes/feed-invoice-classification.md`, `module-invoice-classification.md` — Known quirk, verified_at bumped; INDEX regenerated

## Tests
Per rule: `(a+)+$` against 40 a's + "!" returns false in under 5 s; `[abc` falls back to Contains.

## How to verify
`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~InvoiceClassification"`

## Notes
Red-phase run of the timeout tests was skipped (pre-fix they would run for minutes).

## PR Summary
Regex patterns in invoice classification rules now have a 200 ms match timeout so a pathological user-defined pattern cannot stall the hourly classification job. A timed-out match counts as no match.

### Changes
- `ClassificationRegex.cs` — shared match helper
- three rule classes — use the helper
- tests and process docs updated

## Status
DONE
