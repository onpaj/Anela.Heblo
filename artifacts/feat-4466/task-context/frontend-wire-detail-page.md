### task: frontend-wire-detail-page
Files:
- Modify `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`
- Modify existing tests under `frontend/src/components/manufacture/pages/__tests__/ManufactureOrderDetail.*.test.tsx` only if they break

Steps:
1. Delete `getStateTransitions` and the local `isStateTransitionBackward`; import them from `../detail/stateTransitions`.
2. Replace line `const currentStateTransitions = ...` with `order?.state !== undefined ? deriveStateTransitions(order.state, orderData?.availableTransitions, order.manufactureType) : { next: null, previous: null }`.
3. Keep `handleStateChange` / `executeStateChange` / `StateTransitionControls` unchanged.
4. Add (or extend) a component-level test asserting that a transition button is not rendered when `availableTransitions` omits its target and is rendered when included.
5. Verify: `npm run build`, `npm run lint`, run the manufacture test suites. Confirm no remaining per-state transition maps in the file via grep for `singlePhaseTransitions`.
6. Per CLAUDE.md, run `python3 scripts/process-docs/check.py index`; bump `verified_at` of the manufacture process doc only if the check requires it.
Depends on: frontend-transition-selector.
