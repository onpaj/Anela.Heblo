### task: dark-mode-leaflet-documents-tab

**Goal:** Add Tailwind `dark:` class variants to every raw-color utility in
`LeafletDocumentsTab.tsx` (status badges, delete-confirmation dialog, sortable table header, filter
bar, table body, and pagination footer) so the component renders correctly under the Graphite dark
theme, per ADR-006 and `docs/design/dark-mode-conversion-guide.md`. This is the highest-impact file
in this fix (515 lines) — do it first.

**File to change:** `frontend/src/features/leaflet-generator/LeafletDocumentsTab.tsx`

**Scope discipline:** This is a purely additive, surgical CSS-class change. Do not alter markup
structure, props, state, sorting/pagination/filtering logic, URL-param sync, permission checks
(`canDelete`), or API calls. Do not touch any other file. Do not "fix" the pre-existing
`(response as any).id`-style issues or any other unrelated lint/type/accessibility problem you may
notice — flag it in a comment if you want, but do not change it.

**Concrete class-level changes required** (line numbers are approximate/as-of-spec; locate by exact
string match, content is unique):

**1. `StatusBadge` color map (around lines 12–24).** The `colorMap` object maps status → Tailwind
classes. Add a `dark:` variant to each value string:
- `indexed: 'bg-green-100 text-green-800'` → `'bg-green-100 text-green-800 dark:bg-emerald-900/30 dark:text-emerald-300'`
- `processing: 'bg-yellow-100 text-yellow-800'` → `'bg-yellow-100 text-yellow-800 dark:bg-amber-900/30 dark:text-amber-300'`
- `failed: 'bg-red-100 text-red-800'` → `'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300'`
- fallback `?? 'bg-gray-100 text-gray-800'` → `'bg-gray-100 text-gray-800 dark:bg-graphite-surface-2 dark:text-graphite-muted'`

**2. `ConfirmDeleteDialog` (around lines 26–57):**
- Dialog panel `className="bg-white rounded-lg shadow-xl p-6 max-w-sm w-full"` → add
  `dark:bg-graphite-surface` for `bg-white`. Leave `shadow-xl` unchanged (not in the guide's shadow
  table — do not invent a mapping for it).
- Heading `text-lg font-semibold` — no color utility present, no change.
- Body text `className="text-sm text-gray-600 mb-4"` → add `dark:text-graphite-muted` for
  `text-gray-600`.
- Warning/error text `className="text-sm text-red-600 mb-3"` → add `dark:text-red-400` for
  `text-red-600`.
- Cancel button `className="px-4 py-2 text-sm rounded border border-gray-300 hover:bg-gray-50"` →
  add `dark:border-graphite-border` for `border-gray-300` and `dark:hover:bg-white/5` for
  `hover:bg-gray-50`.
- Confirm/delete button `className="px-4 py-2 text-sm rounded bg-red-600 text-white hover:bg-red-700"`
  → **leave unchanged**. This is a solid destructive-action button (saturated `bg-red-600` +
  `text-white`); the guide does not map solid action buttons, and solid `-600`/`-700` backgrounds
  with white text retain sufficient contrast in both light and dark mode by construction. Do not
  add a `dark:` variant here.

**3. `SortableHeader` (around lines 59–87):**
- `className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100 select-none"`
  → add `dark:text-graphite-muted` for `text-gray-500` and `dark:hover:bg-white/5` for
  `hover:bg-gray-100`.
- Chevron icon ternaries (ChevronUp and ChevronDown, two near-identical template literals):
  ```
  `h-3 w-3 ${isActive && !sortDescending ? 'text-indigo-600' : 'text-gray-300'}`
  `h-3 w-3 -mt-1 ${isActive && sortDescending ? 'text-indigo-600' : 'text-gray-300'}`
  ```
  In BOTH ternaries: `'text-indigo-600'` → `'text-indigo-600 dark:text-graphite-accent'`;
  `'text-gray-300'` → `'text-gray-300 dark:text-graphite-faint'`.

**4. Filter bar (around lines 272–347):**
- Container `className="bg-white shadow rounded-lg p-4 mb-4"` → add `dark:bg-graphite-surface` for
  `bg-white` and `dark:shadow-soft-dark` for `shadow`.
- `<Filter className="h-4 w-4 text-gray-400 mr-2" />` → add `dark:text-graphite-faint` for
  `text-gray-400`.
- `<span className="text-sm font-medium text-gray-900">` → add `dark:text-graphite-text` for
  `text-gray-900`.
- `<Search className="h-4 w-4 text-gray-400" />` → add `dark:text-graphite-faint` for
  `text-gray-400`.
- Filename input `className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 sm:text-sm border-gray-300 rounded-md"`
  → append the full raw-input dark bundle: `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint`.
  Leave `focus:ring-indigo-500`/`focus:border-indigo-500` unchanged (focus rings are fine as-is).
- Status `<select>` `className="block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md"`
  → append `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text` (no
  placeholder dark class — it's a `<select>`, not a text input).
- Content-type `<select>` — identical classes to the status select above → same treatment: append
  `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text`.
- "Filtrovat" button `className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md transition-colors duration-200 text-sm"`
  → **leave unchanged** (solid saturated primary button, same rationale as the delete-confirm
  button above — no guide mapping, contrast already sufficient).
- "Vymazat" button `className="bg-gray-500 hover:bg-gray-600 text-white font-medium py-2 px-3 rounded-md transition-colors duration-200 text-sm"`
  → **leave unchanged**. Note: despite `bg-gray-500` looking like a "neutral surface" candidate at
  a glance, it is a solid button with white text, not a surface — do not map it to
  `dark:bg-graphite-surface-2` or similar. Same rationale as the other solid-button exemptions.

**5. Empty state:**
- `className="text-gray-500 text-sm text-center py-8"` → add `dark:text-graphite-muted` for
  `text-gray-500`.

**6. Table (around lines 355–400):**
- `className="min-w-full divide-y divide-gray-200 text-sm"` → add `dark:divide-graphite-border`
  for `divide-gray-200`.
- `<thead className="bg-gray-50">` → add `dark:bg-graphite-surface-2` for `bg-gray-50`.
- `<th className="px-6 py-3" />` — no color utility, no change.
- `<tbody className="divide-y divide-gray-100">` → add `dark:divide-graphite-border` for
  `divide-gray-100`.
- Row template literal `` `hover:bg-gray-50 ${doc.firstChunkId ? 'cursor-pointer' : ''}` `` → add
  `dark:hover:bg-white/5` for `hover:bg-gray-50` (the `cursor-pointer`/`''` ternary branch itself
  needs no change).
- `className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900"` → add
  `dark:text-graphite-text` for `text-gray-900`.
- Both occurrences of `className="px-6 py-4 whitespace-nowrap text-sm text-gray-500"` → add
  `dark:text-graphite-muted` for `text-gray-500` (apply to both).
- Delete icon button `className="text-gray-400 hover:text-red-600 transition-colors"` → add
  `dark:text-graphite-faint` for `text-gray-400`. Leave `hover:text-red-600` unchanged (no guide
  mapping for danger-hover text; saturated red-600 stays legible on dark surfaces).

**7. Loading skeleton and error text (around lines 256–267):**
- `className="h-10 bg-gray-100 rounded"` (skeleton block) → add `dark:bg-graphite-surface-2` for
  `bg-gray-100`.
- `className="text-red-600 text-sm"` (error text) → add `dark:text-red-400` for `text-red-600`.

**8. Pagination footer (around lines 403–491):**
- `className="bg-white px-3 py-2 flex items-center justify-between border-t border-gray-200 text-xs"`
  → add `dark:bg-graphite-surface` for `bg-white` and `dark:border-graphite-border` for
  `border-gray-200`.
- Mobile "Předchozí" and "Další" buttons (both share the identical class string)
  `className="relative inline-flex items-center px-2 py-1 border border-gray-300 text-xs font-medium rounded text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"`
  → in BOTH buttons: add `dark:border-graphite-border` for `border-gray-300`,
  `dark:text-graphite-muted` for `text-gray-700`, `dark:bg-graphite-surface` for `bg-white`,
  `dark:hover:bg-white/5` for `hover:bg-gray-50`.
- Result-count text `className="text-xs text-gray-600"` → add `dark:text-graphite-muted` for
  `text-gray-600`.
- "Zobrazit:" label `className="text-xs text-gray-600"` → add `dark:text-graphite-muted` for
  `text-gray-600`.
- Page-size `<select>` `className="border border-gray-300 rounded px-1 py-0.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500 focus:border-transparent"`
  → append `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text` (leave
  focus-ring classes unchanged).
- `className="relative z-0 inline-flex rounded shadow-sm -space-x-px"` → add
  `dark:shadow-soft-dark` for `shadow-sm`.
- Prev-page nav button `className="relative inline-flex items-center px-1 py-1 rounded-l border border-gray-300 bg-white text-xs font-medium text-gray-500 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"`
  → add `dark:border-graphite-border` for `border-gray-300`, `dark:bg-graphite-surface` for
  `bg-white`, `dark:text-graphite-muted` for `text-gray-500`, `dark:hover:bg-white/5` for
  `hover:bg-gray-50`.
- Numbered page-button ternary:
  ```
  pageNum === pageNumber
    ? 'z-10 bg-indigo-50 border-indigo-500 text-indigo-600'
    : 'bg-white border-gray-300 text-gray-500 hover:bg-gray-50'
  ```
  - Active branch → `'z-10 bg-indigo-50 border-indigo-500 text-indigo-600 dark:bg-graphite-accent/10 dark:border-graphite-accent dark:text-graphite-accent'`.
  - Inactive branch → `'bg-white border-gray-300 text-gray-500 hover:bg-gray-50 dark:bg-graphite-surface dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5'`.
- Next-page nav button — identical classes to the prev-page nav button above → same treatment:
  add `dark:border-graphite-border`, `dark:bg-graphite-surface`, `dark:text-graphite-muted`,
  `dark:hover:bg-white/5`.

**Important note on "solid action buttons" (do not deviate):** the delete-confirm button, the
"Filtrovat" button, and the "Vymazat" button all use a solid saturated background (`-500`/`-600`+)
with `text-white`. None of them get a `dark:` variant. This is deliberate — the design guide has no
mapping for solid action buttons because their contrast is already sufficient on any background —
not an oversight. Do not add `dark:` classes to these three buttons even if they look inconsistent
with everything else in the file getting a dark variant.

**Acceptance criteria:**
- Every raw Tailwind color utility enumerated above has the specified `dark:` class appended in
  place, in the same string/branch, without altering any light-mode class, JSX structure, prop, or
  handler.
- The three solid action buttons (delete-confirm, Filtrovat, Vymazat) remain unchanged — no `dark:`
  classes added to them.
- No light-only `bg-white`/`bg-gray-*`/`text-gray-*`/`border-gray-*`/`divide-gray-*` class remains
  without a corresponding `dark:` counterpart anywhere in this file, except the three solid action
  buttons noted above.
- `StatusBadge` renders all three named statuses (`indexed`, `processing`, `failed`) plus the
  unknown-status fallback with visibly distinct pill colors when the `dark` class is applied to a
  root ancestor.
- Component compiles with no TypeScript/JSX errors.
- `npm run build` and `npm run lint` (from `frontend/`) pass with no new errors or warnings.

---

