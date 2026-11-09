## Module / File
`frontend/src/components/common/ErrorBoundary.tsx`

## Coverage
Line coverage: 0% (0/13 lines). Filter threshold: 60%

## What's not tested
`getDerivedStateFromError` → error state transition has no test; `resetError` callback has no assertion that it clears state; the conditional rendering path (custom `fallbackComponent` vs built-in error UI) is fully dark.

## Why it matters
ErrorBoundary is the last line of defense preventing a component crash from white-screening the app. If `resetError` silently fails to clear state, the "try again" button is broken — users are stuck on the error screen with no way out, and no test would catch the regression.

## Suggested approach
React Testing Library tests: (a) render a child that throws, verify default fallback UI renders; (b) click "try again" and verify the callback clears state and the child re-mounts; (c) verify a custom `fallbackComponent` receives `error` and `resetError` props correctly. Estimated effort: ~1 hour.

---
_Filed by weekly coverage-gap routine on 2026-11-09. Based on CI nightly run #42456500800 (16643224c30a67e613312ac689cd872155b43bd1)._
