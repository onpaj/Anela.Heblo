# Arch Review r1 — feat-3365

## Assessment

This is a pure presentation-layer change: adding `dark:` Tailwind utility variants to six React
components. No new components, no new abstractions, no data flow changes, no backend impact.

## Architectural concerns

**None blocking.**

### HtmlContent iframe

The `srcdoc` approach for `HtmlContent` renders article HTML in an isolated document with inline
styles. Tailwind `dark:` classes cannot reach inside the iframe. Reading
`document.documentElement.classList.contains('dark')` at render time is the correct strategy and is
consistent with the class-based dark-mode approach (`darkMode: 'class'` in tailwind.config.js).
A live `MutationObserver` is not required by ADR-006 for this MVP iteration; theme switches without
unmounting `ArticleDetail` are an edge case. The simpler static read at render time is acceptable.

### STATUS_COLORS duplication

Issue #3366 (filed alongside this one) tracks the existing `STATUS_COLORS` / `STATUS_LABELS`
duplication between `ArticleDetail.tsx` and `ArticleList.tsx`. This task should **not** consolidate
them — doing so would widen the scope and potentially conflict with whatever approach #3366 takes.
Add `dark:` variants in place in both files independently.

### Tailwind class scanning

All six files are under `frontend/src/features/articles/` which is covered by
`"./src/**/*.{js,jsx,ts,tsx}"` in `tailwind.config.js`. Full class name strings (not dynamically
constructed fragments) are required in source — the spec's class tables use complete literal strings,
so the scanner will detect them correctly.

### `dark:bg-graphite-accent` on submit button

`graphite-accent` (#38BDF8, a sky-blue) is not designed as a button background — it is an
accent/link colour. In dark mode the blue-600 button should remain a blue; `dark:bg-blue-600` (the
same class) with `dark:hover:bg-blue-700` is the correct choice. Avoid mapping the submit button to
`graphite-accent` background.

## Decision

Proceed with the spec as written, with one correction: the submit button in `ArticleGenerationForm`
should keep `bg-blue-600 hover:bg-blue-700` in dark mode (i.e., no dark: override on the button
background colour). The button already reads as blue in dark mode; overriding it to sky-blue would
be inconsistent with the rest of the UI.

All other spec items are approved as-is.
