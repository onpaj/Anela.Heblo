# Specification: JournalEntryForm handleSave / validateForm test coverage

## Summary
Add Jest/RTL tests for `frontend/src/components/JournalEntryForm.tsx` covering `validateForm`, the create-vs-edit branch of `handleSave`, and the catch-block error handling (`ErrorCodes.InvalidJournalTitle`). Test-only change; no production code changes.

## Background
Line coverage is 42.1%. The existing `frontend/src/components/__tests__/JournalEntryForm.test.tsx` only covers associated-product UI. Note: the brief mentions a "toast" for other API errors, but the component has none: the catch block only calls `console.error("Error saving journal entry:", error)` and sets no field error. Tests assert the actual behaviour.

## Functional Requirements

### FR-1: validateForm guards
Clicking the save button (`Vytvořit záznam`) with an empty title, content or date blocks submission.
**Acceptance criteria:**
- Empty title shows "Název je povinný"; neither mutation called.
- Empty content shows "Obsah je povinný"; neither mutation called.
- Cleared date shows "Datum je povinné"; neither mutation called.
- Whitespace-only title/content are treated as empty.

### FR-2: Create branch
**Acceptance criteria:**
- With `isEdit` false and valid input, `createMutation.mutateAsync` is called once with a `CreateJournalEntryRequest` containing trimmed title/content; `updateMutation.mutateAsync` is not called.
- On success `onSave` is called with the result when provided; otherwise `navigate("/journal")` is called.

### FR-3: Edit branch
**Acceptance criteria:**
- With `isEdit` true and `entry.entry` set, `updateMutation.mutateAsync` is called with `{id, request: UpdateJournalEntryRequest}`; create is not called.
- `onSave`/navigate behaviour as in FR-2.
- (Edge) `isEdit` true but `entry.entry` missing falls through to create.

### FR-4: InvalidJournalTitle error
**Acceptance criteria:**
- When mutateAsync rejects with `{errorCode: ErrorCodes.InvalidJournalTitle}` the title field error "Název je povinný" is rendered, `onSave` not called, `console.error` not called.

### FR-5: Other errors
**Acceptance criteria:**
- When mutateAsync rejects with another error, `console.error` is called with "Error saving journal entry:", no field error is rendered, `onSave` not called.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in well under a second each; no real timers/network.
### NFR-2: Security
N/A.

## Data Model
None changed. Uses `GetJournalEntryResponse`, `JournalEntryDto` from the generated client.

## API / Interface Design
Mock `useCreateJournalEntry`, `useUpdateJournalEntry`, `useJournalTags`, `useCreateJournalTag` (already mocked via `jest.mock("../../api/hooks/useJournal")`). `react-router-dom` mock must expose a shared `mockNavigate` so navigation can be asserted.

## Dependencies
Jest, React Testing Library, user-event (already in use).

## Out of Scope
Production code changes; adding a toast; tag-creation and product-association paths.

## Open Questions
None.

## Status: COMPLETE
