# Arch review r1

Options for testing a non-exported closure:
- A. Extract to a pure function (e.g. `getIsFormEditable(state, fieldType)`) exported from the same file or a sibling `transportBoxEditability.ts`, component calls it. Pure unit tests, 9 table-driven cases. Tiny, surgical refactor, no behaviour change.
- B. Test via rendering `TransportBoxDetail` with mocked hooks (existing `__tests__/TransportBoxDetail.test.tsx` already does this) and assert which controls render per state. No source change but indirect; "notes"/"boxNumber"/"items" controls must be mapped to UI elements; heavier.

Recommendation: A, since the issue calls for pure-function unit tests (XS) and project guidance is surgical changes. Keep the component's inner `isFormEditable` as a thin wrapper `(f) => getIsFormEditable(state, f)` so child-component props (`TransportBoxTypes.tsx`) are unchanged. Place helper in `frontend/src/components/transport/box-detail/` per filesystem.md conventions; test in its `__tests__/` folder (Jest, matches existing tests).
Risks: low. No API/DTO/OpenAPI impact. No process-doc impact. State type: use string (generated client state is string-like); verify during implementation.
