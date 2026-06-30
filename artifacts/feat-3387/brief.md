## Module
BackgroundJobs (Frontend)

## Finding
`frontend/src/pages/RecurringJobsPage.tsx` has two elements in the normal (non-error, non-empty) render path that lack `dark:` Tailwind variants, violating ADR-006 which requires every color-bearing class to render correctly in both light and dark mode.

**Line 169 — page heading:**
```tsx
<h1>Správa Recurring Jobs</h1>
```
Missing: `dark:text-graphite-text`

**Line 173 — main content card:**
```tsx

```
Missing: `dark:bg-graphite-surface dark:shadow-soft-dark`

The error-state and empty-state branches for the exact same elements already include the correct variants (lines 119 and 122–123):
```tsx
// error state — correct
<h1>

```

So dark mode works in error/empty but breaks in the happy path: the heading is unreadable and the card background stays white in the dark theme.

## Why it matters
ADR-006 (accepted 2026-06-25): every component that renders color must render correctly in both light and Graphite dark mode. The happy path is the most-used code path, so broken dark mode here is highly visible.

## Suggested fix
Apply the missing `dark:` variants to the happy-path heading and card, matching what the error and empty states already do:

```tsx
// line 169
<h1>Správa Recurring Jobs</h1>

// line 173

```

---
_Filed by daily arch-review routine on 2026-06-26._
