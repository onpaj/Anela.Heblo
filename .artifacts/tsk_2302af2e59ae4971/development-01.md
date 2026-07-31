# Development: Split `IPhotobankRepository` into six per-entity-family interfaces

Implements plan-01.md / design-01.md as amended by architecture-01.md: six narrow interfaces
in the Domain layer, **six independent persistence classes** (not one class implementing all
six interfaces), plain per-interface DI registrations (no shared-instance forwarding), and every
consumer repointed to inject only the interface(s) it actually uses.

## Files created

**Domain** (`backend/src/Anela.Heblo.Domain/Features/Photobank/`):
- `IPhotobankPhotoRepository.cs` — photo CRUD/filter queries + `GetPhotosByIdsAsync`; also holds
  the relocated `PhotoLocator` record.
- `IPhotobankTagRepository.cs` — tag CRUD, counts, get-or-create.
- `IPhotobankPhotoTagRepository.cs` — photo–tag join reads/writes + `RemovePhotoTagsBySourceAsync`.
- `IPhotobankRootRepository.cs` — index root CRUD.
- `IPhotobankTagRuleRepository.cs` — tag rule CRUD.
- `IPhotobankAutoTagRepository.cs` — pending-photo scan, stamp/reset, `GetPhotoRuleCandidatesPageAsync`.

Each interface duplicates `SaveChangesAsync` (per FR-2/design-01.md), so read-only handlers
never depend on a persistence method they don't call.

**Persistence** (`backend/src/Anela.Heblo.Persistence/Photobank/`), per architecture-01.md's
required amendment — six physical classes, each with its own `ApplicationDbContext` field,
mirroring the Journal/PackingMaterials precedent:
- `PhotobankPhotoRepository.cs`, `PhotobankTagRepository.cs`, `PhotobankPhotoTagRepository.cs`,
  `PhotobankRootRepository.cs`, `PhotobankTagRuleRepository.cs`, `PhotobankAutoTagRepository.cs`.

All method bodies moved verbatim from the old `PhotobankRepository.cs` (cut, not rewritten) into
the file matching their interface — no query/logic changes.

Two additional Persistence-layer test files were **renamed** to track their class rename
(class body unchanged besides the type rename): `PhotobankRepositoryFilterTests.cs` →
`PhotobankPhotoRepositoryFilterTests.cs`, `PhotobankRepositoryGetLocatorTests.cs` →
`PhotobankPhotoRepositoryGetLocatorTests.cs`, `PhotobankRepositoryGetTagsSqlShapeTests.cs` →
`PhotobankTagRepositoryGetTagsSqlShapeTests.cs`, `PhotobankRepositoryGetTagsTests.cs` →
`PhotobankTagRepositoryGetTagsTests.cs`.

## Files deleted

- `backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`

## Files changed

**DI**: `PhotobankModule.cs` — six plain `services.AddScoped<TInterface, TImpl>()` lines replace
the single registration. No `GetRequiredService` forwarding — each interface now has its own
concrete class, so DI's standard scoped-`DbContext`-sharing across independently-constructed
repository classes gives every consumer in a scope the same underlying `DbContext`, exactly like
the Journal/PackingMaterials precedent.

**18 UseCase handlers** — constructor(s) repointed to the narrow interface(s) the handler
actually calls, per the design's consumer table (single-interface handlers: `GetPhotosHandler`,
`GetThumbnailHandler`, `GetTagsHandler`, `CreateTagHandler`, `DeleteTagHandler`,
`GetRootsHandler`, `AddRootHandler`, `DeleteRootHandler`, `GetRulesHandler`, `AddRuleHandler`,
`UpdateRuleHandler`, `DeleteRuleHandler`; multi-interface handlers: `AddPhotoTagHandler` (3),
`RemovePhotoTagHandler` (2), `BulkAddPhotoTagHandler` (3), `BulkAddPhotoTagByIdsHandler` (3),
`RetagPhotosHandler` (3), `ReapplyRulesHandler` (4)).

**2 background jobs** — `PhotobankIndexJob` (5 interfaces: Root, TagRule, Photo, Tag, PhotoTag)
and `PhotobankAutoTagJob` (3 interfaces: Tag, AutoTag, PhotoTag), each field/constructor-param
split by family, call sites repointed to the field owning that method. Where multiple
`SaveChangesAsync` calls existed per method, each now calls it on whichever repository's own
write the call is committing (e.g. `PhotobankIndexJob`'s Phase A flush uses
`_photoRepository.SaveChangesAsync`, Phase B uses `_photoTagRepository.SaveChangesAsync`, root
bookkeeping uses `_rootRepository.SaveChangesAsync`) — semantically equivalent because all three
share one scoped `DbContext`.

**20 test files** — `Mock<IPhotobankRepository>` fields replaced with `Mock<T>` per interface
the handler/job under test now injects; multi-family tests got multiple mock fields matched
one-to-one with the new constructor parameters. Files constructing `PhotobankRepository`
directly against a real `ApplicationDbContext` (`PhotobankRepositoryReapplyPrimitivesTests.cs`,
`ReapplyRulesBehaviorPreservationTests.cs`) now construct the 3–4 relevant concrete repository
classes against the same shared `_context`, preserving the "one DbContext, multiple repository
instances" invariant the tests exercise.

## Verification

- `dotnet build Anela.Heblo.sln` — **0 errors** (245 pre-existing warnings, unrelated to this
  change; one nullable-reference warning in `ReapplyRulesHandlerTests.cs` line 107 is carried
  over verbatim from the original file, not introduced by this change).
- `dotnet format Anela.Heblo.sln --no-restore` — ran clean, no residual diffs beyond the
  refactor itself (confirmed via `git diff` spot-check).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank"`
  — **198/198 passed**, 0 failed, 0 skipped.
- `grep -r "IPhotobankRepository\|PhotobankRepository\b" backend/` — zero remaining references
  to the deleted god-interface/class anywhere in the codebase.

## How to verify

```bash
export PATH="$PATH:$HOME/.dotnet"
cd backend
dotnet build ../Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank"
```

## Notes

- One pre-existing, unrelated build-time warning surfaces during `dotnet build`: the
  `GenerateFrontendClientManual`-adjacent access-matrix generator (`Anela.Heblo.AccessMatrixGen`)
  throws a `JsonException` reading its input file and exits non-zero, logged as MSB3073 warning
  — this is not caused by this change (no access-matrix/authorization files were touched) and
  does not fail the build.
- Scope matched plan-01.md/design-01.md/architecture-01.md exactly: no handler logic, query
  logic, or API/DTO contract changed — this is a pure declaration/wiring reorganization plus the
  physical-class split mandated by the architecture review.
