## Module / File
`backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailOptionsValidator.cs`

## Coverage
Line coverage: 44.4% (filter threshold: 60%)

## What's not tested
The validator has five distinct validation rules; coverage data indicates the date-range checks are not exercised:

1. **`BackfillFrom` in the future** — validated against the current UTC month; a future date would cause the backfill job to report success after processing zero months.
2. **`BackfillFrom` before 2020-01-01** — guarded by `EarliestBackfillFrom`; an earlier date would produce thousands of API calls on first run.
3. **Interaction**: the two date checks are in an `if/else if` chain, so only one fires per validation. A test must cover both branches independently.

The comment on each guard explicitly documents the silent-failure risk (the job "reports a clean run and never computes a single month"), which makes these branches a high priority for validation tests.

## Why it matters
`EcomailOptions` is validated at startup via `IValidateOptions`, so a misconfiguration fails fast — but only if the validator is correct. If the future-date check has an off-by-one (e.g., `>=` vs `>` for the current month), a `BackfillFrom` set to the current month would pass validation and silently compute nothing on the first run, making it appear the integration is healthy when it is not.

## Suggested approach
Unit-test `EcomailOptionsValidator.Validate` with minimal `EcomailOptions` instances:
- Valid config with a `BackfillFrom` of one month ago — expect success.
- `BackfillFrom` set to next month — expect failure with a message about future dates.
- `BackfillFrom` set to 2019-12-31 — expect failure with a message about the earliest allowed date.
- `BackfillFrom` set to exactly 2020-01-01 — expect success (boundary).
- Invalid `BaseUrl` (relative URL) — expect failure.
- `HttpTimeoutSeconds` of 0 and of 601 — both should fail.

Effort: ~30 min.

---
_Filed by weekly coverage-gap routine on 2026-10-19. Based on CI run #38726815029 (263a263200d3e8a3df47581d57e8bd8c148a7336)._
