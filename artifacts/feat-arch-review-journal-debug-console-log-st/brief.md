## Module
Journal

## Finding
`frontend/src/components/JournalEntryForm.tsx` contains several `console.log` calls with emoji debug prefixes that were clearly left over from development:

```typescript
// Line 85
console.log("🐛 JournalEntryForm useEffect - entry:", entry, "isEdit:", isEdit);

// Line 88-94
console.log("🐛 Updating form with entry data:", {
  title: entryData.title,
  content: entryData.content,
  entryDate: entryData.entryDate,
  tags: entryData.tags,
  products: entryData.associatedProducts
});

// Line 107
console.log("🐛 Resetting form for new entry");
```

These log the full entry object (including `content`, `tags`, and associated products) to the browser console on every `useEffect` execution — which triggers on every modal open and on every edit.

## Why it matters
- **Data exposure**: journal entry content (potentially sensitive business notes) is printed to the browser console, visible to anyone with DevTools open.
- **Noise**: clutters the console in production and staging environments, making real errors harder to find.
- **Intent signal**: the `🐛` prefix confirms these are debugging artifacts, not intentional telemetry.

## Suggested fix
Delete lines 85–94 and line 107 of `frontend/src/components/JournalEntryForm.tsx`. No replacement needed — the `useEffect` logic itself is correct, only the log statements should be removed.

---
_Filed by daily arch-review routine on 2026-05-27._