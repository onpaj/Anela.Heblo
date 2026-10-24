### task: frontend-transition-selector
Files:
- Create `frontend/src/components/manufacture/detail/stateTransitions.ts`
- Create `frontend/src/components/manufacture/detail/__tests__/stateTransitions.test.ts`

Steps:
1. Write failing unit tests for `deriveStateTransitions(current, available, manufactureType)` covering: MultiPhase Draft/Planned/SemiProductManufactured/Completed/Cancelled with the backend legal sets from the spec; SinglePhase Planned (next Completed, prev Draft) and Completed (prev Planned, next null); undefined/empty `available` returns `{next: null, previous: null}`; Cancelled returns nulls. Also test `isStateTransitionBackward`.
2. Implement per the design: rank table Draft 0, Planned 1, SemiProductManufactured 2, Completed 3, Cancelled 4; candidates exclude Cancelled and (SinglePhase) SemiProductManufactured; next = lowest-rank candidate above current; previous = highest-rank candidate below current. Export `isStateTransitionBackward` too. Import `ManufactureOrderState` and `ManufactureType` from the generated client as the component does.
3. Verify: `cd frontend && npx jest stateTransitions` (or the repo's test runner), `npm run lint`.
Depends on: regenerate-api-client (types only).

