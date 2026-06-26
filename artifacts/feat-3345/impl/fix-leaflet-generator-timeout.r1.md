# Implementation: fix-leaflet-generator-timeout

## What was implemented
Raised `RESULT_TIMEOUT_MS` from `30_000` to `90_000` in the leaflet generator E2E spec to accommodate LLM calls that can take longer than 30 seconds.

## Files created/modified
- `frontend/test/e2e/marketing/leaflet-generator.spec.ts` — changed `RESULT_TIMEOUT_MS` constant from `30_000` to `90_000` (line 7)

## How to verify
1. Run `git show HEAD -- frontend/test/e2e/marketing/leaflet-generator.spec.ts` and confirm line 7 reads `const RESULT_TIMEOUT_MS = 90_000;`.
2. Run `./scripts/run-playwright-tests.sh` against staging — the "generates a leaflet for a known topic" test should no longer time out when the LLM call exceeds 30 s.

## Notes
No deviations. Change is exactly one line as specified in the task. Staging environment access was not available for live verification; the change is a straightforward constant update per the architectural decision.

## PR Summary
Raise the E2E result timeout in `leaflet-generator.spec.ts` from 30 s to 90 s so that tests waiting on LLM-backed generation no longer flake when the model response takes more than 30 seconds.

### Changes
- `frontend/test/e2e/marketing/leaflet-generator.spec.ts` — `RESULT_TIMEOUT_MS` raised from `30_000` to `90_000`

## Status
DONE
