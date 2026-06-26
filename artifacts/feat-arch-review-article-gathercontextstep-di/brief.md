## Module
Article

## Finding
`GatherContextStep.cs:1` imports:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
```

and at line 17 injects:

```csharp
private readonly IOneDriveService _oneDrive;
```

`IOneDriveService` is defined in `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/`. The Article module directly depends on a service interface owned by the KnowledgeBase module without going through a consumer-owned contract.

The development guidelines (`docs/architecture/development_guidelines.md`) are explicit:

> Communication between modules **exclusively through `contracts/`** (e.g., `IProductQueryService`)

> **Cross-Module Communication Pattern (ILeafletKnowledgeSource):** The consumer (A) defines the contract. Module A declares an interface in its own `Contracts/` folder... The provider (B) implements the contract via an adapter... A reflection-based test in `ModuleBoundariesTests.cs` enforces that types contain no references to the other module's namespaces.

Using `KnowledgeBase.Services.IOneDriveService` directly in the Article module creates a hard compile-time dependency on the KnowledgeBase namespace, which the architecture tests are designed to prevent.

## Why it matters
- Violates the module-boundary invariant that the architecture tests enforce for other cross-module relationships.
- If KnowledgeBase module reorganises or renames `IOneDriveService` (e.g., moves it to a shared infrastructure adapter), the Article module breaks silently.
- Prevents future extraction of either module into a separate deployable without refactoring Article.
- The `ILeafletKnowledgeSource` pattern exists in this codebase precisely for this scenario — it is the documented pattern to follow.

## Suggested fix
Apply the same pattern used for `ILeafletKnowledgeSource`:

1. **Article module defines its own contract** in `Application/Features/Article/Contracts/IArticleStyleGuideSource.cs`:
   ```csharp
   public interface IArticleStyleGuideSource
   {
       Task<string> DownloadStyleGuideTextAsync(string driveId, string path, CancellationToken ct);
   }
   ```

2. **KnowledgeBase (or a shared adapter) implements it** in its `Infrastructure/` folder:
   ```csharp
   public class OneDriveArticleStyleGuideSource : IArticleStyleGuideSource
   {
       private readonly IOneDriveService _oneDrive;
       public Task<string> DownloadStyleGuideTextAsync(string driveId, string path, CancellationToken ct)
           => _oneDrive.DownloadFileTextByPathAsync(driveId, path, ct);
   }
   ```

3. **KnowledgeBase module registers the binding** in its `KnowledgeBaseModule.cs`.

4. **`GatherContextStep` injects `IArticleStyleGuideSource`** instead of `IOneDriveService`.

5. **Add a module-boundary architecture test** for Article → KnowledgeBase, mirroring `ModuleBoundariesTests.cs`.

---
_Filed by daily arch-review routine on 2026-05-25._