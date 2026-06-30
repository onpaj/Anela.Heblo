# Architecture Review: Dark Mode Fix – RecurringJobsPage Happy-Path Render

## Skip Design: true
The fix adds two missing Tailwind `dark:` variants to existing elements. No new visual components, layouts, or design decisions are required — the correct tokens are already defined and used correctly on the sibling error and empty-state branches.

## Architectural Fit Assessment

This is a single-file, two-line correctness fix that aligns with ADR-006 (every color-bearing Tailwind class must carry a `dark:` counterpart). The pattern being applied — `text-gray-900 dark:text-graphite-text` for headings and `bg-white shadow dark:bg-graphite-surface dark:shadow-soft-dark` for content cards — is already established in the same file at lines 119 and 123 (error branch) and lines 151 and 155 (empty-state branch). The fix closes the parity gap on the happy-path branch at lines 169 and 173.

All design tokens used (`graphite-text`, `graphite-surface`, `shadow-soft-dark`) are confirmed present in `frontend/tailwind.config.js`. No new tokens, no config changes, and no architectural changes are required.

## Proposed Architecture

### Component Overview

```
RecurringJobsPage.tsx
  ├── Loading branch  (line 106)  — no color-bearing elements, no changes needed
  ├── Error branch    (line 114)  — CORRECT: dark: variants present
  ├── Empty branch    (line 146)  — CORRECT: dark: variants present
  └── Happy branch    (line 165)  — BROKEN: missing dark: variants on two elements
        ├── <h1> line 169        — fix: add dark:text-graphite-text
        └── <div> line 173       — fix: add dark:bg-graphite-surface dark:shadow-soft-dark
```

### Key Design Decisions

#### Decision 1: Apply tokens that already exist on sibling branches verbatim
**Options considered:**
- Add the same `dark:` classes already present on the error and empty-state branches.
- Audit the file for any other missing variants before making changes.

**Chosen approach:** Apply only the two missing variant pairs identified in the spec; no broader audit in this changeset.

**Rationale:** The spec explicitly scopes this to two elements. An audit of other components or pages is out of scope and belongs in a separate ADR-006 compliance sweep. Surgical changes reduce review surface and risk.

## Implementation Guidance

### Directory / Module Structure

Only one file requires modification:

```
frontend/src/pages/RecurringJobsPage.tsx
```

No new files. No directory changes.

### Interfaces and Contracts

No interfaces, types, or API contracts are affected. This is a pure presentational change.

### Data Flow

No data flow changes. The fix is limited to JSX class strings in the return statement of the happy-path render branch.

**Exact changes required:**

**Line 169** — replace:
```tsx
<h1 className="text-lg font-semibold text-gray-900">Správa Recurring Jobs</h1>
```
with:
```tsx
<h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Správa Recurring Jobs</h1>
```

**Line 173** — replace:
```tsx
<div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
```
with:
```tsx
<div className="flex-1 bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg overflow-hidden flex flex-col min-h-0">
```

Both changes must match the class order already used on lines 123 and 155 exactly, so diffs stay readable and reviewable.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Typo in token name causing silent light-mode fallback | Low | Tokens `graphite-text`, `graphite-surface`, and `shadow-soft-dark` are all confirmed in `tailwind.config.js`; a typo produces no build error but is caught by visual review in dark mode |
| Touching wrong branch (error/empty instead of happy-path) | Low | All three render branches share near-identical JSX structure; confirm the edit target is the `return` after line 165, not lines 114–142 or 146–163 |
| Extraneous class changes invalidating "no other classes changed" acceptance criteria | Low | Limit diff to the two specified elements; do not reformat surrounding code |

## Specification Amendments

None required. The spec is precise and complete for the scope of this change.

## Prerequisites

None. All Tailwind tokens are already registered in `frontend/tailwind.config.js`. No migrations, config updates, or infrastructure changes are needed before implementation can start.
