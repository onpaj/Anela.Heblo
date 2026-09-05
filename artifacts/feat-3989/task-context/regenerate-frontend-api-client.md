### task: regenerate-frontend-api-client

## Goal
Regenerate `frontend/src/api/generated/api-client.ts` from the updated backend schema so `SubmitArticleFeedbackRequest`'s generated class loses the `articleId` field, verifying via `git diff` that no unrelated schema drift is bundled into this change.

## Files to change

**Edit (regenerated, not hand-edited):**
- `frontend/src/api/generated/api-client.ts`

**Verify only, no change expected:**
- Every other exported type/method in `api-client.ts` — must be byte-identical after regeneration; any other diff indicates unrelated backend schema drift on this branch that must not be bundled into this change.

**Do not touch:**
- `frontend/src/api/hooks/useArticles.ts` and its test file — those are handled in the next task, after this task confirms the regenerated type shape.

## Steps

- [ ] **Step 1: Confirm the working tree is clean before regenerating**

```bash
git status --short
```

Expected: no output (clean tree) other than the commit from the previous task — i.e., nothing already modified in `frontend/src/api/generated/api-client.ts`.

- [ ] **Step 2: Regenerate the TypeScript client**

Run from the repository root. Note: `frontend/package.json` has no `generate-client`/`prebuild` npm script (confirmed absent), so use the MSBuild target directly:

```bash
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
```

Expected: output ends with:
```
Generating TypeScript API client for frontend...
Frontend API client generation completed.
Build succeeded.
```

- [ ] **Step 3: Diff the regenerated file and confirm the change is scoped to `SubmitArticleFeedbackRequest`**

```bash
git diff --stat frontend/src/api/generated/api-client.ts
git diff frontend/src/api/generated/api-client.ts
```

Expected: the diff touches only the `SubmitArticleFeedbackRequest` class and the `ISubmitArticleFeedbackRequest` interface, removing:
- the `articleId?: string;` field from the class body,
- the `this.articleId = _data["articleId"];` line from `init(...)`,
- the `data["articleId"] = this.articleId;` line from `toJSON(...)`,
- the `articleId?: string;` field from `ISubmitArticleFeedbackRequest`.

**If the diff includes changes outside `SubmitArticleFeedbackRequest`/`ISubmitArticleFeedbackRequest`** (e.g. unrelated methods or types from other in-flight backend work), do not commit the full regeneration. Instead, revert the file (`git checkout -- frontend/src/api/generated/api-client.ts`) and hand-apply only the four lines listed above, matching the generator's actual output for that type exactly. Re-run this diff check afterward to confirm the change is scoped correctly.

- [ ] **Step 4: Confirm the swagger document itself no longer lists articleId (spot check)**

```bash
grep -n "articleId" frontend/src/api/generated/api-client.ts
```

Expected: no output (no matches) — the string `articleId` no longer appears anywhere in the generated file.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore(api-client): regenerate client for SubmitArticleFeedbackRequest ArticleId removal

Regenerated via 'dotnet msbuild backend/src/Anela.Heblo.API
-t:GenerateFrontendClientManual' after adding [JsonIgnore] to
SubmitArticleFeedbackRequest.ArticleId on the backend. Diff confirmed
scoped to SubmitArticleFeedbackRequest/ISubmitArticleFeedbackRequest
only.

Refs #3989"
```

---
