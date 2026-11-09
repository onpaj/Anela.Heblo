# Specification: ErrorBoundary Unit Test Coverage

## Summary
Add React Testing Library unit tests for `frontend/src/components/common/ErrorBoundary.tsx`, currently at 0% line coverage (0/13). Tests must cover the error state transition, the reset callback, and both render paths (built-in UI vs custom `fallbackComponent`). No production code changes.

## Background
ErrorBoundary is the last line of defense against white-screening the app (used in `App.tsx`). A broken `resetError` would trap users on the error screen. Filed by the weekly coverage-gap routine (CI nightly run #42456500800). Existing tests for sibling components live in `frontend/src/components/common/__tests__/` (Jest via react-scripts, RTL, jest-dom).

## Functional Requirements

### FR-1: Default fallback on child error
Render a child that throws during render inside `ErrorBoundary` (no `fallbackComponent`).
**Acceptance criteria:**
- Child content is not rendered; heading "Nastala chyba při načítání komponenty" and button "Zkusit znovu" are shown.
- The thrown error message appears in the "Technické detaily" details block.
- When the error has no message/stack, "Neznámá chyba" is displayed (covers the fallback branch).
- `console.error` is called by `componentDidCatch` ("ErrorBoundary caught an error:" with the error).

### FR-2: Happy path passthrough
**Acceptance criteria:**
- With a non-throwing child, children render and no error UI is shown.

### FR-3: Reset via default UI
**Acceptance criteria:**
- After a child throws, make the child stop throwing (mutable flag), click "Zkusit znovu"; the boundary clears state and the child re-mounts and renders its content; error UI disappears.
- If the child still throws after reset, the error UI is shown again (boundary re-catches).

### FR-4: Custom fallbackComponent
**Acceptance criteria:**
- Custom fallback is rendered instead of built-in UI and receives `error` (the thrown Error instance) and a function `resetError`.
- Calling `resetError` from the fallback clears state and re-mounts the child.
- Built-in UI text is absent when custom fallback is used.

## Non-Functional Requirements

### NFR-1: Performance
Test file runs in well under 5 seconds; no timers/network.

### NFR-2: Security
N/A (test-only change).

### NFR-3: Test hygiene
`console.error` is mocked (jest.spyOn, restored in afterEach) to keep output clean and to assert on the logging; React's own error logs are suppressed by the same mock.

## Data Model
None.

## API / Interface Design
Test file: `frontend/src/components/common/__tests__/ErrorBoundary.test.tsx`, importing default export `ErrorBoundary` from `../ErrorBoundary`.

## Dependencies
`@testing-library/react`, `@testing-library/jest-dom`, `lucide-react` (rendered for real; no mock needed). Jest 27 via react-scripts.

## Out of Scope
- Changes to ErrorBoundary.tsx behavior or i18n.
- E2E tests, tests of App.tsx wiring, error reporting integration.

## Open Questions
None.

## Status: COMPLETE
