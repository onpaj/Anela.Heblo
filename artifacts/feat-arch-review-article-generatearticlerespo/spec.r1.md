# Specification: Complete GenerateArticleResponse Contract

## Summary
The `POST /api/Articles/generate` endpoint currently returns only `ArticleId`, omitting the `HangfireJobId` and `Status` fields required by the article-generation feature spec. This change extends `GenerateArticleResponse` with the two missing fields and captures the Hangfire job ID in the handler so clients can correlate the request with the background job and begin polling immediately.

## Background
The article-generation feature (see `docs/features/article-generation.md`, section 7) defines a three-field response contract for the generate endpoint: `ArticleId`, `HangfireJobId`, and `Status`. The current implementation in `GenerateArticleResponse.cs` exposes only `ArticleId`, and `GenerateArticleHandler.cs:53` discards the string job ID returned by `IBackgroundJobClient.Enqueue<T>(...)`.

Consequences of the gap:
- **No job correlation.** Clients and operators cannot map a generate request to its Hangfire job without polling the article record. With `HangfireJobId`, operators can open the job in the Hangfire dashboard, inspect failures, or cancel programmatically.
- **Frontend must infer state.** Without `Status: Queued` in the response, the frontend has to assume the initial state to start its polling loop. Explicit status removes the inference.
- **Acceptance criterion unmet.** The feature's own acceptance criterion explicitly names the three-field response shape; shipping without it means the feature does not satisfy its definition of done.

This is a contract-completion fix, not a redesign. The handler logic, persistence, and job dispatch behavior are unchanged — only the response shape and a single discarded return value are touched.

## Functional Requirements

### FR-1: Extend `GenerateArticleResponse` with `HangfireJobId` and `Status`
`GenerateArticleResponse` must expose the two additional properties defined in the feature spec.

**Final shape:**
```csharp
public sealed class GenerateArticleResponse : BaseResponse
{
    public Guid? ArticleId { get; set; }
    public string? HangfireJobId { get; set; }
    public ArticleStatus Status { get; set; }
}
```

Notes:
- `ArticleId` becomes nullable to match the spec's contract (it remains populated on success; nullability accommodates failure responses where `BaseResponse.Success == false`).
- `HangfireJobId` is nullable for the same reason — on validation/business-rule failure, no job is enqueued.
- `Status` is non-nullable. On success it is `ArticleStatus.Queued`. On failure the handler returns a `BaseResponse`-style error response; `Status` defaults to its enum zero value, which callers must treat as meaningful only when `Success == true`.
- The class remains a **plain class, not a C# record** (per project rule: DTOs are classes; the OpenAPI generator mishandles record parameter order).

**Acceptance criteria:**
- `GenerateArticleResponse` contains `ArticleId` (nullable `Guid?`), `HangfireJobId` (nullable `string?`), and `Status` (`ArticleStatus`).
- The class is declared `class`, not `record`.
- The regenerated TypeScript OpenAPI client exposes all three fields on the response type.

### FR-2: Capture Hangfire job ID in `GenerateArticleHandler`
The handler must capture the return value of `IBackgroundJobClient.Enqueue<T>(...)` and include it in the response.

**Target handler code (replacing the discarded-return-value site):**
```csharp
var jobId = _backgroundJobClient.Enqueue<GenerateArticleJob>(
    j => j.RunAsync(article.Id, CancellationToken.None));

return new GenerateArticleResponse
{
    ArticleId = article.Id,
    HangfireJobId = jobId,
    Status = ArticleStatus.Queued,
};
```

**Acceptance criteria:**
- The return value of `_backgroundJobClient.Enqueue<GenerateArticleJob>(...)` is assigned to a local variable and passed into the response.
- On the success path, the response contains a non-null `ArticleId`, a non-null `HangfireJobId`, and `Status == ArticleStatus.Queued`.
- Failure paths (validation errors, business-rule violations) continue to return the existing `BaseResponse`-style error response; they do not enqueue a job and do not populate `HangfireJobId`.

### FR-3: Endpoint contract conformance
`POST /api/Articles/generate` must return the full three-field JSON payload on success.

**Example success response:**
```json
{
  "articleId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "hangfireJobId": "12345",
  "status": "Queued",
  "success": true
}
```

**Acceptance criteria:**
- A successful call returns HTTP 200 with all three fields populated and `Status == "Queued"`.
- Response time stays within the existing 500ms target (this change adds no work — it only surfaces a value already returned by `Enqueue`).

### FR-4: Test coverage
Existing handler unit tests and integration tests must be updated to assert the new contract.

**Acceptance criteria:**
- Handler unit test asserts `response.HangfireJobId` equals the value returned by the mocked `IBackgroundJobClient.Enqueue<GenerateArticleJob>(...)`.
- Handler unit test asserts `response.Status == ArticleStatus.Queued` on the success path.
- Any existing controller/integration test for `POST /api/Articles/generate` is updated to assert the three-field shape.
- All affected tests pass (`dotnet build` + targeted `dotnet test`).

## Non-Functional Requirements

### NFR-1: Performance
No new I/O, allocations, or background work introduced. The change reads a value already returned synchronously by `IBackgroundJobClient.Enqueue` and assigns it to a property. The endpoint must continue to respond within the existing 500ms target.

### NFR-2: Backwards compatibility
The change is **additive** on the wire: two new optional fields appear on the response. Existing TypeScript/C# clients that only read `articleId` continue to work after regenerating the OpenAPI client. No API version bump required.

### NFR-3: OpenAPI client regeneration
The TypeScript OpenAPI client is auto-generated on build (per `docs/development/api-client-generation.md`). The build must regenerate the client so the frontend type for `GenerateArticleResponse` includes `hangfireJobId` and `status`.

### NFR-4: Code style
- Property names follow project C# conventions (PascalCase on the server; the OpenAPI generator handles camelCase on the wire).
- Local `jobId` variable uses `var` (matches surrounding handler style).
- No comments added unless something non-obvious requires explanation; the change is mechanical.

## Data Model

No database schema or domain entity changes. `ArticleStatus` is an existing enum whose `Queued` member is already used elsewhere in the article-generation flow. `Article.Id` (the `Guid` returned as `ArticleId`) is unchanged.

**Touched contract type:**
- `GenerateArticleResponse : BaseResponse` — gains two properties; `ArticleId` becomes nullable.

## API / Interface Design

**Endpoint:** `POST /api/Articles/generate` (unchanged route, method, and request body).

**Response shape (success):**

| Field           | Type              | Nullable | Notes                                                                |
|-----------------|-------------------|----------|----------------------------------------------------------------------|
| `articleId`     | `Guid`            | Yes      | Populated on success; null on failure.                               |
| `hangfireJobId` | `string`          | Yes      | Hangfire job identifier returned by `IBackgroundJobClient.Enqueue`.  |
| `status`        | `ArticleStatus`   | No       | `Queued` on success. Treat as meaningful only when `success == true`. |
| `success`       | `bool`            | No       | From `BaseResponse`.                                                 |
| `message`       | `string`          | Yes      | From `BaseResponse` (existing).                                      |

**Response shape (failure):** unchanged from current behavior. `ArticleId` and `HangfireJobId` are null; `Status` defaults to the enum zero value but is not semantically meaningful when `success == false`.

## Dependencies

- **Hangfire** (`IBackgroundJobClient`) — already integrated; this change only consumes its existing return value.
- **`ArticleStatus` enum** — already defined; `Queued` member already used in the article-generation flow.
- **OpenAPI client generation** — runs on build; no configuration change needed.
- **Feature spec:** `docs/features/article-generation.md`, section 7 (response contract) and the named acceptance criterion.

## Out of Scope

- Cancellation endpoint or "cancel by `HangfireJobId`" UX. The job ID enables this in the future but the cancellation flow is not part of this change.
- Frontend consumption of the new fields (polling-loop adjustments, status display). Frontend work happens separately once the regenerated client is available.
- Hangfire dashboard hardening, retries, or job-lifecycle changes.
- Renaming, restructuring, or revisiting any other part of `GenerateArticleHandler` beyond the two-line capture-and-return change.
- Changes to `BaseResponse`.

## Open Questions

None.

## Status: COMPLETE