### task: fix-step-status-type-safety

**Goal:** Replace `string` with `ArticleGenerationStepStatus` throughout the article-trace step DTO, generated client interface, React hook, and debug panel component — eliminating stringly-typed status comparisons with no behavioral change.

**Files to change:**

- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceResponse.cs`: add using directive; change `Status` property type from `string` to `ArticleGenerationStepStatus`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceHandler.cs`: remove `.ToString()` call when mapping `Status`
- `frontend/src/api/hooks/useArticleTrace.ts`: change the locally-declared `status: string` field in the hook's own interface to `status: ArticleGenerationStepStatus`; add the import from the generated client
- `frontend/src/features/articles/ArticleDebugPanel.tsx`: import `ArticleGenerationStepStatus`; type `STEP_STATUS_COLORS` and `STEP_STATUS_LABELS` as `Record<ArticleGenerationStepStatus, string>`; replace string literals `'Running'`, `'Succeeded'`, `'Failed'` with the corresponding enum members

**Steps:**

1. Open `GetArticleTraceResponse.cs`. Add `using Anela.Heblo.Domain.Features.Article;` to the using block. Change the `Status` property declaration from `public string Status { get; set; } = string.Empty;` to `public ArticleGenerationStepStatus Status { get; set; }`.

2. Open `GetArticleTraceHandler.cs`. In the step-mapping projection, change `Status = s.Status.ToString(),` to `Status = s.Status,`.

3. Open `frontend/src/api/hooks/useArticleTrace.ts`. Add an import for `ArticleGenerationStepStatus` from `'../generated/api-client'`. In the hook's local interface (or type) that redeclares the step shape, change `status: string` to `status: ArticleGenerationStepStatus`.

4. Open `frontend/src/features/articles/ArticleDebugPanel.tsx`. Add `import { ArticleGenerationStepStatus } from '../../api/generated/api-client';`. Change the type of `STEP_STATUS_COLORS` from an implicit or loosely-typed object to `Record<ArticleGenerationStepStatus, string>`. Do the same for `STEP_STATUS_LABELS`. Replace every occurrence of the string literals `'Running'`, `'Succeeded'`, and `'Failed'` used as keys or comparands with `ArticleGenerationStepStatus.Running`, `ArticleGenerationStepStatus.Succeeded`, and `ArticleGenerationStepStatus.Failed`.

5. From the repo root, run `dotnet build` and confirm zero errors.

6. From `frontend/`, run `npm run build`. This regenerates the NSwag TypeScript client (emitting the `ArticleGenerationStepStatus` enum) and then compiles the React app. Confirm zero type errors and zero build errors.

7. Run `dotnet format` and `npm run lint` and fix any formatting issues that are flagged.

**Acceptance criteria:**

- `dotnet build` exits with code 0 and no warnings related to the changed files.
- `npm run build` exits with code 0; the generated client at `frontend/src/api/generated/api-client.ts` contains an `ArticleGenerationStepStatus` enum with members `Running`, `Succeeded`, and `Failed`.
- `GetArticleTraceResponse` no longer has a `string` `Status` property; it uses `ArticleGenerationStepStatus`.
- `GetArticleTraceHandler` mapping does not call `.ToString()` on the status.
- `useArticleTrace.ts` does not contain `status: string` for the step status field.
- `ArticleDebugPanel.tsx` contains no bare string literals `'Running'`, `'Succeeded'`, or `'Failed'` used as status values or map keys.
- `npm run lint` passes with no new errors.
- `dotnet format` produces no diff.

**Dependencies:** none
