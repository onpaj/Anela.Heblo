## Module
Article

## Finding
`BackfillArticleRequestedByHandler` in the Article module has a direct compile-time dependency on `IGraphService`, which is owned by the UserManagement module:

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs:1
using Anela.Heblo.Application.Features.UserManagement.Services;
```

The handler injects `IGraphService` (line 16) and calls `_graph.GetGroupMembersAsync(request.GroupId, ct)` (line 37). `IGraphService` is declared in `Anela.Heblo.Application.Features.UserManagement.Services.IGraphService` — a type owned and registered by the UserManagement module.

This violation is **not covered** by the existing `ModuleBoundariesTests.cs`. The architecture test file (`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`) has an `Article → KnowledgeBase` rule but no `Article → UserManagement` rule, so this cross-module reference would pass CI undetected.

## Why it matters
The development guidelines and the cross-module communication pattern (`ILeafletKnowledgeSource`) are explicit: the consuming module must define its own contract interface and have the providing module implement an adapter. Directly importing `IGraphService` creates a hard coupling between Article and UserManagement:
- A rename or refactor of `IGraphService` in UserManagement breaks the Article module at compile time.
- The Article module gains an implicit dependency on UserManagement's DI registration (`AddUserManagementModule` must register `IGraphService` before the Article backfill command works).
- The boundary is invisible to `ModuleBoundariesTests`, so future regressions in this direction won't fail CI.

## Suggested fix

1. **Define a consumer-owned contract in Article's `Contracts/` folder:**
   ```csharp
   // backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs
   public interface IArticleUserResolver
   {
       Task<List<ArticleUserMatch>> ResolveByGroupAsync(string groupId, CancellationToken ct);
   }
   public record ArticleUserMatch(string Id, string DisplayName);
   ```

2. **Have UserManagement provide an adapter** (in `UserManagement/Infrastructure/`):
   ```csharp
   public sealed class GraphArticleUserResolver : IArticleUserResolver
   {
       private readonly IGraphService _graph;
       public GraphArticleUserResolver(IGraphService graph) => _graph = graph;
       public async Task<List<ArticleUserMatch>> ResolveByGroupAsync(string groupId, CancellationToken ct)
       {
           var members = await _graph.GetGroupMembersAsync(groupId, ct);
           return members.Select(m => new ArticleUserMatch(m.Id, m.DisplayName)).ToList();
       }
   }
   ```

3. **Register the binding in `UserManagementModule.cs`:** `services.AddScoped<IArticleUserResolver, GraphArticleUserResolver>();`

4. **Add an `Article → UserManagement` rule to `ModuleBoundariesTests.cs`** (no allowlist entries needed after the fix).

---
_Filed by daily arch-review routine on 2026-05-27._