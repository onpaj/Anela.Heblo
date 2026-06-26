## Module
Journal

## Finding
`JournalEntryForm.tsx` validates that `title` is non-empty and blocks save if it is:

```typescript
// frontend/src/components/JournalEntryForm.tsx:111-113
if (!title.trim()) {
  newErrors.title = "Název je povinný";
}
```

The backend contract and domain entity both declare title as explicitly **optional**:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs:12
public string? Title { get; set; }

// backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:14
public string? Title { get; set; }
```

The handler stores it as nullable (`Title = request.Title?.Trim()`). The backend intentionally supports entries without a title.

**Concrete impact**: if an entry without a title exists in the database (created via the API, a script, or a future import), opening it in the edit modal produces an immediate "Název je povinný" validation error. The save button becomes inert until the user adds a title — even if they wanted to change only the content or tags. This is a silent data trap: the form appears to work until it doesn't.

## Why it matters
Frontend and backend contract layers disagree on what constitutes a valid entry. When they diverge, one of two things is true: either the backend contract is wrong (title should be required and the `[Required]` annotation is missing from `CreateJournalEntryRequest`) or the frontend validation is wrong (title is optional and the `if (!title.trim())` guard should be removed). Both interpretations are fixable, but the current state is inconsistent.

## Suggested fix
Decide which intent is correct and make both layers match:

**Option A — Title is required by design**: add `[Required]` to `CreateJournalEntryRequest.Title` and change `string? Title` to `string Title` in the request DTO and the domain entity. The frontend validation is then correct.

**Option B — Title is genuinely optional**: remove the `if (!title.trim())` guard from `JournalEntryForm.tsx:111-113` (and the corresponding `errors.title` rendering at line 254). The backend is then correct as-is.

Option A is the safer default if a title is always expected from the UI.

---
_Filed by daily arch-review routine on 2026-06-10._