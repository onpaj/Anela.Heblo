# Design: Split `IPhotobankRepository` into per-entity-family interfaces

No user interface is involved — this is an internal backend interface-declaration refactor
(Domain/Application/Persistence layers only). The UX/UI section is omitted.

## Decisions carried forward from plan-01.md

I verified the plan's FR-1 consumer table against source (`grep` on every multi-family
consumer: `PhotobankIndexJob`, `PhotobankAutoTagJob`, `ReapplyRulesHandler`,
`RetagPhotosHandler`, plus a sample of single-family consumers). All method-to-family
assignments in the plan match actual call sites, including both documented deviations
(`GetPhotosByIdsAsync` → Photo, `RemovePhotoTagsBySourceAsync` → PhotoTag). I'm resolving
the plan's three open questions as follows:

1. **`GetPhotoRuleCandidatesPageAsync` placement** → stays in `IPhotobankAutoTagRepository`.
   It and `GetPhotosPendingAutoTagAsync` are the only two paged-candidate-scan methods
   returning `PhotoAutoTagCandidate`; splitting them into a 7th interface for one caller
   (`ReapplyRulesHandler`) adds a file without reducing any consumer's real surface (that
   handler already needs `IPhotobankAutoTagRepository`-shaped data).
2. **Six interfaces, no merging** → confirmed. `IPhotobankRootRepository` (4 methods, 3
   single-family consumers) stays separate: it's a distinct entity family with its own
   lifecycle (index roots), and merging it into another interface would force at least one
   consumer to depend on a method it doesn't call — reintroducing the ISP violation this
   task exists to fix.
3. **DI wiring** → shared-instance-forwarding pattern from FR-5, made concrete below.

## Component design

### Domain layer — six interfaces replacing one

All six files live in `backend/src/Anela.Heblo.Domain/Features/Photobank/`, one interface
per file (matching the existing one-type-per-file convention in that folder, e.g. `Tag.cs`,
`TagRule.cs`). `IPhotobankRepository.cs` is deleted.

**`IPhotobankPhotoRepository.cs`** — photo CRUD + filter/lookup queries. Also holds the
`PhotoLocator` record (moved from the old file; it's a Photo-family DTO with no other home).

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public sealed record PhotoLocator(string DriveId, string SharePointFileId, DateTime ModifiedAt);

public interface IPhotobankPhotoRepository
{
    Task<(List<Photo> Items, int Total)> GetPhotosAsync(
        List<string>? tags, string? search, bool useRegex, bool withoutTags, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<int> CountFilteredPhotosAsync(List<string>? tags, string? search, CancellationToken cancellationToken);
    Task<List<int>> GetFilteredPhotoIdsMissingTagAsync(List<string>? tags, string? search, int tagId, CancellationToken cancellationToken);
    Task<List<int>> GetExistingPhotoIdsMissingTagAsync(IReadOnlyList<int> photoIds, int tagId, CancellationToken cancellationToken);
    Task<int> CountExistingPhotosAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
    Task<Photo?> GetPhotoByIdAsync(int id, CancellationToken cancellationToken);
    Task<PhotoLocator?> GetLocatorAsync(int id, CancellationToken cancellationToken);
    Task<Photo?> GetPhotoBySharePointFileIdAsync(string sharePointFileId, CancellationToken cancellationToken);
    Task AddPhotoAsync(Photo photo, CancellationToken cancellationToken);
    Task RemovePhotoAsync(Photo photo, CancellationToken cancellationToken);
    Task<List<Photo>> GetPhotosByIdsAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**`IPhotobankTagRepository.cs`** — tag CRUD, counts, get-or-create.

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public interface IPhotobankTagRepository
{
    Task<IReadOnlyList<TagCount>> GetTagsWithCountsAsync(CancellationToken cancellationToken);
    Task<Tag?> GetOrCreateTagAsync(string normalizedName, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, int>> GetOrCreateTagsAsync(IReadOnlyCollection<string> normalizedNames, CancellationToken cancellationToken);
    Task<Tag?> GetTagByIdAsync(int id, CancellationToken cancellationToken);
    Task<Tag?> GetTagByNameAsync(string normalizedName, CancellationToken cancellationToken);
    Task DeleteTagAsync(Tag tag, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**`IPhotobankPhotoTagRepository.cs`** — photo–tag join writes and reads.

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public interface IPhotobankPhotoTagRepository
{
    Task AddPhotoTagAsync(PhotoTag photoTag, CancellationToken cancellationToken);
    Task AddPhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
    Task RemovePhotoTagAsync(int photoId, int tagId, CancellationToken cancellationToken);
    Task<bool> PhotoTagExistsAsync(int photoId, int tagId, CancellationToken cancellationToken);
    Task RemoveRuleTagsAsync(string? scopeToTagName, CancellationToken cancellationToken);
    Task<HashSet<(int PhotoId, int TagId)>> GetOccupiedTagPairsAsync(string? scopeToTagName, CancellationToken cancellationToken);
    Task<List<PhotoTag>> GetPhotoTagsByPhotoAndSourceAsync(int photoId, PhotoTagSource source, CancellationToken cancellationToken);
    Task RemovePhotoTagsAsync(IEnumerable<PhotoTag> photoTags, CancellationToken cancellationToken);
    Task RemovePhotoTagsBySourceAsync(IReadOnlyList<int> photoIds, PhotoTagSource source, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**`IPhotobankRootRepository.cs`** — index root CRUD.

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public interface IPhotobankRootRepository
{
    Task<List<PhotobankIndexRoot>> GetRootsAsync(CancellationToken cancellationToken);
    Task<PhotobankIndexRoot> AddRootAsync(PhotobankIndexRoot root, CancellationToken cancellationToken);
    Task<bool> DeleteRootAsync(int id, CancellationToken cancellationToken);
    Task<List<PhotobankIndexRoot>> GetActiveRootsWithDriveAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**`IPhotobankTagRuleRepository.cs`** — tag rule CRUD.

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public interface IPhotobankTagRuleRepository
{
    Task<List<TagRule>> GetRulesAsync(CancellationToken cancellationToken);
    Task<TagRule> AddRuleAsync(TagRule rule, CancellationToken cancellationToken);
    Task<TagRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken);
    Task UpdateRuleAsync(TagRule rule, CancellationToken cancellationToken);
    Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken);
    Task<List<TagRule>> GetActiveTagRulesAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

**`IPhotobankAutoTagRepository.cs`** — pending-photo scan, stamp/reset, rule-reapply
candidate scan.

```csharp
namespace Anela.Heblo.Domain.Features.Photobank;

public interface IPhotobankAutoTagRepository
{
    Task<List<PhotoAutoTagCandidate>> GetPhotosPendingAutoTagAsync(int pageSize, int offset, CancellationToken cancellationToken);
    Task StampAutoTaggedAtAsync(IReadOnlyList<int> photoIds, DateTime timestamp, CancellationToken cancellationToken);
    Task ResetAutoTaggedAtAsync(IReadOnlyList<int> photoIds, CancellationToken cancellationToken);
    Task<List<PhotoAutoTagCandidate>> GetPhotoRuleCandidatesPageAsync(int pageSize, int offset, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

Per FR-2, `SaveChangesAsync` is duplicated verbatim across all six interfaces rather than
factored into a shared base/seventh interface — that would just recreate a mandatory
cross-cutting dependency for the four read-only handlers (`GetTagsHandler`,
`GetPhotosHandler`, `GetRootsHandler`, `GetRulesHandler`) plus `GetThumbnailHandler`, which
defeats the purpose of the split. Six identical one-line method signatures is the accepted
cost.

### Persistence layer — single class, six implemented interfaces

`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`: only the class
declaration line changes.

```csharp
public class PhotobankRepository :
    IPhotobankPhotoRepository,
    IPhotobankTagRepository,
    IPhotobankPhotoTagRepository,
    IPhotobankRootRepository,
    IPhotobankTagRuleRepository,
    IPhotobankAutoTagRepository
```

No method bodies change. The existing single `SaveChangesAsync` method (currently at the
end of the file, calling `_context.SaveChangesAsync`) satisfies all six interface copies
of that member — C# allows one method to implement the same-signature member declared on
multiple interfaces.

### DI wiring — `PhotobankModule.AddPhotobankModule`

This is the one part of the refactor that isn't purely mechanical (FR-5). Replace the
current line:

```csharp
services.AddScoped<IPhotobankRepository, PhotobankRepository>();
```

with:

```csharp
services.AddScoped<PhotobankRepository>();
services.AddScoped<IPhotobankPhotoRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankTagRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankPhotoTagRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankRootRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankTagRuleRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankAutoTagRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
```

Rationale: registering `PhotobankRepository` once as a scoped concrete type, then
forwarding each narrow interface to `GetRequiredService<PhotobankRepository>()` within the
same scope, guarantees every interface resolves to the *same instance* (and therefore the
same `ApplicationDbContext`) per request/job scope. Multi-family consumers
(`PhotobankIndexJob`, `PhotobankAutoTagJob`, `ReapplyRulesHandler`, `RetagPhotosHandler`)
inject 2–5 of these interfaces and call `SaveChangesAsync` on whichever one they hold —
that has to commit writes made through the others in the same scope. Registering each
interface with its own `AddScoped<TInterface, PhotobankRepository>()` line (six separate
concrete-type registrations) would instead produce six distinct `PhotobankRepository`
instances per scope, silently breaking that invariant — writes made via one interface
would not be visible/persisted when `SaveChangesAsync` is called via another.

### Application layer — consumer repointing

Each handler/job constructor changes its injected type(s) per the table below (identical
to plan-01.md's FR-1 table, reproduced here as the authoritative consumer→interface
mapping for implementation):

| Consumer | Injected interface(s) |
|---|---|
| `GetPhotosHandler` | `IPhotobankPhotoRepository` |
| `GetThumbnailHandler` | `IPhotobankPhotoRepository` |
| `AddPhotoTagHandler` | `IPhotobankPhotoRepository`, `IPhotobankTagRepository`, `IPhotobankPhotoTagRepository` |
| `RemovePhotoTagHandler` | `IPhotobankPhotoRepository`, `IPhotobankPhotoTagRepository` |
| `BulkAddPhotoTagHandler` | `IPhotobankPhotoRepository`, `IPhotobankTagRepository`, `IPhotobankPhotoTagRepository` |
| `BulkAddPhotoTagByIdsHandler` | `IPhotobankPhotoRepository`, `IPhotobankTagRepository`, `IPhotobankPhotoTagRepository` |
| `RetagPhotosHandler` | `IPhotobankPhotoRepository`, `IPhotobankPhotoTagRepository`, `IPhotobankAutoTagRepository` |
| `GetTagsHandler` | `IPhotobankTagRepository` |
| `CreateTagHandler` | `IPhotobankTagRepository` |
| `DeleteTagHandler` | `IPhotobankTagRepository` |
| `GetRootsHandler` | `IPhotobankRootRepository` |
| `AddRootHandler` | `IPhotobankRootRepository` |
| `DeleteRootHandler` | `IPhotobankRootRepository` |
| `GetRulesHandler` | `IPhotobankTagRuleRepository` |
| `AddRuleHandler` | `IPhotobankTagRuleRepository` |
| `UpdateRuleHandler` | `IPhotobankTagRuleRepository` |
| `DeleteRuleHandler` | `IPhotobankTagRuleRepository` |
| `ReapplyRulesHandler` | `IPhotobankTagRuleRepository`, `IPhotobankTagRepository`, `IPhotobankPhotoTagRepository`, `IPhotobankAutoTagRepository` |
| `PhotobankIndexJob` | `IPhotobankRootRepository`, `IPhotobankTagRuleRepository`, `IPhotobankPhotoRepository`, `IPhotobankTagRepository`, `IPhotobankPhotoTagRepository` |
| `PhotobankAutoTagJob` | `IPhotobankTagRepository`, `IPhotobankAutoTagRepository`, `IPhotobankPhotoTagRepository` |

Verified directly against source for the four multi-family consumers plus a sample of
single-family ones (`AddPhotoTagHandler`, `BulkAddPhotoTagHandler`,
`BulkAddPhotoTagByIdsHandler`, `RemovePhotoTagHandler`, `GetThumbnailHandler`) — all match.

Where a handler injects multiple interfaces, each gets its own constructor parameter and
backing field (e.g. `RetagPhotosHandler` goes from one `IPhotobankRepository _repository`
field to three: `IPhotobankPhotoRepository _photoRepository`,
`IPhotobankPhotoTagRepository _photoTagRepository`, `IPhotobankAutoTagRepository
_autoTagRepository`), with call sites updated to call through the field owning that
method (mechanical, no logic change). Naming follows the existing `_repository`/`_repo`
convention in each file, disambiguated by family when more than one is injected.

### Test doubles

Each of the ~13 test files under `backend/test/Anela.Heblo.Tests/Features/Photobank/`
replaces its `Mock<IPhotobankRepository>` field(s) with `Mock<T>` for the interface(s)
the handler under test now injects (same mapping as the table above). Multi-family
handler tests get multiple mock fields, matched one-to-one with the handler's new
constructor parameters.

## Data schemas

No data schema changes. This is a compile-time interface reorganization:

- **Request/response DTOs** (`Contracts/*.cs`, `UseCases/*/`) — untouched.
- **Domain entities** (`Photo`, `Tag`, `PhotoTag`, `PhotobankIndexRoot`, `TagRule`,
  `PhotoAutoTagCandidate`, `TagCount`) — untouched.
- **`PhotoLocator` record** — relocated from `IPhotobankRepository.cs` into
  `IPhotobankPhotoRepository.cs`, same namespace (`Anela.Heblo.Domain.Features.Photobank`),
  same shape. No consumer-visible change (consumers reference it by type name via the
  shared namespace, not by file).
- **HTTP/API contracts** (`PhotobankController.cs`) — untouched; the controller talks to
  MediatR requests/responses, never to `IPhotobankRepository` directly.
- **EF Core model / migrations** — untouched; `PhotobankRepository`'s method bodies and
  `ApplicationDbContext` usage are unchanged.

## Verification plan

1. `dotnet build` (solution-wide) — must succeed with zero references to
   `IPhotobankRepository` remaining anywhere (`grep -r IPhotobankRepository backend/` should
   only match the six new type names as substrings, not the deleted type itself).
2. `dotnet format` — style compliance.
3. `dotnet test` on `backend/test/Anela.Heblo.Tests/Features/Photobank/**` — full green,
   no assertion changes beyond mock type renames.
4. Manual/code-level sanity check of the DI registration (FR-5): confirm
   `PhotobankIndexJob` and `PhotobankAutoTagJob` — the two consumers with the widest
   interface surface — resolve correctly at startup and that a write made via one
   injected interface is visible after `SaveChangesAsync` is called via another, within
   the existing integration test coverage for those jobs if present, otherwise via a
   one-time manual run.
