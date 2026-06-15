# Implementation: article-generation doc auth fix

## What was implemented

Corrected two stale authorization paragraphs in `docs/features/article-generation.md`:

1. **Section 7 line 143** — replaced the old "POST /generate requires role `marketing_writer`; reads require `heblo_user`" sentence with a description of the `FeatureAuthorize` model naming the `marketing.article.write` and `marketing.article.read` permission strings.

2. **Section 14 (Auth & Roles)** — replaced five stale bullets referencing `marketing_writer`, `[Authorize(Roles = "marketing_writer")]`, and the deleted `AuthorizationConstants` class with:
   - Prose explaining the `FeatureAuthorize` custom attribute
   - A verbatim C# snippet from `ArticlesController.cs` (class-level + method-level attributes)
   - Bullet list of which endpoints require which permission strings
   - Source-of-truth paragraph explaining the `access-matrix.json` → `AccessMatrixGen` → `AccessRoles.generated.cs` pipeline
   - Migration-aware historical note covering `article_generator`, `marketing_reader`, `marketing_writer` retirement

Pre-flight verification confirmed `AuthorizationConstants.cs` is absent (deleted by the 2026-06-08 migration), so the spec's original `AuthorizationConstants.Policies.MarketingReader` target no longer exists. The arch-review's amended spec was followed instead.

## Files created/modified

- `docs/features/article-generation.md` — two replacements (section 7 line 143 and section 14 body)

## Tests

No test files — documentation-only change. The "test" is a grep sweep:

```bash
grep -nE 'marketing_writer|marketing_reader|article_generator|AuthorizationConstants' \
  docs/features/article-generation.md
# expected: 0 matches
```

Both spec compliance reviewer and code quality reviewer confirmed ✅ COMPLIANT / ✅ APPROVED.

## How to verify

```bash
cd /path/to/worktree
git show HEAD -- docs/features/article-generation.md
grep -nE 'marketing_writer|marketing_reader|article_generator|AuthorizationConstants' docs/features/article-generation.md  # must be 0
grep -cE 'marketing\.article\.read' docs/features/article-generation.md   # must be non-zero
grep -cE 'marketing\.article\.write' docs/features/article-generation.md  # must be non-zero
```

## Notes

- The spec quoted `[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]` as the correct snippet — this is stale. `AuthorizationConstants.cs` was deleted by the 2026-06-08 migration. The arch-review caught this and provided the correct target (`FeatureAuthorize`). The implementation follows the arch-review, not the spec.
- The correct role for `POST /generate` is `marketing.article.write` (write-level, matching `AccessLevel.Write` on the method), not `marketing.article.read` (the spec's original target).
- No `.cs` or `.ts` files were touched; build/format gates do not apply.

## PR Summary

Corrects two stale authorization sections in the article-generation feature doc that still referenced the pre-migration model (`[Authorize(Roles = "marketing_writer")]`, `AuthorizationConstants.Policies.MarketingReader`) after the 2026-06-08 permission-source-of-truth migration deleted `AuthorizationConstants.cs` and replaced both attributes on `ArticlesController` with `[FeatureAuthorize(Feature.Marketing_Article)]` (class-level, Read default) and `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` (on `Generate`). The generated permission strings are `marketing.article.read` and `marketing.article.write`; the legacy role strings no longer exist in the codebase.

Both sections now show the correct C# attribute form, the actual permission strings, the source-of-truth pipeline (`access-matrix.json` → `Anela.Heblo.AccessMatrixGen` → `AccessRoles.generated.cs`), and a migration-aware historical note so future readers don't re-introduce the retired role names.

### Changes
- `docs/features/article-generation.md` — section 7 line 143 (auth summary sentence) and section 14 (Auth & Roles body) rewritten to match current `FeatureAuthorize`/`access-matrix.json` model

## Status
DONE
