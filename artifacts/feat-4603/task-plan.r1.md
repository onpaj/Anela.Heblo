# Task Plan: JournalEntryForm test coverage (#4603)

Single file touched: `frontend/src/components/__tests__/JournalEntryForm.test.tsx`. No production changes.

### task: add-handlesave-tests

**Goal:** Cover validateForm, create/edit branches and error handling of `JournalEntryForm.handleSave`.

**Files:**
- Modify: `/frontend/src/components/__tests__/JournalEntryForm.test.tsx`

**Steps:**
1. Change the `react-router-dom` mock to use a hoisted `mockNavigate` (`const mockNavigate = jest.fn(); jest.mock("react-router-dom", () => ({ useNavigate: () => mockNavigate }));`). Confirm existing tests still pass: `cd frontend && npx jest src/components/__tests__/JournalEntryForm.test.tsx`.
2. Add imports: `ErrorCodes`, `CreateJournalEntryRequest`, `UpdateJournalEntryRequest`, `GetJournalEntryResponse` from `../../api/generated/api-client`.
3. Add `describe("JournalEntryForm - handleSave")` with `beforeEach` wiring `createMutateAsync` / `updateMutateAsync` jest.fns into the hook mocks (plus tags, createTag, catalog autocomplete mocks as in the existing describe), clearing `mockNavigate`.
4. Write validation tests (empty title, whitespace content, cleared date via `fireEvent.change`) asserting the Czech messages and that neither mutation was called.
5. Write create test (resolved value `{id: 1}`; assert `toBeInstanceOf(CreateJournalEntryRequest)`, trimmed fields, `onSave` called) and navigate fallback test (no onSave -> `mockNavigate("/journal")`).
6. Write edit test (`isEdit`, `entryFixture`; assert update called with `{id: 7, request: expect.any(UpdateJournalEntryRequest)}`, create not called).
7. Write InvalidJournalTitle test (reject `{errorCode: ErrorCodes.InvalidJournalTitle}`; title error shown; `console.error` spy not called; onSave not called) and generic error test (reject `new Error("boom")`; console.error called with "Error saving journal entry:"; no "Název je povinný" text; onSave not called). Spy via `jest.spyOn(console, "error").mockImplementation(() => {})`, restored in `afterEach`.
8. Run: `cd frontend && npx jest src/components/__tests__/JournalEntryForm.test.tsx --coverage --collectCoverageFrom=src/components/JournalEntryForm.tsx`; confirm all pass and line coverage materially above 42%. Run `npm run lint`.

**Acceptance:** all 7 design cases pass; existing tests unchanged and passing; lint clean.
