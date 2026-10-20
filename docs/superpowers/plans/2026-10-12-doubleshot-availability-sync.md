# Double-shot Availability Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A recurring job that keeps each double-shot `V` variant's Shoptet stock equal to its `S` mirror set's computed stock, dry-run until a feature flag is switched on.

**Architecture:** A pure Domain helper maps `V`↔`S` codes (shared with #4480). A pure Application planner turns the Shoptet stock CSV into adjustments. An `IRecurringJob` reads the CSV once, plans, and either logs (flag off) or writes relative `amountChange` movements per `V` (flag on).

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, Hangfire (`IRecurringJob` auto-discovery), Microsoft.FeatureManagement / `IFeatureFlagChecker`.

**Spec:** `docs/superpowers/specs/2026-10-12-doubleshot-availability-sync-design.md`

## Global Constraints

- Feature flag key: `is-doubleshot-availability-sync-enabled`, default `false`; off = dry run (log only, no writes). Going live = switching the flag on, nothing else.
- Job id `doubleshot-availability-sync`, category `Catalog`, cron `*/15 * * * *`, `DefaultIsEnabled = true`.
- Variant code pattern `^SA\d+V$`, set code pattern `^SA\d+S$`; mapping swaps only the final letter.
- Target = `max(S.Stock, 0)`; write = `IEshopStockClient.UpdateStockAsync(vCode, target - current)`.
- Never write to an `S` code. Never write `realStock`.
- No live Shoptet writes during development — all Shoptet I/O in tests is mocked.
- DTO rule (classes, not records) applies only to API contracts; internal types here may be records.
- Commits: conventional (`feat:`, `test:`, `docs:`), ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test runs: build first, then `dotnet test --no-build -p:UseSharedCompilation=false` (concurrent worktrees hang otherwise). Solution is at repo root.

## Review Focus

1. A `V` row with no `S` row in the CSV (mirror deleted/renamed in Shoptet) → skipped with a warning, never written to 0.
2. Duplicate codes in the CSV (same `S` code twice) → planner must not throw (`ToDictionary` would); first row wins.
3. Fractional stock in CSV (`decimal`) → delta computed in decimal, converted to double only at the write.
4. CSV read throws (timeout / 5xx after retries) → job logs error and returns, no writes, no unhandled exception.
5. Cancellation mid-run → `OperationCanceledException` propagates (not swallowed as a per-item failure).

---

### Task 1: `DoubleShotCode` — shared V↔S mapping (Domain)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShots/DoubleShotCode.cs`
- Test: `backend/test/Anela.Heblo.Tests/Domain/Catalog/DoubleShotCodeTests.cs`

**Interfaces:**
- Produces: `public static class DoubleShotCode` in namespace `Anela.Heblo.Domain.Features.Catalog.DoubleShots` with
  `bool IsVariantCode(string? code)`, `bool IsSetCode(string? code)`, `string? ToSetCode(string? variantCode)`, `string? ToVariantCode(string? setCode)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Catalog.DoubleShots;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class DoubleShotCodeTests
{
    [Theory]
    [InlineData("SA015030V", true)]
    [InlineData("SA014005V", true)]
    [InlineData("SA017100V", true)]
    [InlineData("BAL0001V", false)]
    [InlineData("SA015030S", false)]
    [InlineData("SA001", false)]
    [InlineData("SA015030", false)]
    [InlineData("sa015030v", false)]
    [InlineData("SAV", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsVariantCode_MatchesOnlySaDigitsV(string? code, bool expected)
    {
        DoubleShotCode.IsVariantCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("SA015030S", true)]
    [InlineData("SA015030V", false)]
    [InlineData("SA001", false)]
    [InlineData(null, false)]
    public void IsSetCode_MatchesOnlySaDigitsS(string? code, bool expected)
    {
        DoubleShotCode.IsSetCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData("SA015030V", "SA015030S")]
    [InlineData("SA014100V", "SA014100S")]
    public void ToSetCode_SwapsFinalLetter(string variant, string expected)
    {
        DoubleShotCode.ToSetCode(variant).Should().Be(expected);
    }

    [Theory]
    [InlineData("BAL0001V")]
    [InlineData("SA015030S")]
    [InlineData("")]
    [InlineData(null)]
    public void ToSetCode_ReturnsNull_ForNonVariantCodes(string? code)
    {
        DoubleShotCode.ToSetCode(code).Should().BeNull();
    }

    [Fact]
    public void ToVariantCode_SwapsFinalLetter()
    {
        DoubleShotCode.ToVariantCode("SA016005S").Should().Be("SA016005V");
    }

    [Theory]
    [InlineData("SA016005V")]
    [InlineData("SA001")]
    [InlineData(null)]
    public void ToVariantCode_ReturnsNull_ForNonSetCodes(string? code)
    {
        DoubleShotCode.ToVariantCode(code).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false` (from repo root)
Expected: build FAILS — `DoubleShotCode` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.Catalog.DoubleShots;

/// <summary>
/// Code convention for double-shot sets. The e-shop shows each double-shot as a variant product
/// (<c>SA015030V</c>); its mirror product set (<c>SA015030S</c>) carries the real composition.
/// The two codes differ only in the final letter. Pure mapping — callers must check the mirror
/// actually exists in their own data. Shared by the availability sync (#4479) and the order swap (#4480).
/// </summary>
public static partial class DoubleShotCode
{
    private const char VariantSuffix = 'V';
    private const char SetSuffix = 'S';

    [GeneratedRegex(@"^SA\d+V$")]
    private static partial Regex VariantPattern();

    [GeneratedRegex(@"^SA\d+S$")]
    private static partial Regex SetPattern();

    public static bool IsVariantCode(string? code) =>
        !string.IsNullOrEmpty(code) && VariantPattern().IsMatch(code);

    public static bool IsSetCode(string? code) =>
        !string.IsNullOrEmpty(code) && SetPattern().IsMatch(code);

    public static string? ToSetCode(string? variantCode) =>
        IsVariantCode(variantCode) ? variantCode![..^1] + SetSuffix : null;

    public static string? ToVariantCode(string? setCode) =>
        IsSetCode(setCode) ? setCode![..^1] + VariantSuffix : null;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~DoubleShotCodeTests"`
Expected: all PASS.

- [ ] **Step 5: Commit, then link the commit on #4477**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShots/DoubleShotCode.cs backend/test/Anela.Heblo.Tests/Domain/Catalog/DoubleShotCodeTests.cs
git commit -m "feat: shared double-shot V<->S code mapping (#4479)"
git push -u origin feature/doubleshot-availability-sync
gh issue comment 4477 --repo onpaj/Anela.Heblo --body "\`DoubleShotCode\` landed in commit <sha> on \`feature/doubleshot-availability-sync\` — #4480 can cherry-pick it."
```

---

### Task 2: `DoubleShotStockPlanner` — pairing + target computation (Application, pure)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/DoubleShots/DoubleShotStockPlanner.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/DoubleShots/DoubleShotStockPlannerTests.cs`

**Interfaces:**
- Consumes: `DoubleShotCode.IsVariantCode`, `DoubleShotCode.ToSetCode` (Task 1); `Anela.Heblo.Domain.Features.Catalog.Stock.EshopStock` (`Code`, `Stock` decimal).
- Produces (namespace `Anela.Heblo.Application.Features.Catalog.DoubleShots`):
  - `public sealed record DoubleShotStockAdjustment(string VariantCode, string SetCode, decimal Current, decimal Target) { public decimal Delta => Target - Current; }`
  - `public sealed record DoubleShotStockPlan(IReadOnlyList<DoubleShotStockAdjustment> Adjustments, int PairCount, IReadOnlyList<string> UnpairedVariantCodes)`
  - `public static class DoubleShotStockPlanner { public static DoubleShotStockPlan Plan(IReadOnlyList<EshopStock> stock); }`

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Application.Features.Catalog.DoubleShots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DoubleShots;

public class DoubleShotStockPlannerTests
{
    private static EshopStock Row(string code, decimal stock) => new() { Code = code, Stock = stock };

    [Fact]
    public void Plan_EmitsAdjustment_WhenVariantDiffersFromMirror()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0), Row("SA015030S", 213)]);

        plan.PairCount.Should().Be(1);
        plan.Adjustments.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { VariantCode = "SA015030V", SetCode = "SA015030S", Current = 0m, Target = 213m, Delta = 213m });
    }

    [Fact]
    public void Plan_EmitsNoAdjustment_WhenAlreadyEqual()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 213), Row("SA015030S", 213)]);

        plan.PairCount.Should().Be(1);
        plan.Adjustments.Should().BeEmpty();
    }

    [Fact]
    public void Plan_ClampsNegativeMirrorToZero()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 5), Row("SA015030S", -3)]);

        plan.Adjustments.Should().ContainSingle().Which.Target.Should().Be(0m);
        plan.Adjustments[0].Delta.Should().Be(-5m);
    }

    [Fact]
    public void Plan_LowersVariant_WhenMirrorDropped()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA014005V", 400), Row("SA014005S", 396)]);

        plan.Adjustments.Should().ContainSingle().Which.Delta.Should().Be(-4m);
    }

    [Fact]
    public void Plan_RaisesNegativeVariant_ToMirror()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA014005V", -2), Row("SA014005S", 10)]);

        plan.Adjustments.Should().ContainSingle().Which.Delta.Should().Be(12m);
    }

    [Fact]
    public void Plan_ReportsUnpairedVariant_AndDoesNotAdjustIt()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA099030V", 7)]);

        plan.PairCount.Should().Be(0);
        plan.Adjustments.Should().BeEmpty();
        plan.UnpairedVariantCodes.Should().Equal("SA099030V");
    }

    [Fact]
    public void Plan_IgnoresNonDoubleShotCodes()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("BAL0001V", 1000), Row("BAL0001S", 3), Row("SA001", 28), Row("SA015030S", 213)]);

        plan.PairCount.Should().Be(0);
        plan.Adjustments.Should().BeEmpty();
        plan.UnpairedVariantCodes.Should().BeEmpty();
    }

    [Fact]
    public void Plan_NeverTargetsSetCodes()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0), Row("SA015030S", 213), Row("SA016005V", 1), Row("SA016005S", 385)]);

        plan.Adjustments.Select(a => a.VariantCode).Should().OnlyContain(c => c.EndsWith('V'));
    }

    [Fact]
    public void Plan_ToleratesDuplicateCodes_FirstRowWins()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0), Row("SA015030S", 213), Row("SA015030S", 1), Row("SA015030V", 99)]);

        plan.PairCount.Should().Be(1);
        plan.Adjustments.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Current = 0m, Target = 213m });
    }

    [Fact]
    public void Plan_KeepsFractionalStockExact()
    {
        var plan = DoubleShotStockPlanner.Plan([Row("SA015030V", 0.5m), Row("SA015030S", 2.25m)]);

        plan.Adjustments.Should().ContainSingle().Which.Delta.Should().Be(1.75m);
    }

    [Fact]
    public void Plan_ReturnsEmpty_ForEmptyInput()
    {
        var plan = DoubleShotStockPlanner.Plan([]);

        plan.PairCount.Should().Be(0);
        plan.Adjustments.Should().BeEmpty();
        plan.UnpairedVariantCodes.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: build FAILS — `DoubleShotStockPlanner` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using Anela.Heblo.Domain.Features.Catalog.DoubleShots;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.DoubleShots;

public sealed record DoubleShotStockAdjustment(string VariantCode, string SetCode, decimal Current, decimal Target)
{
    public decimal Delta => Target - Current;
}

public sealed record DoubleShotStockPlan(
    IReadOnlyList<DoubleShotStockAdjustment> Adjustments,
    int PairCount,
    IReadOnlyList<string> UnpairedVariantCodes);

/// <summary>
/// Pairs each double-shot variant (<c>SA…V</c>) with its mirror set (<c>SA…S</c>) in the Shoptet
/// stock export and computes the variant's target stock = max(set stock, 0).
/// </summary>
public static class DoubleShotStockPlanner
{
    public static DoubleShotStockPlan Plan(IReadOnlyList<EshopStock> stock)
    {
        var stockByCode = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in stock)
        {
            if (!string.IsNullOrEmpty(row.Code))
                stockByCode.TryAdd(row.Code, row.Stock);
        }

        var adjustments = new List<DoubleShotStockAdjustment>();
        var unpaired = new List<string>();
        var pairCount = 0;

        foreach (var (code, current) in stockByCode)
        {
            if (!DoubleShotCode.IsVariantCode(code))
                continue;

            var setCode = DoubleShotCode.ToSetCode(code)!;
            if (!stockByCode.TryGetValue(setCode, out var setStock))
            {
                unpaired.Add(code);
                continue;
            }

            pairCount++;
            var target = Math.Max(setStock, 0m);
            if (target != current)
                adjustments.Add(new DoubleShotStockAdjustment(code, setCode, current, target));
        }

        return new DoubleShotStockPlan(adjustments, pairCount, unpaired);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~DoubleShotStockPlannerTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/DoubleShots backend/test/Anela.Heblo.Tests/Features/Catalog/DoubleShots/DoubleShotStockPlannerTests.cs
git commit -m "feat: double-shot stock planner pairs V variants with S mirrors (#4479)"
```

---

### Task 3: Feature flag + `DoubleShotAvailabilitySyncJob` + adapter body test

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagKeys.cs` (add constant after `DeliveredOrderCompletionTestSource`)
- Modify: `backend/src/Anela.Heblo.Application/Features/FeatureFlags/FeatureFlagRegistry.cs` (add entry after `DeliveredOrderCompletionTestSource`)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (`FeatureManagement` section)
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/DoubleShotAvailabilitySyncJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/DoubleShots/DoubleShotAvailabilitySyncJobTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs` (add one test)

No frontend mirror: `FeatureFlagRegistryFrontendMirrorTests` only requires frontend keys to exist in the backend, and the frontend never reads this flag. `AppSettings_FeatureManagement_ExactlyMatchesBackendRegistry` **does** require the appsettings entry.

**Interfaces:**
- Consumes: `DoubleShotStockPlanner.Plan`, `DoubleShotStockPlan`, `DoubleShotStockAdjustment` (Task 2); `IEshopStockClient.ListAsync(CancellationToken)`, `IEshopStockClient.UpdateStockAsync(string, double, CancellationToken)`; `ICatalogResilienceService.ExecuteWithResilienceAsync<T>(Func<CancellationToken, Task<T>>, string, CancellationToken)`; `IRecurringJobStatusChecker.IsJobEnabledAsync(string, CancellationToken, bool = true)`; `IFeatureFlagChecker.IsEnabledAsync(string, CancellationToken)`.
- Produces: `FeatureFlagKeys.DoubleShotAvailabilitySync = "is-doubleshot-availability-sync-enabled"`; job `JobName = "doubleshot-availability-sync"`.

- [ ] **Step 1: Add the flag (registry + appsettings) — required by the job test to compile**

`FeatureFlagKeys.cs`:
```csharp
    /// <summary>
    /// When on, the double-shot availability sync writes each SA…V variant's stock to match its
    /// SA…S mirror set. When off, the job runs in dry-run mode and only logs what it would change.
    /// </summary>
    public const string DoubleShotAvailabilitySync = "is-doubleshot-availability-sync-enabled";
```

`FeatureFlagRegistry.cs`:
```csharp
        new(FeatureFlagKeys.DoubleShotAvailabilitySync,
            Description: "When on, the double-shot availability sync sets each SA…V variant's Shoptet stock to its SA…S mirror set's stock; when off it only logs what it would do (dry run).",
            DefaultValue: false),
```

`appsettings.json` (`FeatureManagement`), after `is-delivered-order-completion-test-source-enabled`:
```json
    "is-doubleshot-availability-sync-enabled": false,
```

- [ ] **Step 2: Write the failing job tests**

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.DoubleShots;

public class DoubleShotAvailabilitySyncJobTests
{
    private static EshopStock Row(string code, decimal stock) => new() { Code = code, Stock = stock };

    private static (DoubleShotAvailabilitySyncJob Sut, Mock<IEshopStockClient> Stock) MakeSut(
        List<EshopStock>? rows = null,
        bool jobEnabled = true,
        bool applyChanges = true,
        Exception? listError = null)
    {
        var stock = new Mock<IEshopStockClient>();
        if (listError is null)
            stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(rows ?? []);
        else
            stock.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(listError);

        var resilience = new Mock<ICatalogResilienceService>();
        resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<List<EshopStock>>> op, string _, CancellationToken ct) => op(ct));

        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(jobEnabled);

        var flags = new Mock<IFeatureFlagChecker>();
        flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.DoubleShotAvailabilitySync, It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyChanges);

        var sut = new DoubleShotAvailabilitySyncJob(
            stock.Object, resilience.Object, statusChecker.Object, flags.Object,
            NullLogger<DoubleShotAvailabilitySyncJob>.Instance);
        return (sut, stock);
    }

    [Fact]
    public void Metadata_MatchesSpec()
    {
        var (sut, _) = MakeSut();

        sut.Metadata.JobName.Should().Be("doubleshot-availability-sync");
        sut.Metadata.Category.Should().Be(RecurringJobCategory.Catalog);
        sut.Metadata.CronExpression.Should().Be("*/15 * * * *");
        sut.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenJobDisabled()
    {
        var (sut, stock) = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        stock.Verify(s => s.ListAsync(It.IsAny<CancellationToken>()), Times.Never);
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WritesNothing_WhenFlagOff_DryRun()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)], applyChanges: false);

        await sut.ExecuteAsync();

        stock.Verify(s => s.ListAsync(It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        stock.Verify(s => s.SetRealStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WritesDeltaForEachDifferingVariant_WhenFlagOn()
    {
        var (sut, stock) = MakeSut([
            Row("SA015030V", 0), Row("SA015030S", 213),
            Row("SA016005V", 400), Row("SA016005S", 385),
            Row("SA017100V", 24), Row("SA017100S", 24),
        ]);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync("SA015030V", 213d, It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.UpdateStockAsync("SA016005V", -15d, It.IsAny<CancellationToken>()), Times.Once);
        stock.Verify(s => s.UpdateStockAsync("SA017100V", It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NeverWritesSetCodes_OrRealStock()
    {
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213), Row("BAL0001V", 1000)]);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync(It.Is<string>(c => !c.StartsWith("SA") || !c.EndsWith('V')), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
        stock.Verify(s => s.SetRealStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesAfterOneWriteFails()
    {
        var (sut, stock) = MakeSut([
            Row("SA015030V", 0), Row("SA015030S", 213),
            Row("SA016005V", 0), Row("SA016005S", 385),
        ]);
        stock.Setup(s => s.UpdateStockAsync("SA015030V", It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("stock-change-not-allowed"));

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync("SA016005V", 385d, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WritesNothing_AndDoesNotThrow_WhenStockListFails()
    {
        var (sut, stock) = MakeSut(listError: new HttpRequestException("timeout"));

        var act = () => sut.ExecuteAsync();

        await act.Should().NotThrowAsync();
        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellation_DuringWrites()
    {
        using var cts = new CancellationTokenSource();
        var (sut, stock) = MakeSut([Row("SA015030V", 0), Row("SA015030S", 213)]);
        stock.Setup(s => s.UpdateStockAsync("SA015030V", It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var act = () => sut.ExecuteAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_SkipsUnpairedVariant()
    {
        var (sut, stock) = MakeSut([Row("SA099030V", 7)]);

        await sut.ExecuteAsync();

        stock.Verify(s => s.UpdateStockAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false`
Expected: build FAILS — `DoubleShotAvailabilitySyncJob` does not exist.

- [ ] **Step 4: Implement the job**

```csharp
using Anela.Heblo.Application.Features.Catalog.DoubleShots;
using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Catalog.DoubleShots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure.Jobs;

/// <summary>
/// Keeps each double-shot variant's (SA…V) Shoptet stock equal to its mirror product set's (SA…S)
/// computed stock. Dry run unless <see cref="FeatureFlagKeys.DoubleShotAvailabilitySync"/> is on.
/// See docs/processes/job-doubleshot-availability-sync.md.
/// </summary>
public sealed class DoubleShotAvailabilitySyncJob : IRecurringJob
{
    private readonly IEshopStockClient _stockClient;
    private readonly ICatalogResilienceService _resilience;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly IFeatureFlagChecker _featureFlags;
    private readonly ILogger<DoubleShotAvailabilitySyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "doubleshot-availability-sync",
        Category = RecurringJobCategory.Catalog,
        DisplayName = "Double-shot Availability Sync",
        Description = "Sets each double-shot variant's (SA…V) Shoptet stock to its mirror product set's (SA…S) stock.",
        CronExpression = "*/15 * * * *",
        DefaultIsEnabled = true,
    };

    public DoubleShotAvailabilitySyncJob(
        IEshopStockClient stockClient,
        ICatalogResilienceService resilience,
        IRecurringJobStatusChecker statusChecker,
        IFeatureFlagChecker featureFlags,
        ILogger<DoubleShotAvailabilitySyncJob> logger)
    {
        _stockClient = stockClient;
        _resilience = resilience;
        _statusChecker = statusChecker;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var applyChanges = await _featureFlags.IsEnabledAsync(
            FeatureFlagKeys.DoubleShotAvailabilitySync, cancellationToken);

        var stock = await TryListStockAsync(cancellationToken);
        if (stock is null)
            return;

        var plan = DoubleShotStockPlanner.Plan(stock);
        foreach (var code in plan.UnpairedVariantCodes)
        {
            _logger.LogWarning(
                "DoubleShotAvailabilitySync: variant {VariantCode} has no mirror set {SetCode} in the stock export. Skipped.",
                code, DoubleShotCode.ToSetCode(code));
        }

        var (changed, failed) = await ApplyAsync(plan.Adjustments, applyChanges, cancellationToken);

        _logger.LogInformation(
            "DoubleShotAvailabilitySync: {Pairs} pair(s), {Planned} to change, {Changed} changed, {Failed} failed, {Unpaired} unpaired{Mode}.",
            plan.PairCount, plan.Adjustments.Count, changed, failed, plan.UnpairedVariantCodes.Count,
            applyChanges ? string.Empty : " [DRY RUN — no changes applied]");
    }

    private async Task<List<EshopStock>?> TryListStockAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _resilience.ExecuteWithResilienceAsync(
                ct => _stockClient.ListAsync(ct), "DoubleShotAvailabilitySync.ListStock", cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "DoubleShotAvailabilitySync: failed to read the Shoptet stock export. No changes made.");
            return null;
        }
    }

    private async Task<(int Changed, int Failed)> ApplyAsync(
        IReadOnlyList<DoubleShotStockAdjustment> adjustments, bool applyChanges, CancellationToken cancellationToken)
    {
        var changed = 0;
        var failed = 0;

        foreach (var a in adjustments)
        {
            if (!applyChanges)
            {
                _logger.LogInformation(
                    "DoubleShotAvailabilitySync [DRY RUN]: {VariantCode} {Old} -> {New} (delta {Delta}, mirror {SetCode}). No changes made.",
                    a.VariantCode, a.Current, a.Target, a.Delta, a.SetCode);
                continue;
            }

            try
            {
                await _stockClient.UpdateStockAsync(a.VariantCode, (double)a.Delta, cancellationToken);
                changed++;
                _logger.LogInformation(
                    "DoubleShotAvailabilitySync: {VariantCode} {Old} -> {New} (delta {Delta}, mirror {SetCode}).",
                    a.VariantCode, a.Current, a.Target, a.Delta, a.SetCode);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                _logger.LogWarning(ex,
                    "DoubleShotAvailabilitySync: failed to update {VariantCode} ({Old} -> {New}). Will retry next run.",
                    a.VariantCode, a.Current, a.Target);
            }
        }

        return (changed, failed);
    }
}
```

Note: the `when (!cancellationToken.IsCancellationRequested)` filters follow the `TaskCanceledException` gotcha — an HTTP timeout is still treated as a per-item failure, while real cancellation propagates.

- [ ] **Step 5: Add the adapter body test (parses JSON, not substrings)**

Append to `ShoptetStockClientTests` (uses the file's existing `BuildClient` / `Json` helpers):
```csharp
    [Fact]
    public async Task UpdateStockAsync_Body_IsSingleAmountChangeItem_WithoutRealStock()
    {
        string? capturedBody = null;
        var client = BuildClient(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new { data = (object?)null, errors = (object?)null });
        });

        await client.UpdateStockAsync("SA015030V", -15);

        using var doc = JsonDocument.Parse(capturedBody!);
        var items = doc.RootElement.GetProperty("data");
        items.GetArrayLength().Should().Be(1);
        var item = items[0];
        item.GetProperty("productCode").GetString().Should().Be("SA015030V");
        item.GetProperty("amountChange").GetDouble().Should().Be(-15);
        item.TryGetProperty("realStock", out _).Should().BeFalse();
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false && dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "FullyQualifiedName~DoubleShot|FullyQualifiedName~ShoptetStockClientTests|FullyQualifiedName~FeatureFlagRegistry|FullyQualifiedName~RecurringJob|FullyQualifiedName~ModuleBoundaries"`
Expected: all PASS. If the adapter test fails because `realStock` is serialized as `null`, that is a real bug in the DTO's `JsonIgnore` — fix the DTO, not the test.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/FeatureFlags backend/src/Anela.Heblo.API/appsettings.json backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/DoubleShotAvailabilitySyncJob.cs backend/test/Anela.Heblo.Tests/Features/Catalog/DoubleShots/DoubleShotAvailabilitySyncJobTests.cs backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetStockClientTests.cs
git commit -m "feat: double-shot availability sync job, dry run behind feature flag (#4479)"
```

---

### Task 4: Process doc + Shoptet findings

**Files:**
- Create: `docs/processes/job-doubleshot-availability-sync.md`
- Modify: `docs/processes/module-catalog.md` (frontmatter `related:` + `## Processes` list)
- Modify: `docs/integrations/shoptet-api.md` (new §8.8 after §8.7)
- Modify: `docs/processes/INDEX.md` (regenerated by the script)

- [ ] **Step 1: Write the process doc**

```markdown
---
process: job-doubleshot-availability-sync
kind: job
module: catalog
summary: Keeps each double-shot variant's (SA…V) Shoptet stock equal to its mirror product set's (SA…S) computed stock, so the e-shop shows the right availability without manual restocking.
owns:
  - backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShots/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/DoubleShots/**
  - backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/DoubleShotAvailabilitySyncJob.cs
verified_at: "<short sha of HEAD>"
related: [sync-catalog-stock]
---

# Double-shot availability sync

## Purpose
Double-shots are sold as one variant product per serum (`SA0xxNNNV`, 5/30/100 ml). Shoptet cannot show a
product set as a variant, so each variant has a mirror product set (`SA0xxNNNS`) whose stock Shoptet computes
from its components. Nobody maintains the variant stock; this job copies the set's stock onto the variant so
customers see correct availability. Requested by Andy (Teams "Cenotvorba", 8 Oct 2026); issue #4479.

## Trigger
Hangfire `doubleshot-availability-sync`, cron `*/15 * * * *` (Europe/Prague), enabled by default.
Can be disabled or run manually in Recurring Jobs. Writes only when feature flag
`is-doubleshot-availability-sync-enabled` is on; otherwise it is a dry run that only logs.

## Data flow
1. Shoptet stock CSV export (`StockClient:Url`) via `IEshopStockClient.ListAsync`, wrapped in `ICatalogResilienceService`.
2. `DoubleShotStockPlanner.Plan` pairs every `^SA\d+V$` code with the same code ending in `S`.
3. For each pair where target ≠ current: `PATCH /api/stocks/{Shoptet:StockId}/movements` with
   `{ productCode: <V>, amountChange: target − current }`.
No Heblo table is written. Every change is logged (`code old -> new`); a summary line ends every run.

## Logic & formulas
- target = max(stock of S, 0). The CSV `stock` is free-for-order stock (net of open-order `claim`).
- Relative `amountChange`, not `realStock`: `realStock` is gross and Shoptet subtracts `claim` from it;
  a delta keeps free stock exact even with pending V orders.
- Variants without an S row in the export are skipped with a warning (never zeroed).
- `S` codes are never written (Shoptet rejects set writes with `stock-change-not-allowed`).
- Codes like `BAL0001V` do not match the pattern and are never touched.
- The V↔S mapping is `DoubleShotCode` (Domain), shared with the order swap (#4480).

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `FeatureManagement:is-doubleshot-availability-sync-enabled` | `false` | Off = dry run (log only). On = writes. Overridable at runtime in the feature-flag admin. |
| `StockClient:Url` | KV `StockClient--Url` | Shoptet stock CSV export URL. |
| `Shoptet:StockId` | `1` | Warehouse id for the movements PATCH. |

## Runtime facts
- 12 pairs exist in prod: SA014–SA017 × 005/030/100 — stock CSV, read-only — 2026-10-12.
- All V variants had stock 0 before the job existed; S mirrors carried computed stock (e.g. SA015030S = 213) — stock CSV — 2026-10-12.
- CSV `stock` equals `GET /api/stocks/1/supplies` `amount` (free stock, net of `claim`) — compared 141 items with claims — 2026-10-12.

## Known quirks
- A V order placed between the CSV read and the PATCH causes a drift of that quantity for one cycle; the next run corrects it.
- The CSV export may be cached by Shoptet for some minutes; the job mirrors whatever the export shows.
- If someone edits V stock by hand in Shoptet, the next run overwrites it.

## Code entry points
- `backend/src/Anela.Heblo.Domain/Features/Catalog/DoubleShots/DoubleShotCode.cs` — V↔S code mapping (shared with #4480)
- `backend/src/Anela.Heblo.Application/Features/Catalog/DoubleShots/DoubleShotStockPlanner.cs` — pairing and target
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/Jobs/DoubleShotAvailabilitySyncJob.cs` — job, dry run, writes
```

- [ ] **Step 2: Update `module-catalog.md`**

Add `job-doubleshot-availability-sync` to the frontmatter `related:` list, and under `## Processes` add:
```markdown
- `job-doubleshot-availability-sync` — copies each double-shot SA…S set's stock onto its SA…V variant in Shoptet; Hangfire `doubleshot-availability-sync` every 15 min; dry run until flag `is-doubleshot-availability-sync-enabled`.
```

- [ ] **Step 3: Add Shoptet findings — `shoptet-api.md` §8.8**

```markdown
### 8.8 Product sets and double-shot variants in the stock CSV (verified 2026-10-12, production, read-only)

- Product sets **are** listed in the stock CSV export, with their Shoptet-computed stock in the `stock` column
  (e.g. `SA015030S` = 213).
- The CSV `stock` column equals `GET /api/stocks/{id}/supplies` → `amount`: free-for-order stock, already net of
  `claim`. Writing `realStock` (§8.7) sets `amount = realStock − claim`, so to set free stock exactly, send a
  relative `amountChange = target − csvStock`.
- Double-shot convention: variant product `SA0xxNNNV` (variants share a `pairCode`, e.g. 81–84) mirrors product
  set `SA0xxNNNS`; codes differ only in the last letter. 12 pairs exist (SA014–SA017 × 005/030/100).
  Mapping: `DoubleShotCode` in Domain.
- Other codes ending in `V` exist (`BAL00xxV` packages, hand-set stock 1000) — they are **not** double-shots.
- Prod uses App Setting `Shoptet__ApiToken`; the KV secret `Shoptet--Token` is rejected as `invalid-token`.
```

- [ ] **Step 4: Regenerate and check the index**

Run (repo root): `python3 scripts/process-docs/check.py index && python3 scripts/process-docs/check.py`
Expected: index updated with the new doc, no errors. Set `verified_at` to `git rev-parse --short HEAD`.

- [ ] **Step 5: Commit**

```bash
git add docs/processes docs/integrations/shoptet-api.md
git commit -m "docs: double-shot availability sync process doc and Shoptet stock findings (#4479)"
```

---

### Task 5: Validation + PR

- [ ] **Step 1:** `dotnet build Anela.Heblo.sln -p:UseSharedCompilation=false` → 0 errors.
- [ ] **Step 2:** `dotnet format Anela.Heblo.sln --verify-no-changes` → if it fails, run `dotnet format Anela.Heblo.sln` and commit `chore: dotnet format`.
- [ ] **Step 3:** `dotnet test backend/test/Anela.Heblo.Tests --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"` → all PASS (report the counts).
- [ ] **Step 4:** Code review over the diff (`csharp-reviewer` agent); fix CRITICAL/HIGH; re-build and re-test.
- [ ] **Step 5:** Push and open PR `feat(catalog): double-shot availability sync (#4479)` with summary, dry-run rollout steps, test plan, the `Shoptet--Token` KV note, and `Closes #4479`.
