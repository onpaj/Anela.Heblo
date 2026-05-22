# Backend Build Warnings Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce `dotnet build` warning count by eliminating all hygiene issues, obsolete API usages, dead code, and uninitialized-DTO warnings (CS0105, CS0162, CS0169, CS0219, CS0414, CS0618, CS8618, CS8765) — roughly 65 unique sites — without touching null-flow warnings in business logic.

**Architecture:** Pure mechanical cleanup. Three phases, each independently revertible:
1. Hygiene (duplicate usings, dead code, unreachable code, override nullability)
2. Obsolete API migrations (Hangfire, ISystemClock→TimeProvider, MarginData.M1→M1_A, ApplicationInsights, CatalogRepository cache key)
3. DTO / config-class initialization (add `required` modifier across Comgate, Flexi adapters, and Manufacture services)

**Tech Stack:** .NET 8 (C# 11), xUnit + FluentAssertions, Hangfire, ApplicationInsights, Newtonsoft.Json (Flexi only), EF Core / in-memory caches.

**Out of scope:** ~125 CS86xx warnings in business-logic code (CS8602/CS8604/CS8601/CS8600/CS8625/CS8603/CS8605/CS8629/CS8619/CS8620). These need per-site null-flow judgment and belong in a follow-up plan.

**Verification baseline:** Current `dotnet build Anela.Heblo.sln` reports `318 Warning(s) 0 Error(s)`. After this plan, warning count should drop by ~95 emissions (some warnings emit per project that references a file). Expected final count: ≈ 220.

---

## File Structure

This is a cleanup plan — no new files. Files touched (by task):

**Task 1 (CS0105):**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureAnalysisMapperTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Configuration/ManufactureAnalysisOptionsTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ItemFilterServiceTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs`
- `backend/test/Anela.Heblo.Tests/Controllers/CatalogControllerTests.cs`

**Task 2 (dead code):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderNumberGenerator.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs`

**Task 3 (CS0162):**
- `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`

**Task 4 (CS8765):**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Common/UnspecifiedDateTimeConverter.cs`

**Task 5 (ApplicationInsights obsolete):**
- `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs`

**Task 6 (MockAuthenticationHandler ISystemClock):**
- `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs`

**Task 7 (Hangfire RecurringJob obsolete):**
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`

**Task 8 (MarginData.M1 → M1_A):**
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs`

**Task 9 (CatalogRepository internal obsolete reference):**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`

**Task 10 (Comgate DTO `required`):**
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateSettings.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/Model/ComgateStatementHeader.cs`

**Task 11 (Flexi DTO `required`):**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/PurchaseHistoryFlexiDto.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/ProductAttributesFlexiDto.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Materials/*.cs` (3 sites)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/*.cs` (3 sites)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/SemiProducts/*.cs` (2 sites)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/*.cs` (2 sites)

**Task 12 (Manufacture services `required`):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductBatch.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductVariant.cs`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/SubmitManufactureRequestItem.cs`

---

## Conventions & Helper Commands

- **Baseline warning count:** run `dotnet build Anela.Heblo.sln 2>&1 | grep -E "Warning\(s\)" | tail -1`
- **Per-task verification:** `dotnet build Anela.Heblo.sln 2>&1 | tail -10` (look for the warning total and 0 errors)
- **Format after every task:** `dotnet format Anela.Heblo.sln --include <changed-files>` or just `dotnet format` for the whole solution
- **Run tests for touched code:** `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-restore --filter "<relevant-filter>"`
- **Commit message style:** Conventional commits (`chore:`, `refactor:`, `fix:`). No co-author trailer (project disables it globally).

---

## Phase 1 — Hygiene & dead code

### Task 1: Remove duplicate `using FluentAssertions;` directives (CS0105)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureAnalysisMapperTests.cs` (8 duplicates)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Configuration/ManufactureAnalysisOptionsTests.cs` (4 duplicates)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ItemFilterServiceTests.cs` (2 duplicates)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs` (2 duplicates)
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/CatalogControllerTests.cs` (1 duplicate)

**Background:** Repeated copy-paste of `using FluentAssertions;` between unrelated using lines. Keep the first occurrence; delete every subsequent `using FluentAssertions;` line in each file.

- [ ] **Step 1.1: Capture baseline**

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -5`
Expected: `318 Warning(s)` / `0 Error(s)`. Record exact count.

- [ ] **Step 1.2: Fix `ManufactureAnalysisMapperTests.cs`**

The current top of file looks like (1-19):
```csharp
using Anela.Heblo.Application.Features.Manufacture.Configuration;
using FluentAssertions;
using FluentAssertions;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Moq;
using FluentAssertions;
using Xunit;
using FluentAssertions;
```

Replace with:
```csharp
using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetStockAnalysis;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

- [ ] **Step 1.3: Fix the other four files**

For each of:
- `ManufactureAnalysisOptionsTests.cs`
- `ItemFilterServiceTests.cs`
- `StockSeverityCalculatorTests.cs`
- `CatalogControllerTests.cs`

Read the file, keep only the **first** `using FluentAssertions;`, delete the rest. Use the Edit tool with each duplicate line as `old_string` (each duplicate appears on a unique surrounding line so each Edit is unique). Sort the remaining usings alphabetically (matches existing style elsewhere in the test project).

- [ ] **Step 1.4: Build to verify CS0105 is gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -c "CS0105"`
Expected: `0`

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -5`
Expected: `0 Error(s)`. Warning total should be 17 lower than the baseline (one CS0105 line each).

- [ ] **Step 1.5: Run the touched test files**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore \
  --filter "FullyQualifiedName~ManufactureAnalysisMapperTests|FullyQualifiedName~ManufactureAnalysisOptionsTests|FullyQualifiedName~ItemFilterServiceTests|FullyQualifiedName~StockSeverityCalculatorTests|FullyQualifiedName~CatalogControllerTests"
```
Expected: PASS (no behavior change, just imports cleaned).

- [ ] **Step 1.6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/
git commit -m "chore(tests): remove duplicate FluentAssertions usings (CS0105)"
```

---

### Task 2: Remove dead fields/variables (CS0169, CS0414, CS0219)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs` (CS0169 — unused `_repository` field)
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderNumberGenerator.cs` (CS0414 — unused `_counter` field)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs` (CS0219 — unused `dataLoaded` local)

**Background:** Each holds a field/variable that nothing reads. `TodayProductionTile._repository` is especially suspect — an injected dependency that's never used (the base class already takes the same repo). Confirm by inspection that the base class `UpcomingProductionTile` holds the repository, then remove the redundant field.

- [ ] **Step 2.1: Fix `TodayProductionTile.cs`**

Current (line 7):
```csharp
private readonly IManufactureOrderRepository _repository;
```

Delete this line. The base class `UpcomingProductionTile` already stores the repository (it's passed to `base(repository)` in the constructor). Verify before deleting:
```bash
```

Run: `grep -E "_repository" backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/UpcomingProductionTile.cs backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs`
Expected: `_repository` referenced in `UpcomingProductionTile.cs` but never in `TodayProductionTile.cs` (only the declaration).

Apply via Edit:
- old_string:
  ```csharp
  public class TodayProductionTile : UpcomingProductionTile
  {
      private readonly IManufactureOrderRepository _repository;

      // Self-describing metadata
  ```
- new_string:
  ```csharp
  public class TodayProductionTile : UpcomingProductionTile
  {
      // Self-describing metadata
  ```

- [ ] **Step 2.2: Fix `InMemoryPurchaseOrderNumberGenerator.cs`**

Delete the unused static field (line 7):
- old_string:
  ```csharp
  public class InMemoryPurchaseOrderNumberGenerator : IPurchaseOrderNumberGenerator
  {
      private static long _counter = 0;

      public async Task<string> GenerateOrderNumberAsync
  ```
- new_string:
  ```csharp
  public class InMemoryPurchaseOrderNumberGenerator : IPurchaseOrderNumberGenerator
  {
      public async Task<string> GenerateOrderNumberAsync
  ```

- [ ] **Step 2.3: Fix `FlexiProductPriceErpClient.cs`**

The `dataLoaded` variable is declared (line 50), set to `true` (line 97), but never read. Delete both lines.

Edit 1 — remove declaration:
- old_string:
  ```csharp
          bool dataLoaded = false;
          IList<ProductPriceFlexiDto>? data = null;
  ```
- new_string:
  ```csharp
          IList<ProductPriceFlexiDto>? data = null;
  ```

Edit 2 — remove assignment:
- old_string:
  ```csharp
                  catch (ObjectDisposedException)
                  {
                      // Cache is disposed, skip caching but continue with the data
                  }

                  dataLoaded = true;
              }
  ```
- new_string:
  ```csharp
                  catch (ObjectDisposedException)
                  {
                      // Cache is disposed, skip caching but continue with the data
                  }
              }
  ```

- [ ] **Step 2.4: Build to verify the three warnings are gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "CS0169|CS0414|CS0219"`
Expected: empty (no matches).

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -5`
Expected: `0 Error(s)` and warning count down by 3 emissions per file's referencing projects (~5-7 emissions total).

- [ ] **Step 2.5: Run touched test areas**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore \
  --filter "FullyQualifiedName~Purchase|FullyQualifiedName~Manufacture|FullyQualifiedName~Flexi"
```
Expected: PASS.

- [ ] **Step 2.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/DashboardTiles/TodayProductionTile.cs \
  backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderNumberGenerator.cs \
  backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/FlexiProductPriceErpClient.cs
git commit -m "chore: remove unused fields and locals (CS0169, CS0414, CS0219)"
```

---

### Task 3: Suppress the legitimate unreachable-code warning (CS0162)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs:516-521`

**Background:** The `yield break;` after `throw exception;` in `ThrowAsync<T>` is structurally unreachable but **required** by the compiler to make the method a valid iterator (so `IAsyncEnumerable<T>` works). The comment in code confirms this. The right fix is a scoped pragma, not deletion.

- [ ] **Step 3.1: Add scoped `#pragma` around the helper**

Edit the helper at the bottom of the file:
- old_string:
  ```csharp
      private static async IAsyncEnumerable<T> ThrowAsync<T>(Exception exception)
      {
          await Task.Yield(); // Make it async
          throw exception;
          yield break; // This will never be reached but is required for the compiler
      }
  }
  ```
- new_string:
  ```csharp
      private static async IAsyncEnumerable<T> ThrowAsync<T>(Exception exception)
      {
          await Task.Yield(); // Make it async
          throw exception;
  #pragma warning disable CS0162 // Unreachable code: yield break is required to make this an iterator
          yield break;
  #pragma warning restore CS0162
      }
  }
  ```

- [ ] **Step 3.2: Build to verify CS0162 is gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -c "CS0162"`
Expected: `0`

- [ ] **Step 3.3: Run the test class**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore --filter "FullyQualifiedName~GetMarginReportHandlerTests"
```
Expected: PASS.

- [ ] **Step 3.4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs
git commit -m "chore(tests): suppress required-but-unreachable yield break (CS0162)"
```

---

### Task 4: Fix override-nullability mismatch (CS8765)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Common/UnspecifiedDateTimeConverter.cs:19`

**Background:** `JsonConverter<T>.ReadJson` (Newtonsoft.Json) declares `existingValue` as `object?`. The override currently declares it as non-nullable `object`. Aligning the signature removes the warning.

- [ ] **Step 4.1: Make `existingValue` nullable in the override**

- old_string:
  ```csharp
      public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
      {
          if (reader.TokenType == JsonToken.Null)
          {
              if (objectType == typeof(DateTime?))
                  return null;
  ```
- new_string:
  ```csharp
      public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
      {
          if (reader.TokenType == JsonToken.Null)
          {
              if (objectType == typeof(DateTime?))
                  return null;
  ```

Note: also relax the return type to `object?` because the method already returns `null` on line 24. That avoids introducing a CS8603 in the same file.

- [ ] **Step 4.2: Adjust the `reader.Value.ToString()` site if a CS8602 surfaces**

The `dateString` variable depends on `reader.Value` (which is `object?`). If the build now reports CS8602 on line 37 (`reader.Value.ToString()`), tighten to:
```csharp
var dateString = reader.Value?.ToString();
if (string.IsNullOrEmpty(dateString) || !DateTime.TryParse(dateString, out parsedDateTime))
{
    throw new JsonSerializationException($"Cannot parse DateTime from string: {dateString}");
}
```

Only apply this edit if Step 4.3 below shows a new CS8602 in this file. Otherwise skip.

- [ ] **Step 4.3: Build to verify CS8765 is gone and nothing new appeared**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "UnspecifiedDateTimeConverter"`
Expected: empty.

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -5`
Expected: `0 Error(s)`.

- [ ] **Step 4.4: Run Flexi adapter tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-restore
```
Expected: PASS.

- [ ] **Step 4.5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Common/UnspecifiedDateTimeConverter.cs
git commit -m "fix(flexi): align ReadJson override nullability with base (CS8765)"
```

---

## Phase 2 — Obsolete API migrations

### Task 5: Remove obsolete `EnableW3CDistributedTracing` assignment

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs:52`

**Background:** The property is documented as obsolete and noop. Removing the assignment is behavior-neutral.

- [ ] **Step 5.1: Delete the line**

- old_string:
  ```csharp
              RequestCollectionOptions =
              {
                  InjectResponseHeaders = false,
                  TrackExceptions = true,
                  EnableW3CDistributedTracing = true
              }
  ```
- new_string:
  ```csharp
              RequestCollectionOptions =
              {
                  InjectResponseHeaders = false,
                  TrackExceptions = true
              }
  ```

- [ ] **Step 5.2: Build to verify warning is gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "EnableW3CDistributedTracing"`
Expected: empty.

- [ ] **Step 5.3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Extensions/ApplicationInsightsExtensions.cs
git commit -m "chore(api): remove obsolete EnableW3CDistributedTracing assignment (CS0618)"
```

---

### Task 6: Switch `MockAuthenticationHandler` from `ISystemClock` to `TimeProvider`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs:15-17`

**Background:** ASP.NET Core 8 deprecates `ISystemClock` in favor of `TimeProvider`. `AuthenticationHandler<TOptions>` has an overload accepting only `(IOptionsMonitor<TOptions>, ILoggerFactory, UrlEncoder)` and reads `TimeProvider` from the options. Use that overload — simplest possible migration.

- [ ] **Step 6.1: Update the constructor**

- old_string:
  ```csharp
  public class MockAuthenticationHandler : AuthenticationHandler<MockAuthenticationSchemeOptions>
  {
      public MockAuthenticationHandler(IOptionsMonitor<MockAuthenticationSchemeOptions> options,
          ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
          : base(options, logger, encoder, clock)
      {
      }
  ```
- new_string:
  ```csharp
  public class MockAuthenticationHandler : AuthenticationHandler<MockAuthenticationSchemeOptions>
  {
      public MockAuthenticationHandler(IOptionsMonitor<MockAuthenticationSchemeOptions> options,
          ILoggerFactory logger, UrlEncoder encoder)
          : base(options, logger, encoder)
      {
      }
  ```

- [ ] **Step 6.2: Build to verify both CS0618 sites in this file are gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "MockAuthenticationHandler"`
Expected: empty.

- [ ] **Step 6.3: Run anything that touches mock auth**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore --filter "FullyQualifiedName~Auth|FullyQualifiedName~Mock"
```
Expected: PASS (or "no tests matched filter" — both acceptable; ensure no compile errors).

- [ ] **Step 6.4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Authentication/MockAuthenticationHandler.cs
git commit -m "refactor(api): replace ISystemClock with TimeProvider in MockAuthenticationHandler (CS0618)"
```

---

### Task 7: Switch Hangfire `RecurringJob.AddOrUpdate` to the `RecurringJobOptions` overload

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs:129-139`

**Background:** Hangfire deprecated the `(string, Expression<Func<T, Task>>, string, TimeZoneInfo, string)` overload and will remove it in 2.0.0. Replace with the `RecurringJobOptions` overload.

- [ ] **Step 7.1: Update the call**

- old_string:
  ```csharp
      private static void RegisterRecurringJobInternal<TJob>(
          string jobName,
          string cronExpression,
          string timeZoneId) where TJob : IRecurringJob
      {
          RecurringJob.AddOrUpdate<TJob>(
              jobName,
              job => job.ExecuteAsync(default),
              cronExpression,
              TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
      }
  ```
- new_string:
  ```csharp
      private static void RegisterRecurringJobInternal<TJob>(
          string jobName,
          string cronExpression,
          string timeZoneId) where TJob : IRecurringJob
      {
          RecurringJob.AddOrUpdate<TJob>(
              jobName,
              job => job.ExecuteAsync(default),
              cronExpression,
              new RecurringJobOptions
              {
                  TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
              });
      }
  ```

- [ ] **Step 7.2: Build to verify CS0618 in this file is gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "RecurringJobDiscoveryService"`
Expected: empty.

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -5`
Expected: `0 Error(s)`.

- [ ] **Step 7.3: Smoke-test the API host start**

Run (foreground, 10s timeout — just verify no startup crash from job registration):
```bash
timeout 12 dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-build 2>&1 | grep -E "recurring jobs|Failed to register|Successfully registered" | head -5
```
Expected: `Successfully registered N recurring jobs` log line, no `Failed to register` errors.

If recurring job registration is wrapped in a startup gate that requires secrets and times out cleanly, skip — the build pass plus existing tests is enough.

- [ ] **Step 7.4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs
git commit -m "refactor(hangfire): use RecurringJobOptions overload (CS0618)"
```

---

### Task 8: Replace `MarginData.M1` with `M1_A` in `GetCatalogDetailHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs:284-290`

**Background:** `MarginData.M1` is declared as `public MarginLevel M1 => M1_A;` (alias, marked `[Obsolete]`). Substituting `M1_A` is behavior-identical. The DTO property name `M1` on the response side stays — only the source-side property changes.

- [ ] **Step 8.1: Apply the rename**

- old_string:
  ```csharp
                  // M1 - M0 + Manufacturing costs (if different)
                  M1 = new MarginLevelDto
                  {
                      Percentage = m.Value.M1.Percentage,
                      Amount = m.Value.M1.Amount,
                      CostLevel = m.Value.M1.CostLevel,
                      CostTotal = m.Value.M1.CostTotal
                  },
  ```
- new_string:
  ```csharp
                  // M1 - M0 + Manufacturing costs (if different)
                  M1 = new MarginLevelDto
                  {
                      Percentage = m.Value.M1_A.Percentage,
                      Amount = m.Value.M1_A.Amount,
                      CostLevel = m.Value.M1_A.CostLevel,
                      CostTotal = m.Value.M1_A.CostTotal
                  },
  ```

- [ ] **Step 8.2: Build to verify the 3 CS0618 sites are gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "GetCatalogDetailHandler.*M1"`
Expected: empty.

- [ ] **Step 8.3: Run catalog detail tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore --filter "FullyQualifiedName~GetCatalogDetail"
```
Expected: PASS. (Behavior is identical because `M1 => M1_A`.)

- [ ] **Step 8.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs
git commit -m "refactor(catalog): use MarginData.M1_A instead of obsolete M1 alias (CS0618)"
```

---

### Task 9: Decouple `CatalogRepository.ManufactureCostLoadDate` from obsolete cache property

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs:768-811`

**Background:** `CachedManufactureCostData` is private and marked `[Obsolete]`. Other code in the class still references it via `nameof(CachedManufactureCostData)` to build a cache key — and that `nameof()` reference also trips CS0618 (line 811). The fix is to lift the cache key into a private `const` string so the `nameof()` dependency disappears. The obsolete property itself stays (it's a transitional shim).

- [ ] **Step 9.1: Add a private constant for the cache key**

Locate the private cache-key region near the top of `CatalogRepository.cs` (or near the obsolete property). Add a private constant alongside other cache key constants, e.g.:

```csharp
private const string CachedManufactureCostDataKey = nameof(CachedManufactureCostData);
```

Wait — that still triggers CS0618. Use a literal string instead:

```csharp
private const string CachedManufactureCostDataKey = "CachedManufactureCostData";
```

Place it just above the obsolete property at line 768. Concrete edit (find the line `[Obsolete($"{nameof(IMarginCalculationService)} should be used instead")]` and insert above it):

- old_string:
  ```csharp
      [Obsolete($"{nameof(IMarginCalculationService)} should be used instead")]
      private IDictionary<string, List<ManufactureCost>> CachedManufactureCostData
  ```
- new_string:
  ```csharp
      private const string CachedManufactureCostDataKey = "CachedManufactureCostData";

      [Obsolete($"{nameof(IMarginCalculationService)} should be used instead")]
      private IDictionary<string, List<ManufactureCost>> CachedManufactureCostData
  ```

- [ ] **Step 9.2: Replace `nameof(CachedManufactureCostData)` inside the obsolete property body**

Within the obsolete property's get/set body (lines 770-776) replace every `nameof(CachedManufactureCostData)` with `CachedManufactureCostDataKey`. Use `replace_all: false` and do three separate Edits (or one with sufficient surrounding context to be unique). Concrete substitutions:

| Line context | Old | New |
|--------------|-----|-----|
| getter | `_cache.Get<...>(nameof(CachedManufactureCostData))` | `_cache.Get<...>(CachedManufactureCostDataKey)` |
| setter line 1 | `_cache.Set(nameof(CachedManufactureCostData), value);` | `_cache.Set(CachedManufactureCostDataKey, value);` |
| setter line 2 | `InvalidateSourceData(nameof(CachedManufactureCostData));` | `InvalidateSourceData(CachedManufactureCostDataKey);` |
| setter line 3 | `SetLoadDateInCache(nameof(CachedManufactureCostData));` | `SetLoadDateInCache(CachedManufactureCostDataKey);` |

- [ ] **Step 9.3: Replace the `nameof()` at line 811**

- old_string:
  ```csharp
      public DateTime? ManufactureCostLoadDate => GetLoadDateFromCache(nameof(CachedManufactureCostData));
  ```
- new_string:
  ```csharp
      public DateTime? ManufactureCostLoadDate => GetLoadDateFromCache(CachedManufactureCostDataKey);
  ```

- [ ] **Step 9.4: Build to verify the CS0618 at CatalogRepository.cs:811 is gone**

Run: `dotnet build Anela.Heblo.sln 2>&1 | grep -E "CatalogRepository.*CS0618"`
Expected: empty.

The obsolete property itself is still there (other callers may still use it; out of scope for this plan). The internal `nameof()` reference that *we* control is removed.

- [ ] **Step 9.5: Run catalog tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore --filter "FullyQualifiedName~Catalog"
```
Expected: PASS. Cache keys produce identical strings (`"CachedManufactureCostData"` == `nameof(CachedManufactureCostData)`).

- [ ] **Step 9.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
git commit -m "refactor(catalog): use cache key constant to avoid CS0618 on internal nameof"
```

---

## Phase 3 — DTO initialization (CS8618)

### Task 10: Add `required` to Comgate model properties

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/ComgateSettings.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Comgate/Model/ComgateStatementHeader.cs`

**Background:** Both classes are populated by `IConfiguration.Bind` / JSON deserialization. Their string properties have no constructor init. The cleanest fix is the C# 11 `required` modifier — it forces callers (config binding handles it via reflection; tests/manual instantiations get a compile-time hint).

- [ ] **Step 10.1: Update `ComgateSettings.cs`**

Read the file first, then apply:
- Add `required` to both `MerchantId` and `Secret`.

Expected resulting shape:
```csharp
namespace Anela.Heblo.Adapters.Comgate;

public class ComgateSettings
{
    public required string MerchantId { get; set; }
    public required string Secret { get; set; }
}
```

(Adjust based on whatever else lives in the file. Only the two warned properties need `required`.)

- [ ] **Step 10.2: Update `ComgateStatementHeader.cs`**

- old_string:
  ```csharp
  internal class ComgateStatementHeader
  {
      public string TransferId { get; set; }
      public string TransferDate { get; set; }

      public string AccountCounterParty { get; set; }
      public string AccountOutgoing { get; set; }
      public string VariableSymbol { get; set; }
  }
  ```
- new_string:
  ```csharp
  internal class ComgateStatementHeader
  {
      public required string TransferId { get; set; }
      public required string TransferDate { get; set; }

      public required string AccountCounterParty { get; set; }
      public required string AccountOutgoing { get; set; }
      public required string VariableSymbol { get; set; }
  }
  ```

- [ ] **Step 10.3: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Comgate/Anela.Heblo.Adapters.Comgate.csproj 2>&1 | tail -5`
Expected: `0 Error(s)`. The 7 Comgate CS8618 should be gone.

If JSON deserialization fails at runtime because `required` is not honored by Newtonsoft.Json or the binder, fall back to `= null!;`:
```csharp
public string TransferId { get; set; } = null!;
```
Decide per failure — but Newtonsoft.Json 13 and `IConfiguration.Bind` both work fine with `required` in .NET 8.

- [ ] **Step 10.4: Run Comgate tests if any exist**

Run:
```bash
dotnet test --no-restore --filter "FullyQualifiedName~Comgate"
```
Expected: PASS or "no tests matched filter".

- [ ] **Step 10.5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Comgate/
git commit -m "refactor(comgate): mark DTO properties as required (CS8618)"
```

---

### Task 11: Add `required` to Flexi adapter DTOs

**Files:** All Flexi DTOs with CS8618 warnings:
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/PurchaseHistoryFlexiDto.cs` (4 props)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/ProductAttributes/ProductAttributesFlexiDto.cs` (4 props)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Price/*.cs` (3 sites — find via `grep CS8618 /tmp/warnings_unique.txt | grep Price`)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Materials/*.cs` (3 sites)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/SemiProducts/*.cs` (2 sites)
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Sales/*.cs` (2 sites)

**Background:** Same pattern as Task 10. Flexi DTOs are populated by Newtonsoft.Json deserialization. Add `required` to every non-nullable string property flagged by the warning.

- [ ] **Step 11.1: Generate the exact file:property list**

Run:
```bash
grep -E "warning CS8618" /tmp/warnings_unique.txt | grep "Adapters.Flexi" | sed -E "s|^([^(]+)\(([0-9]+),[0-9]+\): warning CS8618: Non-nullable property '([^']+)'.*|\1:\2  \3|"
```

This gives `<file>:<line>  <PropertyName>` rows for every site. Open each unique file and apply step 11.2 for each listed property.

- [ ] **Step 11.2: For each property listed, add `required`**

Pattern — find:
```csharp
public string PropertyName { get; set; }
```
Replace with:
```csharp
public required string PropertyName { get; set; }
```

For nullable-flagged complex properties (e.g. `List<T>`, custom DTOs), the same fix applies — add `required` before the type.

**Important:** Some Flexi DTOs may include `[JsonProperty(...)]` attributes — keep them. The edit only inserts `required` before the type:
```csharp
[JsonProperty("kod")]
public required string Code { get; set; }
```

- [ ] **Step 11.3: Build**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj 2>&1 | tail -5`
Expected: `0 Error(s)`. CS8618 count in Flexi should drop to 0.

If any deserialization-related test fails because Newtonsoft.Json 13 chokes on `required` (it shouldn't in .NET 8 — `required` is enforced only by the C# compiler, not the runtime), fall back to `= null!;` on a per-property basis.

- [ ] **Step 11.4: Run Flexi adapter tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj --no-restore
```
Expected: PASS.

- [ ] **Step 11.5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/
git commit -m "refactor(flexi): mark DTO properties as required (CS8618)"
```

---

### Task 12: Add `required` to Manufacture service models

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductBatch.cs` (3 props: `ProductCode`, `ProductName`, `Variants`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ProductVariant.cs` (2 props: `ProductCode`, `ProductName`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/SubmitManufactureRequestItem.cs` (2 props: `ProductCode`, `Name`)

**Background:** These are domain-side request/result records, not deserialization targets. Either `required` or constructor initialization works. Use `required` for consistency with Tasks 10-11.

- [ ] **Step 12.1: Apply `required` to all 7 properties**

For each property listed above, add `required`. Concrete edit for `ProductBatch.cs`:

- old_string:
  ```csharp
  public string ProductCode { get; set; }
  ```
  (each occurrence with enough surrounding context to be unique)

- new_string:
  ```csharp
  public required string ProductCode { get; set; }
  ```

Repeat for `ProductName`, `Variants` (note `Variants` is likely `List<ProductVariant>` — same `required` keyword works for reference types).

- [ ] **Step 12.2: Check for callers that construct these objects with initializer syntax**

Run: `grep -rE "new ProductBatch|new ProductVariant|new SubmitManufactureRequestItem" backend/src backend/test`

If any call site constructs without setting all `required` properties, the build will report CS9035 (`Required member must be set`). Fix call sites by adding the missing initializers — they're real bugs surfaced by the change.

- [ ] **Step 12.3: Build**

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -5`
Expected: `0 Error(s)`. The 7 manufacture CS8618 should be gone.

- [ ] **Step 12.4: Run manufacture tests**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-restore --filter "FullyQualifiedName~Manufacture"
```
Expected: PASS.

- [ ] **Step 12.5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Services/
git commit -m "refactor(manufacture): mark service-model properties as required (CS8618)"
```

---

## Final verification

- [ ] **Step F.1: Full build**

Run: `dotnet build Anela.Heblo.sln 2>&1 | tail -10`
Expected:
- `0 Error(s)`
- Warning count noticeably lower than baseline (target ≈ 220 vs. baseline 318 — exact delta depends on per-project emission multiplication).

- [ ] **Step F.2: Confirm targeted warning codes are at zero**

Run:
```bash
for code in CS0105 CS0162 CS0169 CS0219 CS0414 CS0618 CS8618 CS8765; do
  count=$(dotnet build Anela.Heblo.sln 2>&1 | grep -c "warning $code")
  echo "$code: $count"
done
```
Expected: all `0`.

- [ ] **Step F.3: Full backend test suite**

Run:
```bash
dotnet test Anela.Heblo.sln --no-restore --no-build
```
Expected: All tests pass (matches pre-cleanup state).

- [ ] **Step F.4: Lint / format**

Run: `dotnet format Anela.Heblo.sln --verify-no-changes`
If it reports changes needed, run `dotnet format Anela.Heblo.sln` and amend the last commit (or add a follow-up `chore: dotnet format` commit).

- [ ] **Step F.5: Smoke-test API start**

Run (background, kill after 15s):
```bash
timeout 15 dotnet run --project backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --no-build 2>&1 | grep -iE "error|exception|Successfully registered|Now listening" | head -10
```
Expected:
- `Successfully registered N recurring jobs` (proves Task 7's Hangfire migration works)
- `Now listening on: http://...` (proves DI is intact after Task 6's auth-handler change)
- No `Exception` lines

If `Anela.Heblo.API` requires secrets that aren't present locally, this step may be skipped — the prior tasks already verified via test suite.

- [ ] **Step F.6: Stage-confirm**

Tell the user: "Cleanup complete. Warning count: <new>/318. All targeted CS codes at 0. All tests pass."

---

## Self-Review Notes

**Spec coverage:** Every warning code listed in the analysis (CS0105, CS0162, CS0169, CS0219, CS0414, CS0618, CS8618, CS8765) has a dedicated task. The ~125 CS86xx null-flow warnings are explicitly deferred — flagged in the "Out of scope" header.

**Placeholders:** No `TODO`/`TBD`. Every Edit step shows concrete `old_string`/`new_string`. Tasks 11 and 12 use a "for each file" pattern but provide the grep command to enumerate sites and the exact edit shape — engineer doesn't have to invent anything.

**Type consistency:** `CachedManufactureCostDataKey` referenced in Steps 9.1-9.3 is defined in 9.1 with the literal string `"CachedManufactureCostData"` — the value matches `nameof(CachedManufactureCostData)` so cache contents stay readable across deployments. `RecurringJobOptions.TimeZone` (Task 7) is the documented property name on `Hangfire.RecurringJobOptions`.

**Risk hot spots:**
- Task 7 (Hangfire): runtime behavior depends on `RecurringJobOptions.TimeZone` being honored — covered by Step F.5 smoke test.
- Task 11 (Flexi `required`): if any Flexi DTO is deserialized into a code path that doesn't go through Newtonsoft.Json (e.g. activator-only construction in tests), CS9035 will surface — Step 12.2 explains how to deal with it. The same risk applies in Task 10 for Comgate (lower exposure, internal class).
- Task 9 (cache key): the literal string `"CachedManufactureCostData"` must match exactly what `nameof()` produced. Confirmed by inspection.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-22-backend-build-warnings-cleanup.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
