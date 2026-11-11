# Implementation Plan: Backend-Driven Manufacture Order State Transitions

Goal: backend returns `availableTransitions`; frontend derives controls from it. Follow `CLAUDE.md` (DTOs are classes, surgical changes). Tests first.

### task: backend-available-transitions
Files:
- Modify `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrder/GetManufactureOrderResponse.cs`
- Modify `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrder/GetManufactureOrderHandler.cs`
- Modify `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOrderHandlerTests.cs`

Steps:
1. Add failing tests in `GetManufactureOrderHandlerTests`: a `[Theory]` over each `ManufactureOrderState` asserting `AvailableTransitions` equals the expected legal set (Draft: Planned,Cancelled; Planned: Draft,SemiProductManufactured,Completed,Cancelled; SemiProductManufactured: Planned,Completed,Cancelled; Completed: SemiProductManufactured,Planned,Cancelled; Cancelled: empty). Build the order with `InitializeState`; mock the mapper as existing tests do. Add a test that a not-found response has an empty list.
2. Add `public List<ManufactureOrderState> AvailableTransitions { get; set; } = new();` to `GetManufactureOrderResponse` (add `using Anela.Heblo.Domain.Features.Manufacture;`).
3. In the handler set `AvailableTransitions = Enum.GetValues<ManufactureOrderState>().Where(s => s != order.State && order.CanTransitionTo(s)).OrderBy(s => (int)s).ToList()`.
4. Verify: `dotnet test backend --filter GetManufactureOrderHandlerTests`, `dotnet build`, `dotnet format`.

### task: regenerate-api-client
Files:
- Modify (generated) `frontend/src/api/generated/api-client.ts`

Steps:
1. Follow `docs/development/api-client-generation.md`: build the backend / run the frontend build so `GetManufactureOrderResponse` exposes `availableTransitions?: ManufactureOrderState[]`.
2. Confirm with grep that the field exists in `GetManufactureOrderResponse`. Commit the generated file only if it is tracked by git.
Depends on: backend-available-transitions.

### task: frontend-transition-selector
Files:
- Create `frontend/src/components/manufacture/detail/stateTransitions.ts`
- Create `frontend/src/components/manufacture/detail/__tests__/stateTransitions.test.ts`

Steps:
1. Write failing unit tests for `deriveStateTransitions(current, available, manufactureType)` covering: MultiPhase Draft/Planned/SemiProductManufactured/Completed/Cancelled with the backend legal sets from the spec; SinglePhase Planned (next Completed, prev Draft) and Completed (prev Planned, next null); undefined/empty `available` returns `{next: null, previous: null}`; Cancelled returns nulls. Also test `isStateTransitionBackward`.
2. Implement per the design: rank table Draft 0, Planned 1, SemiProductManufactured 2, Completed 3, Cancelled 4; candidates exclude Cancelled and (SinglePhase) SemiProductManufactured; next = lowest-rank candidate above current; previous = highest-rank candidate below current. Export `isStateTransitionBackward` too. Import `ManufactureOrderState` and `ManufactureType` from the generated client as the component does.
3. Verify: `cd frontend && npx jest stateTransitions` (or the repo's test runner), `npm run lint`.
Depends on: regenerate-api-client (types only).

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
