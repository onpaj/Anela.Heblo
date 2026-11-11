# Architecture Review: Backend-Driven Manufacture Order State Transitions

## Skip Design: true
No new or visually changed UI; the same buttons are rendered, only their data source changes.

## Architectural Fit Assessment
Fits Clean Architecture/vertical slice: the query handler in `Application/Features/Manufacture/UseCases/GetManufactureOrder` already loads the domain entity; calling a pure domain predicate there is idiomatic. Verified: `CanTransitionTo` is public, pure, in `ManufactureOrder.cs`; `GetManufactureOrderResponse : BaseResponse` is a class; `ManufactureOrderDto` is shared with the list response so must not be extended.

## Proposed Architecture

### Component Overview
Domain `ManufactureOrder.CanTransitionTo` -> `GetManufactureOrderHandler` (computes `AvailableTransitions`) -> `GetManufactureOrderResponse` -> generated TS client -> `ManufactureOrderDetail.tsx` (pure selector) -> `StateTransitionControls`.

### Key Design Decisions

#### Decision 1: Where the list lives
**Options considered:** field on `ManufactureOrderDto`; field on response; separate endpoint.
**Chosen approach:** field on `GetManufactureOrderResponse`.
**Rationale:** DTO is reused by list queries (extra computation, wider contract); a separate endpoint adds a round trip and staleness.

#### Decision 2: Keep SinglePhase/next/previous out of the backend
**Chosen approach:** backend returns the legal set only; frontend derives `next`/`previous` by ordering plus SinglePhase exclusion of SemiProductManufactured.
**Rationale:** the backend graph does not encode ManufactureType; the issue asks for legality to come from the backend. Moving workflow-shaping would change domain rules (out of scope). The selection rule is proven equivalent to current behaviour for all 5 states x 2 types.

#### Decision 3: Extract selector as a pure function
Put `deriveStateTransitions(currentState, available, manufactureType)` in a small file next to the component so it is unit-testable without rendering the large page.

## Implementation Guidance

### Directory / Module Structure
- Edit `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrder/GetManufactureOrderResponse.cs` and `GetManufactureOrderHandler.cs`.
- Add `frontend/src/components/manufacture/detail/stateTransitions.ts` (+ `__tests__/stateTransitions.test.ts`).
- Edit `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx` (remove `getStateTransitions`, use selector).
- Extend `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOrderHandlerTests.cs`.

### Interfaces and Contracts
`public List<ManufactureOrderState> AvailableTransitions { get; set; } = new();` Handler: `Enum.GetValues<ManufactureOrderState>().Where(s => s != order.State && order.CanTransitionTo(s)).OrderBy(s => (int)s).ToList()`.
Frontend: `deriveStateTransitions(state, available: ManufactureOrderState[] | undefined, type) => {next, previous}`. Use whatever the generated enum is (`ManufactureOrderState`) already imported by the component.

### Data Flow
Fetch order -> response includes `availableTransitions` -> hook data -> selector -> `StateTransitionControls` -> click triggers existing `handleStateChange` (unchanged).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale list after a state change | Low | Existing mutation already invalidates/refetches the order query; verify in tests/manual |
| Generated client lacks field until build | Low | Run backend build to regenerate; commit generated client per repo convention |
| Behavior regression for SinglePhase | Medium | Unit-test selector across all states x both types |
| Existing component tests mock the order without the field | Low | Selector handles undefined (no buttons); update mocks where buttons are asserted |

## Specification Amendments
None.

## Prerequisites
None. No migration. Read `docs/architecture/development_guidelines.md` (DTO rules) and `docs/development/api-client-generation.md`. Process-doc check: this is a read-model change, not a sync/job; if `scripts/process-docs/check.py` flags the manufacture doc, bump `verified_at`.
