# Implementation Plan: ErrorBoundary Unit Test Coverage

Goal: raise `ErrorBoundary.tsx` line coverage from 0% to 100% with RTL tests. No production changes.

### task: add-errorboundary-tests

**Files:**
- Create: `frontend/src/components/common/__tests__/ErrorBoundary.test.tsx`

**Steps:**
1. Create the test file with imports (`React`, `render`, `screen`, `fireEvent` from `@testing-library/react`, `ErrorBoundary` from `../ErrorBoundary`), the `shouldThrow` flag, `Thrower`, and `CustomFallback` helpers as described in `design.r1.md`. Add `beforeEach`/`afterEach` managing the flag and `console.error` spy.
2. Implement test cases 1-8 from design.r1.md:
   - Default UI: assert `screen.getByText("Nastala chyba při načítání komponenty")`, `screen.getByText(/Boom/)`, button "Zkusit znovu"; `queryByText("Child content")` is null.
   - Logging: `expect(console.error).toHaveBeenCalledWith("ErrorBoundary caught an error:", expect.any(Error))`.
   - Empty message: child throws `new Error("")`; expect "Neznámá chyba" text (note stack text may follow in the same `pre`; use a regex matcher).
   - Reset: set `shouldThrow = false`, `fireEvent.click(getByRole("button", {name: "Zkusit znovu"}))`, expect "Child content" visible and error heading absent. Variant: leave `shouldThrow = true`, click, expect error UI again.
   - Custom fallback: render with `fallbackComponent={CustomFallback}`; assert fallback shows "Boom", props received (`error` is Error with message "Boom", `resetError` is a function), default heading absent; then flip flag, click "Custom reset", expect child content.
3. Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/common/__tests__/ErrorBoundary.test.tsx` -- all pass.
4. Run coverage check: `CI=true npx react-scripts test --watchAll=false --coverage --collectCoverageFrom=src/components/common/ErrorBoundary.tsx src/components/common/__tests__/ErrorBoundary.test.tsx` -- expect 100% lines.
5. Run `npm run lint` and `npm run build` in `frontend/`; fix any issues in the new file only.
6. Commit: `test(frontend): add ErrorBoundary unit tests`.

**Acceptance:** all 8 tests pass; ErrorBoundary.tsx line coverage >= 90%; lint and build clean; no files outside the new test file changed.
