Plan saved to `docs/superpowers/plans/2026-06-04-orgchart-options-startup-validation.md`.

**Summary:** Four-task plan to add startup validation to `OrgChartOptions.DataSourceUrl`:

1. **Task 1** — Add `[Required(AllowEmptyStrings = false)]` to `OrgChartOptions.cs` (corrects the spec's incorrect claim about `[Required]` semantics on strings).
2. **Task 2** — Swap `services.Configure<T>(...)` for the standard `AddOptions<T>().Bind().ValidateDataAnnotations().ValidateOnStart()` chain in `OrgChartModule.cs`, matching nine sibling modules.
3. **Task 3** — New xUnit test file with three cases (missing key, empty string, valid URL) using `HostBuilder` so the `ValidateOnStart` `IHostedService` actually runs. Includes a one-shot regression-check step that proves the empty-string test depends on `AllowEmptyStrings = false`.
4. **Task 4** — Full solution build + test sweep to catch `WebApplicationFactory`-based tests that might silently rely on the old non-validating behavior, plus an optional manual smoke test of the failure mode.

The plan addresses all four spec FRs, all four NFRs, all four arch-review Specification Amendments, and the relevant risks (R-2, R-5) with conditional fallbacks for transitive package resolution.