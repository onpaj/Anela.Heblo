# Exclude ArticleId from SubmitArticleFeedbackRequest Body Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `SubmitArticleFeedbackRequest.ArticleId` from being readable/writable over the JSON wire, so the route-derived article id is the only source of truth, and propagate that contract change through the generated TypeScript client, the one frontend call site that constructs the request body, and the docs snippet that models this exact pattern.

**Architecture:** Single-property `[JsonIgnore]` annotation on an existing Application-layer request DTO (`SubmitArticleFeedbackRequest`) — no controller or handler logic changes, since both already treat `ArticleId` as a normal in-memory C# property that only `ArticlesController.SubmitFeedback` ever sets (from the route). The change cascades through NSwag regeneration into `frontend/src/api/generated/api-client.ts`, which forces one call-site edit in `useSubmitArticleFeedbackMutation` and one test-assertion edit, plus a docs example update.

**Tech Stack:** .NET 8, `System.Text.Json.Serialization.JsonIgnoreAttribute`, xUnit + FluentAssertions (backend); React, TypeScript, `@tanstack/react-query`, Jest + Testing Library (frontend); NSwag.MSBuild for client generation.

Full plan also saved to `docs/superpowers/plans/2026-08-30-exclude-articleid-feedback-request-body.md`.

---

### task: backend-json-ignore-article-id

## Goal
Add `[System.Text.Json.Serialization.JsonIgnore]` to `SubmitArticleFeedbackRequest.ArticleId` so it is never populated from the inbound JSON body and never serialized out, proven by a new unit test written first (TDD), with no change to `ArticlesController` or `SubmitArticleFeedbackHandler`.

## Files to change

**Edit:**
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs`

**Create:**
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackRequestSerializationTests.cs`

**Verify only, no change expected:**
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — `request.ArticleId = id;` (line 79) must keep compiling and behaving identically; `[JsonIgnore]` only affects JSON (de)serialization, not normal C# property get/set.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` — reads `request.ArticleId` in four places (ownership check, not-found, not-generated, already-submitted branches); must keep working unchanged since it reads the property after the controller has already set it.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` — constructs `SubmitArticleFeedbackRequest` directly with an `ArticleId` in C# (not via JSON), so it is unaffected by `[JsonIgnore]`; must still pass unmodified.
- `backend/test/Anela.Heblo.Tests/Controllers/ArticlesControllerTests.cs` — does not exercise the feedback endpoint; unaffected.

**Do not touch:**
- The other six controllers with the same `request.<X>Id = id` idiom (`AuthorizationController.cs`, `InvoiceClassificationController.cs`, `JournalController.cs`, `MarketingCalendarController.cs`, `MindMapsController.cs`, `TransportBoxController.cs`) — explicitly out of scope per the spec and arch-review; this PR is the reference example, not a sweep.
- `PrecisionScore`, `StyleScore`, `Comment` properties and their `[Range]`/`[MaxLength]` attributes on `SubmitArticleFeedbackRequest` — unaffected.

## Steps

- [ ] **Step 1: Write the failing serialization test**

Create `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackRequestSerializationTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;
using FluentAssertions;

namespace Anela.Heblo.Tests.Article.UseCases;

public class SubmitArticleFeedbackRequestSerializationTests
{
    [Fact]
    public void Deserialize_ArticleIdInBody_IsIgnored()
    {
        var bodyArticleId = Guid.NewGuid();
        var json = $$"""
            {
                "articleId": "{{bodyArticleId}}",
                "precisionScore": 4,
                "styleScore": 5,
                "comment": "great"
            }
            """;

        var request = JsonSerializer.Deserialize<SubmitArticleFeedbackRequest>(json)!;

        request.ArticleId.Should().Be(Guid.Empty);
        request.PrecisionScore.Should().Be(4);
        request.StyleScore.Should().Be(5);
        request.Comment.Should().Be("great");
    }

    [Fact]
    public void Deserialize_ArticleIdOmittedFromBody_BehavesIdenticallyToWhenPresent()
    {
        var json = """
            {
                "precisionScore": 2,
                "styleScore": 3
            }
            """;

        var request = JsonSerializer.Deserialize<SubmitArticleFeedbackRequest>(json)!;

        request.ArticleId.Should().Be(Guid.Empty);
        request.PrecisionScore.Should().Be(2);
        request.StyleScore.Should().Be(3);
    }

    [Fact]
    public void Serialize_DoesNotIncludeArticleId()
    {
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = Guid.NewGuid(),
            PrecisionScore = 3,
            StyleScore = 2,
            Comment = "ok",
        };

        var json = JsonSerializer.Serialize(request);

        json.Should().NotContain("articleId");
        json.Should().Contain("\"precisionScore\":3");
        json.Should().Contain("\"styleScore\":2");
        json.Should().Contain("\"comment\":\"ok\"");
    }
}
```

- [ ] **Step 2: Run the new tests and confirm they fail**

```bash
dotnet test --filter "FullyQualifiedName~SubmitArticleFeedbackRequestSerializationTests"
```

Expected: build succeeds, but all 3 tests **fail**. Specifically:
- `Deserialize_ArticleIdInBody_IsIgnored` fails because `request.ArticleId` deserializes to `bodyArticleId`, not `Guid.Empty`.
- `Deserialize_ArticleIdOmittedFromBody_BehavesIdenticallyToWhenPresent` passes already (nothing to ignore when the field is absent) — that's fine, it exists to pin the "omitted" half of FR-1's acceptance criteria going forward.
- `Serialize_DoesNotIncludeArticleId` fails because the emitted JSON contains an `"articleId":"..."` key.

If `Deserialize_ArticleIdOmittedFromBody_BehavesIdenticallyToWhenPresent` also fails for an unexpected reason, stop and re-read `SubmitArticleFeedbackRequest.cs` before continuing — the plan assumes today's shape (no `[Required]`/custom converter on `ArticleId`).

- [ ] **Step 3: Add `[JsonIgnore]` to `ArticleId`**

Read the current file first:

```bash
cat backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs
```

Current top of file:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }
```

Change to:

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    [JsonIgnore]
    public Guid ArticleId { get; set; }
```

(Only the `using System.Text.Json.Serialization;` line and the `[JsonIgnore]` attribute are added. `PrecisionScore`, `StyleScore`, `Comment`, and `SubmitArticleFeedbackResponse` below are untouched.)

- [ ] **Step 4: Run the tests again and confirm they pass**

```bash
dotnet test --filter "FullyQualifiedName~SubmitArticleFeedbackRequestSerializationTests"
```

Expected: all 3 tests pass.

- [ ] **Step 5: Run the full existing Article test suite to confirm no regression**

```bash
dotnet test --filter "FullyQualifiedName~Article"
```

Expected: all tests pass, including every test in `SubmitArticleFeedbackHandlerTests` (unaffected — it constructs the request in C#, not via JSON) and `ArticlesControllerTests`.

- [ ] **Step 6: Build the whole backend solution**

```bash
dotnet build
```

Expected: build succeeds with no errors or new warnings.

- [ ] **Step 7: Run dotnet format**

```bash
dotnet format --verify-no-changes
```

Expected: no formatting changes needed. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-stage the affected files before committing.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackRequestSerializationTests.cs
git commit -m "fix(article): exclude ArticleId from SubmitArticleFeedbackRequest JSON contract

ArticlesController.SubmitFeedback already overwrites request.ArticleId
from the route parameter before dispatch, so any articleId a client
sent in the body was silently discarded. [JsonIgnore] makes that
explicit: the property is no longer bound from the request body or
emitted in the OpenAPI schema, while remaining a normal in-memory
property the controller and handler read/write exactly as before.

Refs #3989"
```

---

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

### task: update-frontend-hook-and-test-for-articleid-removal

## Goal
Stop `useSubmitArticleFeedbackMutation` from passing `articleId` into the `SubmitArticleFeedbackRequest` constructor (the field no longer exists on the regenerated type), and update the one existing test assertion that currently expects it in the mocked request body.

## Files to change

**Edit:**
- `frontend/src/api/hooks/useArticles.ts`
- `frontend/src/api/hooks/__tests__/useArticles.test.ts`

**Verify only, no change expected:**
- `frontend/src/api/hooks/useArticles.ts` lines other than the `useSubmitArticleFeedbackMutation` body — e.g. `useArticleFeedbackListQuery`, `articleKeys` — untouched.
- The route-level `articleId` parameter/argument: `useSubmitArticleFeedbackMutation(articleId: string)`'s signature, and the call `client.articles_SubmitFeedback(articleId, request)`, and `queryClient.invalidateQueries({ queryKey: articleKeys.detail(articleId) })` — all stay exactly as they are; only the object literal passed into `new SubmitArticleFeedbackRequest({...})` changes.
- The other two tests in the `useSubmitArticleFeedbackMutation` describe block (409 and 500 paths) — they don't assert on the request body shape, so they need no edit and must keep passing.

**Do not touch:**
- `frontend/src/api/generated/api-client.ts` — already regenerated in the previous task; not touched here.

## Steps

- [ ] **Step 1: Confirm the test currently fails to compile / fails against the regenerated type**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -i "articleId" | head -20
```

Expected: a TypeScript error pointing at `frontend/src/api/hooks/useArticles.ts` around the `new SubmitArticleFeedbackRequest({ articleId, ... })` call, e.g. `Object literal may only specify known properties, and 'articleId' does not exist in type ...`. This confirms the regenerated client (previous task) has already made the current hook code invalid — the fix below is required, not optional.

- [ ] **Step 2: Remove `articleId` from the request body construction**

In `frontend/src/api/hooks/useArticles.ts`, current code (around line 213-227):

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
```

Change to:

```typescript
export const useSubmitArticleFeedbackMutation = (articleId: string) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: SubmitArticleFeedbackPayload): Promise<SubmitArticleFeedbackResult> => {
      const client = getAuthenticatedApiClient();
      const request = new SubmitArticleFeedbackRequest({
        precisionScore: payload.precisionScore,
        styleScore: payload.styleScore,
        comment: payload.comment,
      });
```

(Delete only the `articleId,` line inside the object literal. The function signature `useSubmitArticleFeedbackMutation(articleId: string)`, the `client.articles_SubmitFeedback(articleId, request)` call below it, and everything else in the file are unchanged — `articleId` is still in scope and still used as the route argument.)

- [ ] **Step 3: Confirm the TypeScript error is gone**

```bash
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | grep -i "articleId"
```

Expected: no output.

- [ ] **Step 4: Run the existing hook test suite and confirm the known assertion fails**

```bash
cd frontend && npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts --watchAll=false
```

Expected: the suite reports a failure in `useSubmitArticleFeedbackMutation > resolves with parsed body on 2xx (typed generated response with success: true)`, specifically the `toHaveBeenCalledWith` assertion, because the mocked call no longer receives an `articleId` key in the body object — `objectContaining` fails to match since the actual received object at that key is `undefined`/absent while the expectation still requires `articleId: 'article-1'`. The other two tests in the same describe block still pass.

- [ ] **Step 5: Update the test assertion to drop `articleId` from the expected body shape**

In `frontend/src/api/hooks/__tests__/useArticles.test.ts`, current code (lines 321-329):

```typescript
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(
      'article-1',
      expect.objectContaining({
        articleId: 'article-1',
        precisionScore: 4,
        styleScore: 5,
        comment: 'great',
      }),
    );
```

Change to:

```typescript
    expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(
      'article-1',
      expect.objectContaining({
        precisionScore: 4,
        styleScore: 5,
        comment: 'great',
      }),
    );
```

(Delete only the `articleId: 'article-1',` line. The first positional argument `'article-1'` — the route-level id passed to `client.articles_SubmitFeedback` — is unchanged and stays asserted.)

- [ ] **Step 6: Run the hook test suite again and confirm it passes**

```bash
cd frontend && npx react-scripts test src/api/hooks/__tests__/useArticles.test.ts --watchAll=false
```

Expected: `Tests: 6 passed, 6 total` (3 tests in `useArticleFeedbackListQuery mapping`, 2 in `useArticleFeedbackListQuery parameter passing`... — run `grep -c "it(" frontend/src/api/hooks/__tests__/useArticles.test.ts` beforehand if the exact count is needed; the key expectation is 0 failed, 0 skipped, all `useSubmitArticleFeedbackMutation` tests (3) green).

- [ ] **Step 7: Run the full frontend test suite**

```bash
cd frontend && npm test -- --watchAll=false
```

Expected: all suites pass, no new failures introduced elsewhere.

- [ ] **Step 8: Run the frontend build and lint**

```bash
cd frontend && npm run build
cd frontend && npm run lint
```

Expected: both succeed with no errors (build: no TypeScript compile errors; lint: no new lint errors).

- [ ] **Step 9: Commit**

```bash
git add frontend/src/api/hooks/useArticles.ts frontend/src/api/hooks/__tests__/useArticles.test.ts
git commit -m "fix(article): stop sending articleId in SubmitArticleFeedback request body

The regenerated SubmitArticleFeedbackRequest no longer has an
articleId field (backend now [JsonIgnore]s it), so
useSubmitArticleFeedbackMutation no longer constructs it into the
body. The route-level articleId argument to
client.articles_SubmitFeedback is unchanged. Updates the one existing
test assertion that expected articleId in the mocked request body.

Refs #3989"
```

---

### task: update-api-client-generation-docs-snippet

## Goal
Update the canonical example in `docs/development/api-client-generation.md` — which uses this exact endpoint as its documented illustration of the "business outcome as HTTP status" pattern — so its `SubmitArticleFeedbackRequest` constructor snippet matches the post-fix shape (no `articleId`).

## Files to change

**Edit:**
- `docs/development/api-client-generation.md`

**Verify only, no change expected:**
- The surrounding explanatory prose in that same section (the description of the business-outcome-as-HTTP-status pattern, the `[ProducesResponseType]` C# snippet above it, the `try/catch`/409-handling explanation below it, and the "escape hatch" section that follows) — none of it changes; only the one object-literal line changes.

**Do not touch:**
- Any other doc file under `docs/` — out of scope.

## Steps

- [ ] **Step 1: Locate the exact line to change**

```bash
grep -n "new SubmitArticleFeedbackRequest" docs/development/api-client-generation.md
```

Expected output: `170:const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });`

- [ ] **Step 2: Update the snippet**

Current (line 170, within the fenced ```typescript block starting at line 166):

```typescript
const request = new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment });
```

Change to:

```typescript
const request = new SubmitArticleFeedbackRequest({ precisionScore, styleScore, comment });
```

(Only this line changes — delete `articleId, ` from the object literal. The `import` line above it, the `const client = getAuthenticatedApiClient();` line, and the `try/catch` block below it are untouched.)

- [ ] **Step 3: Confirm no other reference to the old shape remains in the file**

```bash
grep -n "articleId" docs/development/api-client-generation.md
```

Expected: no output (no matches) — the doc no longer mentions `articleId` anywhere, since the code sample was its only occurrence.

- [ ] **Step 4: Commit**

```bash
git add docs/development/api-client-generation.md
git commit -m "docs(api-client-generation): drop articleId from SubmitArticleFeedbackRequest example

Keeps the canonical business-outcome-as-HTTP-status example in sync
with the actual generated client shape after ArticleId was excluded
from the request body contract.

Refs #3989"
```

## Acceptance criteria (full feature)
- `SubmitArticleFeedbackRequest.ArticleId` carries `[JsonIgnore]`; `PrecisionScore`, `StyleScore`, `Comment` are unaffected (FR-1).
- A JSON body with an `articleId` key deserializes with `ArticleId == Guid.Empty` (untouched by the body), proven by `SubmitArticleFeedbackRequestSerializationTests` (FR-1).
- A JSON body without `articleId` behaves identically (FR-1).
- `ArticlesController.SubmitFeedback` and `SubmitArticleFeedbackHandler` are unmodified (FR-1).
- `frontend/src/api/generated/api-client.ts`'s `SubmitArticleFeedbackRequest`/`ISubmitArticleFeedbackRequest` no longer declare `articleId`, confirmed via regeneration + scoped diff, no unrelated type changed (FR-2).
- `useSubmitArticleFeedbackMutation` no longer constructs `articleId` into the request body; `client.articles_SubmitFeedback(articleId, request)`'s route argument is unchanged (FR-3).
- `frontend` builds and type-checks cleanly (FR-3).
- `useArticles.test.ts`'s `toHaveBeenCalledWith` assertion no longer expects `articleId` in the body (FR-3).
- `docs/development/api-client-generation.md`'s example snippet matches the post-fix shape; surrounding prose is untouched (FR-4).
