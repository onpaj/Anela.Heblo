Now the self-review pass against the spec.

**Spec coverage check:**
- FR-1 (`InvoiceImportOptions` class with defaults 10/14) → Task 1.
- FR-2 (`services.Configure<>` in `AnalyticsModule` using existing section name) → Task 2, Step 1.
- FR-3 (handler swaps to `IOptions<>`, removes `IConfiguration` usings) → Task 3, Step 3.
- FR-4 (tests use `Options.Create`, explicit default-value test added) → Task 3, Step 1 (new `Handler_ShouldUseDefaultsWhenOptionsAreParameterless`).
- NFR-5 (build + format + tests green) → Task 4, Steps 2 & 3.
- Arch review amendments — `ConfigurationKey` constant (Task 1), module signature change explicit (Task 2), `ApplicationModule.cs:74` call-site edit explicit (Task 2 Step 2), no new package reference (header), Tile not touched (File Structure section).

**Placeholder scan:** No TBD / "implement later" / handwave validation steps. Every code step has full code. Every command has expected output.

**Type consistency:** `InvoiceImportOptions.ConfigurationKey`, `MinimumDailyThreshold`, `DefaultDaysBack` — used identically across all tasks. Constructor `IOptions<InvoiceImportOptions> invoiceImportOptions` matches between handler code (Task 3 Step 3) and test usage (Task 3 Step 1).

Plan saved to `artifacts/feat-arch-review-analytics-iconfiguration-inj/plan.r1.md`. Four tasks, each ending in a green build and a commit: (1) add `InvoiceImportOptions` POCO, (2) bind it in `AnalyticsModule` + update the single call site in `ApplicationModule.cs:74`, (3) swap the handler to `IOptions<>` and migrate all 4 existing tests + add a defaults-lock test, (4) final `dotnet format` / `dotnet build` / regression slice.