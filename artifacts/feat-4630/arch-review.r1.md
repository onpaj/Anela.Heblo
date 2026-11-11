# Architecture Review: ErrorBoundary Unit Test Coverage

## Skip Design: true
Test-only change; no new or changed UI.

## Architectural Fit Assessment
Fits existing convention: component tests colocated in `frontend/src/components/common/__tests__/<Component>.test.tsx` using RTL + jest-dom (verified: Pagination.test.tsx, NdStar.test.tsx exist). `docs/architecture/testing-strategy.md` governs FE unit tests. No production code touched; no process docs affected (not a sync/calculation/job).

## Proposed Architecture

### Component Overview
Single new test file with a small `Thrower` helper component driven by a module-level mutable flag, plus a `CustomFallback` helper capturing props.

### Key Design Decisions

#### Decision 1: Controlling re-mount after reset
**Options considered:** (a) rerender with different children; (b) mutable `shouldThrow` flag read by the child.
**Chosen approach:** (b). Flip flag to false before clicking reset; ErrorBoundary then re-renders children which no longer throw. This tests the real `resetError` path without prop changes.
**Rationale:** Mirrors the real-world "retry" scenario; (a) would not distinguish reset from a parent re-render (the boundary stays in error state regardless of new children).

#### Decision 2: Console noise
**Chosen approach:** `jest.spyOn(console, "error").mockImplementation(() => {})` in beforeEach, `mockRestore` in afterEach. Assert on the "ErrorBoundary caught an error:" call via `toHaveBeenCalledWith("ErrorBoundary caught an error:", expect.any(Error))`.

#### Decision 3: Query strategy
Use Czech UI text via `getByText`/`getByRole("button", {name: "Zkusit znovu"})`; no test ids added (surgical, no production edits).

## Implementation Guidance

### Directory / Module Structure
- Create `frontend/src/components/common/__tests__/ErrorBoundary.test.tsx` only.

### Interfaces and Contracts
`ErrorBoundary` default export; props `children`, optional `fallbackComponent: ComponentType<{error?: Error; resetError: () => void}>`.

### Data Flow
child throws -> `getDerivedStateFromError` sets `{hasError, error}` -> `componentDidCatch` logs -> render fallback -> reset click -> `setState` clears -> children re-render.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| React dev-mode rethrows/logs uncaught errors via jsdom window error events, polluting output | Low | console.error spy; errors are caught by boundary so tests do not fail |
| Flag leakage between tests | Low | reset flag in beforeEach |
| Empty-message error branch: `new Error("")` gives falsy message but stack present | Low | assert "Neznámá chyba" text via `error.message = ""` and delete stack, or throw a non-Error-like object with undefined message; simplest: throw `new Error("")` and check "Neznámá chyba" appears |

## Specification Amendments
None.

## Prerequisites
None.
