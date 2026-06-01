## Module
Photobank

## Finding
`PhotobankIndexJob` (`backend/src/Anela.Heblo.Application/Features/Photobank/Infrastructure/Jobs/PhotobankIndexJob.cs`) is an Application-layer class that directly injects `ApplicationDbContext` from the Persistence layer and calls EF Core APIs throughout its body:

- Line 12: `private readonly ApplicationDbContext _db;`
- Line 46–49: `_db.PhotobankIndexRoots.Where(...).ToListAsync()`
- Line 77–81: `_db.PhotobankTagRules.Where(...).ToListAsync()`
- Lines 88–104: `_db.Photos.FirstOrDefaultAsync(...)`, `_db.Photos.Remove(...)`, `_db.Photos.Add(...)`
- Lines 148–167: `_db.PhotoTags`, `_db.PhotobankTags` direct manipulation
- Lines 104, 145, 168, 175: `_db.SaveChangesAsync()`

Compare with `PhotobankAutoTagJob` (same directory), which correctly uses `IPhotobankRepository` for all data access and `_repo.SaveChangesAsync()`.

## Why it matters
Clean Architecture requires that the Application layer must NOT depend on the Infrastructure (Persistence) layer — only on abstractions defined in the Domain layer. This violation:
- Makes `PhotobankIndexJob` untestable without a real database (the sibling `PhotobankAutoTagJob` can be unit-tested with a mock repository)
- Couples the indexing logic to EF Core internals; any future schema change or ORM swap requires touching the job
- Breaks the project's own rule: `IPhotobankRepository` is the defined boundary but is entirely bypassed here
- The `PhotobankModule.cs` registers `IPhotobankRepository` precisely so Application code doesn't need to know about `ApplicationDbContext`

## Suggested fix
Extend `IPhotobankRepository` with the operations the job needs that are currently missing (e.g., `GetActiveRootsAsync`, `UpsertPhotoAsync`, `UpdateRootDeltaLinkAsync`) and have `PhotobankRepository` implement them. Replace all `_db.*` calls in `PhotobankIndexJob` with repository calls, and remove the `ApplicationDbContext` constructor parameter. The job should then follow the same pattern as `PhotobankAutoTagJob`.

---
_Filed by daily arch-review routine on 2026-05-27._