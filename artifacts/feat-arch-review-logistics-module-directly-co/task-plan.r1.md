Plan saved to `docs/superpowers/plans/2026-06-02-decouple-logistics-from-catalog-transportboxcompletion.md`.

Eight TDD-style tasks, each with concrete code/diffs and explicit RED→GREEN→commit boundaries:

1. Add Logistics-owned contract types (`LogisticsStockOperationState`, `LogisticsStockOperationStatus`, `ILogisticsStockOperationQueryService`).
2. Write failing tests for `LogisticsStockOperationQueryAdapter` (including the enum-parity guard from arch-review amendment #3).
3. Implement the adapter to make tests green.
4. Register the adapter in `CatalogModule` with `AddTransient` (per arch-review amendment #1, matches the sibling write-side registration).
5. Update `TransportBoxCompletionServiceTests` to mock the new contract — deliberate build break.
6. Rewire `TransportBoxCompletionService` to consume the new query contract — build & tests green.
7. Remove the two pre-existing `LogisticsCatalogAllowlist` entries from `ModuleBoundariesTests` and confirm the existing arch test enforces the boundary (per arch-review amendment #2 — no new test method).
8. Full-solution validation: grep checks, `dotnet format`, `dotnet build`, `dotnet test`, DI smoke check.

All four arch-review amendments are explicitly incorporated and called out in the self-review section.