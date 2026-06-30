## Spec: Fix missing setActive error display in UsersGrid

**Goal:** Surface mutation errors from `setActive` to the user in `UsersGrid.tsx`, consistent with how `createLocalUser` and `setCanPack` errors are already shown.

**Scope:** Single-line addition in `frontend/src/components/pages/access/UsersGrid.tsx`, inserting `{setActive.isError && <p>...</p>}` alongside the existing error paragraphs at lines 195-200.

**Acceptance criteria:**
- When `setActive.mutate(...)` fails, a red error paragraph appears below the form card.
- No other behaviour changes.
