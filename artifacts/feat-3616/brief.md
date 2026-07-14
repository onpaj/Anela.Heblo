## Module / File
`backend/src/Anela.Heblo.Application/Features/Photobank/Validators/UpdateRuleRequestValidator.cs`

## Coverage
Line coverage: 0.0% (filter threshold: 60%)

## What's not tested
The validator registers four rule groups, none of which are exercised by any test:

- `Id > 0` — a negative or zero ID passes the API surface undetected
- `PathPattern` — required, max 500 chars, **and must be a valid regex** via `PhotobankValidationHelpers.BeValidRegex`. The regex check is the most critical: an invalid pattern stored in the database would cause runtime crashes when the photobank tries to compile it for matching.
- `TagName` — required, max 100 chars
- `SortOrder >= 0`

The `BeValidRegex` helper itself is also untested (it does not appear in any covered file), making it a silent dependency.

## Why it matters
If `BeValidRegex` is broken or has an edge-case gap (e.g., it fails to reject catastrophic-backtracking patterns, or returns false for a valid regex), photo tagging rules could either fail to save valid patterns or persist invalid ones that crash the photobank sync at runtime.

## Suggested approach
Unit tests on `UpdateRuleRequestValidator` directly:
- Valid request → no validation errors
- `Id = 0` / `Id = -1` → validation error on Id
- `PathPattern = ""` / `PathPattern = null` → required error
- `PathPattern` > 500 chars → length error
- `PathPattern = "["` (invalid regex) → validation error via `BeValidRegex`
- `PathPattern = "^[a-z]+"` (valid regex) → no error on PathPattern
- `TagName = ""` → required error
- `SortOrder = -1` → validation error

~1 hour effort.

---
_Filed by weekly coverage-gap routine on 2026-07-13. Based on CI run #28968007617 (06d109fe5edcb456730222410f64385606100b1b)._
