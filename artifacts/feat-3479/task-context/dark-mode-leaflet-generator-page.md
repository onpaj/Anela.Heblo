### task: dark-mode-leaflet-generator-page

**Goal:** Add Tailwind `dark:` class variants to the page header and tab-bar navigation in
`LeafletGeneratorPage.tsx` so the tab shell renders correctly under the Graphite dark theme, per
ADR-006 and `docs/design/dark-mode-conversion-guide.md`.

**File to change:** `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx`

**Scope discipline:** This is a purely additive, surgical CSS-class change. Do not alter markup
structure, props, the `tabs` array, conditional tab-rendering logic, or the
`hasPermission('marketing.leaflet.write')` gating — all must remain byte-for-byte unchanged except
for the specific `className` edits below.

**Concrete class-level changes required** (line numbers are approximate/as-of-spec; locate by exact
string match, content is unique):

- Header icon `<FileText className="w-6 h-6 text-blue-600" />` → add `dark:text-graphite-accent`
  for `text-blue-600`.
- Heading `className="text-2xl font-semibold text-gray-900"` → add `dark:text-graphite-text` for
  `text-gray-900`.
- Tab bar container `className="border-b border-gray-200"` → add `dark:border-graphite-border` for
  `border-gray-200`.
- Tab button template literal:
  ```
  className={`py-2 text-sm font-medium border-b-2 transition-colors ${
    activeTab === tab.id
      ? 'border-blue-600 text-blue-600'
      : 'border-transparent text-gray-500 hover:text-gray-700'
  }`}
  ```
  - Active branch `'border-blue-600 text-blue-600'` →
    `'border-blue-600 text-blue-600 dark:border-graphite-accent dark:text-graphite-accent'`.
  - Inactive branch `'border-transparent text-gray-500 hover:text-gray-700'` →
    `'border-transparent text-gray-500 hover:text-gray-700 dark:text-graphite-muted dark:hover:text-graphite-text'`.

**Acceptance criteria:**
- All four raw-color classes identified above have a corresponding `dark:` class appended in the
  same `className` string/branch, with light classes unchanged.
- The active tab indicator (bottom border + label) uses `dark:border-graphite-accent
  dark:text-graphite-accent` in Graphite mode.
- The inactive tab label is `dark:text-graphite-muted` at rest and `dark:hover:text-graphite-text`
  on hover.
- No structural, prop, or logic changes; the `tabs` array and conditional rendering are untouched.
- Component compiles with no TypeScript/JSX errors.
- `npm run build` and `npm run lint` (from `frontend/`) pass with no new errors or warnings.

---

