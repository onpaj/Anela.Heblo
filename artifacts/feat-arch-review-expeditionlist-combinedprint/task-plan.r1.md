Plan saved to `docs/superpowers/plans/2026-06-08-decouple-combinedprintqueuesink-from-di-keying.md`.

Five tasks, surgical scope:
1. **Add** `CombinedPrintQueueSink` in `Anela.Heblo.API/Features/ExpeditionList/` (no DI attributes, namespace mirrors the existing `Features/Users/` precedent).
2. **Rewrite** the `"Combined"` switch arm in `ServiceCollectionExtensions.cs` to use a factory delegate that resolves the two keyed sinks.
3. **Delete** the Application-layer copy and swap one `using` in the existing test file; grep-gate confirms `FromKeyedServices` is purged from `Anela.Heblo.Application`.
4. **Add** `CombinedPrintQueueSinkRegistrationTests` under `backend/test/Anela.Heblo.Tests/API/` (FR-5): asserts `Combined` resolves the composite, keyed `"azure"`/`"cups"` slots resolve their adapters, and `FileSystem` regression-guards the default arm.
5. **Validate**: `dotnet format`, `dotnet build`, full test suite, and the two architectural grep invariants from the arch-review.

Both arch-review amendments are applied: relocation target pinned to `API/Features/ExpeditionList/`, test placement under `API/`, and the side-effect comment updated to "the factory below."