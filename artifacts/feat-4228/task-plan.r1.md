# Remove stale IMarginCalculationService comment from AnalyticsModule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete the stale, incorrect comment in `AnalyticsModule.cs` that falsely documents an `IMarginCalculationService` dependency on `CatalogModule`.

**Architecture:** Single-line comment deletion, no behavioral change. Per `arch-review.r1.md`, this is a backend-only, no-UI, no-interface, no-migration change confined to one file.

**Tech Stack:** C# / .NET 8, xUnit (existing Analytics module test project, used only to confirm no regression).

---

### task: remove-stale-comment

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs:32`

- [ ] **Step 1: Confirm the exact current state of the target line**

Run:
```bash
grep -n "IMarginCalculationService is registered by CatalogModule" backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
```
Expected output: one match, at line 32:
```
32:        // Note: IMarginCalculationService is registered by CatalogModule and injected here
```
(If the line number has drifted, match on the comment text itself, not the line number — the acceptance criterion is the text, not the position.)

- [ ] **Step 2: Delete the stale comment line**

Before (lines 27–34 of `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`):
```csharp
        // Repository
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // Register refactored services for clean separation of concerns
        // Note: IMarginCalculationService is registered by CatalogModule and injected here
        services.AddScoped<IProductFilterService, ProductFilterService>();
        services.AddScoped<IReportBuilderService, ReportBuilderService>();
```

After:
```csharp
        // Repository
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();

        // Register refactored services for clean separation of concerns
        services.AddScoped<IProductFilterService, ProductFilterService>();
        services.AddScoped<IReportBuilderService, ReportBuilderService>();
```

Only the one comment line is removed. The `// Register refactored services for clean separation of concerns` comment immediately above stays, as do both `AddScoped` calls.

- [ ] **Step 3: Verify no other code was touched**

Run:
```bash
git diff backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
```
Expected: a single-line deletion (`-        // Note: IMarginCalculationService is registered by CatalogModule and injected here`), nothing else in the diff.

- [ ] **Step 4: Confirm the stale comment is gone and the real registration is untouched**

Run:
```bash
grep -n "IMarginCalculationService" backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs; echo "exit:$?"
grep -n "IMarginCalculator, MarginCalculator" backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
```
Expected:
- First command: no match, `grep` exits with status 1 (`exit:1`) — confirms `IMarginCalculationService` no longer appears anywhere in the file.
- Second command: one match, e.g. `47:        services.AddScoped<IMarginCalculator, MarginCalculator>();` — confirms the real Analytics margin-calculation registration is unchanged.

- [ ] **Step 5: Build to confirm no regressions**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Expected: `Build succeeded.` — a comment-only change cannot break compilation, but this confirms the file is still well-formed and no adjacent code was accidentally damaged.

- [ ] **Step 6: Run the Analytics module's existing tests**

Run:
```bash
dotnet test --filter "FullyQualifiedName~Analytics"
```
Expected: all Analytics-related tests pass, same pass count as before the change (no test asserts on the removed comment, since comments are not part of any test's observable behavior — this step only guards against an unrelated accidental edit).

- [ ] **Step 7: Format and commit**

Run:
```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
git add backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs
git commit -m "fix(analytics): remove stale IMarginCalculationService comment from AnalyticsModule

Analytics has no dependency on Catalog's IMarginCalculationService; it
uses its own self-registered IMarginCalculator. The comment described
a cross-module dependency that does not exist.

Fixes #4228"
```
Expected: `dotnet format` reports no further changes needed (the deletion itself doesn't introduce formatting issues); the commit succeeds with exactly the one-line diff from Step 3.

---

## Self-Review

**Spec coverage:** FR-1 ("Remove the stale comment") is fully covered by Steps 1–4 (locate, delete, verify diff scope, verify text is gone and real registration survives). NFR-1 ("No behavioral change") is covered by Steps 5–6 (build + existing test suite pass unchanged). No other spec sections (Data Model, API/Interface Design) apply — spec itself marks them "Not applicable."

**Placeholder scan:** No TBD/TODO/"add appropriate handling" phrasing. Every step shows the exact command and exact expected output/diff.

**Type consistency:** N/A — no new types, methods, or signatures are introduced; only pre-existing, verified identifiers (`IAnalyticsRepository`, `IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, `MarginCalculator`) are referenced, exactly as they appear in the current file.
