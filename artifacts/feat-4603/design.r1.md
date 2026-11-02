# Design: JournalEntryForm tests

No UI change. Test design only.

## Setup
- Hoist `const mockNavigate = jest.fn();` and use it in the `react-router-dom` mock factory.
- Per-test mocks: `createMutateAsync`, `updateMutateAsync` as `jest.fn()`, wired into `mockUseCreateJournalEntry` / `mockUseUpdateJournalEntry` return values in `beforeEach`.
- Helper `fillValidForm(user)`: type into `#title` (label "Název záznamu *") and `#content` textarea; date defaults to today.
- Helper `entryFixture`: `{ entry: { id: 7, title: "Old", content: "Body", entryDate: new Date("2024-05-01"), tags: [], associatedProducts: [] } } as GetJournalEntryResponse`.
- Query elements via `container.querySelector("#title")` or `getByLabelText`; save button via `getByRole("button", {name: /Vytvořit záznam|Uložit změny/})`.

## Cases (describe "JournalEntryForm - handleSave")
1. Empty title -> "Název je povinný", no mutations.
2. Empty/whitespace content -> "Obsah je povinný", no mutations.
3. Cleared date -> "Datum je povinné", no mutations.
4. Create: valid form -> createMutateAsync called with CreateJournalEntryRequest (trimmed), update not called; onSave called with result; without onSave, mockNavigate("/journal").
5. Edit: isEdit + entry -> updateMutateAsync called with `{id: 7, request: UpdateJournalEntryRequest}`, create not called; button text "Uložit změny".
6. InvalidJournalTitle rejection -> "Název je povinný" shown, console.error not called, onSave not called.
7. Generic rejection -> console.error called with "Error saving journal entry:", no field error text, onSave not called.
