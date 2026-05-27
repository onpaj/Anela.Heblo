All 7 tasks complete. Writing the output artifact.

---

# Implementation: Decouple Article Module from UserManagement's IGraphService

## What was implemented

Introduced a consumer-owned contract pattern to remove the Article module's direct compile-time dependency on UserManagement's `IGraphService`. The Article module now defines its own `IArticleUserResolver` interface; UserManagement provides an `internal sealed GraphArticleUserResolver` adapter that implements it. A new architecture boundary test prevents future regressions.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs` — New Article-owned interface + `ArticleUserMatch` sealed record
- `backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — New `internal sealed` adapter; delegates to `IGraphService`, maps `UserDto → ArticleUserMatch`
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — Swapped `IGraphService _graph` for `IArticleUserResolver _userResolver`; no behavioral changes
- `backend/src/Anela.Heblo.Application/Features/UserManagement/UserManagementModule.cs` — Added `services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>()` outside the mock/real `IGraphService` if/else
- `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — Replaced `Mock<IGraphService>` with `Mock<IArticleUserResolver>`; all 8 test cases preserved
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — Added `Article -> UserManagement` rule with Domain/Application/Persistence forbidden prefixes; empty allowlist

## Tests

- `BackfillArticleRequestedByHandlerTests.cs` — 8 tests, all passing; covers validation error, GUID/email skip, unique match, ambiguous match, unknown, dry-run, mixed set
- `ModuleBoundariesTests.cs` — New `Article -> UserManagement` theory case passes; load-bearing verification confirmed (sentinel field injection caused failure)
- Full test suite: 4064 passed, 0 failed

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BackfillArticleRequestedByHandlerTests|FullyQualifiedName~ModuleBoundariesTests"
```

Also confirm zero Article → UserManagement imports:
```bash
grep -rn "using Anela.Heblo.Application.Features.UserManagement" \
  src/Anela.Heblo.Application/Features/Article/
```

## Notes

- Adapter is `internal sealed` (not `public`) per `KnowledgeBaseLeafletSourceAdapter` precedent — only DI needs it
- DI registration placed outside the mock/real `IGraphService` if/else so it works in both auth modes
- `ArticleUserMatch` is a `sealed record` (not class) — it is internal to the application layer and never crosses the OpenAPI boundary, so the DTO-must-be-class rule does not apply
- Commit: `80c3fb82`

## PR Summary

Decouples `BackfillArticleRequestedByHandler` from `UserManagement.IGraphService` by introducing the consumer-owned contract pattern already used for `IArticleStyleGuideSource` and `ILeafletKnowledgeSource`. The Article module now defines `IArticleUserResolver` in its own `Contracts/` folder; UserManagement provides `GraphArticleUserResolver` as an `internal sealed` adapter registered once in `AddUserManagement` outside the mock/real branch. A new `ModuleBoundariesTests` rule (Article → UserManagement) enforces the boundary at CI time.

### Changes
- `Features/Article/Contracts/IArticleUserResolver.cs` — new consumer-owned contract + `ArticleUserMatch` record
- `Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs` — new `internal sealed` adapter
- `Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — swap `IGraphService` → `IArticleUserResolver`
- `Features/UserManagement/UserManagementModule.cs` — register adapter outside mock/real if/else
- `Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — swap mock to `IArticleUserResolver`
- `Tests/Architecture/ModuleBoundariesTests.cs` — add `Article -> UserManagement` boundary rule

## Status
DONE