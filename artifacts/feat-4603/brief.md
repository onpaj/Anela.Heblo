# Coverage gap — `frontend/src/components/JournalEntryForm.tsx`

**Line coverage:** 42.1% | **CI run:** 41210660028

## What is untested

The existing test (`frontend/src/components/__tests__/JournalEntryForm.test.tsx`) focuses on product-association UI and does not exercise `handleSave`.

Untested paths in `handleSave`:

1. **Create vs. edit branch** — `isEdit && entry?.entry` decides whether to call the update or create mutation. The update path is never exercised.
2. **Error-code branch** — on API failure, the catch block checks for `ErrorCodes.InvalidJournalTitle` and sets a field-level error rather than a toast. If this check is wrong, the user sees a generic error instead of an inline validation message:
   ```ts
   catch (error) {
     if ((error as any)?.errorCode === ErrorCodes.InvalidJournalTitle) {
       setErrors((prev) => ({ ...prev, title: "Název je povinný" }));
       return;
     }
   }
   ```
3. **`validateForm()` branches** — three independent guards (title, content, date) each set field errors and return early. None are tested.

## Suggested tests

1. Edit mode — submit a valid form, verify the update mutation is called (not create).
2. Create mode — submit a valid form, verify the create mutation is called.
3. `InvalidJournalTitle` API error — verify a title field error is shown and no toast fires.
4. Other API error — verify a toast (or generic error) is shown and no field error is set.
5. Empty title / content / date — verify `validateForm` blocks submission with the right field errors.
