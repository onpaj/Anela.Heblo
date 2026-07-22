## Module
Photobank

## Finding
`ReapplyRulesHandler.Handle` calls `GetAllPhotosAsync` (line 65), which executes `_context.Photos.ToListAsync()` with no pagination or filter, materialising the entire photos table into application memory.

File: `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/ReapplyRules/ReapplyRulesHandler.cs`, line 65
Repository method: `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`, line 147

```csharp
// ReapplyRulesHandler.cs:65
var photos = await _repository.GetAllPhotosAsync(cancellationToken);
// PhotobankRepository.cs:147-149
public async Task<List<Photo>> GetAllPhotosAsync(CancellationToken cancellationToken)
{
    return await _context.Photos.ToListAsync(cancellationToken);
}
```

## Why it matters
Each `Photo` row carries `FileName`, `FolderPath`, `SharePointWebUrl`, `FileSizeBytes`, and navigation properties. At 50 k photos this is tens of MB per request; at 200 k it is hundreds of MB — enough to cause GC pressure or OOM on the container. The `/api/photobank/settings/rules/reapply` endpoint is admin-only and infrequent, but still runs synchronously in the request thread with no timeout guard.

The `ReapplyRulesRequest` already supports a `RuleId` scope that limits tag deletions; the load of all photos is always full regardless of scope.

## Suggested fix
Process photos in batches instead of loading all at once. Replace `GetAllPhotosAsync` with a paginated variant and loop:

```csharp
// New repository method (returns only the fields needed):
Task<List<PhotoLocator>> GetPhotoLocatorsPageAsync(
    int offset, int pageSize, CancellationToken ct);
```

Accumulate `newPhotoTags` across pages, call `SaveChangesAsync` once after the loop. For the `RuleId`-scoped path the page count is typically small, so the change is low-risk.

---
_Filed by daily arch-review routine on 2026-07-18._
