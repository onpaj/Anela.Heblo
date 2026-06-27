## Review Result: CHANGES_REQUESTED

### Blocking

**1. HtmlContent `isDark` is not reactive — theme switches after mount are silently ignored**

In `ArticleDetail.tsx`, the `isDark` flag is computed once at module evaluation time (inside the function body but outside a React hook), so the injected `srcdoc` styles will be wrong if the user switches theme while the component is mounted. Because `srcdoc` is written into an `<iframe>`, React will not re-render the iframe contents on subsequent renders anyway, but the snapshot is taken at whatever moment the component first renders. If the page loads in light mode and the user toggles dark, the iframe keeps light styles — and vice-versa. The fix is to derive the value inside a `useMemo` (or `useEffect` + state) so it is re-evaluated on re-render, and to either force-remount the iframe when the theme changes (e.g., key on `isDark`) or use a `MutationObserver` on `document.documentElement` to push updated styles into the iframe via `contentDocument`. At minimum, the value must be read inside the component body on every render so toggling actually changes the output:

```tsx
// current (buggy — reads once at first call, not on re-render if hoisted)
const isDark = document.documentElement.classList.contains('dark');
const srcdoc = `...${isDark ? ... : ...}...`;
```

Because `srcdoc` is derived from `isDark` inside the same render, it will be correct on the initial render. The real bug is that the iframe does not update when the theme is toggled, because React treats `srcdoc` as the iframe's initial content and does not re-inject it after mount. The component needs a `key={isDark ? 'dark' : 'light'}` prop on the `<iframe>` to force a remount whenever the theme changes, or the styles must be patched via the iframe's `contentDocument` after mount:

```tsx
const isDark = document.documentElement.classList.contains('dark');
// ...
<iframe key={isDark ? 'dark' : 'light'} srcDoc={srcdoc} ... />
```

Without this, the rendered HTML content will display with wrong colors after a theme toggle. This is a correctness bug.

---

**2. Status pill background mapping deviates from the Graphite palette guide**

The dark-mode conversion guide (`docs/design/dark-mode-conversion-guide.md`) prescribes semantic-aware mappings for status pills:

| Light classes | Required dark variant |
|---|---|
| `bg-green-100 text-green-*` | `dark:bg-emerald-900/30 dark:text-emerald-300` |
| `bg-blue-100 text-blue-*` | `dark:bg-blue-900/30 dark:text-blue-300` |
| `bg-purple-100 text-purple-*` | `dark:bg-purple-900/30 dark:text-purple-300` |
| `bg-red-100 text-red-*` | `dark:bg-red-900/30 dark:text-red-300` |
| `bg-gray-100 text-gray-*` | `dark:bg-graphite-surface-2 dark:text-graphite-muted` |

All six STATUS_COLORS maps (in both `ArticleDetail.tsx` and `ArticleList.tsx`, and the STEP_STATUS_COLORS map in `ArticleDebugPanel.tsx`) instead flatten every non-red status to `dark:bg-graphite-surface-2` and use graphite accent/text tokens for the foreground. This erases the semantic color signal that distinguishes "Researching" from "Writing" from "Generated" in dark mode — which is precisely what the guide's semantic mapping exists to preserve. The guide is authoritative on this point. Both files must be corrected to follow the prescribed mappings.

Additionally, the status light classes use `text-green-700` / `text-blue-700` / `text-purple-700` rather than `text-green-800` / `text-blue-800` / `text-purple-800`. The guide's mapping table keys on the `-800` variants, but these are `-700` in the source. The correct dark foreground token in that case is still the semantically matching one (`dark:text-emerald-300`, `dark:text-blue-300`, etc.) — the guide's intent is clear enough that the shade difference does not excuse using graphite tokens instead.

---

**3. Input/select/textarea `dark:bg-graphite-surface` should be `dark:bg-graphite-surface-2`, and `dark:placeholder-graphite-faint` is missing**

The guide specifies for raw inputs/selects/textareas:

> Add together: `dark:bg-graphite-surface-2 dark:border-graphite-border dark:text-graphite-text dark:placeholder-graphite-faint`

In `ArticleGenerationForm.tsx`, every `<input>` and `<select>` was given `dark:bg-graphite-surface` (the lighter surface token used for page/card backgrounds), not `dark:bg-graphite-surface-2` (the slightly elevated surface used for inputs and panels). This will make the inputs look like they are floating at the same depth as the surrounding card rather than sitting inside it — a visual regression. Additionally, `dark:placeholder-graphite-faint` is omitted from all inputs, leaving placeholder text at its browser default (usually white or near-white) in dark mode.

---

**4. `hover:bg-gray-50` mapped to `dark:hover:bg-graphite-hover` instead of `dark:hover:bg-white/5`**

In `ArticleList.tsx`, the hover state on list buttons uses `dark:hover:bg-graphite-hover`. The guide maps `hover:bg-gray-50` to `dark:hover:bg-white/5`. Unless `graphite-hover` is an alias for `white/5` in the Tailwind config, this is an inconsistency with the rest of the codebase. If the value differs, the list item hover will not match other hover states that follow the guide correctly.

---

**5. Selected list item `bg-blue-50` mapped to `dark:bg-graphite-surface` instead of `dark:bg-graphite-accent/10`**

In `ArticleList.tsx`, the selected item active state `bg-blue-50` receives `dark:bg-graphite-surface`. The guide prescribes `dark:bg-graphite-accent/10` for `bg-blue-50` (active/accent background). Using the flat surface token removes the visual distinction between a selected item and an unselected item in dark mode, since non-hovered unselected items also use the surface background.

---

**6. `text-gray-500` mapped inconsistently — some get `dark:text-graphite-muted`, others get `dark:text-graphite-faint`**

The guide maps `text-gray-500` to `dark:text-graphite-muted`. However in `ArticleDetail.tsx`, two `text-gray-500` elements receive `dark:text-graphite-faint` (the topic paragraph and the metadata div). The same shade should map to the same dark token throughout; mixing them produces uneven contrast. `text-gray-400` correctly gets `dark:text-graphite-faint` per the guide, so the faint token should be reserved for that shade only.

---

### Advisory

- **`text-blue-500` (spinner) mapped to `dark:text-graphite-accent`**: The guide maps `text-blue-600` to `dark:text-graphite-accent`; `text-blue-500` is one shade lighter. The mapping is reasonable in intent but the guide does not list it explicitly. This is fine if `graphite-accent` is the intended spinner color, but worth noting for consistency.

- **`text-green-700` (knowledge base source button) mapped to `dark:text-graphite-accent`**: Both Web (blue) and KB (green) source icons and the KB button are collapsed to the same `dark:text-graphite-accent` in `ArticleSourceList.tsx`. The visual distinction between source types is lost in dark mode. Consider `dark:text-emerald-400` for the KB source to mirror the guide's semantic green mapping.

- **Missing `dark:text-red-400` on `text-red-700` error message text in `ArticleDebugPanel.tsx` `StepCard`**: The error message paragraph at line 59 of the diff receives both background and text dark variants correctly, but the `STEP_STATUS_COLORS` `Failed` entry uses `dark:text-red-400` while the guide lists `dark:text-red-300` for `bg-red-100`/`text-red-*` pills. Minor shade difference; either is acceptable, but should be consistent between `ArticleDetail.tsx` and `ArticleDebugPanel.tsx`.

- **`border-t` without a color utility**: All `border-t` dividers in the diff omit an explicit `border-color-*` class in light mode (they inherit `border-gray-200` from the Tailwind base), but then receive an explicit `dark:border-graphite-border`. This works, but if the base border color ever changes the light/dark pair could drift. Consider adding explicit `border-gray-200` to each `border-t` to make the intent self-documenting alongside the dark variant.

- **`PrettyJson` `<pre>` in `ArticleDebugPanel.tsx` is missing text color dark variant**: The `<pre>` element gets `dark:bg-graphite-surface-2` but no `dark:text-graphite-text`. On most browsers, `<pre>` inherits the body text color, so this likely works — but it relies on ambient inheritance rather than an explicit declaration. Adding `dark:text-graphite-text` would be self-documenting.
