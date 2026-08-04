# Review — Replace raw `http.fetch` bypass in `useManufactureOutput` & `useSemiproductRecipePdf`

## Verdict: done

## What I checked

- Re-read plan-01.md / design-01.md / development-01.md and diffed the actual
  working tree against `main` for all five changed files.
- Confirmed the generated `ApiClient` methods used
  (`manufactureOutput_GetManufactureOutput(monthsBack)`,
  `manufactureBatch_GetRecipePdf(productCode, batchSize)`) exist in
  `frontend/src/api/generated/api-client.ts` (lines 7418, 6706) with signatures
  matching the call sites exactly.
- Ran the actual verification commands myself rather than trusting the report:
  - `CI=true npm run build` → compiled successfully, zero TS errors.
  - `npx eslint` on all five touched files (2 hooks, 2 components, 2 new test
    files) → zero errors/warnings.
  - `npx react-scripts test --testPathPattern="useManufactureOutput|useSemiproductRecipePdf"`
    → 2 suites / 11 tests passed.
  - `grep -rn "as any"` on both hook files → no hits.
  - Grepped the whole `src` tree for `ManufactureOutputResponse` and for
    `useManufactureOutput|useSemiproductRecipePdf` usage → only the hook file
    and generated client reference the old name; all consumers
    (`ManufactureOutput.tsx`, `ManufactureOutputModal.tsx`,
    `ManufactureBatchCalculator.tsx`) accounted for, and
    `ManufactureBatchCalculator.tsx` only touches the hook's stable
    `{ openRecipePdf, isLoading }` surface as claimed — no changes needed there.

## Findings

**Conformance to spec/architecture:** Both `(apiClient as any).baseUrl` /
`(apiClient as any).http.fetch` bypasses cited in the arch-review evidence are
gone, replaced by the generated typed methods, exactly as
`docs/development/api-client-generation.md` prescribes. The four hand-declared
interfaces are deleted; the hook now imports and re-exports the generated
`GetManufactureOutputResponse` / `ManufactureOutputMonth` /
`ProductContribution` / `ProductionDetail` types, preserving both consumer
components' existing import paths (a deliberate, documented design choice —
smaller diff, matches an existing project convention).

**Correctness:** The two consumer components were correctly updated for the
generated types' optional fields (`?? []` / `?? 0` guards at every access
point the compiler would otherwise flag) and for `ProductionDetail.date`
changing from a hand-declared `string` to the generated `Date` — `formatDate`
was adjusted accordingly and the redundant `new Date(...)` wrap removed. I
spot-checked the full diff of both components against the design doc's
`?? []`/`?? 0` convention line by line; every read site matches.

**Tests:** Both new test files mock `getAuthenticatedApiClient()` to expose
only the generated method (no `http`/`baseUrl`), so they'd fail if the
implementation regressed back to raw fetch — a meaningful regression guard,
not just a happy-path check. Error paths, default-parameter behavior, and the
`FileResponse.data` blob handling are all covered.

No functional requirement is unmet, no architecture conflict, no missing
required test, no correctness bug found.

## Non-binding note

`development-01.md` mentions a pre-existing, unrelated test failure
(`ManufactureOrderDetail.autoCalculation.test.tsx`) confirmed via `git stash`
to exist on the unmodified branch too — not a blocker, just noting it was
addressed transparently rather than silently ignored.

```json
{"outcome": "done", "summary": "Implementation matches plan-01.md/design-01.md exactly: both (apiClient as any).http.fetch bypasses replaced with the generated typed methods, hand-declared interfaces deleted in favor of generated types, consumer components correctly null-guarded for now-optional fields and the date:string->Date change. Verified independently: build compiles clean, lint is clean on all five touched files, 11/11 new tests pass, no 'as any' remains, and all consumers of the hooks were checked and are unaffected or correctly updated."}
```
