# Review — Split `UpdateManufactureOrderStatusHandler` into state-transition + 2 extracted services

## Verdict: done

## What was checked

Read plan-01, design-01, architecture-01, development-01, and diffed `HEAD~1..HEAD` in full
(12 files, +879/-674). Verified against the approved architecture-01 decisions point by point:

- **`IManufactureInventoryWriteDownService` / `ManufactureInventoryWriteDownService`** — body is a
  byte-for-byte verbatim move of the old `WriteDownInventoryAsync` (catalog lookup, semi-product
  exclusion, product/lot/expiration aggregation, idempotency check via `WasWrittenDownByOrder`,
  merge-or-create, `AddRangeAsync`). Constructor matches design-01/architecture-01 exactly:
  `TimeProvider`, `ILogger<ManufactureInventoryWriteDownService>`,
  `IManufacturedProductInventoryRepository`, `IManufactureCatalogSource`.
- **`IManufactureConditionsCaptureService` / `ManufactureConditionsCaptureService`** — verbatim move
  of `CaptureConditionsReadingAsync` (snapshot call, field mapping, exception fallback to
  `ConditionsReadingSource.Unavailable`, same log message text). Constructor: `TimeProvider`,
  `ILogger<ManufactureConditionsCaptureService>`, `IConditionsReadingProvider`.
- **Handler** — constructor now takes the two new service interfaces in place of the three
  concern-specific dependencies (7 → 6 params, matching architecture-01's resolved "6 is correct"
  decision). The two call sites in `Handle` are unchanged in condition/order, just call the
  injected services. Both extracted private methods and their three now-unused fields are deleted.
  Confirmed via repo-wide grep: no other file constructs `UpdateManufactureOrderStatusHandler` with
  the old 7-arg signature, and no remaining reference to `WriteDownInventoryAsync` /
  `CaptureConditionsReadingAsync` exists outside a `ModuleBoundariesTests.cs` allowlist comment.
- **`ManufactureModule.cs`** — both services registered `AddScoped`, in the correct existing
  "Register application services" block, right after the two `IConfirm*Workflow` registrations —
  matches architecture-01's guidance exactly.
- **Two-file convention, verb-based naming (`WriteDownAsync`/`CaptureAsync`)** — both followed as
  approved.
- **Namespaces verified against actual source files** — `IManufactureCatalogSource`
  (`Anela.Heblo.Application.Features.Manufacture.Contracts`), `IManufacturedProductInventoryRepository`
  (`Anela.Heblo.Domain.Features.Manufacture.Inventory`), `IConditionsReadingProvider`
  (`Anela.Heblo.Domain.Features.Manufacture.Conditions`) — all `using` directives in the new files
  and tests are correct.
- **`ModuleBoundariesTests.cs`** — the two stale compiler-generated-type allowlist entries for the
  deleted `WriteDownInventoryAsync` are replaced with one base entry for
  `ManufactureInventoryWriteDownService -> CatalogAggregate`. Confirmed the "declaring-type fallback
  covers nested/compiler-generated types" mechanism is real and already used identically elsewhere
  in the same file (e.g. the `DataQuality -> Catalog` and `Catalog -> Manufacture` allowlists), so
  this isn't a new/unverified pattern.
- **Test split** — `ManufactureInventoryWriteDownServiceTests.cs` (8 tests) and
  `ManufactureConditionsCaptureServiceTests.cs` (4 tests) exercise the extracted logic directly
  against real service instances, mocking only the concern-specific dependencies. The two trimmed
  handler test files now mock `IManufactureInventoryWriteDownService` /
  `IManufactureConditionsCaptureService` and keep orchestration-only assertions (is the right
  service called, with the right arguments, under the right state-transition condition; is it
  *not* called otherwise). All non-inventory/non-conditions tests (state validation, field
  persistence, user-name resolution, error handling) are untouched. No test coverage was dropped —
  every relocated assertion has an equivalent at its new layer.

## Assessment

This is a faithful, low-risk Extract Class refactor that matches the approved architecture exactly:
no behavior change, no contract change, constructor dependencies now map one-to-one to a single
concern, and the test suite was split rather than weakened. No functional requirement is unmet, no
architecture deviation, no missing required test, and no correctness bug found in the moved logic
(confirmed by direct diff comparison against the original method bodies, not just by trusting the
development step's own description).

`dotnet` is unavailable in this sandbox (confirmed independently), so `dotnet build` /
`dotnet format` / `dotnet test` could not be executed here either, consistent with development-01's
own disclosure. This does not block approval of the code itself, but per the repo's own validation
rules these commands **must** be run before merging:
```
dotnet build
dotnet format --verify-no-changes
dotnet test --filter "FullyQualifiedName~Manufacture"
```

## Non-binding cleanup suggestions

- None. The change is intentionally minimal and matches the "surgical changes" / "verbatim move, no
  behavioral cleanup" guidance from CLAUDE.md and architecture-01 §3.
