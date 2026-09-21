# OrgChartPage Outer Wrapper Dark Mode Fix Implementation Plan

**Goal:** Add the missing `dark:` Tailwind variant to `OrgChartPage.tsx`'s outermost wrapper so it uses the Graphite dark-theme token (`graphite-bg`) instead of the light-only indigo/purple gradient, per ADR-006.

**Architecture:** Single-line, single-file Tailwind utility-class addition. No component, state, prop, or data-flow changes. Reuses the exact `dark:from-graphite-bg dark:to-graphite-bg` pattern already present one element below in the same file (line 276), matching the existing `graphite-bg` token defined in `frontend/tailwind.config.js`.

**Tech Stack:** React, TypeScript, Tailwind CSS (`darkMode: 'class'`), `ThemeContext`.

---

### task: add-dark-mode-wrapper-class

**Files:**
- Modify: `frontend/src/pages/OrgChartPage.tsx:195`

**Context for the engineer:**
`OrgChartPage.tsx`'s outermost `<div>` (line 195) currently reads:

```tsx
<div className="h-screen bg-gradient-to-br from-indigo-500 to-purple-600 flex flex-col">
```

This has no `dark:` variant, violating ADR-006 ("Light and Dark Mode Required for Every Frontend Component", `docs/architecture/development_guidelines.md`). Because this wrapper is `h-screen` while its children (controls bar at line 197, chart canvas at line 276) can exceed viewport height, the bright light-mode gradient bleeds through during scroll/overscroll and through any gap between children — clashing badly with the Graphite dark theme.

The fix is additive only: append `dark:from-graphite-bg dark:to-graphite-bg` to the existing class list. This is the identical pattern already used at line 276 in this same file, and `graphite-bg` is already defined in `frontend/tailwind.config.js` — no new token, no config change, no other line in the file is touched.

- [ ] **Step 1: Confirm the exact current line and its established sibling pattern**

Run:
```bash
grep -n "bg-gradient-to-br from-indigo-500 to-purple-600" frontend/src/pages/OrgChartPage.tsx
grep -n "dark:from-graphite-bg dark:to-graphite-bg" frontend/src/pages/OrgChartPage.tsx
```
Expected: the first command shows the outer wrapper's `className` (around line 195) with no `dark:` variant present on that line; the second command shows the chart-canvas wrapper (around line 276) already using `dark:from-graphite-bg dark:to-graphite-bg`. If either line number has drifted from what's documented above, use the line the grep reports — don't guess.

- [ ] **Step 2: Make the one-line edit**

Change:
```tsx
<div className="h-screen bg-gradient-to-br from-indigo-500 to-purple-600 flex flex-col">
```
to:
```tsx
<div className="h-screen bg-gradient-to-br from-indigo-500 to-purple-600 dark:from-graphite-bg dark:to-graphite-bg flex flex-col">
```
Do not touch any other line, class, or element in this file — the controls bar (line 197) and chart canvas wrapper (line 276) are already correctly themed and are out of scope.

- [ ] **Step 3: Verify the class list is syntactically correct and nothing else changed**

Run:
```bash
git -C frontend diff -- src/pages/OrgChartPage.tsx
```
Expected: exactly one changed line, adding only `dark:from-graphite-bg dark:to-graphite-bg` to the outer wrapper's `className`. No other diff hunks.

- [ ] **Step 4: Build the frontend**

Run:
```bash
cd frontend && npm run build
```
Expected: build succeeds with no new TypeScript or Tailwind errors.

- [ ] **Step 5: Lint the frontend**

Run:
```bash
cd frontend && npm run lint
```
Expected: no new lint errors introduced by this change.

- [ ] **Step 6: Manually verify both themes**

Per ADR-006's verification step (`memory/decisions/light-dark-mode-required.md`), start the dev server (or use the Playwright dev instance on `:3100`) and toggle the theme on the OrgChart page:
- Light mode: the outer wrapper still shows the indigo-to-purple gradient, pixel-identical to before this change.
- Dark mode: the outer wrapper shows the flat `graphite-bg` color (matching the chart canvas area beneath it) with no bright gradient visible, including while scrolling/overscrolling the page.

There is no automated test for this visual-only Tailwind change (no new WCAG contrast requirement is introduced, per spec NFR-1) — manual verification in both themes is the acceptance check.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/pages/OrgChartPage.tsx
git commit -m "fix(orgchart): add dark mode variant to outer wrapper (ADR-006)"
```

## Self-Review

**1. Spec coverage:** FR-1 (add `dark:` override to the outer wrapper, preserve light mode, match the line-276 pattern) is covered by Steps 1–3 and 6. NFR-1 (visual consistency with `graphite-bg`) is covered by using the exact token from line 276, verified in Step 1 and checked visually in Step 6. NFR-2 (no regression, additive-only, no new deps/API surface) is enforced by Step 3's diff check and Steps 4–5's build/lint gates. No spec requirement lacks a task.

**2. Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" placeholders. All code shown is the actual before/after class list, not a description of it.

**3. Type consistency:** No new types, functions, or method signatures are introduced anywhere in this plan — the only symbol used (`graphite-bg`) is a Tailwind token already defined in `frontend/tailwind.config.js` and already consumed identically at `OrgChartPage.tsx:276`, so there is nothing to drift out of sync across tasks.
