# Dark Mode Conversion Guide (Graphite theme)

Tailwind `darkMode: 'class'`. Add `dark:` variants alongside existing light classes. NEVER remove or change light classes. NEVER change logic, props, structure, or text. ONLY append `dark:` utility classes to existing `className` strings.

## Mapping table (raw light class → dark variant to ADD)

### Backgrounds (surfaces)
- `bg-white`                  → add `dark:bg-graphite-surface`
- `bg-gray-50`               → add `dark:bg-graphite-surface-2`
- `bg-gray-100`              → add `dark:bg-graphite-surface-2`  (panels/badges/inputs)
- `bg-gray-200`              → add `dark:bg-graphite-hover`
- page/app background `bg-background-*` is already themed globally — leave alone

### Hover backgrounds
- `hover:bg-gray-50`         → add `dark:hover:bg-white/5`
- `hover:bg-gray-100`        → add `dark:hover:bg-white/5`

### Text
- `text-gray-900` / `text-black` / `text-neutral-slate` → add `dark:text-graphite-text`
- `text-gray-800` / `text-gray-700` / `text-gray-600` / `text-gray-500` → add `dark:text-graphite-muted`
- `text-gray-400` / `text-gray-300` → add `dark:text-graphite-faint`

### Borders / dividers
- `border-gray-200` / `border-gray-300` / `border-gray-100` → add `dark:border-graphite-border`
- `divide-gray-200` / `divide-gray-100` → add `dark:divide-graphite-border`

### Shadows
- `shadow` / `shadow-sm` / `shadow-md` → add `dark:shadow-soft-dark`

### Accent (active/selected, indigo or blue primary)
- `text-indigo-600` / `text-blue-600` (active) → add `dark:text-graphite-accent`
- `bg-indigo-50` / `bg-blue-50` (active bg)    → add `dark:bg-graphite-accent/10`
- `border-indigo-500` / `border-blue-500` (active) → add `dark:border-graphite-accent`
- `ring-indigo-500` / `focus:ring-indigo-500`  → leave (focus rings OK as-is)

### Status / semantic pills and text (keep hue, darken)
- `bg-green-100 text-green-800` → add `dark:bg-emerald-900/30 dark:text-emerald-300`
- `bg-blue-100 text-blue-800`   → add `dark:bg-blue-900/30 dark:text-blue-300`
- `bg-yellow-100 text-yellow-800` → add `dark:bg-amber-900/30 dark:text-amber-300`
- `bg-red-100 text-red-800`     → add `dark:bg-red-900/30 dark:text-red-300`
- `bg-orange-100 text-orange-800` → add `dark:bg-orange-900/30 dark:text-orange-300`
- `bg-purple-100 text-purple-800` → add `dark:bg-purple-900/30 dark:text-purple-300`
- `text-green-600` → add `dark:text-emerald-400`
- `text-red-600` / `text-red-500` → add `dark:text-red-400`
- `text-yellow-600` → add `dark:text-amber-400`
- `text-blue-600` (non-accent info) → add `dark:text-blue-400`

### Inputs / selects / textareas (raw, not using `.input` class)
Add together: `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint`

## Rules
1. Surgical: only append `dark:` classes. Do not reorder or rewrite existing classes.
2. If an element already uses a design-system class (`.card`, `.input`, `.btn-primary`, `.text-h1`, `.badge-*`, `.text-body*`) it is ALREADY themed — do NOT add dark variants to it.
3. Skip files where every colored element already relies on design-system classes (nothing to do).
4. For conditional/ternary className strings, add the dark variant inside each branch consistently.
5. For color maps (objects mapping status→classes), add dark variants to each value string.
6. Icons with `text-gray-*`/`text-gray-400` follow the text mapping.
7. Do NOT touch: `.test.tsx`, `__tests__`, files under `components/test/`.
8. Leave chart library colors (recharts fill/stroke hex props) alone unless trivially a className.
9. Preserve `error`/`success`/`warning` semantic classes already present; just add the dark pill mapping if it's a raw `bg-*-100`.
