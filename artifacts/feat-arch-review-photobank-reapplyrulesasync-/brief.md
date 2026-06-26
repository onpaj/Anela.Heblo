## Module
Photobank

## Finding
`PhotobankRepository.ReapplyRulesAsync` (`backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankRepository.cs`, lines 275–372) is a ~100-line method that performs application-layer orchestration rather than data access:

- Loads **all** photos into memory: `await _context.Photos.ToListAsync(...)` (line 328)
- Calls domain logic directly: `TagRuleMatcher.GetMatchingTags(photo.FolderPath, photo.FileName, activeRules)` (line 336)
- Creates missing tags on the fly (lines 316–327)
- Maintains deduplication hash sets (`addedPairs`, `occupiedSet`)
- Decides which source wins (Rule vs Manual/AI) and constructs `PhotoTag` entities accordingly

The `ReapplyRulesHandler` (`UseCases/ReapplyRules/ReapplyRulesHandler.cs`) is only 16 lines because it has delegated its entire responsibility to the repository. The handler calls the repository and calls `SaveChangesAsync` — that's it.

## Why it matters
- The repository layer is supposed to be a thin data-access primitive, not an orchestration service. This violates SRP and the principle that *"business logic should be in MediatR handlers"*.
- `TagRuleMatcher` (a domain object) is called from inside the repository, creating an unnatural Domain → Repository dependency chain.
- Loading the entire `Photos` table into memory for every rule re-apply is a scalability concern that the handler layer should be able to control (e.g., by batching) — but it can't when the logic is buried in the repository.
- The method is untestable in isolation: you cannot test the matching logic without an EF DbContext.

## Suggested fix
Move the orchestration loop into `ReapplyRulesHandler` and expose only raw primitives from the repository:

```csharp
// New repository primitives:
Task<List<Photo>> GetAllPhotosAsync(CancellationToken ct);
Task AddPhotoTagsAsync(IEnumerable<PhotoTag> tags, CancellationToken ct);
Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken ct);
```

The handler then:
1. Calls `GetRulesAsync` and `GetAllPhotosAsync`
2. Runs `TagRuleMatcher.GetMatchingTags` per photo
3. Resolves/creates tags, builds `PhotoTag` entities
4. Calls `AddPhotoTagsAsync` + `SaveChangesAsync`

`ReapplyRulesAsync` is removed from the repository interface and implementation entirely.

---
_Filed by daily arch-review routine on 2026-05-21._