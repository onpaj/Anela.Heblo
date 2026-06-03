# Architecture Review: Extract `BulkTagLimit` Constant to Shared `PhotobankConstants`

## Skip Design: true

## Architectural Fit Assessment

The proposal fits the codebase exactly. Three load-bearing facts confirm alignment:

1. **Filesystem convention already names this file.** `docs/architecture/filesystem.md` explicitly lists `{Feature}Constants.cs` as a permitted top-level file under `Anela.Heblo.Application/Features/{Feature}/`. The proposed path `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs` matches verbatim.
2. **Sibling features already follow this pattern.** `CatalogConstants.cs`, `ManufactureConstants.cs`, `AnalyticsConstants.cs`, `MeetingTasksConstants.cs`, `InventoryConstants.cs` all exist as `public static`/`internal static` classes under their feature folders. The Photobank module is currently the outlier.
3. **The duplication is real, not coincidental.** Both `BulkAddPhotoTagHandler.cs:15` and `BulkAddPhotoTagByIdsHandler.cs:15` declare `private const int BulkTagLimit = 5_000;` and emit the same `ErrorCodes.BulkTagLimitExceeded` with the same `Params["Count"]` / `Params["Limit"]` payload shape. A single canonical source eliminates the drift risk with no behavioral impact.

Integration points: the two MediatR handlers in `UseCases/BulkAddPhotoTag/` and `UseCases/BulkAddPhotoTagByIds/`, and the two existing test classes (`BulkAddPhotoTagHandlerTests.cs`, `BulkAddPhotoTagByIdsHandlerTests.cs`) that already assert on `Params["Limit"] == "5000"`.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application/Features/Photobank/
├── PhotobankConstants.cs                    ← NEW (single source of truth)
│   └── public const int BulkTagLimit = 5_000
│
├── UseCases/
│   ├── BulkAddPhotoTag/
│   │   └── BulkAddPhotoTagHandler.cs        ← references PhotobankConstants.BulkTagLimit
│   └── BulkAddPhotoTagByIds/
│       └── BulkAddPhotoTagByIdsHandler.cs   ← references PhotobankConstants.BulkTagLimit
│
└── (other existing files unchanged)

backend/test/Anela.Heblo.Tests/Features/Photobank/
├── BulkAddPhotoTagHandlerTests.cs           ← asserts via PhotobankConstants.BulkTagLimit
└── BulkAddPhotoTagByIdsHandlerTests.cs      ← asserts via PhotobankConstants.BulkTagLimit
```

No module registration, DI wiring, or migration is required. `public const int` is inlined at compile time.

### Key Design Decisions

#### Decision 1: Visibility (`public` vs `internal`)
**Options considered:** `internal static`, `public static`.
**Chosen approach:** `public static class PhotobankConstants` with `public const int BulkTagLimit`.
**Rationale:** Matches the dominant convention in this layer (`CatalogConstants`, `ManufactureConstants`, `AnalyticsConstants`, `InventoryConstants` are all `public static`). `MeetingTasksConstants` is `internal`, but it holds a single feature-internal DI key — not a comparable case. Tests in `Anela.Heblo.Tests` will reference `PhotobankConstants.BulkTagLimit` in assertions (per spec FR-5) and live in a separate assembly, so `public` is required for the assertion-via-constant pattern to compile without `InternalsVisibleTo`.

#### Decision 2: Namespace style and location
**Options considered:** (a) place in `Anela.Heblo.Application.Features.Photobank` root with file-scoped namespace; (b) match Photobank's existing block-scoped namespace style.
**Chosen approach:** File at `backend/src/Anela.Heblo.Application/Features/Photobank/PhotobankConstants.cs`, namespace `Anela.Heblo.Application.Features.Photobank`. Use **file-scoped** namespace declaration to match every other `*Constants.cs` file in this project (Catalog, Manufacture, Analytics, MeetingTasks, Inventory all use file-scoped). The fact that some Photobank handlers use block-scoped namespaces is incidental; the convention across `*Constants.cs` files is uniform and worth following.
**Rationale:** Reader picks up the convention from neighboring files of the same role, not from neighboring files in the same folder.

#### Decision 3: Identifier casing (`BulkTagLimit` vs `BULK_TAG_LIMIT`)
**Options considered:** Preserve `BulkTagLimit` (PascalCase) or rename to `BULK_TAG_LIMIT` (UPPER_SNAKE_CASE).
**Chosen approach:** Keep `BulkTagLimit` exactly as written in the spec.
**Rationale:** The codebase is inconsistent — `CatalogConstants` / `ManufactureConstants` / `AnalyticsConstants` use UPPER_SNAKE_CASE; `MeetingTasksConstants` uses PascalCase; the existing private constant in both handlers is `BulkTagLimit`. The brief is explicitly scoped to deduplication, not to a broader naming sweep. Keeping the name minimizes the surface area of the change and avoids touching unrelated norms. The inconsistency itself is out of scope.

#### Decision 4: Scope discipline — single constant only
**Options considered:** Pre-populate `PhotobankConstants` with other Photobank magic numbers (e.g. cache TTLs, indexing batch sizes).
**Chosen approach:** Define **only** `BulkTagLimit`. Out of scope per spec.
**Rationale:** YAGNI. Other constants live in `PhotobankTagsCacheOptions`, `AutoTagOptions`, etc., and may belong to dedicated options classes rather than a flat constants bag. Conflating them with a DRY refactor would expand the blast radius and make the diff harder to review.

## Implementation Guidance

### Directory / Module Structure
```
backend/src/Anela.Heblo.Application/Features/Photobank/
    PhotobankConstants.cs              ← CREATE
    UseCases/BulkAddPhotoTag/
        BulkAddPhotoTagHandler.cs      ← EDIT (remove private const, add reference)
    UseCases/BulkAddPhotoTagByIds/
        BulkAddPhotoTagByIdsHandler.cs ← EDIT (remove private const, add reference)

backend/test/Anela.Heblo.Tests/Features/Photobank/
    BulkAddPhotoTagHandlerTests.cs        ← EDIT (replace "5000" literal with PhotobankConstants.BulkTagLimit.ToString())
    BulkAddPhotoTagByIdsHandlerTests.cs   ← EDIT (same)
```

Each handler's `using` block already imports `Anela.Heblo.Application.Features.Photobank.Services` and `Anela.Heblo.Application.Shared`. Since both handlers live under the `Anela.Heblo.Application.Features.Photobank.UseCases.{X}` namespace, the parent namespace `Anela.Heblo.Application.Features.Photobank` is resolved without any new `using` directive (.NET implicit parent-namespace resolution).

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Application.Features.Photobank;

public static class PhotobankConstants
{
    /// <summary>
    /// Maximum number of photos that can be tagged in a single bulk-tag operation.
    /// Enforced by BulkAddPhotoTagHandler and BulkAddPhotoTagByIdsHandler; exceeding it
    /// returns <see cref="Anela.Heblo.Application.Shared.ErrorCodes.BulkTagLimitExceeded"/>.
    /// </summary>
    public const int BulkTagLimit = 5_000;
}
```

Removed: `BulkAddPhotoTagHandler.BulkTagLimit` (private), `BulkAddPhotoTagByIdsHandler.BulkTagLimit` (private). Neither was visible outside its containing class.

### Data Flow

Unchanged. The constant is a compile-time literal, inlined at the use sites. The two existing guard paths remain:

1. `BulkAddPhotoTagHandler`: `total = await _repository.CountFilteredPhotosAsync(...)` → if `total > PhotobankConstants.BulkTagLimit`, return `BulkTagLimitExceeded` with `Params["Count"]=total`, `Params["Limit"]=PhotobankConstants.BulkTagLimit.ToString()`.
2. `BulkAddPhotoTagByIdsHandler`: `request.PhotoIds.Count` → if `> PhotobankConstants.BulkTagLimit`, return `BulkTagLimitExceeded` with same payload shape.

Wire-level error contract is byte-identical to current behavior.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Inlining semantics: existing assemblies that referenced `BulkTagLimit` would be stale after a value change. | LOW | The constants in scope are `private`, so no external assembly references them. After this refactor, `public const` is consumed only by handlers + tests in the same solution and rebuilt together. Not a risk in this monorepo. |
| Test assertion drift: current tests assert `Params["Limit"]` equals the string `"5000"`. Spec FR-5 says to assert against `PhotobankConstants.BulkTagLimit`. The dictionary value is a string, the constant is an `int`. | LOW | Update assertions to `.Should().Be(PhotobankConstants.BulkTagLimit.ToString())`. A direct `.Be(PhotobankConstants.BulkTagLimit)` will not compile (string vs int). See **Specification Amendments**. |
| Future temptation to add unrelated constants into `PhotobankConstants` opportunistically. | LOW | Spec is explicit: scope is `BulkTagLimit` only. Reviewer should reject additions in this PR. |
| Namespace collision with another `PhotobankConstants` type. | NONE | Verified by repo grep: no `PhotobankConstants` exists today. |
| `dotnet format` reformatting unrelated lines in edited handlers. | LOW | Keep edits surgical: only the `const` line removal and the `BulkTagLimit` → `PhotobankConstants.BulkTagLimit` rewrites. Run `dotnet format` only against the four files touched, then confirm `git diff` shows only intended changes. |

## Specification Amendments

1. **FR-5 assertion type clarification.** The spec says: *"Asserts `Params["Limit"]` equals `PhotobankConstants.BulkTagLimit`"*. `Params` is `Dictionary<string, string>` (verified in both handlers, lines 34–38 and 36–40). The assertion must call `.ToString()` on the constant:

   ```csharp
   result.Params.Should().ContainKey("Limit")
         .WhoseValue.Should().Be(PhotobankConstants.BulkTagLimit.ToString());
   ```

   Existing tests at `BulkAddPhotoTagHandlerTests.cs:99` and `BulkAddPhotoTagByIdsHandlerTests.cs:87` use the literal `"5000"`. Update both to the constant-based form so the tests survive a future limit change. This is the only test edit required; the test method names, arrange/act sections, and other assertions stay as-is.

2. **Namespace style clarification.** Spec says namespace must match "existing Photobank feature folder conventions." The Photobank module uses block-scoped namespaces in handlers, but every `*Constants.cs` file in `Anela.Heblo.Application/Features/*/` uses file-scoped namespaces. Use **file-scoped** for the new file — this matches the role-specific convention and is shorter. If a future stylistic alignment in the module is desired, that's a separate task.

3. **No other amendments.** The spec's functional requirements (FR-1 through FR-5), non-functional requirements, and out-of-scope list are correct and complete.

## Prerequisites

None. This is a pure code-only refactor:

- No database migration.
- No `appsettings*.json` change.
- No Key Vault secret.
- No DI registration (no service is added; `const` does not need DI).
- No frontend regeneration of the OpenAPI client (no endpoint or DTO shape changes).
- No feature flag.

Implementation can start immediately. Validation: `dotnet build` (no new warnings), `dotnet format` (clean diff), `dotnet test` against `Anela.Heblo.Tests` (all Photobank tests green, including the two updated boundary assertions).