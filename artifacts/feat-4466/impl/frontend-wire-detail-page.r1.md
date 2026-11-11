# Implementation: frontend-wire-detail-page (r1)

- ManufactureOrderDetail.tsx: removed local getStateTransitions / isStateTransitionBackward (and singlePhaseTransitions map); now imports deriveStateTransitions + isStateTransitionBackward from ../detail/stateTransitions; currentStateTransitions derived from orderData?.availableTransitions.
- Added ManufactureOrderDetail.transitions.test.tsx (2 tests: button shown when target in availableTransitions, hidden when omitted).
- Verified: manufacture jest suites 9/9 (74 tests) pass; npm run build OK; eslint clean on touched files (pre-existing lint errors elsewhere); process-docs index check ran, no doc changes required.
