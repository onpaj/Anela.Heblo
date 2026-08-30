# Design: Exclude ArticleId from SubmitArticleFeedbackRequest Body Contract

## Component Design

### `SubmitArticleFeedbackRequest` (backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs)
- Responsibility: MediatR request DTO for `POST /api/Articles/{id:guid}/feedback`. Carries the feedback payload plus the article id needed by the handler.
- Change: annotate `ArticleId` with `[System.Text.Json.Serialization.JsonIgnore]`. The property stays a normal public `{ get; set; }` `Guid` — only its JSON (de)serialization and OpenAPI-schema visibility are affected. `PrecisionScore`, `StyleScore`, `Comment` and their validation attributes (`[Range(1,5)]`, `[MaxLength(1000)]`) are untouched.
- Contract after change: any `articleId` present in the inbound JSON body is ignored by the model binder; the property is not listed in the generated OpenAPI schema.

### `ArticlesController.SubmitFeedback` (backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs)
- Responsibility: unchanged. Accepts route `Guid id` and `[FromBody] SubmitArticleFeedbackRequest request`, executes `request.ArticleId = id;` before dispatch, sends via `_mediator.Send(request, ct)`.
- No logic change — `[JsonIgnore]` only affects wire (de)serialization, not in-memory C# property access, so this remains the sole place `ArticleId` is ever set to a meaningful value.

### `SubmitArticleFeedbackHandler`
- Responsibility: unchanged. Continues to read `request.ArticleId` as a normal in-memory property (ownership check, status check, duplicate-feedback check, persistence). No code changes required.

### Generated OpenAPI schema / TypeScript client (`frontend/src/api/generated/api-client.ts`)
- Responsibility: build-time artifact regenerated via NSwag (`dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`, or `npm run generate-client`).
- Contract after change: `SubmitArticleFeedbackRequest`'s generated class/interface loses the `articleId?: string` field, and its `init`/`toJSON` methods no longer reference it. No other generated type changes. Never hand-edited — regenerate and diff to confirm only this type changed.

### `useSubmitArticleFeedbackMutation` (frontend/src/api/hooks/useArticles.ts)
- Responsibility: builds the request DTO and invokes the typed client method.
- Change: the `new SubmitArticleFeedbackRequest({ ... })` object literal drops the `articleId` key (compile error otherwise, since the field no longer exists on the regenerated type). The call `client.articles_SubmitFeedback(articleId, request)` is unaffected — the route-level `articleId` argument is unchanged and still carries the id.
- Contract after change:
  ```typescript
  const request = new SubmitArticleFeedbackRequest({
    precisionScore: payload.precisionScore,
    styleScore: payload.styleScore,
    comment: payload.comment,
  });
  const response = await client.articles_SubmitFeedback(articleId, request);
  ```

### Test: `frontend/src/api/hooks/__tests__/useArticles.test.ts`
- The existing assertion on the mocked call (`~line 321-325`, `expect.objectContaining({ articleId: 'article-1', ... })` for the body argument) must be updated to drop `articleId` from the expected body shape. The route-level `articleId` argument passed to `client.articles_SubmitFeedback` is unaffected and stays in the assertion.

### Documentation: `docs/development/api-client-generation.md`
- The canonical example snippet (`~line 166-183`) constructing `SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment })` is updated to drop `articleId` from the constructor call, matching the post-fix generated shape. Surrounding explanatory text (business-outcome-as-HTTP-status pattern) is left as-is.

## Data Schemas

### Request schema — `POST /api/Articles/{id:guid}/feedback`

Before:
```json
{
  "articleId": "guid (ignored/overwritten server-side today)",
  "precisionScore": 1,
  "styleScore": 1,
  "comment": "string, max 1000 chars, optional"
}
```

After:
```json
{
  "precisionScore": 1,
  "styleScore": 1,
  "comment": "string, max 1000 chars, optional"
}
```
- `articleId` is removed from the OpenAPI schema and from the wire contract entirely. The article acted on is always the one identified by the `{id}` route segment.
- Validation unchanged: `precisionScore`/`styleScore` in `[1,5]`, `comment` optional with `[MaxLength(1000)]`.

### Response schema — unchanged
`SubmitArticleFeedbackResponse` (`precisionScore`, `styleScore`, `feedbackComment`, plus the `BaseResponse` success/error envelope). Status codes 200 and 409 unchanged.

### Generated TypeScript client shape (after fix)
```typescript
class SubmitArticleFeedbackRequest {
  precisionScore?: number;
  styleScore?: number;
  comment?: string | undefined;
  // articleId field removed
}

// unchanged signature — route id is a separate positional argument
articles_SubmitFeedback(id: string, request: SubmitArticleFeedbackRequest): Promise<SubmitArticleFeedbackResponse>;
```

No database schema, domain entity, or event payload changes — this is a wire-contract-only change confined to one DTO's JSON visibility.
