### Question 1
`steps` element typing. If the generated `GetArticleTraceResponse.steps` element type does not match the local `ArticleGenerationStep` interface exactly, is a single narrow cast `as ArticleGenerationStep[]` acceptable (as the brief's suggested fix uses), or should the local interface be aligned with the generated type instead?

**Answer:** **Neither — explicitly map each generated `ArticleGenerationStepDto` to the local `ArticleGenerationStep` shape inside the query function, mirroring the existing mapping pattern used by `useGetArticleQuery` for `sources` (`frontend/src/api/hooks/useArticles.ts:170-182`) and for `createdAt`/`generatedAt` (`...:166-167`).** Do **not** use a blanket `as ArticleGenerationStep[]` cast and do **not** modify the local `ArticleGenerationStep` interface. Concretely, the refactored hook should produce each step as:

```typescript
steps: (data.steps ?? []).map((step) => ({
  id: step.id ?? '',
  stepName: step.stepName ?? '',
  sequence: step.sequence ?? 0,
  status: step.status ?? '',
  startedAt: step.startedAt?.toISOString() ?? '',
  finishedAt: step.finishedAt?.toISOString() ?? null,
  durationMs: step.durationMs ?? null,
  model: step.model ?? null,
  inputJson: step.inputJson ?? null,
  outputJson: step.outputJson ?? null,
  errorMessage: step.errorMessage ?? null,
})),
```

The same mapping discipline (do not cast, project explicitly) MUST also be applied in **`useArticleFeedbackListQuery`**: the generated `GetArticleFeedbackListResponse` exposes `items` (not `articles`), each item has `createdAt` (not `generatedAt`) and `hasComment` (not `hasFeedback`), and no `feedbackComment` field at all — so `return data;` will not satisfy the consumer-facing `ArticleFeedbackListResponse` shape that `useArticleFeedbackAdapter.ts:15-25` reads. The hook MUST project the generated response into the existing local interface (`articles` ← `items`, `generatedAt` ← `createdAt.toISOString()`, `hasFeedback` ← `hasComment`, `feedbackComment` ← `null` for now), preserving FR-4 (no consumer changes).

**Rationale:** Direct inspection of the generated client confirms the types genuinely differ, not just nominally: `ArticleGenerationStepDto.startedAt` is `Date | undefined` while local `ArticleGenerationStep.startedAt` is `string`; `finishedAt` is `Date | undefined` vs `string | null`; every field is optional on the generated DTO but required on the local interface (`frontend/src/api/generated/api-client.ts:13077-13151` vs `frontend/src/api/hooks/useArticleTrace.ts:4-16`). A `as ArticleGenerationStep[]` cast would lie to TypeScript about the date types and re-introduce the same "stale `as any`" class of fragility this refactor is removing — defeating NFR-2. Explicit mapping is already the codebase's established pattern (sources, createdAt, generatedAt in `useGetArticleQuery`), so this answer keeps one pattern across all Article hooks (NFR-4) and surfaces any future generated-shape drift at compile time. The local interface stays untouched because it is the consumer-facing contract and changing it would expand scope beyond FR-4. The feedback-list extension of the same answer is mandatory because returning the generated DTO directly there would silently break the existing consumer adapter (`useArticleFeedbackAdapter.ts`) which reads field names that don't exist on the generated type.

### Question 2
Follow-up for `useSubmitArticleFeedbackMutation`. Should this refactor also file a tracking issue / TODO comment flagging that mutation for a future review (it still uses `(apiClient as any).baseUrl`)?

**Answer:** **Add a single-line `// TODO` comment at the raw-fetch site, no separate GitHub issue.** Place it immediately above the `const fullUrl = ...` line at `frontend/src/api/hooks/useArticles.ts:222`, worded: `// TODO(arch-review 2026-05-25): Uses private apiClient internals (baseUrl/http) via `as any` — same fragility as the hooks refactored in this PR. Keep raw fetch only for 409 branch; revisit when generated client exposes typed-mutation 409 handling.` Do **not** touch the mutation's behavior, do **not** change the fetch call. The TODO carries the date and the justification so future readers see why this one mutation was left as-is.

**Rationale:** The project is a solo-dev workspace with AI-assisted PR review (per `CLAUDE.md` "Project facts"), so a GitHub issue adds tracking overhead with negligible discovery benefit over an in-file `// TODO` that any future reader of `useArticles.ts` will encounter the moment they look at the function. The dated TODO meets the spec's FR-5 requirement that the mutation be flagged for follow-up, satisfies the "Surgical changes" rule (one line, no behavior change), and keeps the technical-debt note co-located with the code it describes (which a GitHub issue would not).

### Question 3
Authenticated-client accessor name. The brief uses `getAuthenticatedApiClient()`; confirm this matches the actual exported helper used by `useListArticlesQuery` in this codebase.

**Answer:** **Confirmed — use `getAuthenticatedApiClient` exactly.** It is exported from `frontend/src/api/client.ts:232`, imported by `useArticleTrace.ts:2` and `useArticles.ts:2` already, and is the helper `useListArticlesQuery` calls at `useArticles.ts:129`. All three refactored hooks (`useArticleTraceQuery`, `useArticleFeedbackListQuery`, `useGetArticleQuery`) already import it; no new import is required. Do **not** use the alternative `getAuthenticatedApiClientWithProvider` (`client.ts:342`) — that variant is for callers that want to inject a custom token provider and is not the project's standard accessor.

**Rationale:** Grep across `frontend/src` confirms `getAuthenticatedApiClient` is the singular accessor used by every Article hook today, including the two outliers (`useArticleTraceQuery:27`, `useArticleFeedbackListQuery:260`) that already obtain the client through it before falling back to raw fetch — so the refactor merely uses the already-obtained client's typed methods instead of poking at its private fields. The provider variant exists for a different use case (custom auth provider injection) and applying it here would expand scope beyond the brief and break FR-4's "no consumer changes" guarantee.
