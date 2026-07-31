# Review: Split `IPhotobankRepository` into six per-entity-family interfaces

## Verdict: done

## What I checked

- **Build**: `dotnet build Anela.Heblo.sln` — 0 errors (only pre-existing warnings, none new).
- **Tests**: `dotnet test ... --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Photobank"` — 198/198 passed.
- **Format**: `dotnet format Anela.Heblo.sln --no-restore --verify-no-changes` — exit 0, no diffs needed.
- **Dead references**: grepped the whole `backend/` tree for `IPhotobankRepository` and the old `PhotobankRepository` class — zero remaining references outside the class's own member accesses.

## Conformance to architecture-01.md / design-01.md

- Six Domain interfaces (`IPhotobankPhotoRepository`, `IPhotobankTagRepository`, `IPhotobankPhotoTagRepository`, `IPhotobankRootRepository`, `IPhotobankTagRuleRepository`, `IPhotobankAutoTagRepository`) match the method groupings from both docs exactly, including the `PhotoLocator` relocation and the per-interface `SaveChangesAsync` duplication (FR-2).
- **Persistence**: read all six new classes (`PhotobankPhotoRepository`, `PhotobankTagRepository`, `PhotobankPhotoTagRepository`, `PhotobankRootRepository`, `PhotobankTagRuleRepository`, `PhotobankAutoTagRepository`) — each is a standalone class with its own `ApplicationDbContext _context` field and constructor, exactly matching the architecture review's required amendment (no shared-instance/`GetRequiredService` forwarding, mirroring the Journal/PackingMaterials precedent). Method bodies read as verbatim moves — no query-logic changes spotted (e.g. `BuildFilterQuery`, `GetTagsWithCountsAsync`'s join, `ExecuteDeleteAsync`/`ExecuteUpdateAsync` usages all intact).
- **DI**: `PhotobankModule.cs` registers all six as plain `services.AddScoped<TInterface, TImpl>()` lines — matches architecture-01.md's mandated registration verbatim, no forwarding.
- **Consumers**: read `PhotobankIndexJob`, `PhotobankAutoTagJob`, and the multi-interface handlers (`AddPhotoTagHandler`, `RemovePhotoTagHandler`, `BulkAddPhotoTagHandler`, `RetagPhotosHandler`, `ReapplyRulesHandler`) plus several single-interface handlers. Each field is injected from the correct narrow interface per the design's consumer table, and every `SaveChangesAsync` call is issued on the repository instance that owns the mutation just made (e.g. `PhotobankIndexJob.UpsertPhotoBatchAsync` flushes via `_photoRepository` after Phase A and via `_photoTagRepository` after Phase B) — correct given all repositories share one scoped `DbContext`, exactly the invariant the architecture review relied on.

## Correctness

No logic errors found in the sampled handlers/jobs/persistence classes. No missing error handling introduced (none of the split logic touches error paths). No concurrency concerns beyond what the shared-`DbContext`-per-scope pattern already relies on, which is pre-existing and explicitly validated against the Journal/PackingMaterials precedent in architecture-01.md.

## Completeness

- 18 handlers + 2 jobs + DI module all repointed; old interface/class deleted; 20 test files updated including 4 renamed persistence test files tracking their class renames. No remaining references to the deleted types anywhere in `backend/`.
- Scope matches the finding/plan/design/architecture chain: pure declaration/wiring reorganization, no behavior change, no API/DTO contract change.

## Non-blocking observations

- None worth flagging — the development step's own verification notes (build/format/test/grep) all reproduced cleanly in this review.

```json
{"outcome": "done", "summary": "Verified build (0 errors), dotnet format (clean), and Photobank test suite (198/198 passing) firsthand. Read all six new Domain interfaces and Persistence classes, the DI module, and the riskiest multi-interface consumers (PhotobankIndexJob, PhotobankAutoTagJob, ReapplyRulesHandler, RetagPhotosHandler, BulkAddPhotoTagHandler, AddPhotoTagHandler, RemovePhotoTagHandler) — all match architecture-01.md's mandated six-physical-class/plain-DI pattern exactly, with correct per-repository SaveChangesAsync routing under the shared-scoped-DbContext invariant. No remaining references to the deleted IPhotobankRepository/PhotobankRepository anywhere in backend/. No functional, architectural, or correctness issues found."}
```
