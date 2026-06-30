## Module
Users (frontend)

## Finding
`frontend/src/components/pages/access/UsersGrid.tsx` shows inline error messages for two of its three mutations but silently drops the third.

```tsx
// Line 195-196 — shown:
{createLocalUser.isError && (
  <p>Failed to create operator. Please try again.</p>
)}
// Line 197-200 — shown:
{setCanPack.isError && (
  <p>Failed to update packing permission. Please try again.</p>
)}
// Lines 288-300 — setActive.mutate() is called but setActive.isError is never rendered
```

When the toggle-active/disable button (line 288) fails, no error is surfaced to the user. The mutation silently errors while the button label stays at its pre-click state (because the query cache is only invalidated on success).

## Why it matters
Toggling a user between active and disabled is a privileged, consequential action. Silently dropping its failure leaves the operator believing the action succeeded when it did not. The inconsistency is visible within the same component — the other two mutations both surface errors — so this is a gap rather than a deliberate design choice. Violates "missing error/loading states" for frontend code.

## Suggested fix
Add the missing error paragraph alongside the other two, in the filters/create-form card (lines 195-200):

```tsx
{setActive.isError && (
  <p>Failed to update user status. Please try again.</p>
)}
```

Minimal change, matches the existing error display pattern exactly.

---
_Filed by daily arch-review routine on 2026-06-23._
