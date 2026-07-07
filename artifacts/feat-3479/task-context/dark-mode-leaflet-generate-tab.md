### task: dark-mode-leaflet-generate-tab

**Goal:** Add Tailwind `dark:` class variants to the error banners and loading-skeleton bars in
`LeafletGenerateTab.tsx` so they render correctly under the Graphite dark theme, per ADR-006 and
`docs/design/dark-mode-conversion-guide.md`.

**File to change:** `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`

**Scope discipline:** This is a purely additive, surgical CSS-class change, limited strictly to the
banner and inline skeleton markup owned directly by `LeafletGenerateTab`. `LeafletForm` and
`LeafletResult` (rendered as children elsewhere in this file) are out of scope — do not touch them
or their source files. Do not fix the pre-existing `(response as any).id` cast or any other
unrelated issue in this file — leave it untouched.

**Concrete class-level changes required** (line numbers are approximate/as-of-spec; locate by exact
string match, content is unique):

- Error banner ternary:
  ```
  errorBanner.kind === 'insufficient'
    ? 'bg-amber-100 text-amber-900'
    : 'bg-red-100 text-red-900'
  ```
  - `'bg-amber-100 text-amber-900'` → `'bg-amber-100 text-amber-900 dark:bg-amber-900/30 dark:text-amber-300'`.
  - `'bg-red-100 text-red-900'` → `'bg-red-100 text-red-900 dark:bg-red-900/30 dark:text-red-300'`.
- Loading skeleton bars — three sibling divs, each with a `bg-gray-200` base:
  `className="h-4 bg-gray-200 rounded w-3/4"`, `className="h-4 bg-gray-200 rounded"`,
  `className="h-4 bg-gray-200 rounded w-5/6"` → in EACH of the three, add `dark:bg-graphite-hover`
  for `bg-gray-200` (note: this is a different target token than the `bg-gray-100` skeleton in
  `LeafletDocumentsTab.tsx`, which maps to `dark:bg-graphite-surface-2` — that is correct and
  intentional; the two files' skeletons use different source classes and therefore different
  target tokens per the guide's literal class-based mapping. Do not "harmonize" them to the same
  token.)

**Acceptance criteria:**
- Both error-banner variants (`insufficient` / other/transient) render with the `~900/30`
  background + `~300` text pattern in Graphite mode.
- All three loading-skeleton bars use `dark:bg-graphite-hover` and are visually distinguishable
  from the Graphite page background.
- `LeafletForm` and `LeafletResult` remain untouched — no changes outside this file's own banner
  and skeleton markup.
- Component compiles with no TypeScript/JSX errors.
- `npm run build` and `npm run lint` (from `frontend/`) pass with no new errors or warnings.
