# Architecture Review: Complete GenerateArticleResponse Contract

## Skip Design: true

This is a backend-only contract completion. No new or modified UI components, screens, or visual decisions are involved. The frontend will receive the regenerated TypeScript client but that consumption is explicitly out of scope.

## Architectural Fit Assessment

The change aligns cleanly with existing patterns. The codebase already follows:

- **Vertical Slice / MediatR**: `GenerateArticleHandler` returns `GenerateArticleResponse : BaseResponse` and is dispatched via `IMediator.Send` in `ArticlesController.Generate` (`backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:29-35`).
- **`BaseResponse` envelope**: All response DTOs derive from `Anela.Heblo.Application.Shared.BaseResponse` (`Success`, `ErrorCode`, `Params`). `BaseApiController.HandleResponse` routes status codes by `ErrorCode`. The proposed extension does not perturb this contract — it only adds two payload properties.
- **DTOs as classes**: `GetArticleResponse`, `ListArticlesResponse`, and the existing `GenerateArticleResponse` are `sealed class`, not `record` — matching the project rule that DTOs must be classes for OpenAPI codegen.
- **OpenAPI client generation**: The frontend `api-client.ts` (`frontend/src/api/generated/api-client.ts:12742`) already mirrors `GenerateArticleResponse extends BaseResponse`. Adding two optional/enum properties is an additive, codegen-friendly change.
- **Hangfire job identity**: `IBackgroundJobClient.Enqueue<T>(...)` already returns a `string` job ID — the change consumes a value that exists, no new dependency.

The single integration point that requires care is the **Moq verification pattern** for `IBackgroundJobClient`: `Enqueue<T>` is an extension that delegates to `IBackgroundJobClient.Create(Job, IState)`. Existing tests mock `Create`, so test setup for returning a job ID must target the underlying `Create` method.

## Proposed Architecture

### Component Overview

```
┌───────────────┐     POST /api/Articles/generate     ┌──────────────────────┐
│  Frontend /   │ ──────────────────────────────────► │ ArticlesController   │
│  API client   │ ◄── { articleId, hangfireJobId,     │ .Generate            │
└───────────────┘        status, success } HTTP 200   └──────────┬───────────┘
                                                                 │ MediatR.Send
                                                                 ▼
                                                      ┌──────────────────────┐
                                                      │ GenerateArticle      │
                                                      │  Handler             │
                                                      └──┬────────┬──────────┘
                                                         │        │ Enqueue<T>()
                                              IArticle   │        │   returns jobId
                                              Repository │        ▼
                                                         │   ┌───────────────────────┐
                                                         │   │ IBackgroundJobClient  │
                                                         │   │ (Hangfire)            │
                                                         │   └────────┬──────────────┘
                                                         ▼            │
                                              ┌─────────────────┐     │
                                              │  Postgres       │     │ runs later
                                              │  articles row   │     ▼
                                              │  Status=Queued  │  ┌───────────────────┐
                                              └─────────────────┘  │ GenerateArticleJob│
                                                                   │  .RunAsync(id)    │
                                                                   └───────────────────┘
```

The arrow back to the caller now carries `hangfireJobId` and `status: Queued`, sourced from the existing return value of `Enqueue` and a constant respectively. No new components, no new collaborators.

### Key Design Decisions

#### Decision 1: Nullability of `ArticleId` and `HangfireJobId`

**Options considered:**
- (A) Keep `ArticleId` non-nullable, leave `HangfireJobId` non-nullable, rely on `Success == false` to indicate "ignore these".
- (B) Make both nullable (spec proposal): explicit "no value on failure" at the type level.
- (C) Introduce a discriminated success/failure shape.

**Chosen approach:** (B), exactly as the spec defines.

**Rationale:** The codebase uses the `BaseResponse` envelope where failure responses share the same DTO type as success responses. Nullable success-only fields is the established convention (e.g., `GetArticleResponse.Title`, `HtmlContent`, `GeneratedAt` are all nullable for non-terminal states). (C) would require new infrastructure for a single endpoint. (A) silently lies about `ArticleId` on failure paths. Note that changing `ArticleId` from `Guid` to `Guid?` is a source-breaking change for any C# consumer that reads `.ArticleId` into a non-nullable variable — there are none today.

#### Decision 2: `Status` is non-nullable with enum default `0 == Queued`

**Options considered:**
- (A) Keep `ArticleStatus Status` non-nullable; default zero value happens to equal `Queued` (spec).
- (B) Make `ArticleStatus? Status` nullable so failure responses do not falsely advertise `Queued`.
- (C) Add a sentinel `ArticleStatus.None = 0` and renumber the enum.

**Chosen approach:** (A), as the spec dictates.

**Rationale:** The spec is explicit. (C) is a destructive change to a shared enum already serialized to the database and TypeScript client — out of scope and not justified by this change. (B) would be cleaner semantically but the spec is locked. The "treat `Status` as meaningful only when `Success == true`" contract is acceptable because `BaseApiController.HandleResponse` does not emit a 2xx for failures — failure responses ride 4xx/5xx, so a client that branches on HTTP status will never read `Status` from a failure body in practice. **Document this invariant in code via XML doc on the `Status` property** so future readers do not misuse it.

#### Decision 3: Use Hangfire's `Enqueue` return value directly; do not wrap it

**Options considered:**
- (A) Capture the `string jobId` from `IBackgroundJobClient.Enqueue<T>` directly into the response (spec).
- (B) Introduce a thin `IArticleJobScheduler` abstraction that returns a strongly typed `JobId` value object.

**Chosen approach:** (A).

**Rationale:** YAGNI. Hangfire's job ID is a `string` everywhere in the framework, including the dashboard URL and cancellation API. Wrapping it adds a layer with zero current benefit and would force a second change when the frontend wants to call the Hangfire dashboard URL directly. A future cancellation endpoint can still accept `string jobId` cleanly.

## Implementation Guidance

### Directory / Module Structure

No new files. Edits only:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleResponse.cs` | Add `HangfireJobId` and `Status`; change `ArticleId` to `Guid?`. |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs` | Capture `Enqueue` return into `var jobId`; populate response. |
| `backend/test/Anela.Heblo.Tests/Article/UseCases/GenerateArticleHandlerTests.cs` | Mock `Create` to return a deterministic ID; assert `HangfireJobId` and `Status`; update happy-path test to assert `ArticleId.HasValue`. |
| `frontend/src/api/generated/api-client.ts` | Regenerated on build — do not hand-edit. |

### Interfaces and Contracts

```csharp
// GenerateArticleResponse.cs
namespace Anela.Heblo.Application.Features.Article.UseCases.GenerateArticle;

public sealed class GenerateArticleResponse : BaseResponse
{
    public Guid? ArticleId { get; set; }
    public string? HangfireJobId { get; set; }

    /// <summary>
    /// Initial article status. Meaningful only when <see cref="BaseResponse.Success"/> is true;
    /// on failure responses defaults to ArticleStatus.Queued (enum zero) and must be ignored.
    /// </summary>
    public ArticleStatus Status { get; set; }

    public GenerateArticleResponse() { }

    public GenerateArticleResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

This requires `using Anela.Heblo.Domain.Features.Article;` for `ArticleStatus`. Add it.

```csharp
// GenerateArticleHandler.cs — replacement for lines 53–55
var jobId = _backgroundJobClient.Enqueue<GenerateArticleJob>(
    j => j.RunAsync(article.Id, CancellationToken.None));

return new GenerateArticleResponse
{
    ArticleId = article.Id,
    HangfireJobId = jobId,
    Status = ArticleStatus.Queued,
};
```

### Data Flow

1. Controller receives `POST /api/Articles/generate` and dispatches via MediatR.
2. Handler builds a `DomainArticle` with `Status = ArticleStatus.Queued`, persists, then calls `IBackgroundJobClient.Enqueue<GenerateArticleJob>(...)`.
3. Hangfire returns the job ID (`string`) synchronously after persisting the job to its store.
4. Handler returns `{ ArticleId, HangfireJobId, Status = Queued }`.
5. `BaseApiController.HandleResponse` emits HTTP 200 with the serialized JSON envelope.
6. Hangfire later picks up the job and executes `GenerateArticleJob.RunAsync`, which advances `Article.Status` through `Researching → Writing → Generated/Failed` independently of the HTTP response.

Failure flow is unchanged: validation/business-rule errors return a `BaseResponse`-style error with `Success = false` and an `ErrorCode`. No `Enqueue` call, `ArticleId`/`HangfireJobId` remain `null`, `Status` is the enum default and must not be consumed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Mocking `Enqueue<T>` extension fails — Moq cannot intercept extension methods | **High** | Set up the underlying interface: `_backgroundJobClient.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>())).Returns("job-123");`. The existing `Handle_PersistsAndEnqueuesHangfireJob` test already uses `Verify(c => c.Create(...))` — mirror that for `Setup`. |
| `Status == Queued` on failure responses misleads consumers reading the body | Medium | XML doc on `Status` as shown above. `HandleResponse` already returns 4xx/5xx for failure, so HTTP-status-aware clients are unaffected. Spec explicitly accepts this. |
| `ArticleId` becoming `Guid?` is a source-breaking change for any C# consumer that reads it | Low | No internal C# consumer exists today (controller only forwards the DTO to JSON serialization). Grep confirmed only the handler writes `ArticleId`. |
| TypeScript client not regenerated, frontend types stale | Medium | The OpenAPI client is regenerated on build per `docs/development/api-client-generation.md`. Run a full `dotnet build` after the BE edit; verify `frontend/src/api/generated/api-client.ts` includes `hangfireJobId` and `status` on `GenerateArticleResponse`/`IGenerateArticleResponse`, then commit the regenerated file. |
| Hangfire's `Enqueue<T>` could throw if the storage is unavailable, causing an orphaned `Article` row in `Queued` state | Low | Pre-existing behavior — out of scope. Note for follow-up: a future change could wrap persist+enqueue in a unit of work or compensating action. Do NOT introduce it now. |
| Test `Handle_HappyPath_CreatesArticleWithMappedFields` asserts `response.ArticleId.Should().NotBe(Guid.Empty)` which fails to compile against `Guid?` | Low | Update the assertion to `response.ArticleId.Should().NotBeNull().And.NotBe(Guid.Empty);` or `response.ArticleId!.Value.Should().NotBe(Guid.Empty);`. |

## Specification Amendments

The spec is internally consistent and correctly grounded in the existing code. Two minor additions:

1. **Add an XML doc comment on `Status`** clarifying it is meaningful only when `Success == true`. This is a small surface change but prevents future misuse and codifies what the spec calls out in prose only. (Project rule "no comments unless non-obvious" applies — this *is* non-obvious because the type system cannot express it.)
2. **Update the FR-4 acceptance criterion** to explicitly require setting up the mock via `IBackgroundJobClient.Create` (the underlying interface method), not `Enqueue<T>` (extension). Without this, the test author may waste time discovering that Moq cannot mock the extension.

Both are additive clarifications, not direction changes. No re-planning required.

## Prerequisites

None. All dependencies already exist:

- `ArticleStatus.Queued` is defined in `backend/src/Anela.Heblo.Domain/Features/Article/ArticleStatus.cs`.
- `IBackgroundJobClient` is already injected into `GenerateArticleHandler`.
- `BaseResponse` envelope and `BaseApiController.HandleResponse` already in place.
- OpenAPI client regeneration runs on `dotnet build`.

The change is ready to implement.