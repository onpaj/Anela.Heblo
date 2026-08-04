# Design: Server-side validation for GenerateArticleRequest.Scope and Length

No UI section — this is a backend-only DTO validation change. The frontend (`ArticleGenerationForm.tsx`) already restricts input to the same value sets via `<select>` elements and requires no changes; no new user-facing surface is introduced.

## Component design

No new components. The change is confined to one existing component's contract; two existing components are affected only by the pipeline that already surrounds them (no code changes required in either).

### `GenerateArticleRequest` (modified)

`backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleRequest.cs`

Responsibility: the MediatR request DTO bound directly from the `POST /api/Articles/generate` JSON body. It is the single point where the "enum-validated" contract promised by the spec must be enforced, because it's the last shared choke point before the value is written to `Article` and interpolated into the LLM prompt.

Change: add DataAnnotations validation attributes to `Scope` and `Length`, mirroring the existing pattern already used on `Topic` in the same class (`[Required, MinLength(3), MaxLength(500)]`).

```csharp
[Required]
[AllowedValues("overview", "deep-dive", "how-to", "comparison",
    ErrorMessage = "Scope must be one of: overview, deep-dive, how-to, comparison")]
public string Scope { get; set; } = DomainArticle.DefaultScope;

[Required]
[AllowedValues("brief (500w)", "medium (1000w)", "long (2000w)",
    ErrorMessage = "Length must be one of: brief (500w), medium (1000w), long (2000w)")]
public string Length { get; set; } = DomainArticle.DefaultLength;
```

`Audience`, `Angle`, `LanguageNote`, `UseKnowledgeBase`, `UseWebSearch`, `StyleGuideDriveId`, `StyleGuideItemPath` are untouched — the finding scopes the gap to `Scope`/`Length` only.

### `ArticlesController.Generate` (unchanged, behavior only)

`backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:27-35`

No code change. The action is decorated `[ApiController]` at the class level, which makes ASP.NET Core run automatic model-state validation on `[FromBody] GenerateArticleRequest request` before the action body executes. Today `Topic` already exercises this path; adding attributes to `Scope`/`Length` extends the same automatic behavior to two more properties. When validation fails, the framework short-circuits: the action body never runs, `_mediator.Send(request, ct)` is never called, and no `Article` is persisted.

### `GenerateArticleHandler` / `WriteArticleStep` (unchanged)

`GenerateArticleHandler.cs:37-38` and `WriteArticleStep.BuildUserMessage` (`Pipeline/WriteArticleStep.cs:112-113`) are not modified. They continue to trust `request.Scope`/`request.Length` as before — that trust is now backed by the boundary validation instead of being unbacked. This keeps the fix a pure input-boundary change with no ripple into the generation pipeline.

## Data schemas

### Request schema — `POST /api/Articles/generate`

Before (effective JSON Schema fragment):

```json
{
  "scope": { "type": "string" },
  "length": { "type": "string" }
}
```

After:

```json
{
  "scope": {
    "type": "string",
    "enum": ["overview", "deep-dive", "how-to", "comparison"],
    "default": "overview"
  },
  "length": {
    "type": "string",
    "enum": ["brief (500w)", "medium (1000w)", "long (2000w)"],
    "default": "medium (1000w)"
  }
}
```

Whether Swashbuckle actually renders `[AllowedValues]` as an OpenAPI `enum` (vs. leaving the schema as a plain `string` and only enforcing the constraint at runtime) is a build-time fact to confirm during implementation, not a design decision — either way the runtime behavior described below is identical. If the generated OpenAPI schema doesn't pick up the constraint, the generated TypeScript client types stay `string`, which is a pre-existing/acceptable gap (the frontend already self-constrains via its own hardcoded option lists) and not something this change needs to paper over.

Wire values are unchanged: no new fields, no renamed fields, no type changes (both remain JSON strings, not converted to a numeric/string enum wire type). This preserves compatibility with the existing frontend payload and any other current caller sending one of the four/three known values.

### Validation failure response

When `scope` or `length` (or both) fail `[AllowedValues]`/`[Required]`, `[ApiController]`'s automatic model validation returns the standard ASP.NET Core `ValidationProblemDetails` shape (`application/problem+json`, HTTP 400) — **not** the app's own `BaseResponse`/`ErrorCode` envelope that `BaseApiController.HandleResponse` produces for handler-level failures elsewhere in this API. This is expected and consistent with how the existing `Topic` validation already behaves on this same endpoint; it's a pre-existing asymmetry in the API (model-binding failures vs. handler failures), not something introduced or fixed by this change.

Example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Scope": [
      "Scope must be one of: overview, deep-dive, how-to, comparison"
    ]
  }
}
```

### Persisted entity / domain schema

No change. `Article.Scope` and `Article.Length` (`backend/src/Anela.Heblo.Domain/Features/Article/Article.cs:10,13`) remain plain `string` columns. Enforcement lives entirely at the DTO boundary, one layer above the domain entity — by the time a value reaches `Article`, it has already passed the allow-list check.

### Defaults interaction

`Scope` defaults to `DomainArticle.DefaultScope` ("overview") and `Length` defaults to `DomainArticle.DefaultLength` ("medium (1000w)") via the property initializer. Both defaults are members of their respective allow-lists, so a request that omits `scope`/`length` entirely still validates successfully and behaves exactly as today — this is a runtime fact to be confirmed by a test case (per the plan), not a schema change.

## Out of scope (per plan)

No enum conversion in the domain, no EF/migration changes, no frontend changes, no changes to `GenerateArticleHandler` or `WriteArticleStep`. These boundaries carry over unchanged from the plan and are restated here only because they bound what the component/data design above covers.
