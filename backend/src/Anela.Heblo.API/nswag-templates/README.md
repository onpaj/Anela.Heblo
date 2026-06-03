# NSwag Template Overrides

This directory contains the minimum set of Liquid template overrides applied during NSwag TypeScript client generation (`dotnet nswag run nswag.frontend.json` from `backend/src/Anela.Heblo.API/`).

## Why this exists

The default NSwag Fetch template emits `processX(response)` method bodies that handle exactly one success status (`200`) and throw on every other status, including 4xx responses that the backend declares via `[ProducesResponseType(typeof(SomeBody), StatusCodes.Status409Conflict)]`. Some of our endpoints (e.g. `POST /api/articles/{id}/feedback`) intentionally return the same DTO on 200 and 409 — the 409 case is "already submitted", a business outcome, not an error. We want the generated client to return the parsed body on 409, not throw.

## What is overridden

Exactly one Liquid template: the one that emits the body of `processX(response)`. The override is functionally a no-op for any operation whose 4xx response body schema does NOT equal its 2xx response body schema. For all other operations, it generates byte-for-byte identical output to the default NSwag template.

## The predicate

For each operation, the override emits a typed non-throwing branch for a 4xx status if and only if BOTH:

1. The operation declares a `[ProducesResponseType]` for that 4xx status, AND
2. The body schema for that 4xx response is the same schema as the 2xx response (i.e. the same DTO type).

If the predicate does not match, the default `throwException(...)` branch is emitted unchanged.

The "schema equality" predicate is intentional. It means typed-non-throwing 4xx branches are **opt-in via backend choice**: the backend signals "this 4xx is a business outcome, not an error" by returning the same DTO on both paths. Future 404 / 422 / 403 responses that return a different error envelope (e.g. `ProblemDetails`) will not be affected — they continue to throw, as today.

## How callers discriminate

Both branches return the same TypeScript type — there is no type-level discrimination. Callers discriminate at runtime via the `BaseResponse` envelope already used project-wide:

```ts
const response = await client.articles_SubmitFeedback(id, request);
if (response.success === false && response.errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted) {
  // 409 path
}
// 200 path
```

See `frontend/src/api/hooks/useArticles.ts::useSubmitArticleFeedbackMutation` for the canonical example.

## Forked from NSwag version

This override was forked from **NSwag 14.1.0** (`NSwag.MSBuild` `Version="14.1.0"` in `Anela.Heblo.API.csproj`). The unmodified template source for that version lives at:

https://github.com/RicoSuter/NSwag/tree/v14.1.0/src/NSwag.CodeGeneration.TypeScript/Templates

When upgrading NSwag, re-diff the upstream template against this override and re-verify the byte-equality criterion below.

## Verification

After regeneration the diff in `frontend/src/api/generated/api-client.ts` MUST be limited to:

1. The return type and method body of `articles_SubmitFeedback` and `processArticles_SubmitFeedback`.
2. Any new imports required by (1).

If any OTHER `process*` method changes, the predicate is too broad. Revert and narrow. Verify with:

```bash
git diff frontend/src/api/generated/api-client.ts | grep -E '^\+\+\+|^---|^@@' | head -30
```

The change MUST also be idempotent — running `dotnet nswag run nswag.frontend.json` twice in a row produces no diff on the second run.

## Known consequence — sibling endpoints

The pattern is opt-in via the backend's `[ProducesResponseType(409)]` annotation. Sibling endpoints with the same business shape — `LeafletController.SubmitFeedback` (`LeafletFeedbackAlreadySubmitted = 2503`), `KnowledgeBaseController.SubmitFeedback` — currently do NOT declare a 409 in OpenAPI. The moment someone adds the annotation, the consumer hook will receive a typed `Promise<...>` on 409 instead of a throw, and the hook MUST be updated at the same time. The breaking change is visible because TypeScript's `try { ... } catch` block on `SwaggerException` becomes dead code (the compiler will not warn — verify the hook in the same PR).
