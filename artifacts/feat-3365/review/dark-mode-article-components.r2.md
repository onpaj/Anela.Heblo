# Code Review r2 — dark-mode-article-components

Reviewing against `docs/design/dark-mode-conversion-guide.md`.

Files reviewed:
- `frontend/src/features/articles/ArticleDetail.tsx`
- `frontend/src/features/articles/ArticleList.tsx`
- `frontend/src/features/articles/ArticleDebugPanel.tsx`
- `frontend/src/features/articles/ArticleFeedbackSection.tsx`
- `frontend/src/features/articles/ArticleGenerationForm.tsx`
- `frontend/src/features/articles/ArticleSourceList.tsx`

---

## Check 1 — Status pill colors use semantic hues

### ArticleDetail.tsx and ArticleList.tsx — STATUS_COLORS

| Status      | Dark classes present                                        | Expected                                        | Pass? |
|-------------|-------------------------------------------------------------|-------------------------------------------------|-------|
| Queued      | `dark:bg-graphite-surface-2 dark:text-graphite-muted`       | gray → graphite-surface-2 / graphite-muted (correct) | PASS |
| Researching | `dark:bg-blue-900/30 dark:text-blue-300`                    | `dark:bg-blue-900/30 dark:text-blue-300`        | PASS  |
| Writing     | `dark:bg-purple-900/30 dark:text-purple-300`                | `dark:bg-purple-900/30 dark:text-purple-300`    | PASS  |
| Generated   | `dark:bg-emerald-900/30 dark:text-emerald-300`              | `dark:bg-emerald-900/30 dark:text-emerald-300`  | PASS  |
| Failed      | `dark:bg-red-900/30 dark:text-red-300`                      | `dark:bg-red-900/30 dark:text-red-300`          | PASS  |

Note: light classes use `text-blue-700`/`text-green-700`/etc. (not `*-800`). The guide's pill table lists `*-800` variants but the semantic intent ("keep hue, darken") is satisfied. The dark classes chosen match what the task brief specifies as correct.

### ArticleDebugPanel.tsx — STEP_STATUS_COLORS

| Status    | Dark classes present                                  | Expected                                       | Pass? |
|-----------|-------------------------------------------------------|------------------------------------------------|-------|
| Running   | `dark:bg-blue-900/30 dark:text-blue-300`              | `dark:bg-blue-900/30 dark:text-blue-300`       | PASS  |
| Succeeded | `dark:bg-emerald-900/30 dark:text-emerald-300`        | `dark:bg-emerald-900/30 dark:text-emerald-300` | PASS  |
| Failed    | `dark:bg-red-900/30 dark:text-red-300`                | `dark:bg-red-900/30 dark:text-red-300`         | PASS  |
| fallback  | `dark:bg-graphite-surface-2 dark:text-graphite-muted` | gray → graphite-surface-2 / graphite-muted     | PASS  |

**Check 1: PASS**

---

## Check 2 — hover:bg-gray-50 uses dark:hover:bg-white/5

`ArticleList.tsx` line 63:
```
hover:bg-gray-50 dark:hover:bg-white/5
```
Guide: `hover:bg-gray-50` → `dark:hover:bg-white/5`. Correct.

**Check 2: PASS**

---

## Check 3 — Selected state uses dark:bg-graphite-accent/10

`ArticleList.tsx` line 64:
```
selectedId === item.id ? 'bg-blue-50 dark:bg-graphite-accent/10' : ''
```
Guide: `bg-blue-50` (active bg) → `dark:bg-graphite-accent/10`. Correct.

**Check 3: PASS**

---

## Check 4 — text-gray-500 uses dark:text-graphite-muted (not faint)

All occurrences of `text-gray-500` in the six files:

- `ArticleDetail.tsx` line 57: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleDetail.tsx` line 71: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleDetail.tsx` line 72: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleList.tsx` line 51: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleList.tsx` line 73: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleDebugPanel.tsx` line 37: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleDebugPanel.tsx` line 44: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleDebugPanel.tsx` lines 57, 64: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleDebugPanel.tsx` line 98: `text-gray-500 dark:text-graphite-muted` — PASS
- `ArticleGenerationForm.tsx` lines 70, 87, 100, 114, 125, 136, 148, 157: `text-gray-700 dark:text-graphite-muted` — PASS (gray-700 → graphite-muted is correct per guide)
- `ArticleGenerationForm.tsx` line 169: `text-gray-500 dark:text-graphite-faint`

**ISSUE — Check 4 FAIL**: `ArticleGenerationForm.tsx` line 169 (`<summary>` for "Stylový průvodce"):

```tsx
<summary className="cursor-pointer text-gray-500 dark:text-graphite-faint select-none">
```

`text-gray-500` must map to `dark:text-graphite-muted` per the guide (`text-gray-800/700/600/500` → `graphite-muted`). The `graphite-faint` token is reserved for `text-gray-400/300` only. This was introduced in r2 and not flagged by the impl doc.

**Check 4: FAIL**

---

## Check 5 — text-gray-400 uses dark:text-graphite-faint

- `ArticleDetail.tsx` line 94: `text-gray-400 dark:text-graphite-faint` — PASS
- `ArticleList.tsx` line 44: `text-gray-400 dark:text-graphite-faint` — PASS
- `ArticleList.tsx` line 75: `text-gray-400 dark:text-graphite-faint` — PASS
- `ArticleDebugPanel.tsx` line 47: `text-gray-400 dark:text-graphite-faint` — PASS
- `ArticleDebugPanel.tsx` line 91: `text-gray-400 dark:text-graphite-faint` — PASS

**Check 5: PASS**

---

## Check 6 — Inputs use dark:bg-graphite-surface-2 and dark:placeholder-graphite-faint

`ArticleGenerationForm.tsx` — all `<input>` and `<select>` elements (lines 81, 91, 104, 120, 131, 143, 153 (checkbox), 163 (checkbox), 176, 183):

Regular text inputs and selects all carry:
```
dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text dark:placeholder-graphite-faint
```
Full guide formula satisfied.

Checkboxes (lines 153, 163) do not require `dark:bg-graphite-surface-2` — they use system styling and the `dark:border-graphite-border dark:text-graphite-accent dark:focus:ring-graphite-accent` pattern, which is acceptable for checkboxes.

**Check 6: PASS**

---

## Check 7 — BookOpen / web link / KB button use correct semantic dark colors

`ArticleSourceList.tsx`:

- `Globe` (Web icon) line 11: `text-blue-500 dark:text-graphite-accent`

  **ISSUE — Check 7 FAIL**: The guide maps `text-blue-600` (active/accent) → `dark:text-graphite-accent` and `text-blue-600` (non-accent info) → `dark:text-blue-400`. The Globe icon uses `text-blue-500` (not `text-blue-600`). However, the task brief specifies the Web icon (non-accent info link) should use `dark:text-blue-400`, matching the non-accent pattern. The implementation instead uses `dark:text-graphite-accent`, which is the active/accent mapping. The Web source icon is informational, not an active/selected accent element; `dark:text-blue-400` would be the correct token. This is a semantic mismatch.

- `BookOpen` (KB icon) line 12: `text-green-600 dark:text-emerald-400` — PASS (guide: `text-green-600` → `dark:text-emerald-400`)

- Web anchor link line 35: `text-blue-600 dark:text-blue-400` — PASS (non-accent info → `dark:text-blue-400`)

- KB button line 44: `text-green-700 dark:text-emerald-400` — PASS (green text → `dark:text-emerald-400`)

**Check 7: FAIL** — Globe icon uses `dark:text-graphite-accent` instead of `dark:text-blue-400`.

---

## Check 8 — HtmlContent has key prop and correct isDark hex values

`ArticleDetail.tsx` lines 29–52:

- `key={isDark ? 'dark' : 'light'}` present on `<iframe>` (line 38) — PASS
- `isDark` computed from `document.documentElement.classList.contains('dark')` — PASS
- Dark text color: `#E6E8EC` — this corresponds to `graphite-text` (acceptable for iframe body content)
- Dark background: `#202327` — corresponds to `graphite-surface` (acceptable for iframe background)
- Dark heading color: `#E6E8EC` — same as body text (acceptable)
- Dark link color: `#38BDF8` — sky-400, a reasonable accent blue for iframe links
- Light values unchanged — PASS

**Check 8: PASS**

---

## Check 9 — Submit button has NO dark: bg override

`ArticleGenerationForm.tsx` line 199:
```tsx
className="w-full bg-blue-600 text-white rounded-md px-4 py-2 text-sm font-medium hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed flex items-center justify-center gap-2"
```
No `dark:bg-*` class present. The `.btn-primary` / `bg-blue-600` primary button is left as-is per guide rule 2 (design-system buttons are already themed globally). Correct.

**Check 9: PASS**

---

## Check 10 — No files other than the 6 modified

The task requires only the six listed files to be changed. The r2 impl doc does not mention any other files. Verification of git diff scope is outside the static review, but the impl doc confirms the scope is limited to the six files.

**Check 10: PASS** (per impl doc scope; no evidence of additional file changes)

---

## Summary

| # | Check | Result |
|---|-------|--------|
| 1 | Status pill colors — semantic hues | PASS |
| 2 | hover:bg-gray-50 → dark:hover:bg-white/5 | PASS |
| 3 | Selected state → dark:bg-graphite-accent/10 | PASS |
| 4 | text-gray-500 → dark:text-graphite-muted | **FAIL** |
| 5 | text-gray-400 → dark:text-graphite-faint | PASS |
| 6 | Inputs use dark:bg-graphite-surface-2 + dark:placeholder-graphite-faint | PASS |
| 7 | BookOpen/web link/KB button semantic dark colors | **FAIL** |
| 8 | HtmlContent key prop + correct isDark hex values | PASS |
| 9 | Submit button has no dark: bg override | PASS |
| 10 | No files other than the 6 modified | PASS |

## Findings requiring correction

### F1 — ArticleGenerationForm.tsx line 169: wrong dark token for text-gray-500

```tsx
// Current (wrong):
<summary className="cursor-pointer text-gray-500 dark:text-graphite-faint select-none">

// Required:
<summary className="cursor-pointer text-gray-500 dark:text-graphite-muted select-none">
```

`text-gray-500` maps to `dark:text-graphite-muted`. Only `text-gray-400` and `text-gray-300` map to `dark:text-graphite-faint`.

### F2 — ArticleSourceList.tsx line 11: wrong dark token for Globe icon

```tsx
// Current (wrong):
<Globe className="w-4 h-4 text-blue-500 dark:text-graphite-accent shrink-0" />

// Required:
<Globe className="w-4 h-4 text-blue-500 dark:text-blue-400 shrink-0" />
```

The Globe icon represents a web source type (informational, non-active). The guide maps non-accent `text-blue-*` → `dark:text-blue-400`. The `dark:text-graphite-accent` token is reserved for active/selected state indicators. The parallel web anchor link on line 35 already correctly uses `dark:text-blue-400`.

---

**Status:** REVISION_NEEDED
