## Implementation: fix-set-active-error

**File changed:** `frontend/src/components/pages/access/UsersGrid.tsx`

**Change:** Added the missing `setActive.isError` error paragraph after the existing `setCanPack.isError` block (line ~200), matching the exact pattern of the two existing error displays:

```tsx
{setActive.isError && (
  <p className="text-sm text-red-600">Failed to update user status. Please try again.</p>
)}
```

**Verification:**
- `npm run build` — passes, no TypeScript errors
- `eslint UsersGrid.tsx` — no issues
- Pre-existing lint errors elsewhere in the repo are unrelated to this change
