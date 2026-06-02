Plan saved to `docs/superpowers/plans/2026-06-02-grid-layout-malformed-json.md`.

**Summary:** Four tasks, TDD-driven, all confined to `GetGridLayoutHandler.cs` and its test file:

1. **Task 1** — Add the malformed-JSON test (`"{not json"`), watch it fail, add the inner `try`/`catch (JsonException)` with the `LogWarning("Malformed LayoutJson for user={UserId} gridKey={GridKey}; returning null layout", ...)` template, watch it pass.
2. **Task 2** — Add the empty-string test (`string.Empty`); already covered by Task 1's catch — passes without further code changes.
3. **Task 3** — Add the literal-`"null"` test, watch it fail with NRE (because Task 1 dropped the `?? new GridLayoutDto()` fallback), add the explicit `if (dto is null) return null` guard, watch it pass. The full method body is shown in this step.
4. **Task 4** — `dotnet build`, `dotnet format`, re-run the test class, diff-scope sanity check (must only touch the two files per FR-5), commit.

Spec coverage is mapped at the bottom of the plan: every FR-1..5 and NFR-1..4 ties to a specific task/step. The arch-review amendments (explicit `dto is null` branch with no log; exact log template phrasing) are baked into Task 3 and Task 1 respectively.