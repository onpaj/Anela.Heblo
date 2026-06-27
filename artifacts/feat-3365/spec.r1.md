# Spec r1 — Article Sub-Components Dark Mode (feat-3365)

## Problem

`ArticlesPage.tsx` was partially migrated to the Graphite dark theme (ADR-006, accepted 2026-06-25).
Its six child components were never updated: every color-bearing Tailwind class is light-mode-only,
producing jarring white/light surfaces inside a correctly-dark page shell whenever the `dark` class
is present on `<html>`.

An additional complication exists in `ArticleDetail.tsx`: `HtmlContent` renders article body HTML
inside an `<iframe>` whose `srcdoc` contains hardcoded light-mode CSS (`color:#1f2937`,
`color:#111827`, `color:#2563eb`). That inline CSS is invisible to Tailwind's dark-mode mechanism
and must be fixed in JavaScript by reading `document.documentElement.classList.contains('dark')` at
render time.

---

## Files that need to change

1. `frontend/src/features/articles/ArticleDetail.tsx`
2. `frontend/src/features/articles/ArticleList.tsx`
3. `frontend/src/features/articles/ArticleDebugPanel.tsx`
4. `frontend/src/features/articles/ArticleFeedbackSection.tsx`
5. `frontend/src/features/articles/ArticleGenerationForm.tsx`
6. `frontend/src/features/articles/ArticleSourceList.tsx`

No backend files change. No new components are added.

---

## Per-file class inventory and required dark: variants

### Graphite palette mapping used throughout

| Light class | Dark variant | Notes |
|---|---|---|
| `text-gray-900`, `text-gray-800` | `dark:text-graphite-text` | Primary text |
| `text-gray-700`, `text-gray-600` | `dark:text-graphite-muted` | Secondary text |
| `text-gray-500`, `text-gray-400` | `dark:text-graphite-faint` | Tertiary / muted text |
| `bg-gray-50`, `bg-gray-100` | `dark:bg-graphite-surface-2` | Subtle background |
| `border-gray-*`, `divide-gray-100`, `border-t` | `dark:border-graphite-border` | Borders and dividers |
| `hover:bg-gray-50` | `dark:hover:bg-graphite-hover` | Hover state |
| `bg-blue-50`, selected state | `dark:bg-graphite-surface` | Selected row |
| `text-blue-500`, `text-blue-600` | `dark:text-graphite-accent` | Accent / links |
| `text-blue-700`, `bg-blue-100` | `dark:text-graphite-accent dark:bg-graphite-surface-2` | Status badge — In Progress / Researching |
| `text-purple-700`, `bg-purple-100` | `dark:text-graphite-accent-strong dark:bg-graphite-surface-2` | Status badge — Writing |
| `text-green-700`, `bg-green-100` | `dark:text-graphite-text dark:bg-graphite-surface-2` | Status badge — Generated / KB source |
| `text-green-600` | `dark:text-graphite-accent` | Source icon (KB) |
| `text-gray-700`, `bg-gray-100` | `dark:text-graphite-muted dark:bg-graphite-surface-2` | Status badge — Queued |
| `text-red-700`, `bg-red-100` | `dark:text-red-400 dark:bg-red-950/40` | Status badge — Failed |
| `bg-red-50 border-red-200 text-red-700` | `dark:bg-red-950/40 dark:border-red-800 dark:text-red-400` | Error block |
| `text-red-600` | `dark:text-red-400` | Inline error text |
| `text-amber-600` | `dark:text-amber-400` | Permission warning |
| `border-gray-300` (input/select borders) | `dark:border-graphite-border` | Form control border |
| `focus:ring-blue-500` | `dark:focus:ring-graphite-accent` | Focus ring |
| `bg-blue-600 hover:bg-blue-700` (submit button) | `dark:bg-graphite-accent dark:hover:bg-graphite-accent-strong` | Primary button |
| `text-white` (button label) | no change needed (white on accent works in dark) | — |
| `text-blue-600 focus:ring-blue-500` (checkbox) | `dark:text-graphite-accent dark:focus:ring-graphite-accent` | Checkbox accent |

---

### 1. `ArticleDetail.tsx`

#### `STATUS_COLORS` (lines 20–26) — add dark: to every entry

| Status | Current | Add |
|---|---|---|
| Queued | `bg-gray-100 text-gray-700` | `dark:bg-graphite-surface-2 dark:text-graphite-muted` |
| Researching | `bg-blue-100 text-blue-700` | `dark:bg-graphite-surface-2 dark:text-graphite-accent` |
| Writing | `bg-purple-100 text-purple-700` | `dark:bg-graphite-surface-2 dark:text-graphite-accent-strong` |
| Generated | `bg-green-100 text-green-700` | `dark:bg-graphite-surface-2 dark:text-graphite-text` |
| Failed | `bg-red-100 text-red-700` | `dark:bg-red-950/40 dark:text-red-400` |

#### `HtmlContent` (lines 28–51) — iframe srcdoc

Replace the static `srcdoc` string with a dynamic value that branches on
`document.documentElement.classList.contains('dark')`. When dark mode is active, use:

```
body  { color: #E6E8EC; background: #202327; ... }
h1,h2,h3 { color: #E6E8EC }
a { color: #38BDF8 }
```

When light mode is active, keep the existing values (`color:#1f2937`, `color:#111827`, `a color:#2563eb`).
The `srcdoc` should be recomputed on every render — add a `useEffect` + `MutationObserver` on
`<html>.classList` (or read the class once at render time if live theme switching is not required
by ADR-006; confirm before implementation).

#### `InProgressView` (line 55)

- `text-gray-500` → add `dark:text-graphite-faint`
- `text-blue-500` (Loader2) → add `dark:text-graphite-accent`

#### `ArticleView` — `h2` (line 67)

- `text-gray-900` → add `dark:text-graphite-text`

#### `ArticleView` — topic `p` (line 69)

- `text-gray-500` → add `dark:text-graphite-faint`

#### `ArticleView` — metadata `div` (line 70)

- `text-gray-500` → add `dark:text-graphite-faint`

#### Loading spinner (line 92)

- `text-gray-400` → add `dark:text-graphite-faint`

#### Inline error text (line 98)

- `text-red-600` → add `dark:text-red-400`

#### Error block (line 115)

- `bg-red-50` → add `dark:bg-red-950/40`
- `border-red-200` → add `dark:border-red-800`
- `text-red-700` → add `dark:text-red-400`

---

### 2. `ArticleList.tsx`

#### `STATUS_COLORS` (lines 20–26) — identical structure to ArticleDetail

Apply the same dark: additions as the table in §1 above.

#### Loading spinner (line 44)

- `text-gray-400` → add `dark:text-graphite-faint`

#### Empty-state text (line 51)

- `text-gray-500` → add `dark:text-graphite-faint`

#### List `<ul>` (line 58)

- `divide-gray-100` → add `dark:divide-graphite-border`

#### List item `<button>` (lines 62–65)

- `hover:bg-gray-50` → add `dark:hover:bg-graphite-hover`
- `bg-blue-50` (selected branch) → add `dark:bg-graphite-surface`

#### Article title `p` (line 69)

- `text-gray-900` → add `dark:text-graphite-text`

#### Topic `p` (line 73)

- `text-gray-500` → add `dark:text-graphite-faint`

#### Date `p` (line 75)

- `text-gray-400` → add `dark:text-graphite-faint`

---

### 3. `ArticleDebugPanel.tsx`

#### `STEP_STATUS_COLORS` (lines 9–13)

| Status | Current | Add |
|---|---|---|
| Running | `bg-blue-100 text-blue-700` | `dark:bg-graphite-surface-2 dark:text-graphite-accent` |
| Succeeded | `bg-green-100 text-green-700` | `dark:bg-graphite-surface-2 dark:text-graphite-text` |
| Failed | `bg-red-100 text-red-700` | `dark:bg-red-950/40 dark:text-red-400` |

#### Fallback color in `StepCard` (line 31)

- `bg-gray-100 text-gray-700` → add `dark:bg-graphite-surface-2 dark:text-graphite-muted`

#### `PrettyJson` `<pre>` (lines 24 and 27)

- `bg-gray-50` → add `dark:bg-graphite-surface-2`

#### `StepCard` outer `<div>` (line 35) — `border`

The bare `border` class inherits the browser default border color in light mode. Add:
- `dark:border-graphite-border`

#### Sequence `<span>` (line 37)

- `text-gray-500` → add `dark:text-graphite-faint`

#### Step name `<span>` (line 38)

- `text-gray-800` → add `dark:text-graphite-text`

#### Model `<span>` (line 44)

- `text-gray-500` → add `dark:text-graphite-faint`

#### Duration `<span>` (line 47)

- `text-gray-400` → add `dark:text-graphite-faint`

#### Step error `<p>` (line 52)

- `text-red-600` → add `dark:text-red-400`
- `bg-red-50` → add `dark:bg-red-950/40`

#### Details summary (lines 57 and 64)

- `text-gray-500` → add `dark:text-graphite-faint`
- `hover:text-gray-700` → add `dark:hover:text-graphite-muted`

#### Panel toggle button (line 80)

- `text-gray-600` → add `dark:text-graphite-muted`
- `hover:text-gray-800` → add `dark:hover:text-graphite-text`

#### Loading spinner inside panel (line 91)

- `text-gray-400` → add `dark:text-graphite-faint`

#### Trace error text (line 95)

- `text-red-600` → add `dark:text-red-400`

#### Empty steps text (line 98)

- `text-gray-500` → add `dark:text-graphite-faint`

#### Panel container `border-t` (line 77)

- add `dark:border-graphite-border`

---

### 4. `ArticleFeedbackSection.tsx`

#### Feedback display wrapper `<div>` (line 20)

- `border-t` — add `dark:border-graphite-border`

#### Scores `<p>` (line 21)

- `text-gray-700` → add `dark:text-graphite-muted`

#### Comment `<p>` (line 25)

- `text-gray-600` → add `dark:text-graphite-muted`

#### Form wrapper `<div>` (line 32)

- `border-t` — add `dark:border-graphite-border`

#### Error text (line 46)

- `text-red-600` → add `dark:text-red-400`

---

### 5. `ArticleGenerationForm.tsx`

#### All form `<label>` elements (lines 70, 87, 100, 114, 125, 136, 148, 157)

Each carries `text-gray-700` — add `dark:text-graphite-muted` to all.

#### All `<input type="text">` and `<select>` elements (lines 81, 91, 104, 121, 132, 143, 176, 182)

Each carries `border-gray-300` and `focus:ring-blue-500` — add:
- `dark:border-graphite-border dark:bg-graphite-surface dark:text-graphite-text dark:focus:ring-graphite-accent`

#### Checkbox `<input>` elements (lines 149–154 and 160–164)

Carry `border-gray-300 text-blue-600 focus:ring-blue-500` — add:
- `dark:border-graphite-border dark:text-graphite-accent dark:focus:ring-graphite-accent`

#### Checkbox `<label>` wrappers (lines 148, 157)

- `text-gray-700` → add `dark:text-graphite-muted`

#### Style guide `<details>` summary (line 169)

- `text-gray-500` → add `dark:text-graphite-faint`

#### API error text (line 189)

- `text-red-600` → add `dark:text-red-400`

#### Permission warning (line 193)

- `text-amber-600` → add `dark:text-amber-400`

#### Submit `<button>` (line 199)

- `bg-blue-600` → add `dark:bg-graphite-accent`
- `hover:bg-blue-700` → add `dark:hover:bg-graphite-accent-strong`
- `text-white` — no change needed

---

### 6. `ArticleSourceList.tsx`

#### Section wrapper `<div>` (line 22)

- `border-t` — add `dark:border-graphite-border`

#### Section heading `<h3>` (line 23)

- `text-gray-700` → add `dark:text-graphite-muted`

#### Web source icon `Globe` (line 11)

- `text-blue-500` → add `dark:text-graphite-accent`

#### KB source icon `BookOpen` (line 12)

- `text-green-600` → add `dark:text-graphite-accent`

#### Web link `<a>` (line 35)

- `text-blue-600` → add `dark:text-graphite-accent`

#### KB chunk `<button>` (line 44)

- `text-green-700` → add `dark:text-graphite-accent`

#### Fallback `<span>` (line 49)

- `text-gray-700` → add `dark:text-graphite-muted`

---

## Success criteria

1. **Visual**: With `dark` class on `<html>`, every rendered surface in the six components uses Graphite
   palette colours — no white/light backgrounds, no near-black text.

2. **Visual**: With `dark` class absent, all existing light-mode appearances are unchanged.

3. **IFrame dark mode**: `HtmlContent` renders article body text in `#E6E8EC` on `#202327` background
   when dark mode is active, and the existing light colours when it is not.

4. **STATUS_COLORS coverage**: All five `ArticleStatus` badge variants and all three `STEP_STATUS_COLORS`
   variants display correctly in both modes.

5. **Build passes**: `npm run build` (in `frontend/`) completes without error; the TypeScript compiler
   accepts all class strings (no structural changes, only string concatenation additions).

6. **Lint passes**: `npm run lint` produces no new warnings or errors.

7. **No light-mode regressions**: A visual diff of the Article section in light mode shows zero
   changes from baseline.

8. **Scope discipline**: No files outside the six listed above are modified.
