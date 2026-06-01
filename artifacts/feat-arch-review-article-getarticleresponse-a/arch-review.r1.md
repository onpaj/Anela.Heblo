# Architecture Review: Type-Safe ArticleStatus in Response DTOs

## Skip Design: true

No UI/UX work. This is a backend type-system refactor whose only observable frontend effect is a cast removal in a React Query hook; the rendered UI, request/response shapes, and JSON wire format are unchanged.

## Architectural Fit Assessment

The change aligns cleanly with existing conventions and is, in effect, a corrective alignment toward an already-established pattern:

- **Vertical Slice + DTO ownership** — `GetArticleResponse`, `ArticleListItemDto`, and `GenerateArticleResponse` already live inside their respective use-case folders under `backend/src/Anela.Heblo.Application/Features/Article/UseCases/<UseCase>/`. No directory layout changes are required.
- **DTOs are classes, not records** — All three DTOs are already `public sealed class`. The project's NSwag-related rule (records mishandled by client generators) is preserved.
- **Global enum serialization** — `Program.cs:121` registers `JsonStringEnumConverter` once at the MVC level. `GenerateArticleResponse.Status` (already typed `ArticleStatus`) round-trips through that converter today, proving the wire format is identical regardless of whether the DTO is typed as `string` or `ArticleStatus`. The proposed fix is to use the same pattern in two more places.
- **Cross-module isolation** — `ArticleStatus` lives in `Anela.Heblo.Domain.Features.Article`. `Anela.Heblo.Application.Features.Article.UseCases.*` may legitimately depend on it (Application already references the Domain assembly in `GetArticleHandler.cs:2` and elsewhere). No new cross-module edges are introduced.
- **Integration points** — Three edges matter: (1) the JsonStringEnumConverter, (2) the NSwag OpenAPI export feeding the TS generator, (3) the React Query polling logic in `useArticles.ts` that depends on `status` being a real `ArticleStatus` enum string at runtime. The change strengthens (3) by promoting the runtime guarantee into a compile-time guarantee.

One important point not fully reflected in the spec: **the existing handler tests assert against the `string` form of the property** (e.g., `response.Status.Should().Be(nameof(ArticleStatus.Generated))` in `GetArticleHandlerTests.cs:96`, and `Be(nameof(ArticleStatus.Generated))` / `Be(nameof(ArticleStatus.Queued))` in `ListArticlesHandlerTests.cs:51,53`). After retyping the property to `ArticleStatus`, those `.Should().Be(string)` calls will compare an enum against a string and fail compilation. These call sites must be updated as part of FR-1/FR-2, not left for FR-5.

## Proposed Architecture

### Component Overview

```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/
├── GetArticle/
│   ├── GetArticleResponse.cs        (* change Status: string -> ArticleStatus)
│   └── GetArticleHandler.cs         (* drop .ToString() at line 42)
├── ListArticles/
│   ├── ArticleListItemDto.cs        (* change Status: string -> ArticleStatus)
│   └── ListArticlesHandler.cs       (* drop .ToString() at line 32)
└── GenerateArticle/
    └── GenerateArticleResponse.cs   (unchanged — already correct, used as reference)

backend/src/Anela.Heblo.Domain/Features/Article/
└── ArticleStatus.cs                 (unchanged: enum { Queued, Researching, Writing, Generated, Failed })

backend/src/Anela.Heblo.API/Program.cs:121
└── JsonStringEnumConverter          (unchanged — load-bearing for wire format)

backend/test/Anela.Heblo.Tests/Article/UseCases/
├── GetArticleHandlerTests.cs        (* update assertion at line 96)
├── ListArticlesHandlerTests.cs      (* update assertions at lines 51, 53)
└── (new) ArticleStatusWireFormatTests.cs     (FR-5 — serialization snapshot)

frontend/src/api/generated/api-client.ts
└── status?: ArticleStatus           (regenerated for both DTOs; do not hand-edit)

frontend/src/api/hooks/useArticles.ts
├── line 139 — remove `as ArticleStatus`
└── line 162 — remove `as ArticleStatus`
       (keep `?? ArticleStatus.Queued` because NSwag emits `status?: ArticleStatus | undefined`)
```

### Key Design Decisions

#### Decision 1: Keep the global `JsonStringEnumConverter` as the single serialization authority
**Options considered:**
- (A) Per-DTO `[JsonConverter(typeof(JsonStringEnumConverter))]` attributes.
- (B) Hand-rolled mapping in each handler.
- (C) Continue to rely on the global converter registered in `Program.cs:121`.

**Chosen approach:** (C).

**Rationale:** The global registration already governs `GenerateArticleResponse.Status` and every other enum that crosses the API boundary. Adding per-DTO attributes duplicates intent and creates two places to keep in sync; hand-rolled mapping in handlers is what we are removing. The spec NFR-1 (byte-identical wire format) is satisfied automatically by reusing the existing converter.

#### Decision 2: Keep the `?? ArticleStatus.Queued` fallback in `useArticles.ts`
**Options considered:**
- (A) Drop the fallback because the property is "always present" in practice.
- (B) Keep the fallback because NSwag emits the property as `status?: ArticleStatus | undefined`.

**Chosen approach:** (B).

**Rationale:** Inspection of the generated client (`api-client.ts:12872` today, equivalent for `GenerateArticleResponse` at `12745`) shows NSwag emits enum properties as optional (`status?: ArticleStatus`). The corresponding `IGetArticleResponse` interface and `init` method also mark `status` optional, so even after regeneration the static type is `ArticleStatus | undefined`. Removing the fallback would force a non-null assertion or a `if` guard at the call site. The existing fallback is correct and trivially safe — keep it. This matches FR-4's conditional clause.

#### Decision 3: Update existing handler tests in the same commit as the DTO change
**Options considered:**
- (A) Defer test updates and treat compile failures as a follow-up.
- (B) Update `GetArticleHandlerTests.cs:96` and `ListArticlesHandlerTests.cs:51,53` together with the DTO type change so the solution always builds.

**Chosen approach:** (B).

**Rationale:** Those assertions reference the property directly; once `Status` is an enum, `Should().Be(nameof(ArticleStatus.Generated))` becomes a type mismatch and `dotnet build`/`dotnet test` will fail. The spec's FR-1/FR-2 acceptance criteria require a clean build, so the test fixes are part of those FRs, not optional cleanup.

#### Decision 4: Implement FR-5 as a focused serialization test, not an end-to-end HTTP test
**Options considered:**
- (A) Add a `WebApplicationFactory<Program>`-based integration test that hits `/api/articles/{id}` and inspects the JSON string.
- (B) Add a focused unit test that serializes a `GetArticleResponse` / `ArticleListItemDto` instance using the same `JsonSerializerOptions` configuration the API uses (`new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }`).

**Chosen approach:** (B).

**Rationale:** The spec's intent (FR-5) is to lock the wire format so a future regression in the DTO or the converter registration is caught at compile/test time. A focused serializer test does exactly that with no infrastructure overhead, mirroring the pattern already established in `SmartsuppWebhookAuditControllerTests.cs:15-19`. An HTTP-level test would also catch the regression but is heavier and overlaps with existing handler coverage. The test should serialize a representative instance and assert the substring `"status":"Generated"` (and a `Queued` case to guard against the enum-zero default).

## Implementation Guidance

### Directory / Module Structure
No new directories or files except the FR-5 test:

- **Modify:**
  - `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticle/GetArticleResponse.cs` (note: spec says `Articles/` — actual folder is `Article/`).
  - `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticle/GetArticleHandler.cs`
  - `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ArticleListItemDto.cs`
  - `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs`
  - `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleHandlerTests.cs` (assertion on `Status`)
  - `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs` (assertions on `Status`)
  - `frontend/src/api/generated/api-client.ts` (auto-regenerated; commit the change)
  - `frontend/src/api/hooks/useArticles.ts` (drop two `as ArticleStatus` casts)

- **Create:**
  - `backend/test/Anela.Heblo.Tests/Article/UseCases/ArticleStatusWireFormatTests.cs` (FR-5).

### Interfaces and Contracts

The C# DTO surface becomes:

```csharp
// GetArticleResponse.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;   // add this using

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticle;

public sealed class GetArticleResponse : BaseResponse
{
    // ...
    public ArticleStatus Status { get; set; }   // was: string Status { get; set; } = string.Empty;
    // ...
}

// ArticleListItemDto.cs
using Anela.Heblo.Domain.Features.Article;    // add this using

namespace Anela.Heblo.Application.Features.Article.UseCases.ListArticles;

public sealed class ArticleListItemDto
{
    // ...
    public ArticleStatus Status { get; set; }   // was: string Status { get; set; } = string.Empty;
    // ...
}
```

The TS contract surface (post-regeneration) becomes:

```typescript
// api-client.ts (generated)
export class GetArticleResponse extends BaseResponse implements IGetArticleResponse {
  status?: ArticleStatus;   // was: status?: string
  // ...
}
export class ArticleListItemDto implements IArticleListItemDto {
  status?: ArticleStatus;   // was: status?: string
  // ...
}
```

The HTTP/JSON contract for `GET /api/articles/{id}` and `GET /api/articles` is unchanged: the body still contains `"status": "<EnumName>"` produced by the global `JsonStringEnumConverter`. This is the project's existing convention as proven by `GenerateArticleResponse.Status` already shipping in this form.

### Data Flow

`GET /api/articles/{id}`:

```
Controller → MediatR
   → GetArticleHandler.Handle
       → IArticleRepository.GetByIdAsync(id, ct)
       → returns new GetArticleResponse { ..., Status = article.Status, ... }   (* enum, not .ToString() *)
   → ASP.NET MVC pipeline serializes via JsonSerializerOptions registered in Program.cs:119-122
       → JsonStringEnumConverter writes "status":"Generated"   (unchanged wire format)

Browser
   → useGetArticleQuery
       → client.articles_GetById(id)        (generated, response.status: ArticleStatus | undefined)
       → maps into ArticleDetail.status (no cast; fallback `?? ArticleStatus.Queued` retained)
   → refetchInterval reads ArticleDetail.status; IN_PROGRESS_STATUSES.has(status) still type-checks.
```

`GET /api/articles` follows the identical pattern via `ListArticlesHandler` → `ArticleListItemDto[]` → `useListArticlesQuery` → `ArticleListItem[]`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing handler tests fail to compile after the DTO type change (assertions compare enum to string at `GetArticleHandlerTests.cs:96`, `ListArticlesHandlerTests.cs:51,53`). | High | Update those assertions to `Should().Be(ArticleStatus.Generated)` / `Be(ArticleStatus.Queued)` in the same change set. Add to spec acceptance criteria for FR-1/FR-2. |
| TS client regeneration is skipped (developer forgets, or runs Release build), leaving the frontend casts and the new backend types out of sync. | Medium | Per `docs/development/api-client-generation.md`, the C# client auto-generates only in Debug. Ensure the developer runs the documented full regen flow for the TS client and commits `api-client.ts`. CI should fail if `api-client.ts` is out of date — confirm during implementation. |
| The `?? ArticleStatus.Queued` fallback gets dropped under the mistaken belief that the new generated type is non-nullable; this would break `refetchInterval` when the response is in-flight. | Medium | FR-4's conditional clause already covers this; the implementer MUST inspect the generated `status?:` declaration after regen and keep the fallback because NSwag still emits it as optional. |
| Wire-format silently drifts in the future if the global `JsonStringEnumConverter` is removed or replaced with `JsonNumericEnumConverter`. | Low | FR-5 serialization snapshot test catches this. Test must use the same `JsonSerializerOptions` shape registered in `Program.cs:119-122` so it accurately reflects production. |
| Polling logic (`IN_PROGRESS_STATUSES.has(status)` at `useArticles.ts:191`) breaks if the cast removal is done before the TS client is regenerated. | Low | Sequence the change correctly: backend DTO change → run TS regen → only then drop casts. A failing `npm run build` will surface the issue immediately. |
| Spec references namespace `Anela.Heblo.Application.Features.Articles` (plural) while the codebase uses `Article` (singular). Implementer searches by spec path and finds nothing. | Low | See Specification Amendments below. |

## Specification Amendments

1. **Path/namespace correction (cosmetic but required for navigation):** The spec consistently writes `Features/Articles/UseCases/...` and `Anela.Heblo.Application.Features.Articles.UseCases.*`. The actual folder and namespace are singular: `Features/Article/UseCases/...` and `Anela.Heblo.Application.Features.Article.UseCases.*`. Line numbers in the spec are correct. Implementer should use the singular path everywhere.

2. **Add to FR-1 acceptance criteria:** "`backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleHandlerTests.cs:96` is updated from `Should().Be(nameof(ArticleStatus.Generated))` to `Should().Be(ArticleStatus.Generated)`. A `using Anela.Heblo.Domain.Features.Article;` directive must be present in the test file (it already is)."

3. **Add to FR-2 acceptance criteria:** "`backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs:51` and `:53` are updated from `Should().Be(nameof(ArticleStatus.<X>))` to `Should().Be(ArticleStatus.<X>)`."

4. **Tighten FR-4 wording:** Inspection of the current generated client (and of `GenerateArticleResponse` at `api-client.ts:12745`) confirms NSwag emits these properties as `status?: ArticleStatus | undefined`. Therefore the fallback `?? ArticleStatus.Queued` MUST be retained. The conditional phrasing in FR-4 should be replaced with: "Retain `?? ArticleStatus.Queued` — the generated type is `ArticleStatus | undefined`."

5. **Clarify FR-5 scope:** The test should use the same `JsonSerializerOptions` configuration registered in `Program.cs:119-122` (specifically, an options instance with `new JsonStringEnumConverter()` in `Converters`). A focused unit test serializing an instance of each DTO is sufficient; an integration test through `WebApplicationFactory<Program>` is not required. Pattern reference: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditControllerTests.cs:15-19`.

6. **Sequencing note (workflow, not contract):** The implementation order must be (1) backend DTO + handler + handler-test fixes, (2) `dotnet build` in Debug to regenerate the C# API client, (3) frontend client regeneration per `docs/development/api-client-generation.md`, (4) drop the two `as ArticleStatus` casts in `useArticles.ts`, (5) add FR-5 wire-format test. Doing (4) before (3) will fail `npm run build` because `(item.status as ArticleStatus)` is currently the bridge.

## Prerequisites

None. All required infrastructure exists today:

- `JsonStringEnumConverter` registration at `backend/src/Anela.Heblo.API/Program.cs:121`.
- NSwag-driven TS client generation pipeline documented in `docs/development/api-client-generation.md`.
- `ArticleStatus` enum at `backend/src/Anela.Heblo.Domain/Features/Article/ArticleStatus.cs`.
- `GenerateArticleResponse` already demonstrates the target pattern and is round-tripping correctly today, so wire-format risk is effectively zero.

No migrations, no config, no infrastructure changes required.