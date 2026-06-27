# Design: Dark Mode Fix – RecurringJobsPage Happy-Path Render

## UX/UI Design

The fix closes a visual parity gap between the happy-path branch and the already-correct error/empty-state branches of `RecurringJobsPage.tsx`.

**Before (broken)**

```
Light mode                       Dark mode
─────────────────────────────    ─────────────────────────────
[Správa Recurring Jobs]          [Správa Recurring Jobs]   ← gray-900 on dark bg (unreadable)
┌──────────────────────────┐     ┌──────────────────────────┐
│  bg-white / shadow       │     │  bg-white / shadow       │  ← white card visible on dark bg
│  ...table content...     │     │  ...table content...     │
└──────────────────────────┘     └──────────────────────────┘
```

**After (fixed)**

```
Light mode                       Dark mode
─────────────────────────────    ─────────────────────────────
[Správa Recurring Jobs]          [Správa Recurring Jobs]   ← graphite-text (readable)
┌──────────────────────────┐     ┌──────────────────────────┐
│  bg-white / shadow       │     │  bg-graphite-surface     │  ← surface token + soft-dark shadow
│  ...table content...     │     │  ...table content...     │
└──────────────────────────┘     └──────────────────────────┘
```

No interaction, layout, or spacing changes. The fix is purely additive Tailwind variant pairs.

## Component Design

Only one component is affected.

**`RecurringJobsPage.tsx`** — `frontend/src/pages/RecurringJobsPage.tsx`

The happy-path `return` block (line 165 onward) contains two elements that need dark-mode variants:

| Element | Location | Change |
|---------|----------|--------|
| `<h1>` page heading | line 169 | add `dark:text-graphite-text` |
| Main content `<div>` | line 173 | add `dark:bg-graphite-surface dark:shadow-soft-dark` |

No new components, hooks, or props are introduced. No interface or contract changes.

## Data Schemas

Not applicable — this change is CSS-only with no data layer impact.
