### task: fix-set-active-error

**File:** `frontend/src/components/pages/access/UsersGrid.tsx`

**Change:** After line 200 (after the `setCanPack.isError` block), insert:

```tsx
{setActive.isError && (
  <p className="text-sm text-red-600">Failed to update user status. Please try again.</p>
)}
```

No other changes.
