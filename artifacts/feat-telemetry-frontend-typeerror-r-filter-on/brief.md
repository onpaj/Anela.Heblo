telemetry-signal: frontend:TypeError-r.filter is not a function@Yq

**Window:** P7D (2026-06-05 – 2026-06-12)
**Occurrences:** 1

## Signal

Browser exception `Uncaught TypeError: r.filter is not a function` at minified symbol `Yq` in the compiled app bundle.

| Field | Value |
|---|---|
| Timestamp | 2026-06-12T05:58:29Z |
| Operation | `/` (SPA root) |
| Browser | Chrome 148.0 |
| ProblemId | `Uncaught TypeError: r.filter is not a function at Yq` |

## Analysis

`r.filter` is called on a value that is `undefined` or `null` — classic symptom of a data-fetch result consumed before it is populated (missing null guard or absent default `[]`). The error fires on the app entry path `/`, so whatever initialises on first load is the culprit.

## Correlation hypothesis

Occurred 2026-06-12T05:58 — same day as a series of UI merges (PRs #2962 "open dashboard to all users with per-tile permission enforcement", #2943 "Move Journal Search Presentation Logic to Frontend", #2948 "Remove Manual refetch Calls from JournalList"), all of which shipped on 2026-06-12. Volume is low (single occurrence) but the timing is consistent with a regression introduced by one of those changes.

## Next step

1. Locate symbol `Yq` in the source map (or in the bundle directly via `grep Yq dist/`).
2. Identify the `.filter()` call site — look for any hook or selector that destructures API data without a default `[]`.
3. Add a null-safe default or optional-chain guard and verify with `npm run build && npm run lint`.