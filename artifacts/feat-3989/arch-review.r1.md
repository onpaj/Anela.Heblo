# Architecture Review: Exclude ArticleId from SubmitArticleFeedbackRequest Body Contract

## Skip Design: true

## Architectural Fit Assessment

This is a narrow, well-contained API-contract correction. It touches exactly one Application-layer request DTO (`SubmitArticleFeedbackRequest`), one controller action that already exists and needs no logic change, one generated client artifact that is rebuilt by tooling, and one frontend hook call site. No new module, service, endpoint, or domain concept is introduced.

The pattern it targets — a route-bound id that the controller stamps onto a `[FromBody]` request DTO before dispatch — is **not unique to Articles**. A grep across `backend/src/Anela.Heblo.API/Controllers/*.cs` for `request.\w*Id = id` turns up the same idiom in at least seven other controllers:

- `AuthorizationController.cs` (`Id`, `GroupId`, `UserId` — 6 occurrences)
- `InvoiceClassificationController.cs`
- `JournalController.cs`
- `MarketingCalendarController.cs`
- `MindMapsController.cs` (`MindMapId`, `Id`)
- `TransportBoxController.cs` (`BoxId`, annotated `// Ensure consistency`)

None of these DTOs currently use `[JsonIgnore]`, `[BindNever]`, or any other mechanism to hide the route-derived field from the body schema — a grep for `JsonIgnore` across `backend/src` finds it used only in adapter-layer HTTP client models (Shoptet, Smartsupp, Logeto) and one pipeline recorder, never in an Application `UseCases` request DTO. **There is no established codebase convention for this specific problem yet.** This fix will be the first instance of the pattern, not an application of an existing one. That's an acceptable and good precedent to set, but the review should be explicit that this PR does not "align with an existing pattern" so much as **introduce** the right one, scoped deliberately (per the spec's Out of Scope section) to only this one property. Treat this PR as the reference example for future cleanups of the other seven call sites, without doing that broader cleanup here.

Confirmed constraints from the codebase that the spec correctly respects:
- `SubmitArticleFeedbackRequest` is already a class, not a record — consistent with the repo's DTO rule.
- `[Range(1,5)]` on `PrecisionScore`/`StyleScore` and `[MaxLength(1000)]` on `Comment` are untouched; `[JsonIgnore]` on `ArticleId` has no interaction with these, since `ArticleId` carries no validation attribute of its own.
- `useSubmitArticleFeedbackMutation` (`frontend/src/api/hooks/useArticles.ts:213-243`) already follows the "standard hook" pattern (`getAuthenticatedApiClient()` + typed client method), not the `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch — so the repo's absolute-URL rule for hand-rolled fetches is **not implicated** by this change; nothing there needs preserving beyond what already exists.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────┐        ┌──────────────────────────────┐
│ ArticlesController      │        │ SubmitArticleFeedbackRequest  │
│ POST {id}/feedback      │──────▶ │  ArticleId   [JsonIgnore] NEW │
│  request.ArticleId = id │ (route)│  PrecisionScore                │
│  (unchanged C# call)    │        │  StyleScore                    │
└─────────────────────────┘        │  Comment                       │
            │                       └──────────────────────────────┘
            ▼ MediatR.Send(request)
┌──────────────────────────────┐
│ SubmitArticleFeedbackHandler  │  (unchanged: reads request.ArticleId
└──────────────────────────────┘   as a normal in-memory property)

            NSwag regeneration (build-time)
                        │
                        ▼
┌───────────────────────────────────────────┐
│ frontend/src/api/generated/api-client.ts   │
│  SubmitArticleFeedbackRequest              │
│    (articleId field REMOVED)               │
└───────────────────────────────────────────┘
                        │
                        ▼
┌───────────────────────────────────────────┐
│ useSubmitArticleFeedbackMutation           │
│  new SubmitArticleFeedbackRequest({        │
│    precisionScore, styleScore, comment })  │  ← articleId dropped from body
│  client.articles_SubmitFeedback(articleId, │  ← route arg unchanged
│                                  request)   │
└───────────────────────────────────────────┘
```

The only new edge in this diagram relative to today is the `[JsonIgnore]` annotation itself — everything downstream (controller call pattern, handler, mutation's route argument) is unchanged. This is a wire-contract change with zero runtime-behavior change on the happy path.

### Key Design Decisions

#### Decision 1: `[JsonIgnore]` on the property vs. removing `ArticleId` from the DTO entirely
**Options considered:**
1. Add `[System.Text.Json.Serialization.JsonIgnore]` to `ArticleId`, keep the property as a normal C# member the controller/handler read and write in-memory.
2. Remove `ArticleId` from `SubmitArticleFeedbackRequest` entirely and instead pass it as a separate method parameter through to the handler (e.g., via `IRequest` construction with a constructor, or a wrapper/tuple).
3. Use `[BindNever]` (ASP.NET Core MVC model-binding attribute) instead of `[JsonIgnore]`.

**Chosen approach:** Option 1, exactly as the brief and spec specify.

**Rationale:** `SubmitArticleFeedbackHandler` and the controller both already treat `ArticleId` as a normal property of the MediatR request — that's how this codebase routes route-derived context into a handler when the request needs it. Removing the property (option 2) would require touching the handler and would go beyond "no handler changes needed," which the spec explicitly rules out. `[BindNever]` (option 3) is an MVC model-binding concept that governs `[FromBody]`/`[FromForm]` binding but has no defined effect on NSwag's OpenAPI schema generation for a request DTO with `[FromBody]` binding via `System.Text.Json` — it's the wrong tool here because the goal is specifically to affect **JSON serialization and the generated OpenAPI schema**, both of which `[JsonIgnore]` governs directly and predictably. `[JsonIgnore]` is also the annotation already in active use elsewhere in this codebase (adapters, pipeline recorder) for "don't serialize this member," so it's consistent with existing developer expectations even though it hasn't been applied to an inbound Application-layer request DTO before.

#### Decision 2: Scope — fix only `ArticleId` on this one DTO, or generalize to the other seven controllers with the same pattern
**Options considered:**
1. Fix only `SubmitArticleFeedbackRequest.ArticleId`, as scoped by the brief/spec.
2. Audit and fix all `request.\w*Id = id` call sites found in `AuthorizationController`, `InvoiceClassificationController`, `JournalController`, `MarketingCalendarController`, `MindMapsController`, and `TransportBoxController` in the same PR.

**Chosen approach:** Option 1. Do not touch the other controllers in this PR.

**Rationale:** The brief and spec are explicit that this is a single arch-review finding about one DTO; the spec's "Out of Scope" section calls out "retroactive cleanup of any other DTOs with similar route-value-overwrites-body-value patterns" by name. Bundling a 7-controller sweep into what's meant to be a tiny, reviewable fix increases blast radius and review burden disproportionately to the finding. Recommend filing a **follow-up arch-review item** (not part of this PR) referencing the same pattern across those six other controllers — mention this in Specification Amendments below so it isn't lost.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Exactly one source edit plus one generated-artifact regeneration plus two follow-on edits:

- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs` — add the `using System.Text.Json.Serialization;` import and `[JsonIgnore]` attribute on `ArticleId`.
- `frontend/src/api/generated/api-client.ts` — regenerated, not hand-edited.
- `frontend/src/api/hooks/useArticles.ts` — remove `articleId` from the `new SubmitArticleFeedbackRequest({...})` object literal in `useSubmitArticleFeedbackMutation` (lines ~213-227).
- `docs/development/api-client-generation.md` — update the canonical example snippet (`~line 178`) that also constructs `SubmitArticleFeedbackRequest({ articleId, ... })`.

No changes to `ArticlesController.cs` or `SubmitArticleFeedbackHandler.cs` — confirmed both only need `ArticleId` as an in-memory property, which `[JsonIgnore]` preserves.

### Interfaces and Contracts

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    [JsonIgnore]
    public Guid ArticleId { get; set; }

    [Range(1, 5)]
    public int PrecisionScore { get; set; }

    [Range(1, 5)]
    public int StyleScore { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}
```

Post-regeneration, the generated TypeScript class (`frontend/src/api/generated/api-client.ts`) must lose the `articleId?: string` field and its `init`/`toJSON` references — verify this by diffing the generated file after running the generator, not by hand-editing it (hand-editing a generated file will be silently clobbered on the next build and masks whether the backend annotation actually worked).

```typescript
// frontend/src/api/hooks/useArticles.ts — useSubmitArticleFeedbackMutation, after fix
const request = new SubmitArticleFeedbackRequest({
  precisionScore: payload.precisionScore,
  styleScore: payload.styleScore,
  comment: payload.comment,
});
const response = await client.articles_SubmitFeedback(articleId, request); // route arg unchanged
```

### Data Flow

1. Client calls `POST /api/Articles/{id}/feedback` with body `{ precisionScore, styleScore, comment }` (no `articleId`).
2. `ArticlesController.SubmitFeedback(Guid id, [FromBody] SubmitArticleFeedbackRequest request, ...)` binds the body (now `[JsonIgnore]`-filtered, so `ArticleId` binds to its C# default `Guid.Empty` regardless of body content) and then executes `request.ArticleId = id;` — same line as today, now the *only* place `ArticleId` is ever set to a meaningful value.
3. `_mediator.Send(request, ct)` dispatches unchanged; `SubmitArticleFeedbackHandler` reads `request.ArticleId` exactly as before.
4. Response flow (`SubmitArticleFeedbackResponse`, 200/409) is untouched end-to-end.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Generated client not regenerated before merge, leaving `api-client.ts` and the frontend hook edit mutually inconsistent (hook stops passing `articleId` but generated type still has it, or vice versa) | Medium | Regenerate via `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (or `npm run generate-client`) as an explicit step in the PR, and confirm via `git diff` that only `SubmitArticleFeedbackRequest`-related lines changed in `api-client.ts` — a broader diff signals an unrelated schema drift that shouldn't be bundled into this PR |
| `frontend/src/api/hooks/__tests__/useArticles.test.ts` currently asserts `expect(mockArticlesSubmitFeedback).toHaveBeenCalledWith(..., expect.objectContaining({ articleId: 'article-1', ... }))` (confirmed at test line ~324) — this assertion will fail once the hook stops constructing `articleId` into the mocked request object, even though FR-2/FR-3 don't call this out explicitly | Medium | Update this specific assertion as part of FR-3's acceptance criteria ("existing unit tests ... continue to pass, updated if they assert on the request body shape") — this is not optional, the test **will** fail without the update; flagged explicitly here since the spec only mentions it conditionally |
| Precedent risk: reviewers or future contributors might read this PR as "the convention is now `[JsonIgnore]` on route-derived ids" and start applying it inconsistently to the other 6 controllers with the same shape, in unrelated PRs, without review | Low | Call this out in the PR description as a deliberate, narrowly-scoped fix; file a separate backlog/arch-review item for the broader pattern (see Specification Amendments) rather than letting it happen ad hoc |
| `[JsonIgnore]` silently drops any `articleId` sent in the body with no validation error, which could mask a genuine client bug (e.g., a client mistakenly believes it's addressing a different article than the route implies) | Low | Accepted risk per spec (explicitly listed in FR-1 acceptance criteria: "body that includes `articleId` ... is accepted without error, value has no effect") — this is the intended behavior, matching how every other controller in this codebase already treats route-vs-body id mismatches (none of them validate agreement either) |

## Specification Amendments

1. **Test file update should be a named acceptance criterion, not implicit.** FR-3's acceptance criteria say tests should be "updated if they assert on the request body shape" — exploration confirms `frontend/src/api/hooks/__tests__/useArticles.test.ts:321-325` **does** assert `articleId: 'article-1'` on the mocked call. Recommend restating this as a firm requirement: update that specific `expect(...).toHaveBeenCalledWith(...)` assertion to drop `articleId` from the expected request-body shape (the route-level `articleId` argument to `client.articles_SubmitFeedback` stays, only the second/body argument's expected shape changes).
2. **Recommend filing a follow-up arch-review/backlog item** (out of scope for this PR, per spec) for the same `request.<X>Id = id` route-overwrites-body pattern found in `AuthorizationController`, `InvoiceClassificationController`, `JournalController`, `MarketingCalendarController`, `MindMapsController`, and `TransportBoxController`. Not a blocking amendment to this spec — just don't let this PR's precedent go unfollowed.
3. No changes needed to FR-1/FR-2/FR-4 — verified accurate against the actual controller, DTO, docs, and NSwag build target.

## Prerequisites

- None. No migrations, no new configuration, no new infrastructure. The NSwag/MSBuild generation target (`GenerateFrontendClientManual` in `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`) and the `frontend` `prebuild`/`generate-client` npm scripts already exist and require no setup — confirmed present in the current worktree.
- Implementation order: (1) add `[JsonIgnore]` to the backend DTO, (2) regenerate the TypeScript client, (3) update the frontend hook call site, (4) update the failing/outdated test assertion, (5) update the doc snippet. Steps 2-5 depend on step 1 landing first so the regenerated client actually reflects the new schema.
