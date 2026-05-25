**READY TO MERGE** — final reviewer approved with no issues.

---

# Implementation: Complete GenerateArticleResponse Contract

## What was implemented
Extended `GenerateArticleResponse` with `HangfireJobId` (string?) and `Status` (ArticleStatus) properties, changed `ArticleId` from `Guid` to `Guid?`, and updated `GenerateArticleHandler` to capture the Hangfire job ID returned by `IBackgroundJobClient.Enqueue<T>`. The TypeScript OpenAPI client was regenerated to expose the two new fields.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleResponse.cs` — Added `HangfireJobId` (string?), `Status` (ArticleStatus), changed `ArticleId` to `Guid?`, XML doc on `Status`, added domain using
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs` — Captured `Enqueue<T>` return into `var jobId`; response now includes `HangfireJobId = jobId` and `Status = ArticleStatus.Queued`
- `backend/test/Anela.Heblo.Tests/Article/UseCases/GenerateArticleHandlerTests.cs` — Updated `Guid?` assertions in happy-path test; added `Handle_HappyPath_ReturnsHangfireJobIdAndQueuedStatus` test mocking `IBackgroundJobClient.Create`
- `frontend/src/api/generated/api-client.ts` — Regenerated; `GenerateArticleResponse` and `IGenerateArticleResponse` now expose `hangfireJobId?: string` and `status?: ArticleStatus`

## Tests
- `Handle_HappyPath_CreatesArticleWithMappedFields` — updated for `Guid?` nullability
- `Handle_AnonymousUser_RequestedByIsNull` — unchanged
- `Handle_PersistsAndEnqueuesHangfireJob` — unchanged
- `Handle_HappyPath_ReturnsHangfireJobIdAndQueuedStatus` — **new**: mocks `Create` to return "job-123", asserts `HangfireJobId`, `Status == Queued`, `ArticleId` non-null

All 4 tests pass. 50 article-suite tests pass.

## How to verify
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateArticleHandlerTests"
```
Commit hash: `3e5e362b`

## Notes
- TypeScript client required explicit regeneration via `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` (not just `dotnet build`)
- Quality reviewer suggested renaming `HangfireJobId` → `JobId` and removing the XML doc; both were rejected as they conflict with explicit spec requirements

## PR Summary
Complete the `GenerateArticleResponse` contract defined in the article-generation feature spec. The response from `POST /api/Articles/generate` now carries `HangfireJobId` and `Status: Queued` alongside the existing `ArticleId`, so the frontend can correlate the request with its Hangfire background job and start the polling loop without inferring initial state.

The only handler change is capturing the `string` job ID already returned synchronously by `IBackgroundJobClient.Enqueue<T>` and assigning it to the response. `ArticleId` becomes `Guid?` to match the `BaseResponse` envelope convention used throughout the codebase (null on failure, populated on success). The TypeScript OpenAPI client was regenerated and the two new optional fields appear on `GenerateArticleResponse` / `IGenerateArticleResponse`.

### Changes
- `GenerateArticleResponse.cs` — Added `HangfireJobId` (string?), `Status` (ArticleStatus), changed `ArticleId` to Guid?, XML doc on Status
- `GenerateArticleHandler.cs` — Capture `Enqueue<T>` return into `var jobId`; populate `HangfireJobId` and `Status` on response
- `GenerateArticleHandlerTests.cs` — Update Guid? assertions; add `Handle_HappyPath_ReturnsHangfireJobIdAndQueuedStatus` test
- `frontend/src/api/generated/api-client.ts` — Regenerated with `hangfireJobId` and `status` on response type

## Status
DONE