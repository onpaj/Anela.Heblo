# Article Generation Doc — Authorization Section Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct two stale paragraphs in `docs/features/article-generation.md` (section 7 line 143 and section 14) so they describe the current `FeatureAuthorize` / `access-matrix.json` authorization model on `ArticlesController` instead of the legacy `marketing_writer` / `[Authorize(Roles=…)]` model that no longer exists in code.

**Architecture:** Documentation-only change in one markdown file. No code, no migrations, no feature flags. The replacement prose is fully prescribed in the tasks below and is derived verbatim from `ArticlesController.cs`, `AccessRoles.generated.cs`, `FeatureAuthorizeAttribute.cs`, and `access-matrix.json`. Verification is a grep over the edited file confirming the legacy literals (`marketing_writer`, `marketing_reader`, `article_generator`, `AuthorizationConstants`, `[Authorize(Roles = "marketing_writer")]`, `heblo_user` in the access-rules context) are absent.

**Tech Stack:** Markdown. `grep`. Git.

---

## Source-of-Truth References

The replacement text in this plan is sourced from these files in the worktree. Do not paraphrase from memory; if any of these no longer match what is quoted here, **stop and re-read them** before proceeding — they are the only source of truth and the spec is known to be stale.

| Fact | File | Line |
|---|---|---|
| Class-level attribute `[FeatureAuthorize(Feature.Marketing_Article)]` | `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` | 15 |
| Method-level attribute `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` on `Generate` | `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` | 28 |
| `FeatureAuthorize` is a custom `AuthorizeAttribute` subclass | `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs` | whole file |
| `MarketingArticleRead = "marketing.article.read"` | `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` | 45 |
| `MarketingArticleWrite = "marketing.article.write"` | `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` | 46 |
| `(Feature.Marketing_Article, AccessLevel.Read) => MarketingArticleRead` | `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` | 104 |
| `(Feature.Marketing_Article, AccessLevel.Write) => MarketingArticleWrite` | `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` | 105 |
| `{ "key": "Marketing_Article", "label": "Články", "hasWrite": true }` | `access-matrix.json` | 26 |

The `AuthorizationConstants.cs` file referenced in the brief and in the spec **no longer exists** — it was deleted by the 2026-06-08 permission-source-of-truth migration (see `docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md`). Do **not** revive any reference to it.

---

## File Structure

Files touched by this plan:

- **Modify:** `docs/features/article-generation.md` — exactly two replacements (section 7 line 143 and section 14 lines 363–369). No other lines are edited.

Files **read** for verification but never modified:

- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs`
- `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs`
- `access-matrix.json`

No new files are created. No files are deleted.

---

## Task Plan

### Task 1: Pre-flight verification of source-of-truth references

The spec and brief are known to be stale. Before editing, confirm the lines this plan quotes still match the current files. If any reference has drifted, halt and notify — do not edit until the references in this plan are reconciled.

**Files:**
- Read: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:1-35`
- Read: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs:40-50`
- Read: `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs:100-110`
- Read: `access-matrix.json` (look for the `Marketing_Article` row)

- [ ] **Step 1: Confirm controller attributes**

Run from the worktree root:

```bash
grep -nE 'FeatureAuthorize|HttpPost\("generate"\)|class ArticlesController' \
  backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs
```

Expected output (line numbers must match; literal text must match):

```
15:[FeatureAuthorize(Feature.Marketing_Article)]
18:public sealed class ArticlesController : BaseApiController
27:    [HttpPost("generate")]
28:    [FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
```

If line numbers shift but the four matches are present and adjacent in the same order, that is fine — record the new numbers and use them in the section 14 prose. If any of the four literals is missing, halt: the controller has been modified in a way this plan does not cover.

- [ ] **Step 2: Confirm permission string constants**

```bash
grep -nE 'MarketingArticleRead|MarketingArticleWrite' \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs
```

Expected (line numbers may drift; literal right-hand sides must not):

```
45:    public const string MarketingArticleRead = "marketing.article.read";
46:    public const string MarketingArticleWrite = "marketing.article.write";
104:        (Feature.Marketing_Article, AccessLevel.Read) => MarketingArticleRead,
105:        (Feature.Marketing_Article, AccessLevel.Write) => MarketingArticleWrite,
```

If either string literal differs (e.g. `marketing.articles.write` plural), halt — the matrix generator output has diverged from this plan and the doc text must be updated to match the new strings.

- [ ] **Step 3: Confirm the access-matrix entry**

```bash
grep -n '"Marketing_Article"' access-matrix.json
```

Expected:

```
26:    { "key": "Marketing_Article", "label": "Články", "hasWrite": true },
```

If `hasWrite` is `false` or absent, halt — the matrix no longer expresses a write level for the feature and the doc text in Task 3 must be revised.

- [ ] **Step 4: Confirm `AuthorizationConstants` is gone**

```bash
test ! -e backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs && \
  echo "OK: AuthorizationConstants.cs is absent (as expected)"
```

Expected output:

```
OK: AuthorizationConstants.cs is absent (as expected)
```

If the file is present, halt — the migration was reverted and the spec's original premise may have become valid again; the plan must be re-scoped.

- [ ] **Step 5: Confirm the legacy doc text is still present (the lines to be replaced)**

```bash
grep -nE 'marketing_writer|marketing_reader|article_generator|AuthorizationConstants' \
  docs/features/article-generation.md
```

Expected matches (line numbers may drift slightly; the literals must all appear):

```
143:All endpoints `[Authorize]`. POST `/generate` requires role `marketing_writer`; reads require `heblo_user`.
365:- Uses the unified `marketing_writer` role — assigned to Heblo human users with content-creation responsibilities (admin assigns)
366:- `[Authorize(Roles = "marketing_writer")]` on `POST /generate`
369:- **Note:** The earlier `article_generator` role was removed and replaced by the unified `marketing_writer` role.
```

If any of these four lines is missing, halt — someone else already partially edited the section and the replacement strategy in Tasks 2 and 3 may not apply cleanly. Re-read the file end-to-end before continuing.

- [ ] **Step 6: Commit a no-op marker is not needed**

No commit yet — Task 1 produced no changes. Proceed to Task 2.

---

### Task 2: Replace section 7 line 143

Replace the single-sentence summary at the end of the "REST API" section opener so it describes the `FeatureAuthorize` model and the actual permission strings.

**Files:**
- Modify: `docs/features/article-generation.md` — line 143 (one sentence)

- [ ] **Step 1: Perform the replacement**

Use the `Edit` tool (or the equivalent in your harness) with these exact strings.

`old_string`:

```
All endpoints `[Authorize]`. POST `/generate` requires role `marketing_writer`; reads require `heblo_user`.
```

`new_string`:

```
All endpoints are gated through `[FeatureAuthorize(Feature.Marketing_Article, …)]` (class-level default is `AccessLevel.Read`). `POST /generate` requires `AccessLevel.Write` — permission string `marketing.article.write` (`AccessRoles.MarketingArticleWrite`). `GET /{id}`, `GET /`, and `POST /{id}/feedback` require `AccessLevel.Read` — permission string `marketing.article.read` (`AccessRoles.MarketingArticleRead`).
```

- [ ] **Step 2: Verify the line was replaced exactly once**

```bash
grep -nE 'FeatureAuthorize\(Feature\.Marketing_Article, …\)' docs/features/article-generation.md
```

Expected output: exactly one match, at the line that used to be 143 (now contains the new sentence).

```bash
grep -cE 'POST `/generate` requires role `marketing_writer`' docs/features/article-generation.md
```

Expected output: `0`

- [ ] **Step 3: Do not commit yet**

Section 14 is still wrong. Continue to Task 3 and commit both edits together in Task 5.

---

### Task 3: Replace section 14 ("Auth & Roles")

Replace the entire body of section 14 (heading `## 14. Auth & Roles` stays, the five bullets and the trailing note are replaced) with prose that:

1. Names `FeatureAuthorize` as the gating attribute and points at its source file.
2. Shows the class-level and method-level attributes verbatim from the controller.
3. Names `marketing.article.write` as the role required by `POST /generate` and `marketing.article.read` as the role required by the read endpoints and the feedback POST.
4. Describes the source-of-truth pipeline (`access-matrix.json` → `AccessMatrixGen` → generated files).
5. Replaces the historical note with the migration-aware text below.

**Files:**
- Modify: `docs/features/article-generation.md` — section 14 (currently lines 363–369 — heading + five bullets + note)

- [ ] **Step 1: Perform the replacement**

Use the `Edit` tool with these exact strings.

`old_string`:

````
## 14. Auth & Roles

- Uses the unified `marketing_writer` role — assigned to Heblo human users with content-creation responsibilities (admin assigns)
- `[Authorize(Roles = "marketing_writer")]` on `POST /generate`
- `[Authorize]` (default `heblo_user`) on `GET` and feedback endpoints
- `KnowledgeBaseUpload` policy is **not** reused — articles are a separate concern
- **Note:** The earlier `article_generator` role was removed and replaced by the unified `marketing_writer` role.
````

`new_string`:

````
## 14. Auth & Roles

`ArticlesController` is gated by the `FeatureAuthorize` attribute (a custom `AuthorizeAttribute` subclass at `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs`). The class-level attribute applies a read-level gate to every action by default; `POST /generate` overrides this with a write-level gate.

```csharp
// backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs
[FeatureAuthorize(Feature.Marketing_Article)]
[ApiController]
[Route("api/[controller]")]
public sealed class ArticlesController : BaseApiController
{
    [HttpPost("generate")]
    [FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
    public async Task<ActionResult<GenerateArticleResponse>> Generate(...)
}
```

- `POST /generate` requires `AccessLevel.Write` — permission string `marketing.article.write` (`AccessRoles.MarketingArticleWrite`).
- `GET /{id}`, `GET /`, and `POST /{id}/feedback` require `AccessLevel.Read` (inherited from the class-level attribute) — permission string `marketing.article.read` (`AccessRoles.MarketingArticleRead`).
- The `KnowledgeBaseUpload` policy is **not** reused — articles are a separate concern.

**Source of truth.** Feature keys and access levels are declared in `access-matrix.json` (e.g. `{ "key": "Marketing_Article", "label": "Články", "hasWrite": true }`). The `Anela.Heblo.AccessMatrixGen` source generator compiles the matrix into `Feature.generated.cs`, `AccessRoles.generated.cs` (which exposes `AccessRoles.For(Feature, AccessLevel) → string` plus the per-feature constants used above), and `accessMatrix.generated.ts` for the frontend. Admins grant access by assigning the resulting permission strings via Azure AD app roles / group claims.

**Note.** The earlier `article_generator` role was removed. The interim `marketing_reader` / `marketing_writer` role strings were retired with the 2026-06-08 permission-source-of-truth migration (see `docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md`). Access is now declared in `access-matrix.json` and exposed via the generated `AccessRoles.MarketingArticleRead` / `AccessRoles.MarketingArticleWrite` constants (permission strings `marketing.article.read` / `marketing.article.write`).
````

- [ ] **Step 2: Verify the section was replaced**

```bash
grep -nE '^## 14\. Auth & Roles' docs/features/article-generation.md
```

Expected output: exactly one match.

```bash
sed -n '/^## 14\. Auth & Roles/,/^## 15\./p' docs/features/article-generation.md | \
  grep -cE 'FeatureAuthorize|marketing\.article\.(read|write)|access-matrix\.json'
```

Expected output: a non-zero count (at least 6 — the prose, the code fence, both permission strings used twice each, and the source-of-truth paragraph).

- [ ] **Step 3: Do not commit yet**

Proceed to Task 4 (document-wide sweep) before committing.

---

### Task 4: Document-wide consistency sweep

Confirm no stale role names or constants remain anywhere in the file. This is the closest analog to a "test" for a docs change: a clean grep is the success signal.

**Files:**
- Read: `docs/features/article-generation.md` (whole file)

- [ ] **Step 1: Grep for legacy literals**

Run from the worktree root:

```bash
grep -nE 'marketing_writer|marketing_reader|article_generator|AuthorizationConstants' \
  docs/features/article-generation.md
```

Expected output: **no matches** (the command exits non-zero with no lines). If any line is returned, that line must also be corrected before commit. Most likely candidates are a stray bullet, a sequence-diagram caption, or a comment in a code fence — re-read the matching line in context, replace `marketing_writer` with `marketing.article.write` (and `marketing_reader` with `marketing.article.read`) following the same prose model as section 14, then re-run this grep until clean.

- [ ] **Step 2: Grep for the old `[Authorize(Roles=…)]` snippet style for this controller**

```bash
grep -nE '\[Authorize\(Roles = "marketing_(writer|reader)"\)\]' \
  docs/features/article-generation.md
```

Expected output: **no matches**.

- [ ] **Step 3: Grep for `heblo_user` used as the read-side authorization claim**

The base role `heblo_user` is still mentioned in section 1 ("infrastructure") as the auth foundation — that mention is legitimate and stays. What must not remain is any sentence claiming that `heblo_user` *alone* gates the article read endpoints.

```bash
grep -nE 'heblo_user' docs/features/article-generation.md
```

Inspect each match. The only acceptable remaining mention is the one in the section 1 infrastructure bullet ("**Azure AD auth** via `Microsoft.Identity.Web` (cookie + JWT, role-based)" — note: that line does not mention `heblo_user` literally; if it does in your version, that is fine because it refers to the base role, not to article access). If a match implies `heblo_user` is sufficient for `GET /api/Articles/{id}`, edit it to reference `marketing.article.read` instead.

- [ ] **Step 4: Confirm both required permission strings appear**

```bash
grep -cE 'marketing\.article\.read' docs/features/article-generation.md
grep -cE 'marketing\.article\.write' docs/features/article-generation.md
```

Expected output: each command prints a non-zero integer (`marketing.article.read` appears at least twice — section 7 sentence and section 14 prose; `marketing.article.write` appears at least three times — section 7 sentence, section 14 bullet, section 14 historical note).

- [ ] **Step 5: Confirm the `FeatureAuthorize` snippet is present and intact**

```bash
grep -nE '\[FeatureAuthorize\(Feature\.Marketing_Article(, AccessLevel\.Write)?\)\]' \
  docs/features/article-generation.md
```

Expected output: at least two matches (the class-level and the method-level attribute lines inside the code fence in section 14).

- [ ] **Step 6: Sanity-check markdown still renders**

Skim section 14 in your editor's markdown preview or with `bat docs/features/article-generation.md` (or `glow`, or just `less`). Confirm:

- The code fence in section 14 opens with ` ```csharp ` and closes with ` ``` ` on its own line.
- Section 13 ("DI / Module Registration") and section 15 ("Frontend") immediately precede and follow section 14 — no orphaned bullets, no missing heading.

No commands here; this is a visual check.

- [ ] **Step 7: Do not commit yet**

All checks must pass before Task 5.

---

### Task 5: Commit

Single conventional commit. No code changes, so no build or test run is required for this file — but the project's CLAUDE.md requires `dotnet build` and `dotnet format` on every BE change. This change is documentation-only and touches no `.cs` file, so the build/format gate does not apply. Note this explicitly in the commit body.

**Files:**
- Stage: `docs/features/article-generation.md`

- [ ] **Step 1: Confirm only the intended file is dirty**

```bash
git status --short
```

Expected output (single line):

```
 M docs/features/article-generation.md
```

If any other file is dirty, stop — do not stage extras. Reset unrelated changes (`git checkout -- <file>`) only if you are certain they are not yours; otherwise ask.

- [ ] **Step 2: Review the diff**

```bash
git diff docs/features/article-generation.md
```

Expected: hunks at the two locations from Tasks 2 and 3, nothing else. Read each hunk in full — both the `-` removals (legacy `marketing_writer` / `marketing_reader` / `article_generator` / `[Authorize(Roles=…)]` references) and the `+` additions (the `FeatureAuthorize` snippet, the two permission strings, the source-of-truth paragraph, the migration-aware note). If a hunk touches anything outside section 7 line 143 or section 14, reset that part of the file and start over.

- [ ] **Step 3: Stage and commit**

```bash
git add docs/features/article-generation.md
git commit -m "$(cat <<'EOF'
docs(article-generation): align section 7 & 14 with FeatureAuthorize model

Section 7 line 143 and section 14 still described the pre-migration
authorization (`[Authorize(Roles = "marketing_writer")]`,
`AuthorizationConstants.Policies.MarketingReader`). The 2026-06-08
permission-source-of-truth migration deleted `AuthorizationConstants.cs`
and replaced both attributes on `ArticlesController` with
`[FeatureAuthorize(Feature.Marketing_Article)]` (class-level, defaults to
Read) and `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]`
(on `Generate`). The generated permission strings are
`marketing.article.read` and `marketing.article.write`; the legacy
`marketing_reader` / `marketing_writer` / `article_generator` role
strings no longer exist in the codebase.

Both passages now match the controller verbatim and reference the
`access-matrix.json` → `Anela.Heblo.AccessMatrixGen` →
`AccessRoles.generated.cs` pipeline. The historical note is rewritten
to acknowledge the migration so a future reader does not re-introduce
the deleted role names.

Documentation-only change. No `.cs` or `.ts` files touched; build and
format gates do not apply.

Refs:
- backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs (class L15, method L28)
- backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs (L45-46, L104-105)
- access-matrix.json (Marketing_Article row)
- docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md
EOF
)"
```

- [ ] **Step 4: Verify the commit landed**

```bash
git log -1 --stat
```

Expected output: HEAD commit shows `1 file changed`, only `docs/features/article-generation.md`, with insertion and deletion counts roughly matching the diff size from Step 2.

```bash
git show HEAD -- docs/features/article-generation.md | grep -cE 'marketing_(writer|reader)|article_generator|AuthorizationConstants'
```

Expected output: zero from the `+` side. (The grep will still match the `-` lines because they show what was removed — that is fine. The check is that no `+` line contains the legacy literals. If you want the strict version: `git show HEAD -- docs/features/article-generation.md | awk '/^\+/ && !/^\+\+\+/' | grep -cE 'marketing_(writer|reader)|article_generator|AuthorizationConstants'` must print `0`.)

- [ ] **Step 5: Done**

No build to run, no tests to run, no migrations, no rollout coordination. The doc is shippable. If a PR is being opened, the description should link to:

- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` line 28 (method-level `[FeatureAuthorize]`)
- `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs` lines 45–46 (permission constants)
- `access-matrix.json` line 26 (`Marketing_Article` row)
- `docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md` (migration that retired the legacy role strings)
- The original arch-review finding from 2026-05-29 (the brief that surfaced the inconsistency)

---

## Self-Review Notes

**Spec coverage** (against the arch-review's amended spec, which supersedes the original spec):

| Amended requirement | Task |
|---|---|
| FR-1 (corrected): name `marketing.article.write` for `POST /generate` and `marketing.article.read` for reads | Task 3, bullets in section 14 prose |
| FR-2 (corrected): snippet shows `[FeatureAuthorize(Feature.Marketing_Article)]` + `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` verbatim | Task 3, code fence in section 14 |
| FR-3 (corrected): historical note covers `article_generator` + retired `marketing_reader` / `marketing_writer` strings, points at migration | Task 3, "Note." paragraph |
| FR-4 (extended): also fix section 7 line 143 | Task 2 |
| NFR-1 (corrected): byte-accurate references to current files | Task 1 verification grep, Task 5 commit body |
| NFR-2 (corrected): PR description links | Task 5 Step 5 |
| NFR-3: reversibility | Trivially met — single-file markdown change |
| Out-of-scope: revive `AuthorizationConstants` | Explicitly prohibited in "Source-of-Truth References" intro |

**Placeholder scan:** No TBDs, no "add appropriate", no "similar to". All commands, file paths, line numbers, and replacement text are spelled out.

**Type consistency:** The two permission strings (`marketing.article.read` and `marketing.article.write`), the constant names (`AccessRoles.MarketingArticleRead`, `AccessRoles.MarketingArticleWrite`), the feature key (`Feature.Marketing_Article`), and the attribute name (`FeatureAuthorize`) are spelled identically across every task and every grep. The historical note uses backticks around all role/permission/constant tokens consistently with the rest of the doc.
