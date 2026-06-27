## Review Result: CLEAN

### Blocking

- None

### Advisory

**1. `red-900/30` applied uniformly to error block backgrounds (minor inconsistency with spec r1)**

`spec.r1.md` specifies `dark:bg-red-950/40` for error _block_ backgrounds (the `bg-red-50 border-red-200` div in `ArticleDetail.tsx`, and the `bg-red-50` step error `<p>` in `ArticleDebugPanel.tsx`), distinct from the `red-900/30` chosen for status-pill backgrounds. The diff applies `dark:bg-red-900/30` to all of them uniformly. The context notes the `red-900/30` correction for status pills; this looks like the implementer applied it everywhere for consistency. The visual difference is subtle (`red-950/40` is slightly darker), and the contrast is fine either way. Worth noting in case the design team cares about the distinction — but no change required.

**2. `dark:placeholder-graphite-faint` on `<select>` elements**

`ArticleGenerationForm.tsx` adds `dark:placeholder-graphite-faint` to all three `<select>` elements. The `placeholder` pseudo-element does not apply to `<select>` in any browser — this class is dead CSS. It is harmless and Tailwind will purge it in production, but it adds noise to the class strings. Could be cleaned up in a follow-on tidy pass.

**3. `HtmlContent` reads `document.documentElement` during render (not a bug, but worth documenting)**

`isDark` is computed synchronously from the DOM on every render. The `key={isDark ? 'dark' : 'light'}` trick only forces iframe remount when the _parent_ re-renders with a different `isDark` value. This means the iframe does _not_ react to live theme switches unless something (e.g., a theme-context state change) causes a re-render of `ArticleDetail`. Confirmed as accepted per ADR-006 scope, but the assumption ("ArticleDetail re-renders on theme change") should be documented as a constraint in a code comment so the next developer doesn't spend time debugging it.

**4. Spec r1 palette table not updated to reflect corrected tokens**

The implementation diverges from `spec.r1.md` in five places that were all intentional corrections (status pill semantic hues, hover/selected tokens, input background surface level, source icon/link colors, submit button arch decision). `spec.r1.md` was never revised to capture these — it still shows the superseded tokens. Consider writing a `spec.r2.md` or a changelog entry in the artifacts directory so that future reviewers don't have to cross-reference the PR context to understand why the diff differs from the spec.
