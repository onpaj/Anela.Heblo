Plan saved to `docs/superpowers/plans/2026-06-03-decouple-manufacture-from-icatalogrepository.md`.

## Summary

The implementation plan covers eight tasks executing the spec + arch-review amendments in this strict order:

1. **Add `IManufactureCatalogSource` contract** (consumer-owned, `Application/Features/Manufacture/Contracts/`) — corrected `GetAllAsync` return to `Task<IEnumerable<CatalogAggregate>>` per arch-review §Amendments #1.
2. **Add `CatalogManufactureCatalogSourceAdapter` + 3 pass-through unit tests** (test-first) — name disambiguated against existing `ManufactureCatalogSourceAdapter`; tests added per arch-review §Amendments #3.
3. **Register adapter as `Scoped` in `CatalogModule`** — mirrors the symmetric `ManufactureModule.cs:59` line.
4. **Add `Manufacture -> Catalog` boundary rule with empty allowlist** — expected to fail; output saved for diffing.
5. **Migrate 11 Manufacture consumer files** (one sub-task each) — constructor parameter rename, field rename, call-site swap, `using` directive swap.
6. **Migrate ~10 Manufacture test files** — single-token `Mock<>` generic swap; test bodies unchanged because signatures match by design.
7. **Populate `ManufactureCatalogAllowlist`** from the post-migration boundary-test output, grouped by referenced type with explanatory `//` comments — never including `ICatalogRepository`.
8. **Full backend validation** — `dotnet build`, `dotnet format`, focused + full `dotnet test`, final grep guard. No frontend touched.

Pipeline note acknowledged — skipping the execution choice prompt.