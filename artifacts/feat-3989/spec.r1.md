# Specification: Exclude ArticleId from SubmitArticleFeedbackRequest Body Contract

## Summary
`SubmitArticleFeedbackRequest.ArticleId` is currently a public, body-bound property that the controller unconditionally overwrites from the route parameter before dispatch, so any value a client sends in the JSON body is silently discarded. This spec covers annotating `ArticleId` with `[JsonIgnore]` so it is excluded from request-body binding and from the generated OpenAPI schema / TypeScript client, and updating the one frontend call site that currently (and now incorrectly) passes `articleId` into the request body.

## Background
`POST /api/Articles/{id:guid}/feedback` (`ArticlesController.SubmitFeedback`, `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:74-82`) binds `SubmitArticleFeedbackRequest` via `[FromBody]` and takes the article id from the route (`Guid id`). Immediately after binding, the controller does:

```csharp
request.ArticleId = id;
var result = await _mediator.Send(request, ct);
```

This means:
- Whatever `articleId` a client places in the JSON body is discarded and replaced with the route value.
- Because `ArticleId` is a plain public `{ get; set; }` property with no `[JsonIgnore]`/`[BindNever]`, NSwag includes it in the generated OpenAPI schema and in the generated TypeScript client's `SubmitArticleFeedbackRequest` class (`frontend/src/api/generated/api-client.ts`) as a writable `articleId?: string` field.
- The existing frontend hook `useSubmitArticleFeedbackMutation` (`frontend/src/api/hooks/useArticles.ts:213-227`) constructs the request with `new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment })`, i.e. it currently duplicates the id into both the body and the route, even though only the route value is ever honored server-side.
- `docs/development/api-client-generation.md` (lines 166-183) uses this exact endpoint as its own canonical documented example of the "business outcome as HTTP status" client pattern, and its snippet also constructs `SubmitArticleFeedbackRequest({ articleId, ... })` — that doc snippet will need a matching update once `articleId` is no longer part of the constructible request shape, so the docs don't show code that no longer type-checks.

This is a small architecture/API-contract cleanup identified by the automated arch-review routine (see `artifacts/feat-3989/brief.md`), not a user-facing feature. The DTO is already a class (not a record), consistent with this repo's rule that API DTOs must be classes — no change needed on that front.

## Functional Requirements

### FR-1: Exclude `ArticleId` from JSON (de)serialization of `SubmitArticleFeedbackRequest`
Annotate the `ArticleId` property on `SubmitArticleFeedbackRequest` (`backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs:9`) with `[System.Text.Json.Serialization.JsonIgnore]`, so the property:
- Is not populated from the incoming JSON request body (any `articleId` sent in the body is ignored rather than silently overwritten later).
- Is not emitted in the OpenAPI schema for this request type.
- Remains a normal public settable C# property, so `ArticlesController.SubmitFeedback` can continue to assign it from the route parameter (`request.ArticleId = id;`) exactly as it does today, and `SubmitArticleFeedbackHandler` can continue to read `request.ArticleId` unchanged.

**Acceptance criteria:**
- `SubmitArticleFeedbackRequest.ArticleId` carries `[JsonIgnore]`.
- No other property on `SubmitArticleFeedbackRequest` (`PrecisionScore`, `StyleScore`, `Comment`) is affected.
- `ArticlesController.SubmitFeedback` and `SubmitArticleFeedbackHandler` require no logic changes — `request.ArticleId = id;` (controller) and `request.ArticleId` reads (handler) continue to work exactly as before, because `[JsonIgnore]` only affects JSON (de)serialization, not normal C# property access.
- A request body that includes `"articleId": "<any guid>"` is accepted without error, and the value has no effect — the article acted on is always the one from the route (`{id}`).
- A request body that omits `articleId` entirely behaves identically to one that includes it (since the property is never read from the body either way).

### FR-2: Regenerate the OpenAPI schema and TypeScript client
Regenerate the frontend TypeScript client so `frontend/src/api/generated/api-client.ts` reflects the updated schema.

**Acceptance criteria:**
- After running `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (or the equivalent `npm run generate-client` from `frontend/`), the generated `SubmitArticleFeedbackRequest` class/interface in `api-client.ts` no longer declares an `articleId` field, and its `toJSON`/`init` methods no longer read/write `articleId`.
- The Swagger/OpenAPI document served at `/swagger/v1/swagger.json` no longer lists `articleId` under the `SubmitArticleFeedbackRequest` schema's properties.
- No other generated types change as a result of this edit.

### FR-3: Update the frontend call site that currently sets `articleId` on the request body
Update `useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/useArticles.ts` to stop passing `articleId` into the `SubmitArticleFeedbackRequest` constructor, since that field no longer exists on the generated type after FR-2 and the value was never honored by the server anyway. The route-level `articleId` argument to `client.articles_SubmitFeedback(articleId, request)` is unaffected and continues to carry the id.

**Acceptance criteria:**
- The `new SubmitArticleFeedbackRequest({ ... })` call in `useSubmitArticleFeedbackMutation` no longer includes an `articleId` key.
- `frontend` builds and type-checks cleanly (`npm run build`) with no reference to a nonexistent `articleId` property on `SubmitArticleFeedbackRequest`.
- The mutation's runtime behavior is unchanged: `client.articles_SubmitFeedback(articleId, request)` is still called with the same `articleId` route argument, and feedback is still submitted for the correct article.
- Existing unit tests in `frontend/src/api/hooks/__tests__/useArticles.test.ts` covering `useSubmitArticleFeedbackMutation` continue to pass, updated if they assert on the request body shape.

### FR-4: Update the documentation snippet that models this exact pattern
Update the example in `docs/development/api-client-generation.md` (the `SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment })` snippet, currently around lines 166-183) so the documented request construction no longer includes `articleId`, keeping the doc's canonical example in sync with the actual (post-fix) generated client shape.

**Acceptance criteria:**
- The code snippet in `docs/development/api-client-generation.md` matches the post-fix `SubmitArticleFeedbackRequest` shape (no `articleId` in the constructor call).
- The rest of the surrounding explanation (business-outcome-as-HTTP-status pattern) is left intact — only the object literal passed to the constructor changes.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact is expected or required; this is a serialization-attribute change on a single small DTO with no effect on request volume, payload size (negligible — one GUID field removed from the body schema, but the field is already ignored/overwritten today), or handler logic.

### NFR-2: Security
No new authN/authZ surface is introduced. If anything, this closes a minor contract-integrity gap: today a client could believe it controls `articleId` via the body, when in fact the server always enforces the route-derived id (`[FeatureAuthorize]`/ownership checks in the handler already key off the server-trusted `id`). After this change, the contract makes that trust boundary explicit and unambiguous — the article id can only come from the URL, never from client-supplied body content.

## Data Model
No data model changes. `SubmitArticleFeedbackRequest` remains a class (per this repo's DTO rule — API DTOs must be classes, not records) with the same four logical fields; only the wire visibility of `ArticleId` changes (excluded from JSON in/out, still a normal in-memory C# property). No changes to the `Article` domain entity, `IArticleRepository`, or `SubmitArticleFeedbackResponse`.

## API / Interface Design
- **Endpoint**: `POST /api/Articles/{id:guid}/feedback` — route and route-parameter binding (`Guid id`) are unchanged.
- **Request body schema (after fix)**:
  ```json
  {
    "precisionScore": 1-5,
    "styleScore": 1-5,
    "comment": "string, max 1000 chars, optional"
  }
  ```
  (`articleId` removed from the schema; the article acted on is always taken from the `{id}` route segment.)
- **Response schema**: unchanged (`SubmitArticleFeedbackResponse` — `precisionScore`, `styleScore`, `feedbackComment`, plus the `BaseResponse` success/error envelope). Status codes 200 and 409 are unchanged, as documented in `docs/development/api-client-generation.md`.
- **Generated TypeScript client**: `SubmitArticleFeedbackRequest` in `api-client.ts` loses the `articleId?: string` field; `articles_SubmitFeedback(id: string, request: SubmitArticleFeedbackRequest)` keeps its existing two-argument signature (route id + body).

## Dependencies
- NSwag client generation tooling (`dotnet msbuild ... -t:GenerateFrontendClientManual`), already in use — no new dependency.
- `System.Text.Json.Serialization.JsonIgnoreAttribute` — part of the BCL, already implicitly available (System.Text.Json is the framework's default MVC JSON serializer in this project; no package changes needed).
- Frontend: no new dependencies; only a call-site edit in `frontend/src/api/hooks/useArticles.ts` and its associated test file if assertions reference the removed field.

## Out of Scope
- Any change to the business logic in `SubmitArticleFeedbackHandler` (ownership check, status check, duplicate-feedback check) — all unaffected and unchanged.
- Any change to the controller's general pattern of assigning route values onto request DTOs before dispatch elsewhere in the codebase (other controllers/actions that may have the same pattern are not touched by this spec; if desired, that would be a separate, broader follow-up).
- Introducing a `[BindNever]`/model-binding-level alternative to `[JsonIgnore]`, or restructuring the request DTO to remove `ArticleId` entirely and pass it as a separate handler parameter — the brief's suggested fix (keep the property, just ignore it on the wire) is what's implemented here.
- Any change to Swagger/OpenAPI response documentation beyond the natural effect of removing `articleId` from the request schema.
- Retroactive cleanup of any other DTOs in the codebase with similar route-value-overwrites-body-value patterns.

## Open Questions
None.

## Status: COMPLETE
