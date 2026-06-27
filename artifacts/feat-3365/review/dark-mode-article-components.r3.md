# Dark Mode Article Components — Revision 3 Review

**Date:** 2026-06-26
**Reviewer:** Claude Code (automated)
**Scope:** Two targeted fixes from Revision 2 feedback

---

## Fix 1 — `ArticleGenerationForm.tsx`: `<summary>` style guide token

**Required:** `text-gray-500 dark:text-graphite-muted`
**Actual (line 169):**
```tsx
<summary className="cursor-pointer text-gray-500 dark:text-graphite-muted select-none">Stylový průvodce (volitelné)</summary>
```

Correct. The previously incorrect `dark:text-graphite-faint` token has been replaced with `dark:text-graphite-muted`.

**Result: PASS**

---

## Fix 2 — `ArticleSourceList.tsx`: Globe icon dark-mode colour

**Required:** `text-blue-500 dark:text-blue-400`
**Actual (line 11):**
```tsx
if (type === 'Web') return <Globe className="w-4 h-4 text-blue-500 dark:text-blue-400 shrink-0" />;
```

Correct. The previously incorrect `dark:text-graphite-accent` token has been replaced with `dark:text-blue-400`, keeping the icon in the blue semantic range in dark mode.

**Result: PASS**

---

## Sanity check — no residual `text-gray-500 dark:text-graphite-faint` in article files

Grepped pattern `text-gray-500 dark:text-graphite-faint` across all `.tsx` files in `frontend/src/features/articles/`.

**Zero matches found.** Every `text-gray-500` element in the six article component files now correctly pairs with `dark:text-graphite-muted`.

---

## Summary

Both revision-3 fixes are correctly applied. No regressions introduced and no residual incorrect tokens remain across the article component set.

**Status:** PASS
