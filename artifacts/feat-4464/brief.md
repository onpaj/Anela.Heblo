## Module
Marketing

## Finding
`MarketingCalendarPage.tsx` maps API list/detail responses to the local `MarketingActionDto` type but passes `folderLinks` through without renaming `folderKey` to `path`:

```tsx
// frontend/src/components/marketing/pages/MarketingCalendarPage.tsx:127-138
const listActions: MarketingActionDto[] = useMemo(
  () =>
    (listQuery.data?.actions ?? []).map((a: ApiMarketingActionDto) => ({
      // ...
      folderLinks: a.folderLinks,  // ← raw API type: { folderKey, folderType }
    })),
  [listQuery.data],
);
```

And in the `detailQuery` effect (lines 197–210):
```tsx
setEditingAction({
  // ...
  folderLinks: a.folderLinks,  // same raw pass-through
});
```

`MarketingActionModal.tsx` then initializes the form from `existingAction.folderLinks` by reading `fl.path`:
```tsx
// frontend/src/components/marketing/detail/MarketingActionModal.tsx:124-128
folderLinks: (existingAction.folderLinks ?? []).map((fl) => ({
  path: fl.path ?? "",  // ← fl.path is always undefined; the actual field is fl.folderKey
  label: fl.label ?? "",
  folderType: resolveFolderType(fl.folderType),
})),
```

Because `fl.path` is `undefined` (the API object has `folderKey`), every existing folder link is initialized with `path = ""`. When the user saves without re-typing paths, the submit filter removes them:
```tsx
folderLinks: form.folderLinks
  .filter((fl) => fl.path.trim())  // "" → falsy → all links dropped
  .map(...)
```

**Result**: opening any action that has folder links for editing and saving without touching the folder section silently deletes all folder links.

## Why it matters
Data loss on edit. A user updates an action's title or dates, sees folder links still displayed in the form, saves — and loses all folder link associations without any warning. The bug is invisible: TypeScript does not flag it because `path` is declared as optional in the local type.

## Suggested fix
Add the `folderKey → path` rename in the two mapping sites in `MarketingCalendarPage.tsx`:
```tsx
folderLinks: (a.folderLinks ?? []).map((fl) => ({
  path: fl.folderKey ?? "",
  folderType: fl.folderType,
})),
```
No backend changes needed.

---
_Filed by daily arch-review routine on 2026-10-09._
