# NSwag-Surfaced HTTP 409 Typed Branch on `articles_SubmitFeedback` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the raw-fetch fallback in `useSubmitArticleFeedbackMutation` with a direct typed call to the NSwag-generated client by surfacing HTTP 409 as a typed, non-throwing branch in both the OpenAPI document and the generated TypeScript client.

**Architecture:** Three coordinated changes — (1) `[ProducesResponseType]` annotations on the `SubmitFeedback` controller action so the OpenAPI document advertises 200 + 409 with the same body type, (2) a minimal NSwag Liquid template override that emits a non-throwing typed branch for any 4xx response whose body schema equals the 2xx body schema, (3) a hook rewrite that calls the generated method and discriminates on the existing `BaseResponse.success` + `errorCode` envelope. The `getApiBaseUrl()` / `getAuthenticatedFetch()` helpers stay but their JSDoc is updated. A separate change in `getAuthenticatedApiClient()`'s `authenticatedHttp.fetch` wrapper suppresses the global "Upozornění" toast on 409 responses whose body is a structured `BaseResponse` — this preserves the toast-free behavior of the prior raw-fetch path (arch-review Specification Amendment #1, option A).

**Tech Stack:** .NET 8 (`ArticlesController`, `[ProducesResponseType]`), NSwag.MSBuild 14.1.0 (Liquid templates, OpenAPI → TypeScript code generation), React 18 + TanStack Query v5 (`useMutation`), Jest + Testing Library (hook tests).

---

## Pre-Flight: Decisions Already Made

These are decisions resolved up-front so implementation is not blocked. They reflect the arch-review's recommended choices.

1. **Toast-regression mitigation:** Implement arch-review option A — extend `authenticatedHttp.fetch` to skip the global error toast when the response status is 409 AND the parsed body is a structured `BaseResponse` (`success: false` + `errorCode` present). Rationale: keeps the toast-free behavior of the prior `getAuthenticatedFetch()` path for every current and future 409-typed-branch endpoint, without per-call-site opt-out. This is global and benefits future endpoints.
2. **Discriminator placement:** Read `response.success === false && response.errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted` directly in the hook. No new wrapper / discriminated union type. Matches the existing `BaseResponse` convention used by `extractErrorMessage` in `client.ts:228-236`.
3. **Template scope predicate:** "4xx with body schema equal to 2xx body schema" — NOT "any 4xx with a body". This protects future 404 / 422 / 403 annotations from being silently converted to non-throwing typed branches.
4. **Escape hatch:** If, during Task 3, the Liquid template fork cannot be made surgical enough to satisfy the byte-equality acceptance criterion in Task 5, the implementer MUST STOP and escalate. The fallback design is hook-level `try { ... } catch (e: SwaggerException) { if (e.status === 409) return ...; throw e; }` — preserves FR-1 (409 in OpenAPI doc), abandons FR-2 (template), keeps the hook typed. Do not silently broaden the template override past the schema-equality predicate.

---

## File Structure

**Files created:**

- `backend/src/Anela.Heblo.API/nswag-templates/README.md` — explains the template override, its predicate, NSwag version forked from, and the verification command.
- `backend/src/Anela.Heblo.API/nswag-templates/Client.Class.ProcessResponse.liquid` (exact filename confirmed in Task 3 by diffing NSwag 14.1 defaults) — overridden Liquid template that emits a typed non-throwing branch for any operation declaring a 4xx response whose body schema equals the 2xx body schema.
- `docs/superpowers/plans/2026-06-03-nswag-409-typed-branch-articles-feedback.md` — this plan.

**Files modified:**

- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — add 2 `[ProducesResponseType]` attributes above the `SubmitFeedback` action at line 72.
- `backend/src/Anela.Heblo.API/nswag.frontend.json` — set `templateDirectory` (currently `null` at line 76) to `"nswag-templates"`.
- `frontend/src/api/generated/api-client.ts` — regenerated; diff is limited to `articles_SubmitFeedback` return type and `processArticles_SubmitFeedback` method body.
- `frontend/src/api/client.ts` — extend `authenticatedHttp.fetch` in `getAuthenticatedApiClient()` (lines 281-356) to suppress the toast on 409-with-structured-body. Update the JSDoc on `getAuthenticatedFetch()` (lines 393-406) to drop the stale `useSubmitArticleFeedbackMutation` reference.
- `frontend/src/api/hooks/useArticles.ts` — rewrite `useSubmitArticleFeedbackMutation` (lines 218-255) to call `apiClient.articles_SubmitFeedback(...)` directly. Drop the `getApiBaseUrl` / `getAuthenticatedFetch` imports at lines 4-5.
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — rewrite the `describe('useSubmitArticleFeedbackMutation')` block (lines 279-374). Leave the two `useArticleFeedbackListQuery` blocks (lines 49-277) untouched.
- `docs/development/api-client-generation.md` — rewrite lines 217-233 to reframe the helpers as a forward-looking escape hatch (per arch-review Specification Amendment #2).

**No file deletions.** `getApiBaseUrl()` and `getAuthenticatedFetch()` remain exported from `client.ts`.

---

## Task 1: Add `[ProducesResponseType]` attributes to `SubmitFeedback`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:72`

This task is the "spec change" — it makes the OpenAPI document advertise both 200 and 409 with the `SubmitArticleFeedbackResponse` body. It has no runtime behavior impact: `HandleResponse(...)` already sets the correct status code from the `[HttpStatusCode(Conflict)]` decoration on `ErrorCodes.ArticleFeedbackAlreadySubmitted`.

- [ ] **Step 1: Add the two attributes above `[HttpPost("{id:guid}/feedback")]`**

In `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`, change lines 72-81 from:

```csharp
    [HttpPost("{id:guid}/feedback")]
    public async Task<ActionResult<SubmitArticleFeedbackResponse>> SubmitFeedback(
        Guid id,
        [FromBody] SubmitArticleFeedbackRequest request,
        CancellationToken ct = default)
    {
        request.ArticleId = id;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

to:

```csharp
    [ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status409Conflict)]
    [HttpPost("{id:guid}/feedback")]
    public async Task<ActionResult<SubmitArticleFeedbackResponse>> SubmitFeedback(
        Guid id,
        [FromBody] SubmitArticleFeedbackRequest request,
        CancellationToken ct = default)
    {
        request.ArticleId = id;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

No `using` directive needed — `StatusCodes` lives in `Microsoft.AspNetCore.Http` which is brought in via `Microsoft.AspNetCore.Mvc` (already imported at line 12), and `ProducesResponseTypeAttribute` lives in `Microsoft.AspNetCore.Mvc` itself. If the build complains, add `using Microsoft.AspNetCore.Http;` to the top of the file.

- [ ] **Step 2: Verify the file builds**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: build succeeds with no new warnings on `ArticlesController.cs`.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs
git commit -m "feat: declare 200 and 409 responses on Articles.SubmitFeedback"
```

---

## Task 2: Create the NSwag template override directory + README

**Files:**
- Create: `backend/src/Anela.Heblo.API/nswag-templates/README.md`

The README is written BEFORE the template override itself. It is the contract that future maintainers and PR reviewers verify the implementation against. Writing it first forces clarity on the predicate, the verification command, and the escape hatch.

- [ ] **Step 1: Create the directory**

Run: `mkdir -p backend/src/Anela.Heblo.API/nswag-templates`

- [ ] **Step 2: Write the README**

Create `backend/src/Anela.Heblo.API/nswag-templates/README.md` with this exact content:

````markdown
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
````

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/nswag-templates/README.md
git commit -m "docs: add README for the NSwag template-override directory"
```

---

## Task 3: Identify and override the Liquid template

**Files:**
- Create: `backend/src/Anela.Heblo.API/nswag-templates/<TemplateFile>.liquid` (exact filename determined in this task).

This is the riskiest task in the plan. The escape hatch in Pre-Flight #4 applies if Step 4 cannot satisfy the byte-equality acceptance criterion in Task 5.

- [ ] **Step 1: Fetch the NSwag 14.1.0 default TypeScript templates**

```bash
mkdir -p /tmp/nswag-14.1.0-templates
curl -L https://github.com/RicoSuter/NSwag/archive/refs/tags/v14.1.0.tar.gz \
  | tar -xz -C /tmp --strip-components=1 \
    NSwag-14.1.0/src/NSwag.CodeGeneration.TypeScript/Templates
mv /tmp/src /tmp/nswag-14.1.0-templates 2>/dev/null || \
  mv /tmp/NSwag-14.1.0/src/NSwag.CodeGeneration.TypeScript/Templates/* /tmp/nswag-14.1.0-templates/
ls /tmp/nswag-14.1.0-templates
```

Expected: a tree of `.liquid` files. The Fetch-template-relevant subdirectory will contain `Fetch/Client.Class.Process.liquid` or `Fetch.Class.Process.liquid` (exact name depends on NSwag's release layout — list to confirm).

If the upstream curl fails (Github outages, etc.), alternative is to extract the NSwag.CodeGeneration.TypeScript 14.1.0 NuGet package and read templates from its `Resources` folder:

```bash
nuget install NSwag.CodeGeneration.TypeScript -Version 14.1.0 -OutputDirectory /tmp/nswag-pkg -DependencyVersion Ignore
find /tmp/nswag-pkg -name '*.liquid' | head
```

- [ ] **Step 2: Identify the template that emits `processX(response)` method bodies**

```bash
grep -rl "processX\|processResponse\|throwException\|statusCode" /tmp/nswag-14.1.0-templates | head
```

Look for a template containing the literal string `throwException` and a `{% for response in operation.Responses %}` (or similar) loop. The likely candidate is `Fetch/Client.Class.ProcessResponse.liquid` or `Class.Process.liquid`. Confirm by reading the file.

Open the candidate file and verify it produces the per-status branches you saw in `frontend/src/api/generated/api-client.ts:586-596`:

```
if (status === 200) { ... fromJS ... }
else if (status !== 200 && status !== 204) { ... throwException ... }
```

- [ ] **Step 3: Copy the file (unmodified) into the override directory**

```bash
# Replace <TemplateFile>.liquid below with the exact filename you identified in Step 2.
cp /tmp/nswag-14.1.0-templates/Fetch/<TemplateFile>.liquid \
   backend/src/Anela.Heblo.API/nswag-templates/<TemplateFile>.liquid
```

Note: NSwag resolves `templateDirectory` by template *filename*. The override file MUST keep the exact filename of the upstream template — that is the matching key.

- [ ] **Step 4: Modify the override to emit a typed non-throwing branch when the predicate matches**

The default template has a loop similar to:

```liquid
{% for response in Responses %}
    {% if response.IsSuccess %}
        if (status === {{ response.StatusCode }}) {
            {% if response.HasResultType %}
                return response.text().then(...);
            {% endif %}
        }
    {% endif %}
{% endfor %}
... else { throwException(...) }
```

Add a second, parallel branch INSIDE the loop that handles the predicate "4xx with same body schema as the success response":

```liquid
{% for response in Responses %}
    {% if response.IsSuccess %}
        if (status === {{ response.StatusCode }}) {
            return response.text().then((_responseText) => {
            let result{{ response.StatusCode }}: any = null;
            let resultData{{ response.StatusCode }} = _responseText === "" ? null : JSON.parse(_responseText, this.jsonParseReviver);
            result{{ response.StatusCode }} = {{ response.Type }}.fromJS(resultData{{ response.StatusCode }});
            return result{{ response.StatusCode }};
            });
        } else
    {% endif %}
{% endfor %}
{% comment %} NEW: typed non-throwing branch for 4xx responses whose body type equals the 2xx body type {% endcomment %}
{% for response in Responses %}
    {% if response.IsSuccess == false and response.StatusCode >= '400' and response.StatusCode < '500' and response.HasResultType and response.Type == operation.ResultType %}
        if (status === {{ response.StatusCode }}) {
            return response.text().then((_responseText) => {
            let result{{ response.StatusCode }}: any = null;
            let resultData{{ response.StatusCode }} = _responseText === "" ? null : JSON.parse(_responseText, this.jsonParseReviver);
            result{{ response.StatusCode }} = {{ response.Type }}.fromJS(resultData{{ response.StatusCode }});
            return result{{ response.StatusCode }};
            });
        } else
    {% endif %}
{% endfor %}
        if (status !== 200 && status !== 204) {
            return response.text().then((_responseText) => {
            return throwException("An unexpected server error occurred.", status, _responseText, _headers);
            });
        }
```

**Note on property names.** The exact NSwag Liquid model property names (`Responses`, `IsSuccess`, `StatusCode`, `HasResultType`, `Type`, `operation.ResultType`) vary slightly between NSwag's TypeScript and CSharp templates and across versions. The names above are taken from NSwag 14.1.0's TypeScript Fetch template. If the upstream template uses different names, MATCH the upstream names — do not invent new ones.

**Note on schema-equality check.** `response.Type == operation.ResultType` is a string comparison on the TypeScript type name (e.g. `"SubmitArticleFeedbackResponse"`). If two responses reference the same C# DTO type they will share the same TS type name — that is the schema-equality signal we need. If the upstream Liquid model exposes a richer object than a string, use whatever equivalent equality test is idiomatic in NSwag's Liquid dialect (Fluid / DotLiquid).

- [ ] **Step 5: Verify the override changes only the intended branches**

Diff your override against the upstream copy:

```bash
diff /tmp/nswag-14.1.0-templates/Fetch/<TemplateFile>.liquid \
     backend/src/Anela.Heblo.API/nswag-templates/<TemplateFile>.liquid
```

Expected: the diff is limited to the inserted `{% comment %} NEW: ... {% endcomment %}` block and the wrapping `{% for %}` loop. No other lines should be touched. If you find yourself rewriting unrelated control flow, STOP — invoke the escape hatch (Pre-Flight #4).

- [ ] **Step 6: Commit the unrendered template override**

The byte-equality verification on the generated TypeScript happens in Task 5 (after Tasks 1 + 4 are also applied). This task ends with the template file checked in but not yet referenced from `nswag.frontend.json`.

```bash
git add backend/src/Anela.Heblo.API/nswag-templates/
git commit -m "feat: override NSwag Fetch process template for typed 4xx-equals-2xx branches"
```

---

## Task 4: Wire `templateDirectory` in the NSwag config

**Files:**
- Modify: `backend/src/Anela.Heblo.API/nswag.frontend.json:76`

- [ ] **Step 1: Change `"templateDirectory": null` to `"templateDirectory": "nswag-templates"`**

In `backend/src/Anela.Heblo.API/nswag.frontend.json`, change line 76 from:

```json
        "templateDirectory": null,
```

to:

```json
        "templateDirectory": "nswag-templates",
```

The path is relative to the working directory NSwag runs in. The `<Exec Command="dotnet nswag run nswag.frontend.json" WorkingDirectory="$(MSBuildThisFileDirectory)" />` in `Anela.Heblo.API.csproj:96` sets that working directory to `backend/src/Anela.Heblo.API/`, so the relative path resolves to `backend/src/Anela.Heblo.API/nswag-templates/`.

- [ ] **Step 2: Do NOT commit yet**

Combined with Task 1's `[ProducesResponseType]` change, the next regeneration will rewrite `api-client.ts`. Task 5 is the verification gate; if it fails, the rollback may include reverting this change.

---

## Task 5: Regenerate the TypeScript client and verify the diff

**Files:**
- Modify (auto-generated): `frontend/src/api/generated/api-client.ts`

This task is purely verification. Either it passes — and the change is sound — or it fails — and the escape hatch in Pre-Flight #4 applies.

- [ ] **Step 1: Snapshot the pre-regen client**

```bash
cp frontend/src/api/generated/api-client.ts /tmp/api-client.before.ts
```

- [ ] **Step 2: Regenerate the client**

```bash
dotnet msbuild backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -t:GenerateFrontendClientManual
```

Expected: NSwag runs, prints `Frontend API client generation completed.`, and overwrites `frontend/src/api/generated/api-client.ts`.

If NSwag prints errors about the template file (e.g. "unknown property `operation.ResultType`"), the Liquid model's property names differ from the assumptions in Task 3 Step 4. Fix the template against the actual model and re-run. Use NSwag's `--verbose` output (the config already has `"verbose": true` for the OpenAPI step) to see which template is being applied.

- [ ] **Step 3: Verify the diff is scoped**

```bash
diff /tmp/api-client.before.ts frontend/src/api/generated/api-client.ts
```

Expected diff — and ONLY this diff:

```
560c560
<     articles_SubmitFeedback(id: string, request: SubmitArticleFeedbackRequest): Promise<SubmitArticleFeedbackResponse> {
---
>     articles_SubmitFeedback(id: string, request: SubmitArticleFeedbackRequest): Promise<SubmitArticleFeedbackResponse> {
```

(Return type may stay identical — both branches return `SubmitArticleFeedbackResponse`. The diff might be limited to `processArticles_SubmitFeedback` body only.)

In `processArticles_SubmitFeedback`, expect a new `else if (status === 409)` block that parses the body via `SubmitArticleFeedbackResponse.fromJS(...)` and returns it instead of throwing.

If the diff includes any OTHER `process*` method (e.g. `processArticles_List`, `processKnowledgeBase_*`, `processLeaflet_*`), the predicate in Task 3 Step 4 is too broad. STOP. Narrow the predicate OR escape via Pre-Flight #4.

- [ ] **Step 4: Verify idempotency**

```bash
dotnet msbuild backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -t:GenerateFrontendClientManual
git diff frontend/src/api/generated/api-client.ts
```

Expected: empty diff on the second run.

If the second run produces a non-empty diff (line endings, whitespace), the template's newline handling is unstable. Pick `LF` consistently and re-verify.

- [ ] **Step 5: Verify the TypeScript build**

```bash
cd frontend && npm run build
```

Expected: build succeeds. The generated 409 branch reuses an existing class (`SubmitArticleFeedbackResponse`); no new TS imports should be required, but if the template added any, verify they resolve.

- [ ] **Step 6: Commit the regen + NSwag config change**

```bash
git add backend/src/Anela.Heblo.API/nswag.frontend.json frontend/src/api/generated/api-client.ts
git commit -m "feat: regenerate client with typed 409 branch on articles_SubmitFeedback"
```

---

## Task 6: Suppress global error toast on 409-with-structured-body

**Files:**
- Modify: `frontend/src/api/client.ts` — extend `authenticatedHttp.fetch` in `getAuthenticatedApiClient()` (lines 281-356).

**TDD note.** `extractErrorMessage` (lines 220-272) and the toast logic in `authenticatedHttp.fetch` are not currently unit-tested. Adding tests here is in scope because we are adding new branching logic; a regression test for "no toast on 409 + structured body" is the only practical way to verify the toast-suppression behavior without a Playwright run.

- [ ] **Step 1: Write a failing test for the toast-suppression behavior**

Create the test file if it does not exist: `frontend/src/api/__tests__/client.test.ts`. If it already exists, append to the appropriate describe block.

```typescript
import {
  getAuthenticatedApiClient,
  setGlobalToastHandler,
  setGlobalTokenProvider,
} from '../client';

// Mock runtimeConfig so getConfig() returns a known apiUrl
jest.mock('../../config/runtimeConfig', () => ({
  getConfig: () => ({ apiUrl: 'https://api.example.test' }),
  shouldUseMockAuth: () => false,
}));

jest.mock('../../auth/e2eAuth', () => ({
  isE2ETestMode: () => false,
  getE2EAccessToken: () => null,
}));

jest.mock('../../auth/mockAuth', () => ({
  mockAuthService: { getAccessToken: () => 'mock-token' },
}));

describe('getAuthenticatedApiClient toast suppression', () => {
  let toastHandler: jest.Mock;
  let originalFetch: typeof global.fetch;

  beforeEach(() => {
    toastHandler = jest.fn();
    setGlobalToastHandler(toastHandler);
    setGlobalTokenProvider(async () => ({ token: 'tok', expiresOn: null }));
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.resetAllMocks();
  });

  it('does NOT fire a toast on 409 when the body is a structured BaseResponse with errorCode', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          success: false,
          errorCode: 'ArticleFeedbackAlreadySubmitted',
          params: { id: 'art-1' },
        }),
        { status: 409, headers: { 'Content-Type': 'application/json' } },
      ),
    ) as jest.Mock;

    const client = getAuthenticatedApiClient();
    // Call the wrapped fetch directly through the http member; the typed method also works
    // but we want to verify the wrapper in isolation, so we call client.http.fetch.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (client as any).http.fetch('https://api.example.test/api/test', { method: 'POST' });

    expect(toastHandler).not.toHaveBeenCalled();
  });

  it('DOES fire a toast on 500 with a structured BaseResponse body', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          success: false,
          errorCode: 'InternalServerError',
        }),
        { status: 500, headers: { 'Content-Type': 'application/json' } },
      ),
    ) as jest.Mock;

    const client = getAuthenticatedApiClient();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (client as any).http.fetch('https://api.example.test/api/test', { method: 'POST' });

    expect(toastHandler).toHaveBeenCalledTimes(1);
  });

  it('still fires a toast on a 409 with an UNSTRUCTURED body (defensive fallback)', async () => {
    global.fetch = jest.fn().mockResolvedValue(
      new Response('Conflict', { status: 409, headers: { 'Content-Type': 'text/plain' } }),
    ) as jest.Mock;

    const client = getAuthenticatedApiClient();
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (client as any).http.fetch('https://api.example.test/api/test', { method: 'POST' });

    expect(toastHandler).toHaveBeenCalledTimes(1);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- client.test --watchAll=false`
Expected: first test FAILS ("expected mock not to be called" — the toast IS fired today on 409). Second and third pass (current behavior).

- [ ] **Step 3: Modify `authenticatedHttp.fetch` in `client.ts` to suppress the toast**

In `frontend/src/api/client.ts`, the relevant block is at lines 310-353 — the `if (shouldHandleHttpErrors || shouldCheckForBusinessErrors)` block.

Replace `extractErrorMessage` calls to also surface a `suppressToast` flag, OR — simpler and the recommended path — wrap the toast call itself in a 409-aware check. The minimal change:

```typescript
        // Global error handling with toast notifications
        // Handle both HTTP errors (!response.ok) and business logic errors (success: false)
        const shouldCheckForBusinessErrors = response.ok && showErrorToasts && globalToastHandler;
        const shouldHandleHttpErrors = !response.ok && showErrorToasts && globalToastHandler;

        if (shouldHandleHttpErrors || shouldCheckForBusinessErrors) {
          // Clone response to preserve it for SwaggerException
          const responseClone = response.clone();

          try {
            const errorInfo = await extractErrorMessage(responseClone);

            // Suppress toast on 409 when the backend returned a structured BaseResponse
            // (success: false + errorCode). These 409s are typed business outcomes (e.g.
            // "feedback already submitted") and the caller's hook handles them as success.
            // Unstructured 409s still surface as toasts.
            const suppressOn409 = response.status === 409 && errorInfo.isStructuredError;

            // Show toast for all errors - centralized handling
            console.log(
              `🔍 Error debug - isStructuredError: ${errorInfo.isStructuredError}, message: "${errorInfo.message}"`,
            );

            if (suppressOn409) {
              console.log(
                `🔇 Toast suppressed for typed 409 business outcome: ${errorInfo.message}`,
              );
            } else if (errorInfo.isStructuredError && globalToastHandler) {
              // Structured API error - show ErrorMessage
              console.error(
                `🚨 Structured API Error [${response.status}] ${url}:`,
                errorInfo.message,
              );
              if (!isTerminalRoute()) globalToastHandler("Upozornění", errorInfo.message);
            } else if (shouldHandleHttpErrors && globalToastHandler) {
              // Only show unstructured errors for HTTP errors, not for business logic warnings
              const title = `Chyba API (${response.status})`;
              console.error(
                `🚨 Unstructured API Error [${response.status}] ${url}:`,
                errorInfo.message,
              );
              if (!isTerminalRoute()) globalToastHandler(title, errorInfo.message);
            }
          } catch (toastError) {
            console.error("🍞 Failed to show error toast:", toastError);
            // Fallback toast only for HTTP errors
            if (shouldHandleHttpErrors && globalToastHandler) {
              if (!isTerminalRoute()) globalToastHandler(
                `Chyba API (${response.status})`,
                "Neočekávaná chyba na serveru",
              );
            }
          }
        }
```

The key addition is the `suppressOn409` flag and the `if (suppressOn409) { ... } else if (...)` ordering. Note: the third test case ("unstructured 409 still fires") relies on `errorInfo.isStructuredError` being `false` for non-JSON or non-`BaseResponse` 409 bodies — which is what `extractErrorMessage` already returns.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npm test -- client.test --watchAll=false`
Expected: all three tests PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/api/__tests__/client.test.ts
git commit -m "feat: suppress global toast on 409 responses with structured BaseResponse body"
```

---

## Task 7: Refactor `useSubmitArticleFeedbackMutation` (TDD)

**Files:**
- Modify: `frontend/src/api/hooks/useArticles.ts:218-255`
- Modify: `frontend/src/api/hooks/__tests__/useArticles.test.ts:279-374`

We do the test rewrite and the implementation in a single task because the existing tests would fail against the new implementation — so they must be rewritten in lockstep. Within this task we use TDD strictly: rewrite tests → fail → rewrite hook → pass.

- [ ] **Step 1: Rewrite the `describe('useSubmitArticleFeedbackMutation')` block**

In `frontend/src/api/hooks/__tests__/useArticles.test.ts`, replace lines 279-374 (the entire third `describe` block) with:

```typescript
describe('useSubmitArticleFeedbackMutation', () => {
  let mockArticlesSubmitFeedback: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    mockArticlesSubmitFeedback = jest.fn();
    mockGetAuthenticatedApiClient.mockReturnValue({
      articles_SubmitFeedback: mockArticlesSubmitFeedback,
    } as any);
  });

  const createMutationWrapper = ({ children }: { children: React.ReactNode }) => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    return React.createElement(
      QueryClientProvider,
      { client: queryClient },
      children,
    );
  };

  const payload: SubmitArticleFeedbackPayload = {
    precisionScore: 4,
    styleScore: 5,
    comment: 'great',
  };

  it('resolves with parsed body on 2xx (typed generated response with success: true)', async () => {
    mockArticlesSubmitFeedback.mockResolvedValue({
      success: true,
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'great',
    });

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation('article-1'),
      { wrapper: createMutationWrapper },
    );

    result.current.mutate(payload);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(mockGetAuthenticatedApiClient).toHaveBeenCalled();
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledTimes(1);
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(
      'article-1',
      expect.objectContaining({
        articleId: 'article-1',
        precisionScore: 4,
        styleScore: 5,
        comment: 'great',
      }),
    );
    expect(result.current.data).toEqual({
      precisionScore: 4,
      styleScore: 5,
      feedbackComment: 'great',
    });
  });

  it('resolves with alreadySubmitted on typed 409 branch (success: false + ArticleFeedbackAlreadySubmitted)', async () => {
    mockArticlesSubmitFeedback.mockResolvedValue({
      success: false,
      errorCode: 'ArticleFeedbackAlreadySubmitted',
      params: { id: 'article-1' },
    });

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation('article-1'),
      { wrapper: createMutationWrapper },
    );

    result.current.mutate(payload);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual({ alreadySubmitted: true });
    expect(result.current.isError).toBe(false);
  });

  it('rejects when generated client throws (e.g. 500 surfaces as SwaggerException)', async () => {
    const swaggerLikeError = Object.assign(new Error('An unexpected server error occurred. 500'), {
      status: 500,
    });
    mockArticlesSubmitFeedback.mockRejectedValue(swaggerLikeError);

    const { result } = renderHook(
      () => useSubmitArticleFeedbackMutation('article-1'),
      { wrapper: createMutationWrapper },
    );

    result.current.mutate(payload);

    await waitFor(() => expect(result.current.isError).toBe(true));

    expect(result.current.isSuccess).toBe(false);
    expect(result.current.error).toBeInstanceOf(Error);
    expect((result.current.error as Error).message).toContain('500');
  });
});
```

The `mockGetAuthenticatedFetch` and `mockGetApiBaseUrl` declarations at the top of the file (lines 25-33) STAY — the `jest.mock` factory at lines 11-18 keeps exporting them, and removing the top-level `const` references would require touching the factory too. Leaving them in is the minimal-blast-radius change.

- [ ] **Step 2: Run the tests; they should fail**

Run: `cd frontend && npm test -- useArticles.test --watchAll=false`
Expected: all three rewritten tests FAIL — the current `useSubmitArticleFeedbackMutation` implementation still calls `getAuthenticatedFetch()`, not `getAuthenticatedApiClient().articles_SubmitFeedback`.

- [ ] **Step 3: Rewrite the hook implementation**

In `frontend/src/api/hooks/useArticles.ts`:

(a) Update the import block at lines 1-12 from:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getAuthenticatedApiClient,
  getApiBaseUrl,
  getAuthenticatedFetch,
  QUERY_KEYS,
} from '../client';
import {
  ArticleStatus,
  GenerateArticleRequest,
  ISubmitArticleFeedbackRequest,
} from '../generated/api-client';
```

to:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getAuthenticatedApiClient,
  QUERY_KEYS,
} from '../client';
import {
  ArticleStatus,
  ErrorCodes,
  GenerateArticleRequest,
  SubmitArticleFeedbackRequest,
} from '../generated/api-client';
```

(`SubmitArticleFeedbackRequest` is the class with a constructor — we instantiate it because the generated method signature in the regenerated client likely accepts the class type rather than `ISubmitArticleFeedbackRequest`. Confirm by reading the regenerated `articles_SubmitFeedback` parameter list after Task 5.)

(b) Replace lines 218-255 (the entire `useSubmitArticleFeedbackMutation` function) with:

```typescript
export const useSubmitArticleFeedbackMutation = (articleId: string) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      const client = getAuthenticatedApiClient();
      const request = new SubmitArticleFeedbackRequest({
        articleId,
        precisionScore: payload.precisionScore,
        styleScore: payload.styleScore,
        comment: payload.comment,
      });

      const response = await client.articles_SubmitFeedback(articleId, request);

      if (
        response.success === false &&
        response.errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted
      ) {
        return { alreadySubmitted: true };
      }

      return {
        precisionScore: response.precisionScore ?? null,
        styleScore: response.styleScore ?? null,
        feedbackComment: response.feedbackComment ?? null,
      };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: articleKeys.detail(articleId) });
    },
  });
};
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npm test -- useArticles.test --watchAll=false`
Expected: all three rewritten tests PASS. The two `useArticleFeedbackListQuery` describe blocks also pass (they were not touched).

- [ ] **Step 5: Verify lint and type-check**

Run: `cd frontend && npm run lint && npm run build`
Expected: both succeed. No new errors. If the linter flags an unused import (`ISubmitArticleFeedbackRequest`), confirm it was actually removed; if eslint reports it because the test file's `jest.mock` factory still references it indirectly, no action is needed (mock factory uses string keys, not the imported symbol).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "feat: call generated client for article feedback; discriminate 409 via BaseResponse"
```

---

## Task 8: Update JSDoc on `getAuthenticatedFetch`

**Files:**
- Modify: `frontend/src/api/client.ts:393-406`

- [ ] **Step 1: Replace the stale JSDoc block**

In `frontend/src/api/client.ts`, change lines 393-406 from:

```typescript
/**
 * Get an authenticated fetch function with the same headers as the API client.
 * Returns a fetch-like function that automatically attaches auth headers.
 *
 * Key behaviors:
 * - Auth header ALWAYS wins: caller-supplied `Authorization` headers are overwritten by the helper's auth header.
 * - Does NOT throw on non-2xx response — the caller owns status-code branching (e.g. 409 → typed result).
 * - Does NOT trigger global error toasts or the 401 login redirect — those belong to `getAuthenticatedApiClient()`.
 *   Callers must handle error UX themselves.
 * - Canonical use case: endpoints where status-code branching is required (e.g. 409 = already submitted).
 *   See `useSubmitArticleFeedbackMutation` in `hooks/useArticles.ts` for the reference implementation.
 *
 * Use `getAuthenticatedApiClient()` instead when you don't need status-code branching.
 */
```

to:

```typescript
/**
 * Get an authenticated fetch function with the same headers as the API client.
 * Returns a fetch-like function that automatically attaches auth headers.
 *
 * Key behaviors:
 * - Auth header ALWAYS wins: caller-supplied `Authorization` headers are overwritten by the helper's auth header.
 * - Does NOT throw on non-2xx response — the caller owns status-code branching.
 * - Does NOT trigger global error toasts or the 401 login redirect — those belong to `getAuthenticatedApiClient()`.
 *   Callers must handle error UX themselves.
 *
 * When to use this:
 * Prefer the typed generated client (`getAuthenticatedApiClient()`) for normal calls. Reach for this
 * helper only when an endpoint's success/business-outcome contract cannot yet be expressed through the
 * generated client — for example, an `If-Match`-based update returning HTTP 412 Precondition Failed
 * before the controller has been annotated with `[ProducesResponseType(StatusCodes.Status412PreconditionFailed)]`
 * and before the NSwag template knows to surface 412 as a typed branch. Once the contract is annotated
 * and the typed branch is generated, migrate the call site back to the generated client.
 */
```

- [ ] **Step 2: Verify no other file references the stale doc**

```bash
grep -rn "useSubmitArticleFeedbackMutation.*reference implementation" frontend/src docs
```

Expected: no matches. If the doc was duplicated elsewhere, update those too.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/client.ts
git commit -m "docs: reframe getAuthenticatedFetch JSDoc as future-endpoint escape hatch"
```

---

## Task 9: Update `docs/development/api-client-generation.md`

**Files:**
- Modify: `docs/development/api-client-generation.md:217-233`

- [ ] **Step 1: Replace lines 217-233**

In `docs/development/api-client-generation.md`, change the block from line 217 (`**✅ CORRECT — for hooks that need to branch on specific HTTP status codes ...**`) through line 233 (`return response.json();\n` close-fence) to:

````markdown
**✅ CORRECT — for endpoints whose business outcomes are surfaced as HTTP status codes (e.g. 409 Conflict):**

The preferred pattern is to model the business outcome in the OpenAPI contract and let the generated client surface it as a typed, non-throwing branch. Annotate the controller action with both the success and the business-outcome status — both pointing at the same response DTO — so that the NSwag template override emits a typed `else if (status === 4xx)` branch:

```csharp
[ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status409Conflict)]
[HttpPost("{id:guid}/feedback")]
public async Task<ActionResult<SubmitArticleFeedbackResponse>> SubmitFeedback(...) { ... }
```

Then call the typed method and discriminate on the existing `BaseResponse.success` + `errorCode` envelope:

```typescript
import { getAuthenticatedApiClient } from './client';
import { ErrorCodes, SubmitArticleFeedbackRequest } from './generated/api-client';

const client = getAuthenticatedApiClient();
const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });
const response = await client.articles_SubmitFeedback(articleId, request);

if (
  response.success === false &&
  response.errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted
) {
  // 409 path — already submitted
  return { alreadySubmitted: true };
}
// 200 path — feedback recorded
return { precisionScore: response.precisionScore, styleScore: response.styleScore };
```

See `useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/useArticles.ts` for the canonical example, and `backend/src/Anela.Heblo.API/nswag-templates/README.md` for the template-override contract.

**Escape hatch — `getApiBaseUrl()` + `getAuthenticatedFetch()`.**

Reach for these helpers only when an endpoint's business outcome cannot yet be expressed through the generated client — for example, an `If-Match`-based update returning HTTP 412 Precondition Failed before the controller has been annotated with `[ProducesResponseType(StatusCodes.Status412PreconditionFailed)]`. The helpers attach auth headers, do not throw on non-2xx, and do not trigger the global error toast — leaving status-code branching entirely to the caller:

```typescript
import { getApiBaseUrl, getAuthenticatedFetch } from './client';

const url = `${getApiBaseUrl()}/api/resources/${id}`;
const response = await getAuthenticatedFetch()(url, {
  method: 'PUT',
  headers: { 'If-Match': etag, 'Content-Type': 'application/json' },
  body: JSON.stringify(body),
});
if (response.status === 412) return { precondition: 'stale' };
if (!response.ok) throw new Error(`HTTP ${response.status}`);
return response.json();
```

Once the controller is annotated and the template-override surfaces the typed branch, migrate the call site back to the generated client.
````

- [ ] **Step 2: Verify the surrounding section is still coherent**

Open `docs/development/api-client-generation.md` and re-read the section "The Solution" and "Enforcement Rules" (the latter at the original line ~235). The Enforcement Rules section should remain correct without further edits, but skim it to confirm it does not still reference `useSubmitArticleFeedbackMutation` as the canonical example.

- [ ] **Step 3: Commit**

```bash
git add docs/development/api-client-generation.md
git commit -m "docs: rewrite api-client-generation status-branching guidance"
```

---

## Task 10: FR-6 audit — produce the `apiClient as any` table

**Files:**
- No code change. This task produces a markdown table to be pasted into the PR description.

The brief expected 0–2 matches. The actual count in this codebase is substantial (verified by `grep -rn "apiClient as any" frontend/src/api/hooks/`). The audit table classifies every match per the spec FR-6 categories.

- [ ] **Step 1: Run the audit grep**

```bash
grep -rn "apiClient as any" frontend/src/api/hooks/ | sort > /tmp/apiClient-audit.txt
wc -l /tmp/apiClient-audit.txt
```

Expected: list of file:line matches (large — tens of hits across ~20+ hook files at the time of writing).

- [ ] **Step 2: Group by file and classify each file**

For each unique file in the audit, classify per spec FR-6:

- **In scope (this PR):** status-code branching where the body is the same DTO on success and on a 4xx. Expected count after this PR merges: **0** — `useSubmitArticleFeedbackMutation` is being migrated; if any other hook fits this pattern, expand this PR's scope or open an immediate follow-up before merging this PR.
- **Out of scope — different bypass pattern:** uses `apiClient as any` to access `.baseUrl` / `.http.fetch` for a reason unrelated to status-branching (binary download, FormData upload, query-string composition the generated method cannot express, dynamic relative URLs).
- **Out of scope — accepted technical debt:** documented and intentional.

Reading each file to classify is the bulk of the work in this task. Spot-check by opening 3-4 of the larger ones (e.g. `useBackgroundRefresh.ts`, `useDashboard.ts`, `useKnowledgeBase.ts`, `useLeaflet.ts`) and reading the surrounding lines:

```bash
sed -n '110,130p' frontend/src/api/hooks/useBackgroundRefresh.ts
```

- [ ] **Step 3: Draft the table in a scratch file**

Create `/tmp/apiClient-audit-table.md` for paste-into-PR-description:

```markdown
## `apiClient as any` audit (FR-6)

Triage of every `apiClient as any` occurrence in `frontend/src/api/hooks/` at the time of this PR.

| File | Lines | Pattern | Classification | Follow-up |
|---|---|---|---|---|
| useArticles.ts | (none) | n/a — fully refactored in this PR | n/a | n/a |
| useBackgroundRefresh.ts | 15, 17, 40, 42, 64, 66, 88, 90, 110, 112, 199, 201 | `(apiClient as any).baseUrl` + `.http.fetch` — dynamic URL pattern | Out of scope — different bypass pattern (relative URL composition not expressible via typed methods) | Issue: arch-review-backgroundrefresh-bypass |
| useBankStatements.ts | 75, 77, 143, 145, 182, 184, 224, 226 | `baseUrl` + `.http.fetch` for downloads / multi-endpoint | Out of scope — different bypass pattern | Issue: arch-review-bankstatements-bypass |
| useCarrierCooling.ts | 37, 38, 50, 51 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useCatalog.ts | 88, 90, 199, 200 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useCatalogDocuments.ts | 56, 57 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useDashboard.ts | 38, 39, 54, 55, 70, 71, 88, 90, 112, 114, 133, 135 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useDataQuality.ts | 105, 107, 141, 143, 170, 172 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useDepartments.ts | 15, 17 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useExpeditionListArchive.ts | 52, 58, 82, 84, 107, 109, 135, 137, 156 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useFinancialOverview.ts | 36, 37 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useGiftPackageManufacturing.ts | 20 | `apiClient as any as GeneratedApiClient` — type cast, not URL bypass | Out of scope — accepted technical debt (typing escape hatch) | (none) |
| useGiftSetting.ts | 24, 25, 37, 38 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useHealth.ts | 23, 25 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useInvoiceImportStatistics.ts | 42, 44 | `baseUrl` + `.http.fetch` (per brief: query-string composition the typed method does not support) | Out of scope — different bypass pattern | Issue: arch-review-invoiceimportstats-bypass |
| useIssuedInvoiceSyncStats.ts | 38, 40 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useIssuedInvoices.ts | 121, 123, 149, 151 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useKnowledgeBase.ts | 220, 222, 246, 248, 278, 280, 304, 306, 337, 339, 364, 366, 391, 393, 429, 436, 477, 479 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern (large surface — consider a single follow-up tracking issue) | Issue: arch-review-knowledgebase-bypass |
| useLeaflet.ts | 149, 151, 175, 177, 202, 204, 231, 233, 261, 267, 297, 299, 340, 342 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | Issue: arch-review-leaflet-bypass |
| useManualCatalogRefresh.ts | 110 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useManufactureOrders.ts | 54, 64, 82, 420, 421 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useManufactureOutput.ts | 39, 41 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useManufacturedProductInventory.ts | 72, 81 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useManufacturingStockAnalysis.ts | 168, 170 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useMaterials.ts | 40, 42, 70, 72 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useMeetingTasks.ts | 115, 116 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| usePackingMaterials.ts | 103, 163 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| usePhotobank.ts | 50, 55, 63, 75, 245 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| usePhotobankSettings.ts | 48, 56, 68, 83 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| usePurchaseOrders.ts | 47, 76, 78, 101, 103, 127, 129, 154, 156, 190, 192, 227, 229, 267, 269 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | Issue: arch-review-purchaseorders-bypass |
| useSemiproductRecipePdf.ts | 15, 16 | `baseUrl` + `.http.fetch` (per brief: binary download) | Out of scope — different bypass pattern | Issue: arch-review-semiproductpdf-bypass |
| useUserManagement.ts | 12, 13 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useAsyncInvoiceImport.ts | 44, 46, 81, 83, 117, 119 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
| useWeatherForecast.ts | 19, 20 | `baseUrl` + `.http.fetch` | Out of scope — different bypass pattern | (TBD per file read) |
```

The plan stipulates the rows verbatim above — but the implementer MUST verify each `(TBD per file read)` row by opening the file and confirming it is actually the URL-composition bypass pattern and not something else. If a file uses `apiClient as any` for a status-code-branching pattern matching this PR's scope, that hook MUST be added to this PR or the merge MUST wait for the same template-based fix on its backend endpoint.

- [ ] **Step 4: For each "Issue" entry, decide whether to open a follow-up issue now or defer**

Per FR-6, follow-ups are "expected" but the spec does not require they be open before merge. Reasonable approach: open ONE umbrella issue titled "Track audit: `apiClient as any` bypass-pattern cleanup in `frontend/src/api/hooks/`" linking the rows that should be revisited, rather than one issue per file. Cite the same arch-review labels already used by the prior arch-review (`feat-arch-review-…`).

Skip per-file issues if the umbrella issue captures the same triage outcome — fewer artifacts to maintain.

- [ ] **Step 5: Save the audit to a scratch file (not committed)**

```bash
cp /tmp/apiClient-audit-table.md /tmp/PR-DESCRIPTION-AUDIT.md
```

The contents are pasted into the PR description in the final task. The scratch file is not committed.

---

## Task 11: Full validation pass

This task is the equivalent of pre-commit verification before the final commit. All gates must be GREEN before proceeding to PR creation.

- [ ] **Step 1: Backend build + format**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: build succeeds, format reports no required changes. If format reports needed changes, run without `--verify-no-changes` to apply them, then commit as a separate `style:` commit.

- [ ] **Step 2: Backend unit tests (touch test)**

```bash
dotnet test backend/test/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Article"
```

Expected: all article-related tests pass. The change introduces no new BE behavior so test counts should be stable.

- [ ] **Step 3: Frontend lint**

```bash
cd frontend && npm run lint
```

Expected: no new warnings. The hook no longer imports `getApiBaseUrl` / `getAuthenticatedFetch`; lint should not complain.

- [ ] **Step 4: Frontend type-check + build**

```bash
cd frontend && npm run build
```

Expected: clean build. Verify no `as any` was introduced in `useArticles.ts` (FR-3 acceptance criterion).

- [ ] **Step 5: Frontend tests — full suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: all tests pass. Specifically, `useArticles.test.ts` reports 3 + 3 + 3 = 9 tests passing (3 in each describe block) and `client.test.ts` reports 3 toast-suppression tests passing.

- [ ] **Step 6: Confirm no `as any` in the refactored hook**

```bash
grep -n "as any\|@ts-ignore\|eslint-disable" frontend/src/api/hooks/useArticles.ts
```

Expected: matches only inside the `useGetArticleQuery` `sources` mapping at lines 175-187 (pre-existing — NOT in scope for this change). No new matches in `useSubmitArticleFeedbackMutation`. Verify lines 218-end show no such matches.

- [ ] **Step 7: Confirm no imports of the helpers remain in `useArticles.ts`**

```bash
grep -n "getApiBaseUrl\|getAuthenticatedFetch" frontend/src/api/hooks/useArticles.ts
```

Expected: empty (no matches). The helpers were removed from this file's imports.

- [ ] **Step 8: Confirm the helpers are still exported and still defined**

```bash
grep -n "export.*getApiBaseUrl\|export.*getAuthenticatedFetch" frontend/src/api/client.ts
```

Expected: matches at the lines they live now (≈177 and ≈407). Both definitions are still present.

- [ ] **Step 9: Confirm the controller annotation is in place**

```bash
grep -B1 -A2 'ProducesResponseType.*StatusCodes.Status409Conflict' backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs
```

Expected: match showing the two `[ProducesResponseType]` lines above the `SubmitFeedback` action.

---

## Task 12: Create the PR

- [ ] **Step 1: Push the branch**

```bash
git push -u origin feat-nswag-surface-409-on-post-api-articles-i
```

- [ ] **Step 2: Open the PR**

```bash
gh pr create --title "feat: surface HTTP 409 as typed branch on articles_SubmitFeedback" --body "$(cat <<'EOF'
## Summary

- Annotate `ArticlesController.SubmitFeedback` with `[ProducesResponseType(200)]` and `[ProducesResponseType(409)]`, both pointing at `SubmitArticleFeedbackResponse`.
- Add a minimal NSwag Liquid template override that emits a typed non-throwing branch for any operation whose 4xx response body schema equals its 2xx body schema. The override is a no-op for every other operation (verified byte-for-byte against the pre-regen snapshot of `api-client.ts`).
- Refactor `useSubmitArticleFeedbackMutation` to call `apiClient.articles_SubmitFeedback(...)` directly and discriminate the 409 outcome via the existing `BaseResponse.success` + `errorCode` envelope. No more raw `fetch`, no more `as any`.
- Suppress the global "Upozornění" toast on 409 responses whose body is a structured `BaseResponse` — preserves the toast-free behavior of the prior `getAuthenticatedFetch()` path (arch-review Specification Amendment #1, option A).
- Keep `getApiBaseUrl()` and `getAuthenticatedFetch()` exported; update their JSDoc and the `docs/development/api-client-generation.md` guidance to frame them as the escape hatch for endpoints not yet expressed through the generated client.
- Rewrite the `useSubmitArticleFeedbackMutation` Jest tests to mock the generated method and exercise the three branches (200, typed 409, throw).

## Test plan

- [ ] `dotnet build backend/Anela.Heblo.sln` — clean
- [ ] `dotnet format backend/Anela.Heblo.sln --verify-no-changes` — clean
- [ ] `dotnet test backend/test/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Article"` — all pass
- [ ] `cd frontend && npm run lint` — clean
- [ ] `cd frontend && npm run build` — clean
- [ ] `cd frontend && npm test -- --watchAll=false` — all pass, includes 3 rewritten cases for `useSubmitArticleFeedbackMutation` and 3 new cases for `getAuthenticatedApiClient` toast suppression.
- [ ] Manually: regenerate the client a second time and confirm `git diff frontend/src/api/generated/api-client.ts` is empty (idempotency).
- [ ] Manually: verify the post-merge feedback flow on staging — submit feedback once, refresh, attempt submit again; expect the previously-stored scores to render with no error toast.

## `apiClient as any` audit (FR-6)

<!-- Paste contents of /tmp/PR-DESCRIPTION-AUDIT.md here -->
EOF
)"
```

When prompted, paste the contents of `/tmp/PR-DESCRIPTION-AUDIT.md` into the `<!-- Paste ... -->` placeholder.

- [ ] **Step 3: Return the PR URL to the user**

The `gh pr create` command prints the PR URL on success. Report it in the final message.

---

## Self-Review Checklist (performed by plan author before handoff)

**Spec coverage:**

- FR-1 (controller annotation) → Task 1 ✅
- FR-2 (NSwag template typed 409) → Tasks 2, 3, 4 + Task 5 verification ✅
- FR-3 (hook calls generated client directly) → Task 7 ✅
- FR-4 (tests rewritten) → Task 7 Step 1 ✅
- FR-5 (helpers stay; JSDoc updated) → Task 8 + Task 9 ✅
- FR-6 (audit) → Task 10 ✅
- NFR-1 (backwards compat / no observable change) → Task 6 (toast suppression preserves prior behavior) + Task 7 (same React Query semantics) ✅
- NFR-2 (zero `as any`) → Task 11 Step 6 verification ✅
- NFR-3 (minimal template change) → Task 3 Step 5 diff check + Task 5 byte-equality check ✅
- NFR-4 (perf / security unchanged) → enforced by Tasks 1 + 7 not modifying the request shape; auth path verified in Task 6 ✅
- NFR-5 (test coverage threshold) → Task 7 retains and rewrites the three mutation tests; the two query describe blocks stay untouched ✅
- Arch-review Specification Amendment #1 (toast regression) → Task 6 ✅
- Arch-review Specification Amendment #2 (docs/development/api-client-generation.md rewrite) → Task 9 ✅
- Arch-review Specification Amendment #3 (tightened byte-equality) → Task 5 Step 3 explicit diff check ✅
- Arch-review Specification Amendment #4 (predicate clarification) → Task 2 README + Task 3 Step 4 comment ✅
- Arch-review Specification Amendment #5 (escape hatch) → Pre-Flight #4 + Task 3 Step 5 STOP condition ✅

**Placeholder scan:** no "TBD" / "implement later" / "add appropriate" in any step that affects code. The audit table in Task 10 Step 3 contains `(TBD per file read)` rows — these are explicitly call-outs for the implementer to verify by reading each file, not placeholders in deliverable code.

**Type / name consistency:** `ErrorCodes.ArticleFeedbackAlreadySubmitted` (string-valued enum confirmed by reading `api-client.ts:12232`), `SubmitArticleFeedbackRequest` (class with constructor — instantiated, not used as `ISubmitArticleFeedbackRequest`), `SubmitArticleFeedbackResponse` (return type — same on both branches), `articleKeys.detail(articleId)` (existing factory at `useArticles.ts:123`, unchanged) — all consistent across Tasks 7-11.
