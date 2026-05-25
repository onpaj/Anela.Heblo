All tasks complete. Writing the output artifact.

---

# Implementation: GraphService HttpClientFactory Migration

## What was implemented

Replaced the raw `new HttpClient()` instantiation in `GraphService.GetGroupMembersAsync` with an `IHttpClientFactory`-provided client. The change eliminates socket exhaustion and DNS-staleness risks against `graph.microsoft.com` while preserving all observable behavior (caching, token acquisition, response parsing, error branches). The implementation follows the arch-review's decisions: literal `"MicrosoftGraph"` (no shared constant), per-request `HttpRequestMessage` for headers (defensive against future multi-call scenarios), and no `using` on the factory-provided client.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — added `_httpClientFactory` field, `IHttpClientFactory` 4th constructor parameter, replaced `new HttpClient()` with `_httpClientFactory.CreateClient("MicrosoftGraph")`, switched from `GetAsync`/`DefaultRequestHeaders` to `SendAsync` with per-request `HttpRequestMessage`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — added `services.AddHttpClient("MicrosoftGraph")` in production branch before `GraphService` registration
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — new file with 12 tests covering constructor, cache-hit, cache-miss + factory verification, DI registration (both branches), all error branches, and no-dispose invariant

## Tests

`backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — 12 tests:
- `Constructor_AcceptsIHttpClientFactory_AsFourthParameter`
- `GetGroupMembersAsync_CacheMiss_InvokesFactory_AndReturnsParsedUsers`
- `GetGroupMembersAsync_CacheHit_DoesNotInvokeFactory`
- `AddUserManagement_ProductionBranch_RegistersMicrosoftGraphNamedClient_AndResolvesGraphService`
- `AddUserManagement_MockBranch_RegistersMockGraphService`
- `GetGroupMembersAsync_TokenAcquisitionMsalException_ReturnsEmptyList_AndDoesNotInvokeFactory`
- `GetGroupMembersAsync_GraphReturnsNonSuccess_ReturnsEmptyList`
- `GetGroupMembersAsync_TransportThrows_ReturnsEmptyList`
- `GetGroupMembersAsync_EmptyGroupId_ReturnsEmptyList_WithoutTouchingFactory`
- `GetGroupMembersAsync_DoesNotDispose_FactoryProvidedClient`

All 3909 tests in the solution pass.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GraphServiceTests" --nologo --verbosity minimal
dotnet build --nologo --verbosity minimal
dotnet format --verify-no-changes --verbosity minimal
```

## Notes

- The arch-review's amendment to use per-request `HttpRequestMessage` (Decision 2) was adopted — `request.Headers.Authorization` instead of `httpClient.DefaultRequestHeaders.Authorization`.
- The arch-review's amendment to use the literal `"MicrosoftGraph"` without a shared constant (Decision 1) was adopted — both `GraphService.cs` and `UserManagementModule.cs` have a one-line cross-reference comment pointing to the sister modules.
- `MockGraphService` and `IGraphService` are unchanged.
- Known follow-up (out of scope): five other modules independently register `AddHttpClient("MicrosoftGraph")` with potentially conflicting handler configs (`PhotobankModule` adds `AllowAutoRedirect`). Consolidating into a shared `AddMicrosoftGraphHttpClient()` extension is recommended as a separate task.

## PR Summary

Migrates `GraphService` from raw `new HttpClient()` to `IHttpClientFactory` to eliminate socket exhaustion and DNS-staleness against `graph.microsoft.com`. This closes the only remaining raw-HttpClient caller in the UserManagement module, aligning it with the five sister Graph-consuming modules (Marketing, MeetingTasks, CatalogDocuments, KnowledgeBase, Photobank).

The change is a three-line production diff: inject the factory, call `CreateClient("MicrosoftGraph")`, switch to per-request `HttpRequestMessage`. All caching, token acquisition, error handling, and response parsing are preserved verbatim.

### Changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Services/GraphService.cs` — added `IHttpClientFactory` constructor dependency, replaced `new HttpClient()` + `DefaultRequestHeaders.Authorization` + `GetAsync` with `CreateClient` + per-request `HttpRequestMessage` + `SendAsync`
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — added `services.AddHttpClient("MicrosoftGraph")` in the production branch
- `backend/test/Anela.Heblo.Tests/Features/UserManagement/GraphServiceTests.cs` — new test class with 12 tests covering the constructor wiring, cache-hit/miss paths, DI registration for both branches, all error paths, and the no-dispose invariant

## Status
DONE