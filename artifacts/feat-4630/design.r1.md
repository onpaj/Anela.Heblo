# Design: ErrorBoundary Unit Test Coverage

## Component Design
Test file `frontend/src/components/common/__tests__/ErrorBoundary.test.tsx`:

- `let shouldThrow` flag; `Thrower` component: throws `new Error("Boom")` when flag true, else renders `<div>Child content</div>`.
- `CustomFallback` helper (jest.fn-wrapped component) rendering `<div>Custom fallback: {error?.message}</div><button onClick={resetError}>Custom reset</button>`.
- `beforeEach`: `shouldThrow = true`; spy on `console.error`. `afterEach`: restore spy.

Test cases (describe "ErrorBoundary"):
1. renders children when no error
2. renders default fallback UI with error message when child throws
3. logs via console.error from componentDidCatch
4. shows "Neznámá chyba" when error message is empty
5. "Zkusit znovu" resets state and re-mounts child
6. re-shows error UI if child still throws after reset
7. custom fallbackComponent receives error and resetError, default UI absent
8. custom fallback resetError clears state and re-mounts child

## Data Schemas
None.
