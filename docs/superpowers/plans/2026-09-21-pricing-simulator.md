# Analýza cen (Pricing Simulator) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Finance screen ("Analýza cen") where the user edits product price, M0, M1 and forecast quantity across a filtered product grid and immediately sees the effect on total revenue and total margin, with scenarios saved to Postgres and exportable to XLSX.

**Architecture:** A new `Pricing` vertical slice in the Application layer. All arithmetic lives server-side in one pure component, `PricingSimulationCalculator`, which the baseline, recalculate and scenario-load paths all call — so the grid, a saved scenario and the exported ceník can never disagree. The frontend holds no business math: it posts a sparse override set on cell blur and renders whatever comes back. Margin edits are normalised to the cost they imply before storage, which makes edit order and reload deterministic.

**Tech Stack:** .NET 8, MediatR, FluentValidation, EF Core + Npgsql, xUnit + FluentAssertions + Moq; React 18, TanStack Query, generated NSwag TypeScript client, exceljs, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-21-pricing-simulator-design.md`

## Global Constraints

- **Branch:** `feature/pricing-simulator` (already created, spec already committed).
- **DTOs are classes, never C# records.** OpenAPI client generators mishandle record parameter order.
- **Every `*Response` in the Application layer must inherit `BaseResponse`** (`Anela.Heblo.Application.Shared`). A reflection contract test fails CI otherwise.
- **Validators are registered manually per module.** There is no `AddValidatorsFromAssembly`. Register both `IValidator<TRequest>` and `IPipelineBehavior<TRequest, TResponse> → ValidationBehavior<TRequest, TResponse>`.
- **MediatR handlers are auto-registered** by the assembly scan in `ApplicationModule` — do not register them individually.
- **Every new `ErrorCodes` member needs two extra edits** or CI fails: a module-range bucket in `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs`, and a Czech translation in `frontend/src/i18n.ts` (enforced by `LocalizationCoverageTests`).
- **Database migrations are manual.** Generate the migration, commit it; deployment does not apply it.
- **Frontend API hooks build absolute URLs** as `${apiClient.baseUrl}${relativeUrl}`. Relative URLs hit port 3001 instead of 5001.
- **The generated TS client throws on non-200.** `if (!response.success)` is dead code; read `errorCode` (a string) from the caught `SwaggerException`.
- **Money is `decimal`, quantity is `double`** — matching `CatalogSaleRecord.AmountB2B/AmountB2C` and `MarginLevel`.
- **All prices are ex-VAT, CZK.** No VAT arithmetic anywhere in this feature.
- **Validation before completion:** BE `dotnet build` + `dotnet format`; FE `CI=false npm run build` + `npm run lint`. Do not gate on `npx tsc --noEmit` — it false-greens in this repo.
- **Error code range for this feature: 36XX** (35XX is Mind Maps, the current highest module range).

---

### Task 1: Error codes and localization

Adds the six `36XX` codes the rest of the feature raises, plus the two companion edits that CI enforces. Doing this first means no later task is blocked by a red contract test.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (append after the Mind Maps 35XX block, before `// External Service errors (90XX)`)
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs:100` (add bucket) and the `categorizedCount` sum
- Modify: `frontend/src/i18n.ts` (Czech strings alongside `MindMapUpdateInProgress`)

**Interfaces:**
- Consumes: nothing.
- Produces: `ErrorCodes.PricingInvalidPrice`, `PricingNegativeMaterialCost`, `PricingNegativeManufacturingCost`, `PricingNegativeQuantity`, `PricingScenarioNotFound`, `PricingScenarioNameConflict`.

- [ ] **Step 1: Add the error codes**

In `ErrorCodes.cs`, immediately after the `MindMapInvalidDocument = 3503,` line:

```csharp
    // Pricing simulator module errors (36XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PricingInvalidPrice = 3601,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PricingNegativeMaterialCost = 3602,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PricingNegativeManufacturingCost = 3603,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    PricingNegativeQuantity = 3604,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    PricingScenarioNotFound = 3605,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    PricingScenarioNameConflict = 3606,
```

- [ ] **Step 2: Run the contract tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ErrorHandlingTests|FullyQualifiedName~LocalizationCoverageTests"`

Expected: FAIL. `ErrorHandlingTests` fails on `Assert.Equal(errorCodes.Count, categorizedCount)` because the six new codes fall outside every declared range; `LocalizationCoverageTests` fails because they have no Czech strings.

- [ ] **Step 3: Add the module-range bucket**

In `ErrorHandlingTests.cs`, after the `mindMapErrors` line (currently line 100):

```csharp
        var pricingErrors = errorCodes.Where(code => code >= 3600 && code < 3700).ToList(); // 36XX range (Pricing simulator)
```

After the `mindMapErrors.Count > 0` assertion:

```csharp
        Assert.True(pricingErrors.Count > 0, "Should have Pricing simulator errors in 36XX range");
```

And add `pricingErrors.Count +` into the `categorizedCount` sum, immediately before `mindMapErrors.Count`.

- [ ] **Step 4: Add the Czech translations**

In `frontend/src/i18n.ts`, in the same object that holds `MindMapUpdateInProgress`:

```ts
        PricingInvalidPrice: "Cena musí být větší než nula",
        PricingNegativeMaterialCost: "Marže M0 je vyšší než cena — materiálové náklady by byly záporné",
        PricingNegativeManufacturingCost: "Marže M1 je vyšší než M0 — výrobní náklady by byly záporné",
        PricingNegativeQuantity: "Množství nesmí být záporné",
        PricingScenarioNotFound: "Scénář nebyl nalezen",
        PricingScenarioNameConflict: "Scénář s tímto názvem už existuje",
```

- [ ] **Step 5: Run the contract tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ErrorHandlingTests|FullyQualifiedName~LocalizationCoverageTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs \
        backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs \
        frontend/src/i18n.ts
git commit -m "feat: add 36XX pricing simulator error codes"
```

---

### Task 2: The calculation core

The heart of the feature, and the only place that knows the pricing rules. Pure — no repository, no clock, no I/O — so every rule is unit-testable in isolation. Everything else in the plan is plumbing around this.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingRowDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingOverrideDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingEditDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingTotalsDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Model/PricingBaselineRow.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Model/PricingSimulationResult.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingEditException.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Services/IPricingSimulationCalculator.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingSimulationCalculator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Pricing/PricingSimulationCalculatorTests.cs`

**Interfaces:**
- Consumes: `ErrorCodes` from Task 1.
- Produces:
  - `PricingBaselineRow` (internal input record): `ProductCode`, `ProductName`, `Price` (`decimal`), `MaterialCost` (`decimal`), `ManufacturingCost` (`decimal`), `Quantity` (`double`), `HasData` (`bool`).
  - `PricingOverrideDto`: `ProductCode`, `decimal? Price`, `decimal? MaterialCost`, `decimal? ManufacturingCost`, `double? ForecastQuantity`.
  - `PricingEditField` enum: `Price, M0Amount, M0Percentage, M1Amount, M1Percentage, ForecastQuantity`.
  - `PricingEditDto`: `ProductCode`, `PricingEditField Field`, `decimal Value`.
  - `PricingRowDto`, `PricingTotalsDto` (shapes below).
  - `IPricingSimulationCalculator.Calculate(IReadOnlyList<PricingBaselineRow>, IReadOnlyList<PricingOverrideDto>, PricingEditDto?) → PricingSimulationResult`.
  - `PricingSimulationResult`: `Rows` (`IReadOnlyList<PricingRowDto>`), `Totals` (`PricingTotalsDto`), `Overrides` (`IReadOnlyList<PricingOverrideDto>` — normalised, including the applied edit).
  - `PricingEditException(ErrorCodes errorCode, Dictionary<string, string> parameters)` with public `ErrorCode` and `Parameters`.

- [ ] **Step 1: Write the contract and model types**

These carry no logic, so they need no test of their own — the calculator tests exercise them. `PricingBaselineRow` and `PricingSimulationResult` are internal model types and may be records; the four `*Dto` types cross the OpenAPI boundary and **must be classes**.

`Contracts/PricingOverrideDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingOverrideDto
{
    public string ProductCode { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public decimal? MaterialCost { get; set; }
    public decimal? ManufacturingCost { get; set; }
    public double? ForecastQuantity { get; set; }
}
```

`Contracts/PricingEditDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public enum PricingEditField
{
    Price,
    M0Amount,
    M0Percentage,
    M1Amount,
    M1Percentage,
    ForecastQuantity
}

public class PricingEditDto
{
    public string ProductCode { get; set; } = string.Empty;
    public PricingEditField Field { get; set; }
    public decimal Value { get; set; }
}
```

`Contracts/PricingRowDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingRowDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    // Baseline — never edited, used for the "before" column and drift detection
    public decimal BaselinePrice { get; set; }
    public decimal BaselineMaterialCost { get; set; }
    public decimal BaselineManufacturingCost { get; set; }
    public double BaselineQuantity { get; set; }

    // Effective = baseline with overrides applied
    public decimal Price { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal ManufacturingCost { get; set; }
    public double ForecastQuantity { get; set; }

    // Derived, never stored
    public decimal M0Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M1Percentage { get; set; }

    public bool IsEdited { get; set; }
    public bool IsExcluded { get; set; }
}
```

`Contracts/PricingTotalsDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingTotalsDto
{
    public decimal RevenueBefore { get; set; }
    public decimal RevenueAfter { get; set; }
    public decimal RevenueDelta { get; set; }
    public decimal RevenueDeltaPercentage { get; set; }

    public decimal M0Before { get; set; }
    public decimal M0After { get; set; }
    public decimal M0Delta { get; set; }
    public decimal M0DeltaPercentage { get; set; }

    public decimal M1Before { get; set; }
    public decimal M1After { get; set; }
    public decimal M1Delta { get; set; }
    public decimal M1DeltaPercentage { get; set; }

    public int EditedProductCount { get; set; }
    public int ExcludedProductCount { get; set; }
}
```

`Model/PricingBaselineRow.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Pricing.Model;

/// <summary>
/// One product's untouched starting point, built from the catalog. Internal to the
/// Pricing slice — never crosses the API boundary, so a record is fine here.
/// </summary>
public record PricingBaselineRow(
    string ProductCode,
    string ProductName,
    decimal Price,
    decimal MaterialCost,
    decimal ManufacturingCost,
    double Quantity,
    bool HasData);
```

`Model/PricingSimulationResult.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;

namespace Anela.Heblo.Application.Features.Pricing.Model;

public record PricingSimulationResult(
    IReadOnlyList<PricingRowDto> Rows,
    PricingTotalsDto Totals,
    IReadOnlyList<PricingOverrideDto> Overrides);
```

`Services/PricingEditException.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.Services;

/// <summary>
/// Raised when an edit would imply an impossible row (negative cost, non-positive price).
/// Handlers translate this into an error response; the cell keeps its previous value.
/// </summary>
public class PricingEditException : Exception
{
    public ErrorCodes ErrorCode { get; }
    public Dictionary<string, string> Parameters { get; }

    public PricingEditException(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base($"Pricing edit rejected: {errorCode}")
    {
        ErrorCode = errorCode;
        Parameters = parameters;
    }
}
```

`Services/IPricingSimulationCalculator.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;

namespace Anela.Heblo.Application.Features.Pricing.Services;

public interface IPricingSimulationCalculator
{
    /// <summary>
    /// Applies the stored overrides and then the incoming edit (if any) to the baseline,
    /// returning fully computed rows, totals, and the normalised override set.
    /// </summary>
    /// <exception cref="PricingEditException">The edit implies a negative cost or a non-positive price.</exception>
    PricingSimulationResult Calculate(
        IReadOnlyList<PricingBaselineRow> baseline,
        IReadOnlyList<PricingOverrideDto> overrides,
        PricingEditDto? edit);
}
```

- [ ] **Step 2: Write the failing tests**

`backend/test/Anela.Heblo.Tests/Features/Pricing/PricingSimulationCalculatorTests.cs`. These tests are the specification of the pricing rules — read them as the source of truth.

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingSimulationCalculatorTests
{
    private readonly PricingSimulationCalculator _sut = new();

    // Price 420, material 175, manufacturing 70, sold 1000.
    // => M0 = 245 (58.33%), M1 = 175 (41.67%)
    private static PricingBaselineRow Row(
        string code = "P1", decimal price = 420m, decimal material = 175m,
        decimal manufacturing = 70m, double quantity = 1000d, bool hasData = true)
        => new(code, $"Product {code}", price, material, manufacturing, quantity, hasData);

    private static List<PricingOverrideDto> NoOverrides() => new();

    private PricingRowDto Single(PricingBaselineRow baseline, PricingEditDto? edit,
        IReadOnlyList<PricingOverrideDto>? overrides = null)
        => _sut.Calculate(new[] { baseline }, overrides ?? NoOverrides(), edit).Rows.Single();

    [Fact]
    public void Baseline_with_no_edits_derives_M0_and_M1_from_costs()
    {
        var row = Single(Row(), edit: null);

        row.M0Amount.Should().Be(245m);
        row.M1Amount.Should().Be(175m);
        row.M0Percentage.Should().BeApproximately(58.33m, 0.01m);
        row.M1Percentage.Should().BeApproximately(41.67m, 0.01m);
        row.IsEdited.Should().BeFalse();
    }

    [Fact]
    public void Editing_price_shifts_both_margins_and_leaves_costs_alone()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.Price, Value = 500m
        });

        row.Price.Should().Be(500m);
        row.MaterialCost.Should().Be(175m);
        row.ManufacturingCost.Should().Be(70m);
        row.M0Amount.Should().Be(325m);   // +80, the price delta
        row.M1Amount.Should().Be(255m);   // +80, the same delta
        row.IsEdited.Should().BeTrue();
    }

    [Fact]
    public void Editing_M0_implies_a_material_cost_and_shifts_M1_by_the_same_delta()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M0Amount, Value = 265m
        });

        row.Price.Should().Be(420m);              // price is pinned
        row.MaterialCost.Should().Be(155m);       // 420 - 265
        row.ManufacturingCost.Should().Be(70m);   // unchanged
        row.M0Amount.Should().Be(265m);
        row.M1Amount.Should().Be(195m);           // +20, the same delta as M0
    }

    [Fact]
    public void Editing_M1_implies_a_manufacturing_cost_and_leaves_M0_untouched()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M1Amount, Value = 195m
        });

        row.Price.Should().Be(420m);
        row.MaterialCost.Should().Be(175m);       // unchanged
        row.ManufacturingCost.Should().Be(50m);   // 245 - 195
        row.M0Amount.Should().Be(245m);           // unchanged
        row.M1Amount.Should().Be(195m);
    }

    [Fact]
    public void Editing_a_margin_percentage_is_equivalent_to_editing_the_amount()
    {
        // 50% of 420 = 210
        var byPercentage = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M0Percentage, Value = 50m
        });
        var byAmount = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M0Amount, Value = 210m
        });

        byPercentage.MaterialCost.Should().Be(byAmount.MaterialCost);
        byPercentage.M0Amount.Should().Be(byAmount.M0Amount);
    }

    [Fact]
    public void Editing_forecast_quantity_does_not_change_the_row_margins()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.ForecastQuantity, Value = 800m
        });

        row.ForecastQuantity.Should().Be(800d);
        row.M0Amount.Should().Be(245m);
        row.M1Amount.Should().Be(175m);
    }

    [Fact]
    public void Edit_order_does_not_matter_because_edits_normalise_to_costs()
    {
        // Edit M0 then price ...
        var first = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M0Amount, Value = 265m
        });
        var m0ThenPrice = _sut.Calculate(new[] { Row() }, first.Overrides, new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.Price, Value = 500m
        }).Rows.Single();

        // ... versus price then M0, where M0 is set to what the first path produced.
        var second = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.Price, Value = 500m
        });
        var priceThenM0 = _sut.Calculate(new[] { Row() }, second.Overrides, new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M0Amount, Value = 345m // 500 - 155
        }).Rows.Single();

        m0ThenPrice.MaterialCost.Should().Be(priceThenM0.MaterialCost);
        m0ThenPrice.ManufacturingCost.Should().Be(priceThenM0.ManufacturingCost);
        m0ThenPrice.M0Amount.Should().Be(priceThenM0.M0Amount);
        m0ThenPrice.M1Amount.Should().Be(priceThenM0.M1Amount);
    }

    [Fact]
    public void Replaying_an_override_set_reproduces_the_same_rows()
    {
        var edited = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M1Amount, Value = 195m
        });

        var replayed = _sut.Calculate(new[] { Row() }, edited.Overrides, edit: null).Rows.Single();
        var original = edited.Rows.Single();

        replayed.Price.Should().Be(original.Price);
        replayed.MaterialCost.Should().Be(original.MaterialCost);
        replayed.ManufacturingCost.Should().Be(original.ManufacturingCost);
        replayed.M1Amount.Should().Be(original.M1Amount);
    }

    [Fact]
    public void Negative_margins_are_allowed_because_showing_a_loss_is_the_point()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.Price, Value = 200m
        });

        row.M0Amount.Should().Be(25m);
        row.M1Amount.Should().Be(-45m);   // 200 - 175 - 70
        row.M1Percentage.Should().BeApproximately(-22.5m, 0.01m);
    }

    [Fact]
    public void Zero_material_cost_is_allowed_M0_of_exactly_100_percent_is_computable()
    {
        var row = Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M0Percentage, Value = 100m
        });

        row.MaterialCost.Should().Be(0m);
        row.M0Percentage.Should().Be(100m);
    }

    [Theory]
    [InlineData(PricingEditField.Price, 0, ErrorCodes.PricingInvalidPrice)]
    [InlineData(PricingEditField.Price, -10, ErrorCodes.PricingInvalidPrice)]
    [InlineData(PricingEditField.M0Amount, 500, ErrorCodes.PricingNegativeMaterialCost)]
    [InlineData(PricingEditField.M0Percentage, 120, ErrorCodes.PricingNegativeMaterialCost)]
    [InlineData(PricingEditField.M1Amount, 300, ErrorCodes.PricingNegativeManufacturingCost)]
    [InlineData(PricingEditField.ForecastQuantity, -1, ErrorCodes.PricingNegativeQuantity)]
    public void Impossible_edits_are_rejected_with_the_matching_error_code(
        PricingEditField field, decimal value, ErrorCodes expected)
    {
        var act = () => Single(Row(), new PricingEditDto
        {
            ProductCode = "P1", Field = field, Value = value
        });

        act.Should().Throw<PricingEditException>()
           .Which.ErrorCode.Should().Be(expected);
    }

    [Fact]
    public void Totals_sum_revenue_and_margin_over_all_rows()
    {
        var baseline = new[] { Row("P1"), Row("P2", price: 100m, material: 40m, manufacturing: 10m, quantity: 500d) };

        var result = _sut.Calculate(baseline, NoOverrides(), edit: null);

        // P1: 420 * 1000 = 420_000 ; P2: 100 * 500 = 50_000
        result.Totals.RevenueBefore.Should().Be(470_000m);
        result.Totals.RevenueAfter.Should().Be(470_000m);
        result.Totals.RevenueDelta.Should().Be(0m);
        // M0: 245 * 1000 + 60 * 500 = 275_000
        result.Totals.M0Before.Should().Be(275_000m);
        // M1: 175 * 1000 + 50 * 500 = 200_000
        result.Totals.M1Before.Should().Be(200_000m);
        result.Totals.EditedProductCount.Should().Be(0);
    }

    [Fact]
    public void A_cost_edit_moves_margin_but_never_revenue()
    {
        var result = _sut.Calculate(new[] { Row() }, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.M1Amount, Value = 195m
        });

        result.Totals.RevenueDelta.Should().Be(0m);
        result.Totals.M1Delta.Should().Be(20_000m);   // +20 per unit x 1000
        result.Totals.M0Delta.Should().Be(0m);
        result.Totals.EditedProductCount.Should().Be(1);
    }

    [Fact]
    public void Rows_without_data_are_flagged_excluded_and_kept_out_of_totals()
    {
        var baseline = new[]
        {
            Row("P1"),
            Row("P2", price: 0m, material: 0m, manufacturing: 0m, quantity: 0d, hasData: false)
        };

        var result = _sut.Calculate(baseline, NoOverrides(), edit: null);

        result.Rows.Should().HaveCount(2);
        result.Rows.Single(r => r.ProductCode == "P2").IsExcluded.Should().BeTrue();
        result.Totals.ExcludedProductCount.Should().Be(1);
        result.Totals.RevenueBefore.Should().Be(420_000m);   // P2 contributes nothing
    }

    [Fact]
    public void Delta_percentage_is_zero_rather_than_infinite_when_the_before_total_is_zero()
    {
        var baseline = new[] { Row("P1", quantity: 0d) };

        var result = _sut.Calculate(baseline, NoOverrides(), new PricingEditDto
        {
            ProductCode = "P1", Field = PricingEditField.Price, Value = 500m
        });

        result.Totals.RevenueBefore.Should().Be(0m);
        result.Totals.RevenueDeltaPercentage.Should().Be(0m);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingSimulationCalculatorTests"`

Expected: FAIL to compile — `PricingSimulationCalculator` does not exist yet.

- [ ] **Step 4: Implement the calculator**

`Services/PricingSimulationCalculator.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.Services;

/// <summary>
/// The single place that knows the pricing rules. Pure: no clock, no repository, no I/O.
///
/// A margin is a function of price and cost, so it cannot be the durable representation of
/// an edit — editing M0 and then the price would leave a stored M0 stale. Every edit is
/// therefore normalised to the cost it implies, and margins are always derived.
/// </summary>
public class PricingSimulationCalculator : IPricingSimulationCalculator
{
    public PricingSimulationResult Calculate(
        IReadOnlyList<PricingBaselineRow> baseline,
        IReadOnlyList<PricingOverrideDto> overrides,
        PricingEditDto? edit)
    {
        var overrideByCode = overrides.ToDictionary(o => o.ProductCode, StringComparer.OrdinalIgnoreCase);

        if (edit is not null)
        {
            var target = baseline.FirstOrDefault(b =>
                string.Equals(b.ProductCode, edit.ProductCode, StringComparison.OrdinalIgnoreCase));

            if (target is not null)
            {
                overrideByCode.TryGetValue(edit.ProductCode, out var existing);
                overrideByCode[edit.ProductCode] = ApplyEdit(target, existing, edit);
            }
        }

        var rows = baseline
            .Select(b => BuildRow(b, overrideByCode.GetValueOrDefault(b.ProductCode)))
            .ToList();

        return new PricingSimulationResult(
            rows,
            BuildTotals(rows),
            overrideByCode.Values.ToList());
    }

    /// <summary>
    /// Translates one user gesture into the normalised override for that row: price,
    /// material cost, manufacturing cost and forecast quantity — the independent variables.
    /// </summary>
    private static PricingOverrideDto ApplyEdit(
        PricingBaselineRow baseline, PricingOverrideDto? existing, PricingEditDto edit)
    {
        // Start from the row's current effective state, not from the baseline.
        var price = existing?.Price ?? baseline.Price;
        var material = existing?.MaterialCost ?? baseline.MaterialCost;
        var manufacturing = existing?.ManufacturingCost ?? baseline.ManufacturingCost;
        var quantity = existing?.ForecastQuantity ?? baseline.Quantity;

        switch (edit.Field)
        {
            case PricingEditField.Price:
                price = edit.Value;
                break;

            case PricingEditField.M0Amount:
                // M0 = P - Cm  =>  Cm = P - M0. Manufacturing is untouched, so M1 shifts with M0.
                material = price - edit.Value;
                break;

            case PricingEditField.M0Percentage:
                material = price - (price * edit.Value / 100m);
                break;

            case PricingEditField.M1Amount:
                // M1 = P - Cm - Cf  =>  Cf = (P - Cm) - M1. Material is untouched, so M0 holds.
                manufacturing = (price - material) - edit.Value;
                break;

            case PricingEditField.M1Percentage:
                manufacturing = (price - material) - (price * edit.Value / 100m);
                break;

            case PricingEditField.ForecastQuantity:
                quantity = (double)edit.Value;
                break;
        }

        Validate(edit, price, material, manufacturing, quantity);

        return new PricingOverrideDto
        {
            ProductCode = baseline.ProductCode,
            Price = price,
            MaterialCost = material,
            ManufacturingCost = manufacturing,
            ForecastQuantity = quantity
        };
    }

    /// <summary>
    /// An edit is valid exactly when the row it implies is physically possible: a positive
    /// price and non-negative costs. Negative margins are deliberately permitted — showing
    /// that a product loses money at a given price is one of the things this tool is for.
    /// </summary>
    private static void Validate(
        PricingEditDto edit, decimal price, decimal material, decimal manufacturing, double quantity)
    {
        var context = new Dictionary<string, string>
        {
            { "productCode", edit.ProductCode },
            { "field", edit.Field.ToString() },
            { "value", edit.Value.ToString("0.##") }
        };

        if (price <= 0m)
            throw new PricingEditException(ErrorCodes.PricingInvalidPrice, context);

        if (material < 0m)
            throw new PricingEditException(ErrorCodes.PricingNegativeMaterialCost, context);

        if (manufacturing < 0m)
            throw new PricingEditException(ErrorCodes.PricingNegativeManufacturingCost, context);

        if (quantity < 0d)
            throw new PricingEditException(ErrorCodes.PricingNegativeQuantity, context);
    }

    private static PricingRowDto BuildRow(PricingBaselineRow baseline, PricingOverrideDto? over)
    {
        var price = over?.Price ?? baseline.Price;
        var material = over?.MaterialCost ?? baseline.MaterialCost;
        var manufacturing = over?.ManufacturingCost ?? baseline.ManufacturingCost;
        var quantity = over?.ForecastQuantity ?? baseline.Quantity;

        var m0 = price - material;
        var m1 = price - material - manufacturing;

        return new PricingRowDto
        {
            ProductCode = baseline.ProductCode,
            ProductName = baseline.ProductName,

            BaselinePrice = baseline.Price,
            BaselineMaterialCost = baseline.MaterialCost,
            BaselineManufacturingCost = baseline.ManufacturingCost,
            BaselineQuantity = baseline.Quantity,

            Price = price,
            MaterialCost = material,
            ManufacturingCost = manufacturing,
            ForecastQuantity = quantity,

            M0Amount = m0,
            M0Percentage = Percentage(m0, price),
            M1Amount = m1,
            M1Percentage = Percentage(m1, price),

            IsEdited = over is not null,
            IsExcluded = !baseline.HasData
        };
    }

    private static PricingTotalsDto BuildTotals(IReadOnlyList<PricingRowDto> rows)
    {
        var counted = rows.Where(r => !r.IsExcluded).ToList();

        var revenueBefore = counted.Sum(r => r.BaselinePrice * (decimal)r.BaselineQuantity);
        var revenueAfter = counted.Sum(r => r.Price * (decimal)r.ForecastQuantity);

        var m0Before = counted.Sum(r => (r.BaselinePrice - r.BaselineMaterialCost) * (decimal)r.BaselineQuantity);
        var m0After = counted.Sum(r => r.M0Amount * (decimal)r.ForecastQuantity);

        var m1Before = counted.Sum(r =>
            (r.BaselinePrice - r.BaselineMaterialCost - r.BaselineManufacturingCost) * (decimal)r.BaselineQuantity);
        var m1After = counted.Sum(r => r.M1Amount * (decimal)r.ForecastQuantity);

        return new PricingTotalsDto
        {
            RevenueBefore = revenueBefore,
            RevenueAfter = revenueAfter,
            RevenueDelta = revenueAfter - revenueBefore,
            RevenueDeltaPercentage = Percentage(revenueAfter - revenueBefore, revenueBefore),

            M0Before = m0Before,
            M0After = m0After,
            M0Delta = m0After - m0Before,
            M0DeltaPercentage = Percentage(m0After - m0Before, m0Before),

            M1Before = m1Before,
            M1After = m1After,
            M1Delta = m1After - m1Before,
            M1DeltaPercentage = Percentage(m1After - m1Before, m1Before),

            EditedProductCount = rows.Count(r => r.IsEdited),
            ExcludedProductCount = rows.Count(r => r.IsExcluded)
        };
    }

    // A percentage of zero is reported as zero rather than infinity or NaN: a total that
    // starts at zero has no meaningful percentage change, and the UI must not print "∞".
    private static decimal Percentage(decimal part, decimal whole)
        => whole == 0m ? 0m : part / whole * 100m;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingSimulationCalculatorTests"`

Expected: PASS, all cases.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Pricing \
        backend/test/Anela.Heblo.Tests/Features/Pricing
git commit -m "feat: add pricing simulation calculator"
```

---

### Task 3: Baseline endpoint

Reads the filtered product set out of the catalog and turns it into baseline rows. This is the only task that touches `CatalogAggregate`, so it is where the "latest month, not 13-month average" decision is implemented.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Services/IPricingBaselineBuilder.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Services/PricingBaselineBuilder.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingFilterDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/GetPricingBaseline/GetPricingBaselineRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/GetPricingBaseline/GetPricingBaselineResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/GetPricingBaseline/GetPricingBaselineHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/PricingModule.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (add `services.AddPricingModule();` beside the other module registrations, around line 119)
- Test: `backend/test/Anela.Heblo.Tests/Features/Pricing/PricingBaselineBuilderTests.cs`

**Interfaces:**
- Consumes: `PricingBaselineRow`, `IPricingSimulationCalculator`, `PricingRowDto`, `PricingTotalsDto` (Task 2); `ICatalogRepository` and `CatalogAggregate` from `Anela.Heblo.Domain.Features.Catalog`.
- Produces:
  - `PricingFilterDto`: `string? ProductCode`, `string? ProductName`, `ProductType? ProductType`.
  - `IPricingBaselineBuilder.BuildAsync(PricingFilterDto filter, CancellationToken ct) → Task<IReadOnlyList<PricingBaselineRow>>`.
  - `GetPricingBaselineResponse : BaseResponse` with `List<PricingRowDto> Rows` and `PricingTotalsDto Totals`.
  - Route `GET /api/pricing-simulator/baseline`.

- [ ] **Step 1: Write the failing tests for the baseline builder**

`backend/test/Anela.Heblo.Tests/Features/Pricing/PricingBaselineBuilderTests.cs`. Build `CatalogAggregate` instances the way the existing catalog tests do — check `backend/test/Anela.Heblo.Tests/Features/` for an existing catalog fixture helper and reuse it rather than inventing one.

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingBaselineBuilderTests
{
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));

    private PricingBaselineBuilder CreateSut() => new(_catalog.Object, _time);

    [Fact]
    public async Task Uses_the_latest_month_costs_not_the_thirteen_month_average()
    {
        // A product whose material cost rose sharply in the most recent month: the average
        // would understate today's cost, which is the whole reason this feature exists.
        var product = CatalogTestData.WithMonthlyMargins(
            productCode: "P1",
            priceWithoutVat: 420m,
            months: new[]
            {
                (Month: new DateTime(2026, 8, 1), Material: 150m, Manufacturing: 70m),
                (Month: new DateTime(2026, 9, 1), Material: 193m, Manufacturing: 70m)
            });
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().MaterialCost.Should().Be(193m);
        rows.Single().ManufacturingCost.Should().Be(70m);
    }

    [Fact]
    public async Task Sums_sales_quantity_over_the_trailing_twelve_months()
    {
        var product = CatalogTestData.WithSales("P1", priceWithoutVat: 420m, sales: new[]
        {
            (Date: new DateTime(2026, 9, 1), Quantity: 100d),
            (Date: new DateTime(2026, 3, 1), Quantity: 250d),
            (Date: new DateTime(2025, 6, 1), Quantity: 999d)   // older than 12 months — excluded
        });
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().Quantity.Should().Be(350d);
    }

    [Fact]
    public async Task Flags_a_product_without_a_price_as_having_no_data()
    {
        var product = CatalogTestData.WithMonthlyMargins("P1", priceWithoutVat: null,
            months: new[] { (Month: new DateTime(2026, 9, 1), Material: 100m, Manufacturing: 10m) });
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().HasData.Should().BeFalse();
    }

    [Fact]
    public async Task Flags_a_product_without_margin_history_as_having_no_data()
    {
        var product = CatalogTestData.WithMonthlyMargins("P1", priceWithoutVat: 420m,
            months: Array.Empty<(DateTime, decimal, decimal)>());
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Single().HasData.Should().BeFalse();
    }

    [Fact]
    public async Task Defaults_to_products_and_goods_when_no_type_filter_is_given()
    {
        _catalog.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate>
                {
                    CatalogTestData.OfType("P1", ProductType.Product),
                    CatalogTestData.OfType("G1", ProductType.Goods),
                    CatalogTestData.OfType("M1", ProductType.Material)
                });

        var rows = await CreateSut().BuildAsync(new PricingFilterDto(), CancellationToken.None);

        rows.Select(r => r.ProductCode).Should().BeEquivalentTo(new[] { "P1", "G1" });
    }
}
```

Write `CatalogTestData` as a private static helper class at the bottom of this test file, building `CatalogAggregate` with `Margins.MonthlyData` entries and `SalesHistory` records. Read `backend/src/Anela.Heblo.Domain/Features/Catalog/CatalogAggregate.cs` and `MonthlyMarginHistory.cs` first to get the shapes exactly right.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingBaselineBuilderTests"`

Expected: FAIL to compile — `PricingBaselineBuilder` does not exist.

- [ ] **Step 3: Implement the filter DTO and the baseline builder**

`Contracts/PricingFilterDto.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Pricing.Contracts;

public class PricingFilterDto
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }
}
```

`Services/IPricingBaselineBuilder.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;

namespace Anela.Heblo.Application.Features.Pricing.Services;

public interface IPricingBaselineBuilder
{
    Task<IReadOnlyList<PricingBaselineRow>> BuildAsync(PricingFilterDto filter, CancellationToken ct);
}
```

`Services/PricingBaselineBuilder.cs` — the shape to implement:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Application.Features.Pricing.Services;

public class PricingBaselineBuilder : IPricingBaselineBuilder
{
    private const int TrailingSalesMonths = 12;

    private readonly ICatalogRepository _catalogRepository;
    private readonly TimeProvider _timeProvider;

    public PricingBaselineBuilder(ICatalogRepository catalogRepository, TimeProvider timeProvider)
    {
        _catalogRepository = catalogRepository;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<PricingBaselineRow>> BuildAsync(PricingFilterDto filter, CancellationToken ct)
    {
        var products = await _catalogRepository.GetAllAsync(ct) ?? Enumerable.Empty<CatalogAggregate>();
        var now = _timeProvider.GetUtcNow().DateTime;
        var salesFrom = now.AddMonths(-TrailingSalesMonths);

        return ApplyFilters(products, filter)
            .OrderBy(p => p.ProductCode)
            .Select(p => ToBaselineRow(p, salesFrom, now))
            .ToList();
    }

    private static IEnumerable<CatalogAggregate> ApplyFilters(
        IEnumerable<CatalogAggregate> products, PricingFilterDto filter)
    {
        // Mirrors GetProductMarginsHandler.ApplyFilters so the two screens agree on what
        // "all products" means.
        if (!string.IsNullOrWhiteSpace(filter.ProductCode))
            products = products.Where(x => x.ProductCode != null &&
                x.ProductCode.Contains(filter.ProductCode, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.ProductName))
            products = products.Where(x => x.ProductName != null &&
                x.ProductName.Contains(filter.ProductName, StringComparison.OrdinalIgnoreCase));

        products = filter.ProductType.HasValue
            ? products.Where(x => x.Type == filter.ProductType.Value)
            : products.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods);

        return products;
    }

    private static PricingBaselineRow ToBaselineRow(CatalogAggregate product, DateTime salesFrom, DateTime now)
    {
        // Latest month, deliberately not Margins.Averages: the premise of this feature is
        // that costs just rose, and a 13-month average would understate today's cost.
        var latest = product.Margins.MonthlyData
            .OrderByDescending(m => m.Key)
            .Select(m => m.Value)
            .FirstOrDefault();

        var price = product.PriceWithoutVat ?? 0m;
        var hasData = product.PriceWithoutVat is > 0m && latest is not null;

        return new PricingBaselineRow(
            ProductCode: product.ProductCode ?? string.Empty,
            ProductName: product.ProductName ?? string.Empty,
            Price: price,
            MaterialCost: latest?.M0.CostLevel ?? 0m,
            ManufacturingCost: latest?.M1_A.CostLevel ?? 0m,
            Quantity: product.GetTotalSold(salesFrom, now),
            HasData: hasData);
    }
}
```

Adjust the `PriceWithoutVat` and `MonthlyData` access to the actual member shapes — read `CatalogAggregate.cs` and `MonthlyMarginHistory.cs` and match them exactly rather than assuming.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingBaselineBuilderTests"`

Expected: PASS.

- [ ] **Step 5: Add the request, response and handler**

`UseCases/GetPricingBaseline/GetPricingBaselineRequest.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;

public class GetPricingBaselineRequest : IRequest<GetPricingBaselineResponse>
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }
}
```

`UseCases/GetPricingBaseline/GetPricingBaselineResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;

public class GetPricingBaselineResponse : BaseResponse
{
    public List<PricingRowDto> Rows { get; set; } = new();
    public PricingTotalsDto Totals { get; set; } = new();
}
```

`UseCases/GetPricingBaseline/GetPricingBaselineHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;

public class GetPricingBaselineHandler
    : IRequestHandler<GetPricingBaselineRequest, GetPricingBaselineResponse>
{
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly IPricingSimulationCalculator _calculator;

    public GetPricingBaselineHandler(
        IPricingBaselineBuilder baselineBuilder,
        IPricingSimulationCalculator calculator)
    {
        _baselineBuilder = baselineBuilder;
        _calculator = calculator;
    }

    public async Task<GetPricingBaselineResponse> Handle(
        GetPricingBaselineRequest request, CancellationToken cancellationToken)
    {
        var filter = new PricingFilterDto
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ProductType = request.ProductType
        };

        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);

        // No overrides and no edit: the untouched starting state.
        var result = _calculator.Calculate(baseline, Array.Empty<PricingOverrideDto>(), edit: null);

        return new GetPricingBaselineResponse
        {
            Rows = result.Rows.ToList(),
            Totals = result.Totals
        };
    }
}
```

- [ ] **Step 6: Add the module and register it**

`PricingModule.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Pricing;

public static class PricingModule
{
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IPricingSimulationCalculator, PricingSimulationCalculator>();
        services.AddScoped<IPricingBaselineBuilder, PricingBaselineBuilder>();

        // MediatR handlers are auto-registered by the assembly scan in ApplicationModule.
        return services;
    }
}
```

In `ApplicationModule.cs`, beside the other module registrations (around line 119):

```csharp
        services.AddPricingModule();
```

- [ ] **Step 7: Add the controller**

`backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Finance_PriceAnalysis)]
[ApiController]
[Route("api/pricing-simulator")]
public class PricingSimulatorController : BaseApiController
{
    private readonly IMediator _mediator;

    public PricingSimulatorController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("baseline")]
    public async Task<ActionResult<GetPricingBaselineResponse>> GetBaseline(
        [FromQuery] GetPricingBaselineRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}
```

`Feature.Finance_PriceAnalysis` does not exist yet — it arrives in Task 10. Until then this will not compile, so **add the access-matrix entry from Task 10 Step 1 and Step 2 now** (the JSON entry plus the regen), then continue. The rest of Task 10 stays where it is.

- [ ] **Step 8: Verify the build and the whole test project**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Pricing"`

Expected: build succeeds, all Pricing tests pass.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Pricing \
        backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Anela.Heblo.Domain/Features/Authorization access-matrix.json \
        backend/test/Anela.Heblo.Tests/Features/Pricing
git commit -m "feat: add pricing baseline endpoint"
```

---

### Task 4: Recalculate endpoint

Turns the calculator into the interactive surface the grid talks to on every cell blur.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/RecalculatePricing/RecalculatePricingRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/RecalculatePricing/RecalculatePricingResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/UseCases/RecalculatePricing/RecalculatePricingHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Validators/RecalculatePricingRequestValidator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Pricing/PricingModule.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Pricing/RecalculatePricingHandlerTests.cs`

**Interfaces:**
- Consumes: `IPricingBaselineBuilder`, `IPricingSimulationCalculator`, `PricingEditException`, `PricingOverrideDto`, `PricingEditDto`, `PricingFilterDto`.
- Produces: `RecalculatePricingResponse : BaseResponse` with `List<PricingRowDto> Rows`, `PricingTotalsDto Totals`, `List<PricingOverrideDto> Overrides`; route `POST /api/pricing-simulator/recalculate`.

- [ ] **Step 1: Write the failing handler tests**

`backend/test/Anela.Heblo.Tests/Features/Pricing/RecalculatePricingHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class RecalculatePricingHandlerTests
{
    private readonly Mock<IPricingBaselineBuilder> _baseline = new();

    // The real calculator, not a mock: this handler's job is to wire it up, and a mock
    // would let a wiring bug through.
    private RecalculatePricingHandler CreateSut()
        => new(_baseline.Object, new PricingSimulationCalculator());

    private void GivenBaseline(params PricingBaselineRow[] rows)
        => _baseline.Setup(b => b.BuildAsync(It.IsAny<PricingFilterDto>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows.ToList());

    private static PricingBaselineRow Row(string code = "P1")
        => new(code, $"Product {code}", 420m, 175m, 70m, 1000d, true);

    [Fact]
    public async Task Applies_the_edit_and_returns_recomputed_rows_and_totals()
    {
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>(),
            Edit = new PricingEditDto
            {
                ProductCode = "P1", Field = PricingEditField.Price, Value = 500m
            }
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Rows.Single().Price.Should().Be(500m);
        response.Totals.RevenueAfter.Should().Be(500_000m);
        response.Overrides.Single().Price.Should().Be(500m);
    }

    [Fact]
    public async Task Returns_the_error_code_when_the_edit_is_impossible()
    {
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>(),
            Edit = new PricingEditDto
            {
                ProductCode = "P1", Field = PricingEditField.M0Amount, Value = 500m
            }
        }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingNegativeMaterialCost);
    }

    [Fact]
    public async Task Rebuilds_the_baseline_from_the_catalog_and_ignores_any_client_supplied_baseline()
    {
        // The client sends only overrides; baselines are always re-derived server-side so
        // a tampered payload cannot change the reported margin.
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>(),
            Edit = null
        }, CancellationToken.None);

        response.Rows.Single().BaselinePrice.Should().Be(420m);
        response.Rows.Single().BaselineMaterialCost.Should().Be(175m);
    }

    [Fact]
    public async Task Replays_an_existing_override_set_when_no_edit_is_supplied()
    {
        GivenBaseline(Row());

        var response = await CreateSut().Handle(new RecalculatePricingRequest
        {
            Overrides = new List<PricingOverrideDto>
            {
                new() { ProductCode = "P1", Price = 500m, MaterialCost = 175m,
                        ManufacturingCost = 70m, ForecastQuantity = 900d }
            },
            Edit = null
        }, CancellationToken.None);

        response.Rows.Single().Price.Should().Be(500m);
        response.Rows.Single().ForecastQuantity.Should().Be(900d);
        response.Totals.RevenueAfter.Should().Be(450_000m);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecalculatePricingHandlerTests"`

Expected: FAIL to compile.

- [ ] **Step 3: Implement request, response, handler and validator**

`RecalculatePricingRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;

public class RecalculatePricingRequest : IRequest<RecalculatePricingResponse>
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }

    /// <summary>Sparse: only products the user has touched. Never carries baselines.</summary>
    public List<PricingOverrideDto> Overrides { get; set; } = new();

    /// <summary>The gesture just made. Null means "replay the overrides as given".</summary>
    public PricingEditDto? Edit { get; set; }
}
```

`RecalculatePricingResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;

public class RecalculatePricingResponse : BaseResponse
{
    public List<PricingRowDto> Rows { get; set; } = new();
    public PricingTotalsDto Totals { get; set; } = new();
    public List<PricingOverrideDto> Overrides { get; set; } = new();

    public RecalculatePricingResponse() { }

    public RecalculatePricingResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
```

`RecalculatePricingHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;

public class RecalculatePricingHandler
    : IRequestHandler<RecalculatePricingRequest, RecalculatePricingResponse>
{
    private readonly IPricingBaselineBuilder _baselineBuilder;
    private readonly IPricingSimulationCalculator _calculator;

    public RecalculatePricingHandler(
        IPricingBaselineBuilder baselineBuilder,
        IPricingSimulationCalculator calculator)
    {
        _baselineBuilder = baselineBuilder;
        _calculator = calculator;
    }

    public async Task<RecalculatePricingResponse> Handle(
        RecalculatePricingRequest request, CancellationToken cancellationToken)
    {
        var filter = new PricingFilterDto
        {
            ProductCode = request.ProductCode,
            ProductName = request.ProductName,
            ProductType = request.ProductType
        };

        var baseline = await _baselineBuilder.BuildAsync(filter, cancellationToken);

        try
        {
            var result = _calculator.Calculate(baseline, request.Overrides, request.Edit);

            return new RecalculatePricingResponse
            {
                Rows = result.Rows.ToList(),
                Totals = result.Totals,
                Overrides = result.Overrides.ToList()
            };
        }
        catch (PricingEditException ex)
        {
            // An impossible edit is a user error, not a fault: the UI keeps the prior cell
            // value and shows the message inline.
            return new RecalculatePricingResponse(ex.ErrorCode, ex.Parameters);
        }
    }
}
```

`Validators/RecalculatePricingRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.UseCases.RecalculatePricing;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Pricing.Validators;

public class RecalculatePricingRequestValidator : AbstractValidator<RecalculatePricingRequest>
{
    public RecalculatePricingRequestValidator()
    {
        RuleForEach(x => x.Overrides)
            .ChildRules(o => o.RuleFor(x => x.ProductCode).NotEmpty());

        When(x => x.Edit is not null, () =>
        {
            RuleFor(x => x.Edit!.ProductCode).NotEmpty();
            RuleFor(x => x.Edit!.Field).IsInEnum();
        });
    }
}
```

- [ ] **Step 4: Register the validator and the pipeline behavior**

In `PricingModule.cs`, before the `return services;`:

```csharp
        services.AddScoped<IValidator<RecalculatePricingRequest>, RecalculatePricingRequestValidator>();
        services.AddScoped<IPipelineBehavior<RecalculatePricingRequest, RecalculatePricingResponse>,
            ValidationBehavior<RecalculatePricingRequest, RecalculatePricingResponse>>();
```

Add the matching `using` directives — copy the exact namespaces from `backend/src/Anela.Heblo.Application/Features/Packaging/PackagingModule.cs`, which does the same thing.

- [ ] **Step 5: Add the controller action**

In `PricingSimulatorController.cs`:

```csharp
    [HttpPost("recalculate")]
    public async Task<ActionResult<RecalculatePricingResponse>> Recalculate(
        [FromBody] RecalculatePricingRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecalculatePricingHandlerTests"`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Pricing \
        backend/src/Anela.Heblo.API/Controllers/PricingSimulatorController.cs \
        backend/test/Anela.Heblo.Tests/Features/Pricing
git commit -m "feat: add pricing recalculate endpoint"
```

---

### Task 5: Scenario persistence

Two tables, a repository, and a manual migration. Nothing user-visible yet — this task's deliverable is that a scenario can be round-tripped through Postgres.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Pricing/PricingScenario.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Pricing/PricingScenarioItem.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Pricing/IPricingScenarioRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioItemConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (add two `DbSet`s beside the existing ones)
- Modify: `backend/src/Anela.Heblo.Application/Features/Pricing/PricingModule.cs` (register the repository)
- Test: `backend/test/Anela.Heblo.Tests/Features/Pricing/PricingScenarioRepositoryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (deliberately — the domain entities do not depend on the Application-layer DTOs).
- Produces:
  - `PricingScenario`: `Guid Id`, `string Name`, `string? Description`, `string CreatedBy`, `DateTime CreatedAt`, `DateTime ModifiedAt`, `string FilterJson`, `ICollection<PricingScenarioItem> Items`.
  - `PricingScenarioItem`: `Guid Id`, `Guid ScenarioId`, `string ProductCode`, `decimal? Price`, `decimal? MaterialCost`, `decimal? ManufacturingCost`, `double? ForecastQuantity`, `decimal BaselinePrice`, `decimal BaselineMaterialCost`, `decimal BaselineManufacturingCost`, `double BaselineQuantity`.
  - `IPricingScenarioRepository`: `GetAllAsync(CancellationToken)`, `GetByIdAsync(Guid, CancellationToken)`, `AddAsync(PricingScenario, CancellationToken)`, `UpdateAsync(PricingScenario, CancellationToken)`, `DeleteAsync(Guid, CancellationToken)`, `ExistsByNameAsync(string name, Guid? excludingId, CancellationToken)`.

- [ ] **Step 1: Write the failing repository tests**

Use the in-memory or Testcontainers pattern the existing persistence tests use — read `backend/test/Anela.Heblo.Tests/Persistence/` first and follow whichever is established there. Note: the EF **InMemory provider throws on `ExecuteDelete`/`ExecuteUpdate`**, so if the tests use InMemory, the repository's delete must load and `RemoveRange` rather than `ExecuteDelete`.

`backend/test/Anela.Heblo.Tests/Features/Pricing/PricingScenarioRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Pricing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingScenarioRepositoryTests
{
    private static PricingScenario NewScenario(string name = "Podzim 2026") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Description = "Návrh nového ceníku",
        CreatedBy = "ondra@anela.cz",
        CreatedAt = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc),
        ModifiedAt = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc),
        FilterJson = "{}",
        Items =
        {
            new PricingScenarioItem
            {
                ProductCode = "DEO002050",
                Price = 500m,
                MaterialCost = 175m,
                ManufacturingCost = 70m,
                ForecastQuantity = 1100d,
                BaselinePrice = 420m,
                BaselineMaterialCost = 175m,
                BaselineManufacturingCost = 70m,
                BaselineQuantity = 1240d
            }
        }
    };

    [Fact]
    public async Task Round_trips_a_scenario_with_its_items()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var sut = fixture.CreateRepository();
        var scenario = NewScenario();

        await sut.AddAsync(scenario, CancellationToken.None);

        var loaded = await fixture.CreateRepository().GetByIdAsync(scenario.Id, CancellationToken.None);
        loaded.Should().NotBeNull();
        loaded!.Items.Should().HaveCount(1);
        loaded.Items.Single().Price.Should().Be(500m);
        loaded.Items.Single().BaselinePrice.Should().Be(420m);
    }

    [Fact]
    public async Task Persists_a_baseline_snapshot_so_a_reopened_scenario_can_detect_drift()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var scenario = NewScenario();
        await fixture.CreateRepository().AddAsync(scenario, CancellationToken.None);

        var loaded = await fixture.CreateRepository().GetByIdAsync(scenario.Id, CancellationToken.None);

        loaded!.Items.Single().BaselineMaterialCost.Should().Be(175m);
        loaded.Items.Single().BaselineQuantity.Should().Be(1240d);
    }

    [Fact]
    public async Task Deleting_a_scenario_removes_its_items()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var scenario = NewScenario();
        await fixture.CreateRepository().AddAsync(scenario, CancellationToken.None);

        await fixture.CreateRepository().DeleteAsync(scenario.Id, CancellationToken.None);

        var loaded = await fixture.CreateRepository().GetByIdAsync(scenario.Id, CancellationToken.None);
        loaded.Should().BeNull();
        fixture.CountItems().Should().Be(0);
    }

    [Fact]
    public async Task Detects_a_duplicate_name_and_ignores_the_scenario_being_edited()
    {
        await using var fixture = PricingPersistenceFixture.Create();
        var scenario = NewScenario();
        await fixture.CreateRepository().AddAsync(scenario, CancellationToken.None);
        var sut = fixture.CreateRepository();

        (await sut.ExistsByNameAsync("Podzim 2026", null, CancellationToken.None)).Should().BeTrue();
        (await sut.ExistsByNameAsync("Podzim 2026", scenario.Id, CancellationToken.None)).Should().BeFalse();
        (await sut.ExistsByNameAsync("Jaro 2027", null, CancellationToken.None)).Should().BeFalse();
    }
}
```

Write `PricingPersistenceFixture` as a small helper in the same file (or next to the existing persistence test helpers), exposing `CreateRepository()` — a *fresh* `ApplicationDbContext` each call, so a test cannot pass on EF's change tracker alone — and `CountItems()`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingScenarioRepositoryTests"`

Expected: FAIL to compile.

- [ ] **Step 3: Write the entities**

`backend/src/Anela.Heblo.Domain/Features/Pricing/PricingScenario.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Pricing;

public class PricingScenario
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    /// <summary>The filter the scenario was built under, so reopening restores the same set.</summary>
    public string FilterJson { get; set; } = "{}";

    public ICollection<PricingScenarioItem> Items { get; set; } = new List<PricingScenarioItem>();
}
```

`backend/src/Anela.Heblo.Domain/Features/Pricing/PricingScenarioItem.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Pricing;

/// <summary>
/// One edited product inside a scenario. Overrides are the independent variables (price and
/// costs) — margins are always derived, never stored, so edit order cannot corrupt a reload.
/// The Baseline* columns snapshot what the catalog said at save time, so a scenario reopened
/// months later still shows what was actually decided against.
/// </summary>
public class PricingScenarioItem
{
    public Guid Id { get; set; }
    public Guid ScenarioId { get; set; }
    public PricingScenario? Scenario { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public decimal? Price { get; set; }
    public decimal? MaterialCost { get; set; }
    public decimal? ManufacturingCost { get; set; }
    public double? ForecastQuantity { get; set; }

    public decimal BaselinePrice { get; set; }
    public decimal BaselineMaterialCost { get; set; }
    public decimal BaselineManufacturingCost { get; set; }
    public double BaselineQuantity { get; set; }
}
```

- [ ] **Step 4: Write the EF configurations and register the DbSets**

`backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Pricing;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Pricing;

public class PricingScenarioConfiguration : IEntityTypeConfiguration<PricingScenario>
{
    public void Configure(EntityTypeBuilder<PricingScenario> builder)
    {
        builder.ToTable("PricingScenarios", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).IsRequired(false).HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(320);
        builder.Property(x => x.CreatedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.ModifiedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.FilterJson).IsRequired().HasColumnType("jsonb");

        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasMany(x => x.Items)
            .WithOne(x => x.Scenario!)
            .HasForeignKey(x => x.ScenarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioItemConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Pricing;

public class PricingScenarioItemConfiguration : IEntityTypeConfiguration<PricingScenarioItem>
{
    public void Configure(EntityTypeBuilder<PricingScenarioItem> builder)
    {
        builder.ToTable("PricingScenarioItems", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductCode).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Price).HasPrecision(18, 4);
        builder.Property(x => x.MaterialCost).HasPrecision(18, 4);
        builder.Property(x => x.ManufacturingCost).HasPrecision(18, 4);
        builder.Property(x => x.BaselinePrice).HasPrecision(18, 4);
        builder.Property(x => x.BaselineMaterialCost).HasPrecision(18, 4);
        builder.Property(x => x.BaselineManufacturingCost).HasPrecision(18, 4);

        builder.HasIndex(x => new { x.ScenarioId, x.ProductCode }).IsUnique();
    }
}
```

In `ApplicationDbContext.cs`, beside the existing `DbSet` declarations:

```csharp
    // Pricing simulator
    public DbSet<Anela.Heblo.Domain.Features.Pricing.PricingScenario> PricingScenarios { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Pricing.PricingScenarioItem> PricingScenarioItems { get; set; } = null!;
```

Check how `ApplicationDbContext` picks up `IEntityTypeConfiguration` classes — if it calls `ApplyConfigurationsFromAssembly`, nothing further is needed; if configurations are applied one by one in `OnModelCreating`, add both there.

- [ ] **Step 5: Write the repository**

`backend/src/Anela.Heblo.Persistence/Pricing/PricingScenarioRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Pricing;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Pricing;

public class PricingScenarioRepository : IPricingScenarioRepository
{
    private readonly ApplicationDbContext _context;

    public PricingScenarioRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<List<PricingScenario>> GetAllAsync(CancellationToken ct = default)
        => _context.PricingScenarios
            .AsNoTracking()
            .OrderByDescending(s => s.ModifiedAt)
            .ToListAsync(ct);

    public Task<PricingScenario?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.PricingScenarios
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task AddAsync(PricingScenario scenario, CancellationToken ct = default)
    {
        _context.PricingScenarios.Add(scenario);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PricingScenario scenario, CancellationToken ct = default)
    {
        // Replace the item set wholesale rather than diffing: a scenario is small, and
        // adding a child with an explicit PK to a tracked parent's collection makes EF emit
        // an UPDATE affecting zero rows instead of an INSERT.
        var existing = await _context.PricingScenarios
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == scenario.Id, ct);

        if (existing is null) return;

        existing.Name = scenario.Name;
        existing.Description = scenario.Description;
        existing.ModifiedAt = scenario.ModifiedAt;
        existing.FilterJson = scenario.FilterJson;

        _context.PricingScenarioItems.RemoveRange(existing.Items);
        foreach (var item in scenario.Items)
        {
            existing.Items.Add(new PricingScenarioItem
            {
                ProductCode = item.ProductCode,
                Price = item.Price,
                MaterialCost = item.MaterialCost,
                ManufacturingCost = item.ManufacturingCost,
                ForecastQuantity = item.ForecastQuantity,
                BaselinePrice = item.BaselinePrice,
                BaselineMaterialCost = item.BaselineMaterialCost,
                BaselineManufacturingCost = item.BaselineManufacturingCost,
                BaselineQuantity = item.BaselineQuantity
            });
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // Load and Remove rather than ExecuteDelete: the EF InMemory provider used by the
        // unit tests throws on ExecuteDelete.
        var scenario = await _context.PricingScenarios
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (scenario is null) return;

        _context.PricingScenarios.Remove(scenario);
        await _context.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsByNameAsync(string name, Guid? excludingId, CancellationToken ct = default)
        => _context.PricingScenarios
            .AnyAsync(s => s.Name == name && (excludingId == null || s.Id != excludingId), ct);
}
```

And the interface, `backend/src/Anela.Heblo.Domain/Features/Pricing/IPricingScenarioRepository.cs`, with exactly those six members.

- [ ] **Step 6: Register the repository**

In `PricingModule.cs`:

```csharp
        services.AddScoped<IPricingScenarioRepository, PricingScenarioRepository>();
```

If `PricingModule` (Application layer) cannot reference the Persistence layer, follow whatever pattern `MindMapsModule` uses — it registers `IMindMapRepository → MindMapRepository` directly, so the same reference direction is available.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingScenarioRepositoryTests"`

Expected: PASS.

- [ ] **Step 8: Generate the migration**

```bash
cd backend/src/Anela.Heblo.Persistence
dotnet ef migrations add AddPricingScenarios --startup-project ../Anela.Heblo.API
```

Read the generated migration and confirm it creates only `PricingScenarios` and `PricingScenarioItems` with the unique indexes — if it contains anything else, the model has drifted and that must be resolved before committing.

**Do not apply it to any database.** Migrations in this project are applied manually, outside deployment.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Pricing \
        backend/src/Anela.Heblo.Persistence/Pricing \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Persistence/Migrations \
        backend/src/Anela.Heblo.Application/Features/Pricing/PricingModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Pricing
git commit -m "feat: add pricing scenario persistence"
```

---

### Task 6: Scenario CRUD endpoints

Exposes the persistence from Task 5, and implements baseline-drift detection on load.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingScenarioSummaryDto.cs`
- Create: `.../UseCases/GetPricingScenarios/{GetPricingScenariosRequest,Response,Handler}.cs`
- Create: `.../UseCases/GetPricingScenario/{GetPricingScenarioRequest,Response,Handler}.cs`
- Create: `.../UseCases/SavePricingScenario/{SavePricingScenarioRequest,Response,Handler}.cs`
- Create: `.../UseCases/DeletePricingScenario/{DeletePricingScenarioRequest,Response,Handler}.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Pricing/Validators/SavePricingScenarioRequestValidator.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/PricingScenariosController.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Pricing/Contracts/PricingRowDto.cs` (add `bool BaselineDrifted`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Pricing/PricingModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Pricing/PricingScenarioHandlersTests.cs`

**Interfaces:**
- Consumes: `IPricingScenarioRepository`, `IPricingBaselineBuilder`, `IPricingSimulationCalculator`, `PricingOverrideDto`, `PricingRowDto`, `PricingTotalsDto`, `ErrorCodes.PricingScenarioNotFound`, `ErrorCodes.PricingScenarioNameConflict`.
- Produces:
  - `PricingScenarioSummaryDto`: `Guid Id`, `string Name`, `string? Description`, `string CreatedBy`, `DateTime CreatedAt`, `DateTime ModifiedAt`, `int EditedProductCount`.
  - `GetPricingScenarioResponse : BaseResponse` with `PricingScenarioSummaryDto Scenario`, `List<PricingRowDto> Rows`, `PricingTotalsDto Totals`, `List<PricingOverrideDto> Overrides`.
  - `SavePricingScenarioRequest`: `Guid? Id` (null = create), `string Name`, `string? Description`, filter fields, `List<PricingOverrideDto> Overrides`.
  - Routes `GET|POST /api/pricing-scenarios`, `GET|PUT|DELETE /api/pricing-scenarios/{id}`.

- [ ] **Step 1: Add the drift flag to the row DTO**

In `PricingRowDto`, after `IsExcluded`:

```csharp
    /// <summary>
    /// True when this row came from a saved scenario whose snapshot no longer matches the
    /// catalog — the cost or price has moved since the scenario was saved.
    /// </summary>
    public bool BaselineDrifted { get; set; }
```

- [ ] **Step 2: Write the failing handler tests**

`backend/test/Anela.Heblo.Tests/Features/Pricing/PricingScenarioHandlersTests.cs`. Cover exactly these behaviours, using a mocked `IPricingScenarioRepository` and a mocked `IPricingBaselineBuilder` with the real `PricingSimulationCalculator`:

```csharp
using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Application.Features.Pricing.Model;
using Anela.Heblo.Application.Features.Pricing.Services;
using Anela.Heblo.Application.Features.Pricing.UseCases.DeletePricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Pricing;

public class PricingScenarioHandlersTests
{
    private readonly Mock<IPricingScenarioRepository> _repository = new();
    private readonly Mock<IPricingBaselineBuilder> _baseline = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero));

    private static PricingBaselineRow Row(
        string code = "P1", decimal price = 420m, decimal material = 175m)
        => new(code, $"Product {code}", price, material, 70m, 1000d, true);

    private void GivenBaseline(params PricingBaselineRow[] rows)
        => _baseline.Setup(b => b.BuildAsync(It.IsAny<PricingFilterDto>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(rows.ToList());

    [Fact]
    public async Task Loading_a_missing_scenario_returns_PricingScenarioNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PricingScenario?)null);

        var handler = new GetPricingScenarioHandler(
            _repository.Object, _baseline.Object, new PricingSimulationCalculator());

        var response = await handler.Handle(
            new GetPricingScenarioRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNotFound);
    }

    [Fact]
    public async Task Loading_a_scenario_replays_its_overrides_onto_the_current_baseline()
    {
        GivenBaseline(Row());
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ScenarioWithItem(id, price: 500m, baselinePrice: 420m, baselineMaterial: 175m));

        var handler = new GetPricingScenarioHandler(
            _repository.Object, _baseline.Object, new PricingSimulationCalculator());

        var response = await handler.Handle(
            new GetPricingScenarioRequest { Id = id }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Rows.Single().Price.Should().Be(500m);
    }

    [Fact]
    public async Task Flags_a_row_whose_baseline_has_drifted_since_the_scenario_was_saved()
    {
        // The catalog now says material costs 193; the scenario was saved when it was 175.
        GivenBaseline(Row(material: 193m));
        var id = Guid.NewGuid();
        _repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(ScenarioWithItem(id, price: 500m, baselinePrice: 420m, baselineMaterial: 175m));

        var handler = new GetPricingScenarioHandler(
            _repository.Object, _baseline.Object, new PricingSimulationCalculator());

        var response = await handler.Handle(
            new GetPricingScenarioRequest { Id = id }, CancellationToken.None);

        response.Rows.Single().BaselineDrifted.Should().BeTrue();
    }

    [Fact]
    public async Task Saving_with_a_name_already_in_use_returns_PricingScenarioNameConflict()
    {
        _repository.Setup(r => r.ExistsByNameAsync("Podzim 2026", null, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);
        GivenBaseline(Row());

        var handler = new SavePricingScenarioHandler(
            _repository.Object, _baseline.Object, _time, CurrentUserStub());

        var response = await handler.Handle(new SavePricingScenarioRequest
        {
            Name = "Podzim 2026",
            Overrides = new List<PricingOverrideDto>()
        }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNameConflict);
    }

    [Fact]
    public async Task Saving_snapshots_the_current_baseline_alongside_each_override()
    {
        GivenBaseline(Row());
        PricingScenario? captured = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<PricingScenario>(), It.IsAny<CancellationToken>()))
                   .Callback<PricingScenario, CancellationToken>((s, _) => captured = s)
                   .Returns(Task.CompletedTask);

        var handler = new SavePricingScenarioHandler(
            _repository.Object, _baseline.Object, _time, CurrentUserStub());

        await handler.Handle(new SavePricingScenarioRequest
        {
            Name = "Podzim 2026",
            Overrides = new List<PricingOverrideDto>
            {
                new() { ProductCode = "P1", Price = 500m, MaterialCost = 175m,
                        ManufacturingCost = 70m, ForecastQuantity = 1100d }
            }
        }, CancellationToken.None);

        captured.Should().NotBeNull();
        var item = captured!.Items.Single();
        item.Price.Should().Be(500m);
        item.BaselinePrice.Should().Be(420m);        // snapshot, not the edited value
        item.BaselineMaterialCost.Should().Be(175m);
        item.BaselineQuantity.Should().Be(1000d);
    }

    [Fact]
    public async Task Deleting_a_missing_scenario_returns_PricingScenarioNotFound()
    {
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PricingScenario?)null);

        var handler = new DeletePricingScenarioHandler(_repository.Object);

        var response = await handler.Handle(
            new DeletePricingScenarioRequest { Id = Guid.NewGuid() }, CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PricingScenarioNotFound);
    }

    private static PricingScenario ScenarioWithItem(
        Guid id, decimal price, decimal baselinePrice, decimal baselineMaterial) => new()
    {
        Id = id,
        Name = "Podzim 2026",
        CreatedBy = "ondra@anela.cz",
        FilterJson = "{}",
        Items =
        {
            new PricingScenarioItem
            {
                ProductCode = "P1",
                Price = price,
                MaterialCost = 175m,
                ManufacturingCost = 70m,
                ForecastQuantity = 1000d,
                BaselinePrice = baselinePrice,
                BaselineMaterialCost = baselineMaterial,
                BaselineManufacturingCost = 70m,
                BaselineQuantity = 1000d
            }
        }
    };
}
```

`CurrentUserStub()` must return whatever abstraction this codebase uses to identify the caller — find it by grepping for `ICurrentUser` or similar in an existing handler that records `CreatedBy`, and follow that exactly.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PricingScenarioHandlersTests"`

Expected: FAIL to compile.

- [ ] **Step 4: Implement the four handlers**

Follow the shapes established in Tasks 3 and 4. Specifics that the tests pin down:

- `GetPricingScenarioHandler`: load the scenario; if null return `PricingScenarioNotFound`. Map `Items` to `PricingOverrideDto`. Build the current baseline using the scenario's stored filter. Call the calculator with `edit: null`. Then set `BaselineDrifted` on each row by comparing the row's current baseline values against the item's snapshot:

```csharp
        var snapshotByCode = scenario.Items.ToDictionary(i => i.ProductCode, StringComparer.OrdinalIgnoreCase);
        foreach (var row in result.Rows)
        {
            if (!snapshotByCode.TryGetValue(row.ProductCode, out var snapshot)) continue;

            row.BaselineDrifted =
                snapshot.BaselinePrice != row.BaselinePrice ||
                snapshot.BaselineMaterialCost != row.BaselineMaterialCost ||
                snapshot.BaselineManufacturingCost != row.BaselineManufacturingCost;
        }
```

- `SavePricingScenarioHandler`: check `ExistsByNameAsync(name, request.Id)` → `PricingScenarioNameConflict`. Build the current baseline and index it by product code. For each override, create a `PricingScenarioItem` carrying the override values **and** the baseline snapshot for that product. Serialize the filter to `FilterJson` with `System.Text.Json`. Set `CreatedAt`/`ModifiedAt` from the injected `TimeProvider` (never `DateTime.Now`). Call `AddAsync` when `request.Id` is null, `UpdateAsync` otherwise.
- `GetPricingScenariosHandler`: map entities to `PricingScenarioSummaryDto`, with `EditedProductCount = scenario.Items.Count`.
- `DeletePricingScenarioHandler`: `GetByIdAsync` → null gives `PricingScenarioNotFound`, otherwise `DeleteAsync`.

- [ ] **Step 5: Add the validator and register it**

`Validators/SavePricingScenarioRequestValidator.cs`:

```csharp
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using FluentValidation;

namespace Anela.Heblo.Application.Features.Pricing.Validators;

public class SavePricingScenarioRequestValidator : AbstractValidator<SavePricingScenarioRequest>
{
    public SavePricingScenarioRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleForEach(x => x.Overrides)
            .ChildRules(o => o.RuleFor(x => x.ProductCode).NotEmpty());
    }
}
```

Register it and its `ValidationBehavior` in `PricingModule.cs`, exactly as in Task 4 Step 4.

- [ ] **Step 6: Add the controller**

`backend/src/Anela.Heblo.API/Controllers/PricingScenariosController.cs` — `[FeatureAuthorize(Feature.Finance_PriceAnalysis)]`, `[Route("api/pricing-scenarios")]`, five actions delegating to MediatR via `HandleResponse`, following `PricingSimulatorController` exactly.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd backend && dotnet build && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Pricing"`

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Pricing \
        backend/src/Anela.Heblo.API/Controllers/PricingScenariosController.cs \
        backend/test/Anela.Heblo.Tests/Features/Pricing
git commit -m "feat: add pricing scenario CRUD endpoints"
```

---

### Task 7: Frontend API hooks

Regenerates the TypeScript client and wraps the five endpoints in TanStack Query hooks.

**Files:**
- Create: `frontend/src/api/hooks/usePricingSimulator.ts`
- Modify: `frontend/src/api/client.ts` (add query keys)
- Test: `frontend/src/api/hooks/__tests__/usePricingSimulator.test.ts`

**Interfaces:**
- Consumes: the generated client methods for the routes added in Tasks 3, 4 and 6.
- Produces: `usePricingBaselineQuery(filter)`, `useRecalculatePricingMutation()`, `usePricingScenariosQuery()`, `usePricingScenarioQuery(id)`, `useSavePricingScenarioMutation()`, `useDeletePricingScenarioMutation()`.

- [ ] **Step 1: Regenerate the TypeScript client**

```bash
cd backend && dotnet msbuild -t:GenerateFrontendClientManual
```

Confirm `frontend/src/api/generated/api-client.ts` now contains `pricingSimulator_GetBaseline`, `pricingSimulator_Recalculate` and the five `pricingScenarios_*` methods. If it does not, the build did not pick up the controllers — resolve that before continuing.

- [ ] **Step 2: Add the query keys**

In `frontend/src/api/client.ts`, beside `productMargins`:

```ts
  pricingBaseline: ["pricing-baseline"] as const,
  pricingScenarios: ["pricing-scenarios"] as const,
```

- [ ] **Step 3: Write the failing hook test**

`frontend/src/api/hooks/__tests__/usePricingSimulator.test.ts`. Follow the existing hook-test pattern in `frontend/src/api/hooks/__tests__/` — mock `getAuthenticatedApiClient`, wrap in a `QueryClientProvider`.

```ts
import { renderHook, waitFor } from "@testing-library/react";
import { usePricingBaselineQuery } from "../usePricingSimulator";
import { getAuthenticatedApiClient } from "../../client";
import { createWrapper } from "./testUtils"; // reuse whatever the neighbouring tests use

jest.mock("../../client", () => ({
  ...jest.requireActual("../../client"),
  getAuthenticatedApiClient: jest.fn(),
}));

describe("usePricingBaselineQuery", () => {
  it("passes the filter through to the generated client", async () => {
    const getBaseline = jest.fn().mockResolvedValue({ rows: [], totals: {} });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingSimulator_GetBaseline: getBaseline,
    });

    const { result } = renderHook(
      () => usePricingBaselineQuery({ productCode: "DEO", productName: undefined, productType: undefined }),
      { wrapper: createWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getBaseline).toHaveBeenCalledWith("DEO", null, null);
  });

  it("refetches when the filter changes", async () => {
    const getBaseline = jest.fn().mockResolvedValue({ rows: [], totals: {} });
    (getAuthenticatedApiClient as jest.Mock).mockResolvedValue({
      pricingSimulator_GetBaseline: getBaseline,
    });

    const { rerender } = renderHook(
      ({ code }) => usePricingBaselineQuery({ productCode: code, productName: undefined, productType: undefined }),
      { wrapper: createWrapper(), initialProps: { code: "DEO" } },
    );
    await waitFor(() => expect(getBaseline).toHaveBeenCalledTimes(1));

    rerender({ code: "KRE" });
    await waitFor(() => expect(getBaseline).toHaveBeenCalledTimes(2));
  });
});
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern usePricingSimulator --watchAll=false`

Expected: FAIL — module not found. (Use `react-scripts test`, not `npx jest`; bare jest gives TS parse errors in this repo.)

- [ ] **Step 5: Implement the hooks**

`frontend/src/api/hooks/usePricingSimulator.ts` — mirror `useProductMargins.ts` for queries and `useMarketingCalendar.ts` for mutations. The mutation hooks must invalidate `QUERY_KEYS.pricingScenarios` on success. Do **not** invalidate `pricingBaseline` after a recalculate: the mutation's own response carries the new rows, and invalidating would refetch the untouched baseline and wipe the user's edits.

- [ ] **Step 6: Run the test to verify it passes**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern usePricingSimulator --watchAll=false`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add frontend/src/api/hooks/usePricingSimulator.ts \
        frontend/src/api/hooks/__tests__/usePricingSimulator.test.ts \
        frontend/src/api/client.ts frontend/src/api/generated/api-client.ts
git commit -m "feat: add pricing simulator API hooks"
```

---

### Task 8: The grid and the totals band (read-only)

Renders the baseline: filter, sticky totals, all rows. No editing yet — that is Task 9. Splitting here means a reviewer can reject the layout without also rejecting the edit machinery.

**Files:**
- Create: `frontend/src/components/pages/PriceAnalysis.tsx`
- Create: `frontend/src/components/pricing/PricingTotalsBar.tsx`
- Create: `frontend/src/components/pricing/PricingGrid.tsx`
- Test: `frontend/src/components/pages/__tests__/PriceAnalysis.test.tsx`

**Interfaces:**
- Consumes: `usePricingBaselineQuery` (Task 7); generated types `PricingRowDto`, `PricingTotalsDto`.
- Produces:
  - `PricingTotalsBar({ totals, isRecalculating })`.
  - `PricingGrid({ rows, onEdit, editingDisabled })` — `onEdit` is unused in this task and wired in Task 9; declare it optional now so Task 9 does not change the signature.

- [ ] **Step 1: Write the failing component tests**

`frontend/src/components/pages/__tests__/PriceAnalysis.test.tsx`. Existing page tests in this directory mock their context dependencies — copy that setup from `ProductMarginsList.test.tsx`, and mock any context `PriceAnalysis` consumes or the test will fail on a missing provider.

Cover: renders a row per product; renders the three totals lines with before, after and delta; shows the excluded-product count when `excludedProductCount > 0` and hides it at zero; shows an empty state when `rows` is empty; shows a loading state while the query is pending.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern PriceAnalysis --watchAll=false`

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the three components**

Follow `docs/design/layout_definition.md` and the structure of `ProductMarginsList.tsx`. Requirements the tests pin down:

- `PricingTotalsBar` is `sticky top-0 z-10` with a solid background — it must not scroll away while the user edits a row far down the grid, because it is the thing they are watching.
- Three rows: Obrat, M0, M1 — each with před / po / Δ in Kč and Δ %.
- Czech number formatting via whatever helper the existing pages use (grep for `toLocaleString("cs-CZ")` and reuse it).
- The excluded-product count renders only when non-zero.
- `PricingGrid` renders every row with no pagination — totals span the whole filtered set, so paging would make them wrong.
- Excluded rows render visibly greyed with a title explaining why, rather than being hidden.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern PriceAnalysis --watchAll=false`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/PriceAnalysis.tsx \
        frontend/src/components/pricing \
        frontend/src/components/pages/__tests__/PriceAnalysis.test.tsx
git commit -m "feat: add price analysis grid and totals bar"
```

---

### Task 9: Cell editing and recalculation

Makes the grid interactive: four editable cells per row, a recalculate on blur, inline errors, and reset.

**Files:**
- Create: `frontend/src/components/pricing/PricingEditableCell.tsx`
- Modify: `frontend/src/components/pricing/PricingGrid.tsx`
- Modify: `frontend/src/components/pages/PriceAnalysis.tsx`
- Test: `frontend/src/components/pricing/__tests__/PricingEditableCell.test.tsx`
- Test: `frontend/src/components/pages/__tests__/PriceAnalysis.editing.test.tsx`

**Interfaces:**
- Consumes: `useRecalculatePricingMutation` (Task 7); `PricingEditField` from the generated client.
- Produces: `PricingEditableCell({ value, field, productCode, onCommit, error })`, committing on blur and on Enter, reverting on Escape.

- [ ] **Step 1: Write the failing tests**

Cover exactly these behaviours:

- Typing in a cell does **not** fire the mutation; blurring does. (This is the whole "recalc on blur" decision — a test that fires on keystroke would silently permit the wrong design.)
- Enter commits; Escape reverts to the previous value and fires nothing.
- Committing an unchanged value fires nothing.
- A successful recalculate replaces rows and totals from the response.
- A rejected edit (`SwaggerException` with `errorCode: "PricingNegativeMaterialCost"`) shows the Czech message on that cell, leaves the prior value in place, and leaves the totals unchanged.
- A network failure marks the totals stale and reverts the cell.
- The per-row reset clears that row's override and recalculates; the global reset clears all of them.

For the error cases, mock the mutation to reject with an object shaped like the generated client's `SwaggerException` — `errorCode` is a **string**, and the client **throws** rather than returning `success: false`, so a test asserting on `response.success` would test dead code.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern "PricingEditableCell|PriceAnalysis" --watchAll=false`

Expected: FAIL.

- [ ] **Step 3: Implement editing**

- `PricingEditableCell` holds its own draft string state, commits the parsed number on blur/Enter, reverts on Escape, and renders an error ring plus message when `error` is set.
- `PriceAnalysis` owns `overrides` state. On commit it calls the mutation with the current filter, the accumulated `overrides`, and the single `edit`. On success it replaces `rows`, `totals` and `overrides` from the response. On failure it sets a per-cell error and leaves state untouched.
- While the mutation is in flight, keep the previous totals rendered and show a spinner in the totals bar — never blank them, or the numbers flash on every edit.
- Edited cells get a distinct background; each edited row gets a reset control.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern "PricingEditableCell|PriceAnalysis" --watchAll=false`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pricing frontend/src/components/pages/PriceAnalysis.tsx \
        frontend/src/components/pages/__tests__
git commit -m "feat: add cell editing and recalculation to price analysis"
```

---

### Task 10: Scenarios and export

The save/load/delete controls and the XLSX ceník draft.

**Files:**
- Create: `frontend/src/components/pricing/PricingScenarioBar.tsx`
- Create: `frontend/src/components/pricing/exportPricingScenario.ts`
- Modify: `frontend/src/components/pages/PriceAnalysis.tsx`
- Test: `frontend/src/components/pricing/__tests__/exportPricingScenario.test.ts`
- Test: `frontend/src/components/pricing/__tests__/PricingScenarioBar.test.tsx`

**Interfaces:**
- Consumes: `usePricingScenariosQuery`, `usePricingScenarioQuery`, `useSavePricingScenarioMutation`, `useDeletePricingScenarioMutation` (Task 7); `exportToXlsx` from `frontend/src/utils/exportToXlsx.ts`.
- Produces: `exportPricingScenario(rows, scenarioName)` → `Promise<void>`.

- [ ] **Step 1: Write the failing export test**

`exportPricingScenario` builds the column set and delegates to the existing `exportToXlsx` util. Mock `exportToXlsx` and assert the columns, in order: Kód, Název, Cena před, Cena po, M0 před Kč, M0 po Kč, M0 po %, M1 před Kč, M1 po Kč, M1 po %, Prodáno 12m, Předpověď, Δ obrat, Δ M1. Assert that only edited rows are exported — the ceník draft is a list of changes, not a full catalog dump — and that the filename includes the scenario name.

- [ ] **Step 2: Write the failing scenario bar test**

Cover: the scenario dropdown lists saved scenarios; save with an empty name is blocked client-side; a `PricingScenarioNameConflict` from the server shows the Czech message; selecting a scenario loads it; delete asks for confirmation first. Use an inline confirmation control, **not** `window.confirm` — a native modal dialog blocks the Playwright E2E in Task 12.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern "exportPricingScenario|PricingScenarioBar" --watchAll=false`

Expected: FAIL.

- [ ] **Step 4: Implement the export and the scenario bar**

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd frontend && CI=true npx react-scripts test --testPathPattern "exportPricingScenario|PricingScenarioBar" --watchAll=false`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/pricing frontend/src/components/pages/PriceAnalysis.tsx
git commit -m "feat: add pricing scenario controls and xlsx export"
```

---

### Task 11: Make the page reachable

Access matrix, route and sidebar. Steps 1 and 2 were pulled forward into Task 3 (the controller needs the `Feature` member to compile); do them there and complete the rest here.

**Files:**
- Modify: `access-matrix.json`
- Modify (generated): `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`, `AccessMatrix.generated.cs`, `AccessRoles.generated.cs`, `access-matrix.generated.json`, `access-matrix-entra.generated.json`
- Modify: `frontend/src/App.tsx`
- Modify: `frontend/src/components/layout/Sidebar.tsx`

- [ ] **Step 1: Add the feature to the access matrix** *(done during Task 3)*

In `access-matrix.json`, in `features`, after the `Finance_MarginAnalysis` entry:

```json
    { "key": "Finance_PriceAnalysis", "label": "Analýza cen", "hasWrite": true },
```

And in `menuPaths`:

```json
    { "path": "/finance/price-analysis",
      "requires": [ { "feature": "Finance_PriceAnalysis", "level": "Read" } ] },
```

Match the exact key casing used by the neighbouring entries — read the file rather than trusting this snippet's field names.

- [ ] **Step 2: Regenerate the access matrix artifacts** *(done during Task 3)*

```bash
cd backend && dotnet run --project tools/Anela.Heblo.AccessMatrixGen
```

Confirm `Feature.Finance_PriceAnalysis` now exists in `Feature.generated.cs`.

- [ ] **Step 3: Register the route**

In `frontend/src/App.tsx`, beside the other finance routes:

```tsx
                        <Route path="/finance/price-analysis" element={guard("/finance/price-analysis", <PriceAnalysis />)} />
```

Add the import alongside the other page imports.

- [ ] **Step 4: Add the sidebar entry**

In `frontend/src/components/layout/Sidebar.tsx`, in the `finance` section's `items`, immediately after `analyza-marzovosti`:

```tsx
        {
          id: "analyza-cen",
          name: "Analýza cen",
          href: "/finance/price-analysis",
          key: "/finance/price-analysis",
        },
```

- [ ] **Step 5: Verify both builds**

Run:
```bash
cd backend && dotnet build && dotnet format --verify-no-changes
cd ../frontend && CI=false npm run build && npm run lint
```

Expected: all four succeed. (`npm run build` is the real type gate here — `npx tsc --noEmit` false-greens in this repo.)

- [ ] **Step 6: Commit**

```bash
git add access-matrix.json access-matrix.generated.json access-matrix-entra.generated.json \
        backend/src/Anela.Heblo.Domain/Features/Authorization \
        frontend/src/App.tsx frontend/src/components/layout/Sidebar.tsx
git commit -m "feat: add Analýza cen to finance menu and access matrix"
```

---

### Task 12: End-to-end test

**Files:**
- Create: `frontend/test/e2e/analytics/price-analysis.spec.ts`
- Modify: `frontend/test/e2e/fixtures/test-data.ts` (only if a suitable product fixture is missing)

- [ ] **Step 1: Write the E2E spec**

Read `docs/testing/playwright-e2e-testing.md` and `docs/testing/test-data-fixtures.md` first.

- Authenticate with `navigateToApp()`. `createE2EAuthSession()` alone skips the frontend session and lands on the Entra ID login screen.
- Take the product from `frontend/test/e2e/fixtures/test-data.ts`. If the expected fixture is absent, **throw** — do not skip.
- Use `exact: true` on `getByRole` name matches: Playwright matches the accessible name as a substring, and short Czech labels collide with longer aria-labels.

The flow: load `/finance/price-analysis` → assert the totals bar renders → read the current Obrat "po" value → edit one product's price → blur → assert Obrat "po" changed and Obrat "před" did not → save the scenario under a unique name → reload the page → load that scenario → assert the edited price is restored → delete the scenario.

- [ ] **Step 2: Run the E2E suite against staging**

Run: `./scripts/run-playwright-tests.sh`

Expected: PASS. Note this runs against **deployed staging**, not your local build — so this step only passes once the branch is deployed there. If it cannot run yet, say so explicitly rather than marking the step done.

- [ ] **Step 3: Commit**

```bash
git add frontend/test/e2e/analytics/price-analysis.spec.ts frontend/test/e2e/fixtures/test-data.ts
git commit -m "test: add price analysis e2e spec"
```

---

## Self-Review

**Spec coverage.** Every spec section maps to a task: calculation model and validation → Task 2; totals → Task 2; backend components → Tasks 2–6; API surface → Tasks 3, 4, 6; persistence and baseline snapshot → Tasks 5, 6; authorization → Tasks 3, 11; frontend layout → Task 8; editing and error handling → Task 9; scenarios and XLSX export → Task 10; menu placement → Task 11; testing → every task, plus Task 12. The non-goals (no ERP write-back, no M2/M3, no elasticity, no VAT) are constraints, implemented as absences.

**One sequencing wrinkle, handled explicitly.** `PricingSimulatorController` carries `[FeatureAuthorize(Feature.Finance_PriceAnalysis)]`, which does not compile until the access matrix is regenerated. Task 3 Step 7 pulls Task 11's Steps 1–2 forward rather than leaving the tree un-buildable between tasks.

**Type consistency.** `PricingOverrideDto`, `PricingEditDto`, `PricingEditField`, `PricingRowDto`, `PricingTotalsDto`, `PricingBaselineRow` and `PricingSimulationResult` are defined once in Task 2 and referenced with identical member names throughout. `BaselineDrifted` is added to `PricingRowDto` in Task 6 Step 1, the one task that populates it. Money is `decimal` and quantity is `double` in every signature.
