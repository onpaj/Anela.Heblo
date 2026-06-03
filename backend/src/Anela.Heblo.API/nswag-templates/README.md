# NSwag Template Overrides

This directory contains the minimum set of Liquid template overrides applied during NSwag TypeScript client generation (`dotnet nswag run nswag.frontend.json` from `backend/src/Anela.Heblo.API/`).

## Why this exists

The default NSwag Fetch template emits `processX(response)` method bodies that handle exactly one success status (`200`) and throw on every other status, including 4xx responses that the backend declares via `[ProducesResponseType(typeof(SomeBody), StatusCodes.Status409Conflict)]`. Some of our endpoints (e.g. `POST /api/articles/{id}/feedback`) intentionally return the same DTO on 200 and 409 — the 409 case is "already submitted", a business outcome, not an error. We want the generated client to return the parsed body on 409, not throw.

## What is overridden

Exactly one Liquid template: the one that emits the body of `processX(response)`. The override is functionally a no-op for any operation whose 4xx response body schema does NOT equal its 2xx response body schema. For all other operations, it generates byte-for-byte identical output to the default NSwag template.

## The predicate

For each operation, the override emits a typed non-throwing branch for a 4xx status if and only if ALL THREE:

1. The status code is `409` (Conflict), AND
2. The operation declares a `[ProducesResponseType]` for that 409 status, AND
3. The body schema for the 409 response is the same schema as the 2xx response (i.e. the same DTO type).

If the predicate does not match, the default `throwException(...)` branch is emitted unchanged.

The status-code restriction to 409 is intentional and deliberate. A broader "schema equality" predicate (checking that the 4xx body type equals the 2xx body type for ANY 4xx status) was evaluated during implementation and rejected: the codebase already contains controllers (e.g. `FeatureFlagsController`) that declare 404 responses with the same DTO shape as their 200 response for unrelated reasons. Those 404s must continue to throw. Restricting the predicate to HTTP 409 Conflict avoids false matches and aligns with the HTTP semantic of 409 as "conflict with the current state of the resource" — a business outcome, not an error.

To add support for additional status codes in the future (e.g. 412 Precondition Failed for optimistic-concurrency endpoints), extend the condition:
```liquid
{%         if response.IsSuccess or ((response.StatusCode == "409" or response.StatusCode == "412") and response.Type == operation.ResultType) -%}
```

## Current wiring status

**⚠️ This template is NOT currently wired** in `nswag.frontend.json` (`"templateDirectory": null`). The template file exists as verified-correct documentation for future activation. The `useSubmitArticleFeedbackMutation` hook currently handles the 409 case via a hook-level `try/catch` on `SwaggerException`.

To activate: set `"templateDirectory": "nswag-templates"` in `nswag.frontend.json`, run `dotnet msbuild ... -t:GenerateFrontendClientManual`, verify the diff (see Verification below), then update the hook to remove the `try/catch` and discriminate on the typed `response` directly.

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
