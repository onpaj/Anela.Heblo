# Implementation Plan: Sales multiplier cookie tests

### task: add-cookie-validation-tests
**Files:** modify `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` only.

- [ ] Step 1: Read the existing file around the "reads initial sales multiplier from cookie" test (~line 536) and the `createWrapper` / `mockData` / `mockUseManufacturingStockAnalysisQuery` definitions.
- [ ] Step 2: Add a nested `describe("sales multiplier cookie validation")` after that test with cookie helpers (set, clear in `beforeEach` and `afterEach`; clear `manufacturing-stock-sales-multiplier`, `a`, `b`, `x-manufacturing-stock-sales-multiplier`) and a `renderPage()` helper.
- [ ] Step 3: Implement the seven cases from design.r1.md using `it.each` where listed: boundary values (0.1, 3.0, 1.5), out-of-range (0.09, 3.01, 0, -1, 100), non-numeric (abc, NaN, empty), multi-cookie lookup, prefix-named cookie not matched, hook receives `salesMultiplier`, and the `1.5=junk` documenting test.
- [ ] Step 4: Run `cd frontend && npx jest src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` (use the repo's test command from package.json); all tests pass, including pre-existing ones. If jsdom rejects an odd cookie value, adjust the case and note it in a comment.
- [ ] Step 5: Sanity-check mutation: temporarily change `parsed < 0.1` to `parsed <= 0.1` in the component, confirm the 0.1 test fails, then revert (do not commit).
- [ ] Step 6: Run `cd frontend && npm run lint` and `npm run build`; fix any issues in the test file. Confirm `git diff --stat` shows only the test file.
- [ ] Step 7: Commit: `test(manufacturing): cover sales multiplier cookie validation (#4588)`.

No process-doc change is needed (test-only; no behaviour change).
