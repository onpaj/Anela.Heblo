# Implementation: frontend-transition-selector (r1)

- Added `frontend/src/components/manufacture/detail/stateTransitions.ts` (`deriveStateTransitions`, `isStateTransitionBackward`, STATE_ORDER ranking per design).
- Added `__tests__/stateTransitions.test.ts` (9 tests: MultiPhase x4, SinglePhase x2, empty/undefined, Cancelled, backward check).
- Verified: `react-scripts test stateTransitions` 9/9 pass; eslint clean on both files. Component not yet wired (next task).
