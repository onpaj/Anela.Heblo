# Implementation: dark-mode-leaflet-generate-tab

## What was implemented
Added Tailwind `dark:` class variants to `LeafletGenerateTab.tsx` so the error banners and the
inline loading-skeleton bars render correctly under the Graphite dark theme, per
`docs/design/dark-mode-conversion-guide.md`.

- Error banner (`insufficient` variant): `bg-amber-100 text-amber-900` →
  `bg-amber-100 text-amber-900 dark:bg-amber-900/30 dark:text-amber-300`.
- Error banner (`transient`/other variant): `bg-red-100 text-red-900` →
  `bg-red-100 text-red-900 dark:bg-red-900/30 dark:text-red-300`.
- Three loading-skeleton bars: each `bg-gray-200` base now also has `dark:bg-graphite-hover`.

No other markup, logic, or the `LeafletForm`/`LeafletResult` components were touched. The
pre-existing `(response as any).id` cast was left untouched per scope instructions.

## Files created/modified
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` — added `dark:` Tailwind
  variants to the error-banner ternary and the three skeleton-bar `className`s.

## Tests
No new tests required for this styling-only change. Ran `npm run build` (compiled successfully,
no new warnings) and `npm run lint` (0 issues in this file; the 148 pre-existing lint errors in
the repo are all in unrelated test files, confirmed unaffected by this change).

## How to verify
1. `cd frontend && npm run build` — should compile successfully.
2. `npm run lint` — should show no errors/warnings for `LeafletGenerateTab.tsx`.
3. Run the app, switch to Graphite dark theme, open the Leaflet Generator "Generate" tab:
   - Trigger a 422 (insufficient KB) response → banner should show `amber-900/30` background with
     `amber-300` text.
   - Trigger any other error → banner should show `red-900/30` background with `red-300` text.
   - While a generation request is in flight, the three skeleton bars should render with
     `graphite-hover` background, visually distinct from the page background.

## Notes
No deviations from the task spec. Class changes match the concrete list exactly, including using
`dark:bg-graphite-hover` (not `dark:bg-graphite-surface-2`, which is the token used by the
unrelated `LeafletDocumentsTab.tsx` skeleton) as specified.

## PR Summary
This change adds dark-mode Tailwind variants to `LeafletGenerateTab.tsx`'s error banners and
loading-skeleton bars so they render legibly under the Graphite dark theme, following the
project's dark-mode conversion guide. The error banners now use a `~900/30` background with
`~300` text color for both the "insufficient knowledge base" and generic/transient error states,
and the three skeleton placeholder bars now include a `dark:bg-graphite-hover` background. The
change is purely additive/CSS-only — no logic changes, and `LeafletForm`/`LeafletResult` were not
touched.

## Status
DONE
