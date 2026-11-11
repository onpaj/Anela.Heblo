# Implementation: add-handlesave-tests (r1)

Modified `frontend/src/components/__tests__/JournalEntryForm.test.tsx` only.

- Hoisted `mockNavigate` in the `react-router-dom` mock.
- Added imports: `ErrorCodes`, `CreateJournalEntryRequest`, `UpdateJournalEntryRequest`, `GetJournalEntryResponse`.
- Added `describe("JournalEntryForm - handleSave")` with 7 tests: empty title, whitespace content, cleared date, create (trimmed fields, onSave), navigate fallback, edit/update, InvalidJournalTitle, generic error (8 `it` blocks total covering the 7 design cases).
- Fixture note: generated `GetJournalEntryResponse` constructor ignores data, so the fixture uses `Object.assign(new GetJournalEntryResponse(), {...})`.

Verification (run via `react-scripts test`, since bare `npx jest` lacks the CRA transform):
- 13/13 tests pass (5 existing unchanged).
- JournalEntryForm.tsx line coverage 42% -> 72.89%.
- eslint on the test file: 0 errors (1 unused `act` warning from the existing import line). Full `npm run lint` reports many pre-existing errors in other files, none in this file.
