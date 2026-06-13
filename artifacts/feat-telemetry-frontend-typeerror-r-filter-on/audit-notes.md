# Telemetry Frontend TypeError r-filter-on Audit Notes

## JournalList audit

Array method calls found in `frontend/src/components/pages/Journal/JournalList.tsx`:

### Line 77: tags.slice() + map()
- **File:** JournalList.tsx:77
- **Variable:** `tags`
- **Method:** `slice(0, 2).map(...)`
- **Guard kind:** Guarded with conditional rendering (`{tags && tags.slice(...)`)
- **Context:** In JournalRow component, tags is a prop with optional array type
- **Verdict:** SAFE — Guarded by logical AND before use

### Line 99-100: associatedProducts.slice() + map()
- **File:** JournalList.tsx:99-100
- **Variable:** `associatedProducts`
- **Method:** `?.slice(0, 2).map(...)`
- **Guard kind:** Optional chaining (`?.slice()`) + logical AND check before rendering
- **Context:** In JournalRow component, associatedProducts is a prop with optional array type
- **Verdict:** SAFE — Protected by optional chaining

### Line 202: entries from OR pattern
- **File:** JournalList.tsx:202
- **Variable:** `entries`
- **Assignment:** `const entries = currentQuery.data?.entries || []`
- **Guard kind:** Fallback to empty array (`|| []`)
- **Context:** currentQuery.data is nullable, entries is extracted with optional chaining and OR guard
- **Verdict:** SAFE — entries is guaranteed to be an array (either from data.entries or empty array)

### Line 422: entries.map() for SearchJournalEntryDto
- **File:** JournalList.tsx:422
- **Variable:** `entries` (cast to SearchJournalEntryDto[])
- **Method:** `map(...)`
- **Guard kind:** Type assertion `(entries as SearchJournalEntryDto[])`
- **Context:** entries is already guarded at line 202; render is conditional inside `entries.length === 0 ? ... : ...`
- **Verdict:** SAFE — entries is guaranteed to be array from line 202 guard, type assertion is correct for ternary branch

### Line 435: entries.map() for JournalEntryDto
- **File:** JournalList.tsx:435
- **Variable:** `entries` (cast to JournalEntryDto[])
- **Method:** `map(...)`
- **Guard kind:** Type assertion `(entries as JournalEntryDto[])`
- **Context:** entries is already guarded at line 202; render is conditional inside `entries.length === 0 ? ... : ...`
- **Verdict:** SAFE — entries is guaranteed to be array from line 202 guard, type assertion is correct for alternate branch

## Summary

**All array method calls are SAFE.** No fixes required.

The critical guard is at line 202:
```typescript
const entries = currentQuery.data?.entries || [];
```

This pattern ensures `entries` is always an array (either the data or an empty array), making all downstream `.map()` calls safe regardless of shape drift in the React Query response.

The component properly follows the established pattern:
- Guards at the React Query consumer boundary (line 202)
- Subsequent array method calls operate on the guarded local variable
- Optional chaining used for nested optional arrays (tags, associatedProducts)
