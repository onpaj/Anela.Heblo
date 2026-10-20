# Marketing Performance ("Výkon reklamy") Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hand-filled `Naklady_reklamy.xlsx` with a daily job that snapshots ad spend (Flexi received invoices bucketed by supplier DIČ) and orders/revenue (our `IssuedInvoices` table) per month, and a Marketing screen with a trend chart, a year-over-year comparison and the spreadsheet's table.

**Architecture:** Two new Postgres tables hold one row per month plus one row per month × channel. A Hangfire `IRecurringJob` recomputes the current and previous month daily (window from settings), locks older months, and a write-gated recompute endpoint enqueues an arbitrary range for backfill. Two GET endpoints derive PNO/ROAS/averages/YoY at read time from stored sums. The React page reuses the Financial Overview layout and Chart.js patterns.

**Tech Stack:** .NET 8, MediatR, EF Core (Npgsql, InMemory for tests), Hangfire, AutoMapper, Rem.FlexiBeeSDK (owner's repo `~/Work/GitHub/FlexiBeeSDK`), xUnit + Moq + FluentAssertions, React 18 + TypeScript (CRA), react-query, Chart.js via react-chartjs-2, Playwright.

**Spec:** `docs/superpowers/specs/2026-09-18-marketing-performance-design.md`

## Global Constraints

- DTOs / requests / responses are **classes, never records**; every `*Response` inherits `Anela.Heblo.Application.Shared.BaseResponse` (a reflection test fails otherwise). Internal domain value types may be records.
- No feature flag. Gating is the permission `Marketing_Performance` only (`marketing.performance.read` / `.write`).
- Nothing is re-summed at read time: GET handlers read only `MarketingPerformanceMonths` + `MarketingPerformanceChannelCosts`.
- Revenue: `IssuedInvoices` where `Currency = 'CZK'`, grouped by `TaxDate` month; wholesale ⇔ `VatPayer == true`; `Price` is the **with-VAT** total (`PriceC` is never populated); without-VAT = with-VAT ÷ `VatRate` (default `1.21`).
- Costs: Flexi received invoices filtered `datUcto` within month **and** `dic in (...)`; `storno = true` skipped; sum `sumZklCelkem`. Standard REST filter, **no Flexi user-defined query**.
- Recompute window: `MarketingPerformance:RecomputeWindowMonths`, default **2** (current + previous month). Months outside the window become `IsLocked = true` and are touched only by the explicit recompute.
- New error codes live in range **37XX** ("Marketing Performance"); each new code needs (a) `ErrorCodes.cs`, (b) the 37XX bucket in `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs`, (c) a Czech string in `frontend/src/i18n.ts`.
- Frontend API hooks call the NSwag-generated client (`frontend/src/api/generated/api-client.ts`), regenerated with `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual`. Any raw `fetch` must use `${apiClient.baseUrl}${relativeUrl}`.
- Czech UI copy; light **and** dark mode (`dark:` Tailwind variants as in `FinancialOverview.tsx`).
- Validation gates before declaring done: `dotnet build`, `dotnet format --verify-no-changes`, `cd frontend && CI=false npm run build && npm run lint`, touched tests green.
- Frontend unit tests run with `cd frontend && CI=true npx react-scripts test --watchAll=false <path>` (never `npx jest`).
- Backend tests: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformance"`. If another worktree is building concurrently, `dotnet build` first, then `dotnet test --no-build -p:UseSharedCompilation=false`.
- Commit messages: `<type>: <description>` (feat/fix/refactor/docs/test/chore). No attribution trailers (disabled globally).
- Migrations are applied **manually**; never run `dotnet ef database update` against a non-local DB from a task.

---

## File Structure

### FlexiBeeSDK repo (`~/Work/GitHub/FlexiBeeSDK`)
- Modify: `src/Rem.FlexiBeeSDK.Model/Invoices/ReceivedInvoiceRequest.cs` — optional `vatIds` → `dic in ("A","B")` filter; `dic` added to `Detail`.
- Modify: `src/Rem.FlexiBeeSDK.Model/Invoices/ReceivedInvoiceFlexiDto.cs` — `[JsonProperty("dic")] VatId`.
- Test: `test/Rem.FlexiBeeSDK.Tests/ReceivedInvoiceRequestTests.cs`.

### Backend — Domain (`backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/`)
- `YearMonth.cs` — readonly record struct (year, month) with `Start`, `EndExclusive`, `AddMonths`, `Parse("yyyy-MM")`, comparison.
- `MarketingPerformanceMonth.cs`, `MarketingPerformanceChannelCost.cs` — entities.
- `IMarketingPerformanceRepository.cs` — range read, upsert, lock.
- `IMonthlyRevenueSource.cs` + `MonthlyRevenueSnapshot.cs` — revenue step contract.
- `IMonthlyAdCostSource.cs` + `AdCostInvoice.cs` — cost step contract (Flexi-backed).
- `MarketingChannelDefinition.cs` — code, label, VAT IDs (domain view of settings).

### Backend — Domain, existing (`Features/InvoiceClassification/`)
- Modify: `IReceivedInvoicesClient.cs` — add `SearchByVatIdsAsync`.
- Modify: `ReceivedInvoice.cs` — add `SupplierVatId`, `AccountingDate`, `TotalAmountWithoutVat`, `IsCancelled`.

### Backend — Application (`backend/src/Anela.Heblo.Application/Features/MarketingPerformance/`)
- `MarketingPerformanceModule.cs` — options + DI.
- `Configuration/MarketingPerformanceOptions.cs`, `Configuration/MarketingChannelOptions.cs`, `Configuration/MarketingPerformanceOptionsValidator.cs`.
- `Services/MarketingMetricsCalculator.cs` — pure derived metrics.
- `Services/ChannelCostBucketer.cs` — invoices → per-channel sums.
- `Services/IMarketingPerformanceRefreshService.cs`, `Services/MarketingPerformanceRefreshService.cs` — per-month two-step refresh + locking.
- `Services/MarketingPerformanceRunGuard.cs` — singleton "a run is in progress" flag.
- `Services/IMarketingPerformanceRecomputeEnqueuer.cs`, `Services/HangfireMarketingPerformanceRecomputeEnqueuer.cs`.
- `Infrastructure/Jobs/MarketingPerformanceRefreshJob.cs` (IRecurringJob), `Infrastructure/Jobs/MarketingPerformanceRecomputeJob.cs` (parametrised fire-and-forget).
- `Contracts/MonthlyMarketingPerformanceDto.cs`, `Contracts/ChannelCostDto.cs`, `Contracts/ChannelInfoDto.cs`, `Contracts/MarketingYearSeriesDto.cs`.
- `UseCases/GetMarketingPerformanceMonths/{Request,Response,Handler}.cs`
- `UseCases/GetMarketingPerformanceComparison/{Request,Response,Handler}.cs`
- `UseCases/RecomputeMarketingPerformance/{Request,Response,Handler}.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — call `AddMarketingPerformanceModule(configuration)`.
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — 37XX range.

### Backend — Persistence (`backend/src/Anela.Heblo.Persistence/Marketing/`)
- `MarketingPerformanceMonthConfiguration.cs`, `MarketingPerformanceChannelCostConfiguration.cs`, `MarketingPerformanceRepository.cs`, `IssuedInvoiceMonthlyRevenueSource.cs`.
- Modify: `ApplicationDbContext.cs` — two DbSets.
- Create: `Migrations/<timestamp>_AddMarketingPerformance.cs` (generated).

### Backend — Flexi adapter (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/`)
- Modify: `FlexiReceivedInvoicesClient.cs`, `FlexiReceivedInvoiceMappingProfile.cs`.
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/MarketingPerformance/FlexiMonthlyAdCostSource.cs`.
- Modify: `FlexiAdapterServiceCollectionExtensions.cs` (register `IMonthlyAdCostSource`), `Anela.Heblo.Adapters.Flexi.csproj` (SDK version bump).

### Backend — API
- Create: `backend/src/Anela.Heblo.API/Controllers/MarketingPerformanceController.cs`.
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` — `MarketingPerformance` section.
- Modify: `access-matrix.json` + regenerate 5 artifacts.

### Backend — Tests (`backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/`)
- `YearMonthTests.cs`, `MarketingPerformanceOptionsValidatorTests.cs`, `MarketingMetricsCalculatorTests.cs`, `ChannelCostBucketerTests.cs`, `IssuedInvoiceMonthlyRevenueSourceTests.cs`, `MarketingPerformanceRefreshServiceTests.cs`, `MarketingPerformanceRefreshJobTests.cs`, `GetMarketingPerformanceMonthsHandlerTests.cs`, `GetMarketingPerformanceComparisonHandlerTests.cs`, `RecomputeMarketingPerformanceHandlerTests.cs`.
- `backend/test/Anela.Heblo.Tests/Adapters/Flexi/ReceivedInvoiceVatIdWireShapeTests.cs`.
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs`.

### Frontend
- `frontend/src/api/hooks/useMarketingPerformance.ts`; modify `frontend/src/api/client.ts` (QUERY_KEYS).
- `frontend/src/components/charts/comparisonColors.ts` (extracted from `financial-overview/comparisonUtils.ts`, which then imports it).
- `frontend/src/components/marketing/performance/`: `metrics.ts`, `MarketingPerformancePage.tsx`, `PerformanceToolbar.tsx`, `PerformanceTrendChart.tsx`, `PerformanceComparisonChart.tsx`, `PerformanceTable.tsx`, `RecomputeDialog.tsx`, `__tests__/`.
- Modify: `frontend/src/App.tsx`, `frontend/src/components/layout/Sidebar.tsx`, `frontend/src/i18n.ts`.
- E2E: `frontend/test/e2e/marketing/marketing-performance.spec.ts`; modify `frontend/test/e2e/helpers/e2e-auth-helper.ts`.

### Docs
- Create: `docs/features/marketing-performance.md`; modify `docs/integrations/flexibee-api.md`, `memory/context/state.md`.

---

## Task 1: FlexiBeeSDK — VAT-ID filter and `dic` projection

**Repo:** `~/Work/GitHub/FlexiBeeSDK` (separate git repo; work on a branch, open a PR, CI publishes to NuGet on merge via GitVersion).

**Files:**
- Modify: `src/Rem.FlexiBeeSDK.Model/Invoices/ReceivedInvoiceRequest.cs`
- Modify: `src/Rem.FlexiBeeSDK.Model/Invoices/ReceivedInvoiceFlexiDto.cs`
- Test: `test/Rem.FlexiBeeSDK.Tests/ReceivedInvoiceRequestTests.cs`

**Interfaces:**
- Produces: `new ReceivedInvoiceRequest(DateTime? dateFrom, DateTime? dateTo, string? label, string? accountingTemplate, string? documentNumber, string? companyId, IEnumerable<string>? vatIds, string dateField = "datVyst")` and `ReceivedInvoiceFlexiDto.VatId` (`dic`). `IsCancelled` (`storno`) and `TotalBaseAmount` (`sumZklCelkem`) and `AccountingDate` (`datUcto`) already exist.

- [ ] **Step 1: Write the failing tests**

Append to `test/Rem.FlexiBeeSDK.Tests/ReceivedInvoiceRequestTests.cs` inside the class:

```csharp
        [Fact]
        public void Constructor_WithVatIds_GeneratesInFilter()
        {
            var request = new ReceivedInvoiceRequest(
                new DateTime(2026, 8, 1), new DateTime(2026, 8, 31),
                vatIds: new[] { "IE9692928F", "CZ26168685" });

            Assert.Equal(
                "((datVyst gte \"2026-08-01\" and datVyst lte \"2026-08-31\") and dic in (\"IE9692928F\",\"CZ26168685\"))",
                request.Filter);
        }

        [Fact]
        public void Constructor_WithEmptyVatIds_AddsNoVatFilter()
        {
            var request = new ReceivedInvoiceRequest(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), vatIds: Array.Empty<string>());
            Assert.Equal("((datVyst gte \"2026-08-01\" and datVyst lte \"2026-08-31\"))", request.Filter);
        }

        [Fact]
        public void Constructor_WithAccountingDateField_FiltersOnDatUcto()
        {
            var request = new ReceivedInvoiceRequest(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), dateField: "datUcto");
            Assert.Equal("((datUcto gte \"2026-08-01\" and datUcto lte \"2026-08-31\"))", request.Filter);
        }

        [Fact]
        public void DetailProperty_ContainsDic()
        {
            var request = new ReceivedInvoiceRequest(DateTime.Now, DateTime.Now);
            Assert.Contains(",dic,", request.Detail);
        }

        [Fact]
        public void Dto_DeserializesDicIntoVatId()
        {
            const string json = "{\"id\":1,\"kod\":\"PF2608001\",\"dic\":\"IE9692928F\",\"storno\":false,\"sumZklCelkem\":1000.5,\"datUcto\":\"2026-08-05\"}";
            var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(json)!;
            Assert.Equal("IE9692928F", dto.VatId);
            Assert.False(dto.IsCancelled);
            Assert.Equal(1000.5, dto.TotalBaseAmount);
            Assert.Equal(new DateTime(2026, 8, 5), dto.AccountingDate);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ~/Work/GitHub/FlexiBeeSDK && dotnet test test/Rem.FlexiBeeSDK.Tests --filter "FullyQualifiedName~ReceivedInvoiceRequestTests"`
Expected: compile errors — `vatIds`/`dateField` parameters and `VatId` do not exist.

- [ ] **Step 3: Implement**

In `ReceivedInvoiceRequest.cs` replace the constructor signature and date filter, and add the VAT filter:

```csharp
    public ReceivedInvoiceRequest(
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? label = null,
        string? accountingTemplate = null,
        string? documentNumber = null,
        string? companyId = null,
        IEnumerable<string>? vatIds = null,
        string dateField = "datVyst")
    {
        var filters = new List<string>();
        if (dateFrom.HasValue && dateTo.HasValue)
        {
            filters.Add($"({dateField} gte \"{dateFrom.Value:yyyy-MM-dd}\" and {dateField} lte \"{dateTo.Value:yyyy-MM-dd}\")");
        }
        // ... existing label / accountingTemplate / documentNumber / companyId filters unchanged ...
        var vatIdFilter = GetVatIdsFilterString(vatIds);
        if (!string.IsNullOrEmpty(vatIdFilter))
            filters.Add(vatIdFilter);

        Filter = filters.Count > 0 ? $"({string.Join(" and ", filters)})" : string.Empty;
    }

    private static string GetVatIdsFilterString(IEnumerable<string>? vatIds)
    {
        var ids = vatIds?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
        if (ids == null || ids.Count == 0)
            return string.Empty;
        return $"dic in ({string.Join(",", ids.Select(v => $"\"{v}\""))})";
    }
```

Add `using System.Linq;`. In `Detail`, insert `dic` right after `ic`: `...,firma,ic,dic,polozkyDokladu(...)`.

In `ReceivedInvoiceFlexiDto.cs` add after `CompanyId`:

```csharp
    [JsonProperty("dic")]
    public string? VatId { get; set; }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ~/Work/GitHub/FlexiBeeSDK && dotnet test test/Rem.FlexiBeeSDK.Tests`
Expected: all PASS (the old `Constructor_WithAllOptionalParameters_GeneratesCorrectFilter` still passes because `vatIds` is null).

- [ ] **Step 5: Commit, push, PR, note the published version**

```bash
cd ~/Work/GitHub/FlexiBeeSDK
git checkout -b feat/received-invoice-vat-id-filter
git add -A
git commit -m "feat: add dic (VAT ID) projection and vatIds/dateField filter to ReceivedInvoiceRequest"
git push -u origin feat/received-invoice-vat-id-filter
gh pr create --fill
```

After merge, read the version CI published (`gh run view --log | grep PackageVersion`, or nuget.org `Rem.FlexiBeeSDK.Client`). Record it; Task 3 bumps Heblo to it. Until it is published, Task 3 can proceed against a local `dotnet pack` output added as a local NuGet source, but the final Heblo commit must reference the published version.

---

## Task 2: Verify Flexi assumptions live (read-only) and document them

**Files:**
- Modify: `docs/integrations/flexibee-api.md` (append a section)

No code. Two facts must be confirmed before schema work: (1) received invoices from Meta / Google / Seznam carry `dic`, and the exact strings; (2) `sumZklCelkem` is the without-VAT base for EU reverse-charge invoices too.

- [ ] **Step 1: Pull one month of MARKETING received invoices from production Flexi (GET only)**

```bash
co=$(az keyvault secret show --vault-name kv-heblo-prod --name FlexiBeeSettings--Company --query value -o tsv)
lg=$(az keyvault secret show --vault-name kv-heblo-prod --name FlexiBeeSettings--Login --query value -o tsv)
pw=$(az keyvault secret show --vault-name kv-heblo-prod --name FlexiBeeSettings--Password --query value -o tsv)
url=https://petra-tesarikova.flexibee.eu
curl -s -u "$lg:$pw" "$url/c/$co/faktura-prijata.json?detail=custom:kod,datUcto,nazFirmy,ic,dic,sumZklCelkem,sumCelkem,mena,storno,stredisko&limit=0&filter=(datUcto%20gte%20%222026-08-01%22%20and%20datUcto%20lte%20%222026-08-31%22)" \
 | python3 -c "import sys,json; rows=json.load(sys.stdin)['winstrom']['faktura-prijata']; [print(r['datUcto'], r['kod'], repr(r.get('dic')), r['nazFirmy'][:30], r['sumZklCelkem'], r['sumCelkem'], r['mena'], r['storno']) for r in rows if any(k in r['nazFirmy'].lower() for k in ('meta','facebook','google','seznam'))]"
```

Expected: rows for Meta Platforms Ireland (dic `IE…`), Google Ireland (dic `IE…`), Seznam.cz (dic `CZ26168685`). If `dic` is empty for any of them, stop and report: the DIČ mapping cannot work for that supplier and the owner must decide (fill DIČ in Flexi, or fall back to `ic`).

- [ ] **Step 2: Check the without-VAT base on one EU invoice**

For one Meta row compare `sumZklCelkem` with `sumCelkem`. Reverse-charge invoices should show `sumZklCelkem == sumCelkem` (no Czech VAT on the document); domestic Seznam rows should show `sumCelkem ≈ sumZklCelkem × 1.21`. Record both observations.

- [ ] **Step 3: Document**

Append to `docs/integrations/flexibee-api.md`:

```markdown
## Received invoices — supplier DIČ filter — VERIFIED LIVE <date> (production company)

- `faktura-prijata` supports `dic in ("…","…")` in the standard REST `filter`; combined with `datUcto gte/lte` it returns only the marketing suppliers for a month in one call (`limit=0`).
- Observed DIČ values: Meta Platforms Ireland `IE…`, Google Ireland `IE…`, Seznam.cz `CZ26168685`. <replace with observed>
- `sumZklCelkem` is the without-VAT base on both reverse-charge EU invoices (equals `sumCelkem`) and domestic ones (`sumCelkem` = base × 1.21). Used as the ad-cost amount by `FlexiMonthlyAdCostSource`.
- `storno = true` documents are returned by the filter and must be skipped by the caller.
```

- [ ] **Step 4: Commit**

```bash
git add docs/integrations/flexibee-api.md
git commit -m "docs: verify Flexi received-invoice DIČ filter and without-VAT base for marketing costs"
```

---

## Task 3: Heblo Flexi adapter — `SearchByVatIdsAsync`

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj:23` (SDK version → the one published in Task 1)
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IReceivedInvoicesClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoicesClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/Flexi/ReceivedInvoiceVatIdWireShapeTests.cs`

**Interfaces:**
- Consumes: SDK `ReceivedInvoiceRequest(..., IEnumerable<string>? vatIds, string dateField)`, `ReceivedInvoiceFlexiDto.VatId/IsCancelled/TotalBaseAmount/AccountingDate` (Task 1).
- Produces: `Task<List<ReceivedInvoice>> IReceivedInvoicesClient.SearchByVatIdsAsync(DateTime accountingDateFrom, DateTime accountingDateTo, IReadOnlyCollection<string> vatIds, CancellationToken ct)`; `ReceivedInvoice.SupplierVatId`, `.AccountingDate`, `.TotalAmountWithoutVat`, `.IsCancelled`. Note: the pre-existing `CompanyVat` is mapped from `ic` (IČ), **not** DIČ — leave it alone, do not reuse it.

- [ ] **Step 1: Bump the SDK reference**

In `Anela.Heblo.Adapters.Flexi.csproj` set `<PackageReference Include="Rem.FlexiBeeSDK.Client" Version="<published version from Task 1>" />`. Run `dotnet restore backend/src/Adapters/Anela.Heblo.Adapters.Flexi`.

- [ ] **Step 2: Write the failing wire-shape + mapping test**

`backend/test/Anela.Heblo.Tests/Adapters/Flexi/ReceivedInvoiceVatIdWireShapeTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Model.Invoices;
using Anela.Heblo.Application.Common;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

/// <summary>
/// Pins the wire shape of a Flexi received invoice carrying a supplier DIČ (`dic`) against the
/// SDK DTO and our mapping. Verbatim-style JSON: the shape Flexi returns for
/// POST /c/{firma}/faktura-prijata.json with the ReceivedInvoiceRequest detail projection.
/// Replace the row with a captured production row (with amounts redacted) once Task 2 ran.
/// </summary>
public class ReceivedInvoiceVatIdWireShapeTests
{
    private const string MetaRow = """
        {"id":123456,"datVyst":"2026-08-03+02:00","kod":"PF2608012","nazFirmy":"Meta Platforms Ireland Limited",
         "cisDosle":"FBADS-123","varSym":"123","stredisko":[{"id":3,"kod":"MARKETING"}],"datSplat":"2026-08-17+02:00",
         "sumZklCelkemMen":0.0,"sumZklCelkem":218986.0,"mena":[{"kod":"CZK","id":1}],"sumCelkemMen":0.0,"sumCelkem":218986.0,
         "juhSum":0.0,"stavUhrK":"stavUhr.uhrazeno","juhSumMen":0.0,"storno":false,"popis":"Facebook Ads 08/2026",
         "zuctovano":true,"datUcto":"2026-08-03+02:00","typUcOp":[{"nazev":"Služby","kod":"SLUZBY","id":7}],
         "bezPolozek":true,"firma":"code:META","ic":"","dic":"IE9692928F","stitky":""}
        """;

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<FlexiReceivedInvoiceMappingProfile>()).CreateMapper();

    [Fact]
    public void Dto_BindsDicStornoBaseAndAccountingDate()
    {
        var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)!;

        dto.VatId.Should().Be("IE9692928F");
        dto.IsCancelled.Should().BeFalse();
        dto.TotalBaseAmount.Should().Be(218986.0);
        dto.AccountingDate.Should().Be(new DateTime(2026, 8, 3));
    }

    [Fact]
    public void Mapping_ExposesSupplierVatIdAndWithoutVatTotal()
    {
        var dto = JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)!;

        var mapped = Mapper().Map<ReceivedInvoice>(dto);

        mapped.SupplierVatId.Should().Be("IE9692928F");
        mapped.TotalAmountWithoutVat.Should().Be(218986m);
        mapped.AccountingDate.Should().Be(new DateTime(2026, 8, 3));
        mapped.IsCancelled.Should().BeFalse();
        mapped.InvoiceNumber.Should().Be("PF2608012");
    }

    [Fact]
    public async Task SearchByVatIdsAsync_BuildsAccountingDateAndDicFilter()
    {
        var sdk = new Mock<IReceivedInvoiceClient>();
        ReceivedInvoiceRequest? captured = null;
        sdk.Setup(c => c.SearchAsync(It.IsAny<ReceivedInvoiceRequest>(), It.IsAny<CancellationToken>()))
           .Callback<ReceivedInvoiceRequest, CancellationToken>((r, _) => captured = r)
           .ReturnsAsync(new List<ReceivedInvoiceFlexiDto> { JsonConvert.DeserializeObject<ReceivedInvoiceFlexiDto>(MetaRow)! });
        var client = new FlexiReceivedInvoicesClient(
            sdk.Object,
            Options.Create(new DataSourceOptions()),
            TimeProvider.System,
            NullLogger<FlexiReceivedInvoicesClient>.Instance,
            Mapper());

        var result = await client.SearchByVatIdsAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), new[] { "IE9692928F", "CZ26168685" }, CancellationToken.None);

        captured!.Filter.Should().Be(
            "((datUcto gte \"2026-08-01\" and datUcto lte \"2026-08-31\") and dic in (\"IE9692928F\",\"CZ26168685\"))");
        result.Should().ContainSingle(i => i.SupplierVatId == "IE9692928F");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedInvoiceVatIdWireShapeTests"`
Expected: compile errors (`SupplierVatId`, `SearchByVatIdsAsync` missing).

- [ ] **Step 4: Implement domain + interface**

`ReceivedInvoice.cs` — add:

```csharp
    /// <summary>Supplier DIČ (Flexi `dic`). Distinct from <see cref="CompanyVat"/>, which holds the IČ.</summary>
    public string? SupplierVatId { get; set; }
    /// <summary>Flexi `datUcto` — the accounting date the cost belongs to.</summary>
    public DateTime? AccountingDate { get; set; }
    /// <summary>Flexi `sumZklCelkem` — total without VAT in document currency.</summary>
    public decimal TotalAmountWithoutVat { get; set; }
    /// <summary>Flexi `storno`.</summary>
    public bool IsCancelled { get; set; }
```

`IReceivedInvoicesClient.cs` — add:

```csharp
    /// <summary>
    /// Received invoices whose accounting date falls in [accountingDateFrom, accountingDateTo] (inclusive, dates only)
    /// and whose supplier DIČ is in <paramref name="vatIds"/>. One Flexi call, no paging (limit=0).
    /// </summary>
    Task<List<ReceivedInvoice>> SearchByVatIdsAsync(
        DateTime accountingDateFrom,
        DateTime accountingDateTo,
        IReadOnlyCollection<string> vatIds,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement adapter + mapping**

`FlexiReceivedInvoicesClient.cs` — add method:

```csharp
    public async Task<List<ReceivedInvoice>> SearchByVatIdsAsync(
        DateTime accountingDateFrom,
        DateTime accountingDateTo,
        IReadOnlyCollection<string> vatIds,
        CancellationToken cancellationToken = default)
    {
        if (vatIds.Count == 0)
        {
            return new List<ReceivedInvoice>();
        }

        var request = new ReceivedInvoiceRequest(
            accountingDateFrom,
            accountingDateTo,
            vatIds: vatIds,
            dateField: "datUcto");

        _logger.LogInformation(
            "Flexi received-invoice search by DIČ: {From:yyyy-MM-dd}..{To:yyyy-MM-dd}, {Count} VAT IDs",
            accountingDateFrom, accountingDateTo, vatIds.Count);

        var invoices = await _client.SearchAsync(request, cancellationToken);
        return _mapper.Map<List<ReceivedInvoice>>(invoices);
    }
```

`FlexiReceivedInvoiceMappingProfile.cs` — add to the `ReceivedInvoiceFlexiDto → ReceivedInvoice` map:

```csharp
            .ForMember(dest => dest.SupplierVatId, opt => opt.MapFrom(src => src.VatId))
            .ForMember(dest => dest.AccountingDate, opt => opt.MapFrom(src => src.AccountingDate))
            .ForMember(dest => dest.TotalAmountWithoutVat, opt => opt.MapFrom(src => (decimal)src.TotalBaseAmount))
            .ForMember(dest => dest.IsCancelled, opt => opt.MapFrom(src => src.IsCancelled))
```

Note `Labels` mapping splits `src.Labels`; the test row has `"stitky":""` so it yields an empty array — fine. If the `Items` map throws on a null `polozkyDokladu`, add `opt => opt.NullSubstitute(new List<ReceivedInvoiceItemFlexiDto>())` — but only if the test shows it.

- [ ] **Step 6: Run tests + build**

Run: `dotnet build backend/src/Anela.Heblo.API && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReceivedInvoice"`
Expected: PASS, including the existing `InvoiceClassification` tests that construct `FlexiReceivedInvoicesClient`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi backend/src/Anela.Heblo.Domain/Features/InvoiceClassification backend/test/Anela.Heblo.Tests/Adapters/Flexi/ReceivedInvoiceVatIdWireShapeTests.cs
git commit -m "feat: Flexi received-invoice search by supplier DIČ and accounting date"
```

---

## Task 4: Domain — `YearMonth`, entities, source and repository contracts

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/YearMonth.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/MarketingPerformanceMonth.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/MarketingPerformanceChannelCost.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/MarketingChannelDefinition.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/MonthlyRevenueSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/IMonthlyRevenueSource.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/AdCostInvoice.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/IMonthlyAdCostSource.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingPerformance/IMarketingPerformanceRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/YearMonthTests.cs`

**Interfaces (produced, used by every later task):**

```csharp
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public DateTime Start;               // yyyy-MM-01 00:00, Kind Unspecified
    public DateTime EndExclusive;        // first day of next month
    public DateTime LastDay;             // last calendar day, date only
    public YearMonth AddMonths(int n);
    public static YearMonth From(DateTime d);
    public static YearMonth Parse(string s); // "yyyy-MM", throws FormatException
    public static bool TryParse(string? s, out YearMonth ym);
    public override string ToString();   // "yyyy-MM"
    public static IEnumerable<YearMonth> Range(YearMonth from, YearMonth toInclusive);
    public static int MonthsBetween(YearMonth from, YearMonth toInclusive); // inclusive count
}
```

- [ ] **Step 1: Write the failing `YearMonth` tests**

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class YearMonthTests
{
    [Fact]
    public void Start_And_EndExclusive_CoverTheWholeMonth()
    {
        var ym = new YearMonth(2026, 2);
        ym.Start.Should().Be(new DateTime(2026, 2, 1));
        ym.EndExclusive.Should().Be(new DateTime(2026, 3, 1));
        ym.LastDay.Should().Be(new DateTime(2026, 2, 28));
    }

    [Fact]
    public void AddMonths_WrapsYears()
    {
        new YearMonth(2026, 1).AddMonths(-1).Should().Be(new YearMonth(2025, 12));
        new YearMonth(2025, 12).AddMonths(1).Should().Be(new YearMonth(2026, 1));
    }

    [Fact]
    public void Parse_AcceptsYyyyDashMm_AndRejectsGarbage()
    {
        YearMonth.Parse("2026-09").Should().Be(new YearMonth(2026, 9));
        YearMonth.TryParse("2026-13", out _).Should().BeFalse();
        YearMonth.TryParse("garbage", out _).Should().BeFalse();
        YearMonth.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Range_IsInclusiveAndOrdered()
    {
        YearMonth.Range(new YearMonth(2025, 11), new YearMonth(2026, 2))
            .Should().Equal(new YearMonth(2025, 11), new YearMonth(2025, 12), new YearMonth(2026, 1), new YearMonth(2026, 2));
        YearMonth.MonthsBetween(new YearMonth(2025, 11), new YearMonth(2026, 2)).Should().Be(4);
    }

    [Fact]
    public void CompareTo_OrdersChronologically()
    {
        (new YearMonth(2025, 12) < new YearMonth(2026, 1)).Should().BeTrue();
        new YearMonth(2026, 3).ToString().Should().Be("2026-03");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~YearMonthTests"`
Expected: compile error, type missing.

- [ ] **Step 3: Implement `YearMonth`**

```csharp
using System.Globalization;

namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>Calendar month key used by the marketing performance snapshot.</summary>
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public DateTime Start => new(Year, Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
    public DateTime EndExclusive => Start.AddMonths(1);
    public DateTime LastDay => EndExclusive.AddDays(-1);

    public YearMonth AddMonths(int months)
    {
        var d = Start.AddMonths(months);
        return new YearMonth(d.Year, d.Month);
    }

    public static YearMonth From(DateTime date) => new(date.Year, date.Month);

    public static YearMonth Parse(string value)
    {
        if (!TryParse(value, out var result))
            throw new FormatException($"'{value}' is not a valid yyyy-MM month.");
        return result;
    }

    public static bool TryParse(string? value, out YearMonth result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!DateTime.TryParseExact(value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        result = new YearMonth(d.Year, d.Month);
        return true;
    }

    public static IEnumerable<YearMonth> Range(YearMonth from, YearMonth toInclusive)
    {
        for (var ym = from; ym <= toInclusive; ym = ym.AddMonths(1))
            yield return ym;
    }

    public static int MonthsBetween(YearMonth from, YearMonth toInclusive) =>
        (toInclusive.Year - from.Year) * 12 + (toInclusive.Month - from.Month) + 1;

    public int CompareTo(YearMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);
    public static bool operator <(YearMonth a, YearMonth b) => a.CompareTo(b) < 0;
    public static bool operator >(YearMonth a, YearMonth b) => a.CompareTo(b) > 0;
    public static bool operator <=(YearMonth a, YearMonth b) => a.CompareTo(b) <= 0;
    public static bool operator >=(YearMonth a, YearMonth b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
```

- [ ] **Step 4: Add entities and contracts**

`MarketingPerformanceMonth.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>One calendar month of the marketing performance snapshot. Sums only — ratios are derived at read time.</summary>
public class MarketingPerformanceMonth : IEntity<int>
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>CZK issued invoices without a customer VAT ID, by tax date.</summary>
    public int RetailOrderCount { get; set; }
    public decimal RetailRevenueWithVat { get; set; }

    /// <summary>CZK issued invoices with a customer VAT ID (wholesale — same rule as Flexi sales query 37).</summary>
    public int WholesaleOrderCount { get; set; }
    public decimal WholesaleRevenueWithVat { get; set; }

    /// <summary>EUR invoices in the month; excluded from revenue, kept visible.</summary>
    public int SkippedEurInvoiceCount { get; set; }

    /// <summary>True once the month left the recompute window; only an explicit recompute touches it.</summary>
    public bool IsLocked { get; set; }

    public DateTime? RevenueComputedAt { get; set; }
    public DateTime? CostsComputedAt { get; set; }
    public string? LastError { get; set; }

    public List<MarketingPerformanceChannelCost> ChannelCosts { get; set; } = new();

    public YearMonth Key => new(Year, Month);
}
```

`MarketingPerformanceChannelCost.cs`:

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public class MarketingPerformanceChannelCost : IEntity<int>
{
    public int Id { get; set; }
    public int MonthId { get; set; }
    public MarketingPerformanceMonth? Month { get; set; }
    /// <summary>Channel code from settings (e.g. "meta"). String, not enum: adding a channel is a config change.</summary>
    public string ChannelCode { get; set; } = string.Empty;
    /// <summary>Sum of Flexi sumZklCelkem for non-storno invoices of the channel's suppliers.</summary>
    public decimal CostWithoutVat { get; set; }
    public int InvoiceCount { get; set; }
}
```

`MarketingChannelDefinition.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public class MarketingChannelDefinition
{
    public string Code { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> VatIds { get; init; } = Array.Empty<string>();
}
```

`MonthlyRevenueSnapshot.cs` + `IMonthlyRevenueSource.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public class MonthlyRevenueSnapshot
{
    public int RetailOrderCount { get; init; }
    public decimal RetailRevenueWithVat { get; init; }
    public int WholesaleOrderCount { get; init; }
    public decimal WholesaleRevenueWithVat { get; init; }
    public int SkippedEurInvoiceCount { get; init; }
}

/// <summary>Revenue step of the snapshot: local IssuedInvoices aggregated for one month.</summary>
public interface IMonthlyRevenueSource
{
    Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken);
}
```

`AdCostInvoice.cs` + `IMonthlyAdCostSource.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingPerformance;

/// <summary>A received invoice relevant for ad spend, already filtered to the configured suppliers.</summary>
public class AdCostInvoice
{
    public string InvoiceNumber { get; init; } = string.Empty;
    public string SupplierVatId { get; init; } = string.Empty;
    public DateTime AccountingDate { get; init; }
    public decimal AmountWithoutVat { get; init; }
    public bool IsCancelled { get; init; }
}

/// <summary>Cost step of the snapshot: received invoices for one month whose supplier DIČ is in <paramref name="vatIds"/>.</summary>
public interface IMonthlyAdCostSource
{
    Task<IReadOnlyList<AdCostInvoice>> GetAsync(YearMonth month, IReadOnlyCollection<string> vatIds, CancellationToken cancellationToken);
}
```

`IMarketingPerformanceRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingPerformance;

public interface IMarketingPerformanceRepository
{
    /// <summary>Months in [from, to] inclusive with ChannelCosts loaded, ascending. Missing months are absent.</summary>
    Task<List<MarketingPerformanceMonth>> GetRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken);

    /// <summary>Tracked row for one month with ChannelCosts, or null.</summary>
    Task<MarketingPerformanceMonth?> GetForUpdateAsync(YearMonth month, CancellationToken cancellationToken);

    Task AddAsync(MarketingPerformanceMonth month, CancellationToken cancellationToken);

    /// <summary>Marks every unlocked month strictly before <paramref name="cutoff"/> as locked. Returns rows affected.</summary>
    Task<int> LockMonthsBeforeAsync(YearMonth cutoff, CancellationToken cancellationToken);

    /// <summary>Latest RevenueComputedAt/CostsComputedAt across all months, for the screen's status line.</summary>
    Task<DateTime?> GetLastComputedAtAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet build backend/src/Anela.Heblo.Domain && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~YearMonthTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingPerformance backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/YearMonthTests.cs
git commit -m "feat: marketing performance domain — YearMonth, snapshot entities, source and repository contracts"
```

---

## Task 5: Persistence — EF configuration, repository, revenue source, migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingPerformanceMonthConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingPerformanceChannelCostConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Marketing/MarketingPerformanceRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Marketing/IssuedInvoiceMonthlyRevenueSource.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:155-157` (add DbSets next to the Marketing ones)
- Create (generated): `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddMarketingPerformance.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/IssuedInvoiceMonthlyRevenueSourceTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceRepositoryTests.cs`

**Interfaces:**
- Consumes: Task 4 entities/contracts; `ApplicationDbContext.IssuedInvoices` (`IssuedInvoice { DateTime TaxDate; string Currency; bool? VatPayer; decimal Price }`).
- Produces: `MarketingPerformanceRepository : IMarketingPerformanceRepository` (ctor `(ApplicationDbContext)`), `IssuedInvoiceMonthlyRevenueSource : IMonthlyRevenueSource` (ctor `(ApplicationDbContext)`), DbSets `MarketingPerformanceMonths`, `MarketingPerformanceChannelCosts`.

- [ ] **Step 1: Write the failing revenue-source test**

```csharp
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class IssuedInvoiceMonthlyRevenueSourceTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public IssuedInvoiceMonthlyRevenueSourceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    private static IssuedInvoice Invoice(string id, DateTime taxDate, decimal price, string currency = "CZK", bool? vatPayer = false) =>
        new()
        {
            Id = id, TaxDate = taxDate, InvoiceDate = taxDate, DueDate = taxDate.AddDays(14),
            Price = price, Currency = currency, VatPayer = vatPayer, CreationTime = taxDate,
        };

    [Fact]
    public async Task GetAsync_SplitsRetailAndWholesale_ByTaxDate_CzkOnly()
    {
        _context.IssuedInvoices.AddRange(
            Invoice("A1", new DateTime(2026, 8, 1), 1000m),
            Invoice("A2", new DateTime(2026, 8, 31, 23, 59, 0), 500m),
            Invoice("B1", new DateTime(2026, 8, 15), 8000m, vatPayer: true),
            Invoice("N1", new DateTime(2026, 8, 10), 300m, vatPayer: null),       // null VatPayer counts as retail
            Invoice("E1", new DateTime(2026, 8, 12), 40m, currency: "EUR"),
            Invoice("X1", new DateTime(2026, 9, 1), 999m),                        // next month
            Invoice("X0", new DateTime(2026, 7, 31), 999m));                      // previous month
        await _context.SaveChangesAsync();
        var source = new IssuedInvoiceMonthlyRevenueSource(_context);

        var snapshot = await source.GetAsync(new YearMonth(2026, 8), CancellationToken.None);

        snapshot.RetailOrderCount.Should().Be(3);
        snapshot.RetailRevenueWithVat.Should().Be(1800m);
        snapshot.WholesaleOrderCount.Should().Be(1);
        snapshot.WholesaleRevenueWithVat.Should().Be(8000m);
        snapshot.SkippedEurInvoiceCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_EmptyMonth_ReturnsZeros()
    {
        var source = new IssuedInvoiceMonthlyRevenueSource(_context);
        var snapshot = await source.GetAsync(new YearMonth(2026, 1), CancellationToken.None);
        snapshot.Should().BeEquivalentTo(new MonthlyRevenueSnapshot());
    }
}
```

- [ ] **Step 2: Write the failing repository test**

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly MarketingPerformanceRepository _repo;

    public MarketingPerformanceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _context = new ApplicationDbContext(options);
        _repo = new MarketingPerformanceRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    private static MarketingPerformanceMonth Month(int y, int m, bool locked = false, DateTime? computed = null) => new()
    {
        Year = y, Month = m, IsLocked = locked, RevenueComputedAt = computed,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 10m, InvoiceCount = 1 } },
    };

    [Fact]
    public async Task GetRangeAsync_ReturnsInclusiveAscendingWithChannelCosts()
    {
        await _repo.AddAsync(Month(2026, 3), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 1), CancellationToken.None);
        await _repo.AddAsync(Month(2025, 12), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 4), CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var rows = await _repo.GetRangeAsync(new YearMonth(2026, 1), new YearMonth(2026, 3), CancellationToken.None);

        rows.Select(r => r.Key).Should().Equal(new YearMonth(2026, 1), new YearMonth(2026, 3));
        rows[0].ChannelCosts.Should().ContainSingle(c => c.ChannelCode == "meta");
    }

    [Fact]
    public async Task LockMonthsBeforeAsync_LocksOnlyOlderUnlockedMonths()
    {
        await _repo.AddAsync(Month(2026, 6), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 7), CancellationToken.None);
        await _repo.AddAsync(Month(2026, 8), CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var affected = await _repo.LockMonthsBeforeAsync(new YearMonth(2026, 8), CancellationToken.None);

        affected.Should().Be(2);
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 8), CancellationToken.None))!.IsLocked.Should().BeFalse();
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 7), CancellationToken.None))!.IsLocked.Should().BeTrue();
    }

    [Fact]
    public async Task GetLastComputedAtAsync_ReturnsMaxOfBothTimestamps()
    {
        await _repo.AddAsync(Month(2026, 7, computed: new DateTime(2026, 8, 1, 5, 0, 0)), CancellationToken.None);
        var m = Month(2026, 8);
        m.CostsComputedAt = new DateTime(2026, 9, 1, 5, 0, 0);
        await _repo.AddAsync(m, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        (await _repo.GetLastComputedAtAsync(CancellationToken.None)).Should().Be(new DateTime(2026, 9, 1, 5, 0, 0));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformance"`
Expected: compile errors (types missing).

- [ ] **Step 4: EF configurations + DbSets**

`MarketingPerformanceMonthConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing;

public class MarketingPerformanceMonthConfiguration : IEntityTypeConfiguration<MarketingPerformanceMonth>
{
    public void Configure(EntityTypeBuilder<MarketingPerformanceMonth> builder)
    {
        builder.ToTable("MarketingPerformanceMonths", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("integer").ValueGeneratedOnAdd();
        builder.Property(e => e.Year).IsRequired();
        builder.Property(e => e.Month).IsRequired();
        builder.Property(e => e.RetailOrderCount).IsRequired();
        builder.Property(e => e.RetailRevenueWithVat).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(e => e.WholesaleOrderCount).IsRequired();
        builder.Property(e => e.WholesaleRevenueWithVat).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(e => e.SkippedEurInvoiceCount).IsRequired();
        builder.Property(e => e.IsLocked).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.RevenueComputedAt).HasColumnType("timestamp with time zone");
        builder.Property(e => e.CostsComputedAt).HasColumnType("timestamp with time zone");
        builder.Property(e => e.LastError).HasColumnType("text");
        builder.Ignore(e => e.Key);
        builder.HasIndex(e => new { e.Year, e.Month }).IsUnique().HasDatabaseName("IX_MarketingPerformanceMonths_Year_Month");
        builder.HasMany(e => e.ChannelCosts).WithOne(c => c.Month).HasForeignKey(c => c.MonthId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

`MarketingPerformanceChannelCostConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Marketing;

public class MarketingPerformanceChannelCostConfiguration : IEntityTypeConfiguration<MarketingPerformanceChannelCost>
{
    public void Configure(EntityTypeBuilder<MarketingPerformanceChannelCost> builder)
    {
        builder.ToTable("MarketingPerformanceChannelCosts", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("integer").ValueGeneratedOnAdd();
        builder.Property(e => e.ChannelCode).IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
        builder.Property(e => e.CostWithoutVat).IsRequired().HasColumnType("numeric(18,2)");
        builder.Property(e => e.InvoiceCount).IsRequired();
        builder.HasIndex(e => new { e.MonthId, e.ChannelCode }).IsUnique().HasDatabaseName("IX_MarketingPerformanceChannelCosts_MonthId_ChannelCode");
    }
}
```

`ApplicationDbContext.cs` — after line 157 add:

```csharp
    public DbSet<MarketingPerformanceMonth> MarketingPerformanceMonths { get; set; } = null!;
    public DbSet<MarketingPerformanceChannelCost> MarketingPerformanceChannelCosts { get; set; } = null!;
```

plus `using Anela.Heblo.Domain.Features.MarketingPerformance;`. Configurations are picked up by `ApplyConfigurationsFromAssembly` (line 201).

- [ ] **Step 5: Repository + revenue source**

`MarketingPerformanceRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Marketing;

public class MarketingPerformanceRepository : IMarketingPerformanceRepository
{
    private readonly ApplicationDbContext _context;

    public MarketingPerformanceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<MarketingPerformanceMonth>> GetRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        var fromKey = from.Year * 100 + from.Month;
        var toKey = to.Year * 100 + to.Month;
        return await _context.MarketingPerformanceMonths
            .AsNoTracking()
            .Include(m => m.ChannelCosts)
            .Where(m => m.Year * 100 + m.Month >= fromKey && m.Year * 100 + m.Month <= toKey)
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .ToListAsync(cancellationToken);
    }

    public Task<MarketingPerformanceMonth?> GetForUpdateAsync(YearMonth month, CancellationToken cancellationToken) =>
        _context.MarketingPerformanceMonths
            .Include(m => m.ChannelCosts)
            .SingleOrDefaultAsync(m => m.Year == month.Year && m.Month == month.Month, cancellationToken);

    public async Task AddAsync(MarketingPerformanceMonth month, CancellationToken cancellationToken) =>
        await _context.MarketingPerformanceMonths.AddAsync(month, cancellationToken);

    public async Task<int> LockMonthsBeforeAsync(YearMonth cutoff, CancellationToken cancellationToken)
    {
        // Load + set rather than ExecuteUpdate: the InMemory provider used in tests does not support ExecuteUpdate.
        var cutoffKey = cutoff.Year * 100 + cutoff.Month;
        var rows = await _context.MarketingPerformanceMonths
            .Where(m => !m.IsLocked && m.Year * 100 + m.Month < cutoffKey)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsLocked = true;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<DateTime?> GetLastComputedAtAsync(CancellationToken cancellationToken)
    {
        var revenue = await _context.MarketingPerformanceMonths.MaxAsync(m => m.RevenueComputedAt, cancellationToken);
        var costs = await _context.MarketingPerformanceMonths.MaxAsync(m => m.CostsComputedAt, cancellationToken);
        if (revenue is null) return costs;
        if (costs is null) return revenue;
        return revenue > costs ? revenue : costs;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _context.SaveChangesAsync(cancellationToken);
}
```

`IssuedInvoiceMonthlyRevenueSource.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Marketing;

/// <summary>
/// Revenue step: aggregates the already-synced Shoptet issued invoices for one month.
/// CZK only, by TaxDate. Price is the with-VAT total (PriceC is never populated).
/// Wholesale = customer has a VAT ID (VatPayer == true) — the same rule Flexi sales query 37 uses.
/// </summary>
public class IssuedInvoiceMonthlyRevenueSource : IMonthlyRevenueSource
{
    private const string Czk = "CZK";
    private const string Eur = "EUR";
    private readonly ApplicationDbContext _context;

    public IssuedInvoiceMonthlyRevenueSource(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyRevenueSnapshot> GetAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var start = month.Start;
        var end = month.EndExclusive;

        var groups = await _context.IssuedInvoices.AsNoTracking()
            .Where(i => i.Currency == Czk && i.TaxDate >= start && i.TaxDate < end)
            .GroupBy(i => i.VatPayer == true)
            .Select(g => new { IsWholesale = g.Key, Count = g.Count(), Sum = g.Sum(i => i.Price) })
            .ToListAsync(cancellationToken);

        var eurCount = await _context.IssuedInvoices.AsNoTracking()
            .CountAsync(i => i.Currency == Eur && i.TaxDate >= start && i.TaxDate < end, cancellationToken);

        var retail = groups.SingleOrDefault(g => !g.IsWholesale);
        var wholesale = groups.SingleOrDefault(g => g.IsWholesale);

        return new MonthlyRevenueSnapshot
        {
            RetailOrderCount = retail?.Count ?? 0,
            RetailRevenueWithVat = retail?.Sum ?? 0m,
            WholesaleOrderCount = wholesale?.Count ?? 0,
            WholesaleRevenueWithVat = wholesale?.Sum ?? 0m,
            SkippedEurInvoiceCount = eurCount,
        };
    }
}
```

- [ ] **Step 6: Run tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformance"`
Expected: PASS (revenue 2, repository 3, YearMonth 5).

- [ ] **Step 7: Generate the migration**

```bash
dotnet ef migrations add AddMarketingPerformance --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```

Open the generated `Up()` and confirm: two `CreateTable`, unique indexes `IX_MarketingPerformanceMonths_Year_Month` and `IX_MarketingPerformanceChannelCosts_MonthId_ChannelCode`, FK cascade. If the migration also contains unrelated model drift (see `memory/gotchas/ef-migration-codebase-drift.md`), stop and report it instead of committing the drift.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence backend/test/Anela.Heblo.Tests/Features/MarketingPerformance
git commit -m "feat: marketing performance persistence — snapshot tables, repository, issued-invoice revenue source, migration"
```

---

## Task 6: Options, validator, module registration, appsettings

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Configuration/MarketingPerformanceOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Configuration/MarketingChannelOptions.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Configuration/MarketingPerformanceOptionsValidator.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs:95` (add `services.AddMarketingPerformanceModule(configuration);` after `AddMarketingModule`)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (new top-level `MarketingPerformance` section next to `MarketingCalendar`, line 7)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceOptionsValidatorTests.cs`

**Interfaces:**
- Produces:

```csharp
public class MarketingPerformanceOptions
{
    public const string SectionName = "MarketingPerformance";
    public int RecomputeWindowMonths { get; set; } = 2;
    public decimal VatRate { get; set; } = 1.21m;
    public string CronExpression { get; set; } = "0 5 * * *";
    public int MaxRecomputeRangeMonths { get; set; } = 60;
    public List<MarketingChannelOptions> Channels { get; set; } = new();
    public IReadOnlyList<MarketingChannelDefinition> ToDefinitions();
}
public class MarketingChannelOptions { public string Code; public string Label; public List<string> VatIds; }
public class MarketingPerformanceOptionsValidator : IValidateOptions<MarketingPerformanceOptions>
```

- [ ] **Step 1: Write the failing validator tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceOptionsValidatorTests
{
    private static MarketingPerformanceOptions Valid() => new()
    {
        RecomputeWindowMonths = 2,
        VatRate = 1.21m,
        Channels =
        {
            new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE9692928F" } },
            new MarketingChannelOptions { Code = "google", Label = "Google", VatIds = { "IE6388047V" } },
        },
    };

    private static readonly MarketingPerformanceOptionsValidator Validator = new();

    [Fact]
    public void Validate_ValidOptions_Succeeds() =>
        Validator.Validate(null, Valid()).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_NoChannels_Fails()
    {
        var o = Valid(); o.Channels.Clear();
        Validator.Validate(null, o).FailureMessage.Should().Contain("at least one channel");
    }

    [Fact]
    public void Validate_ChannelWithoutVatIds_Fails()
    {
        var o = Valid(); o.Channels[0].VatIds.Clear();
        Validator.Validate(null, o).FailureMessage.Should().Contain("meta").And.Contain("VatIds");
    }

    [Fact]
    public void Validate_DuplicateVatIdAcrossChannels_Fails()
    {
        var o = Valid(); o.Channels[1].VatIds.Add("ie9692928f"); // case-insensitive duplicate
        Validator.Validate(null, o).FailureMessage.Should().Contain("IE9692928F");
    }

    [Fact]
    public void Validate_DuplicateChannelCode_Fails()
    {
        var o = Valid(); o.Channels[1].Code = "META";
        Validator.Validate(null, o).FailureMessage.Should().Contain("Code");
    }

    [Theory]
    [InlineData(0, 1.21)]
    [InlineData(2, 1.0)]
    [InlineData(2, 0.5)]
    public void Validate_BadWindowOrVatRate_Fails(int window, double vat)
    {
        var o = Valid(); o.RecomputeWindowMonths = window; o.VatRate = (decimal)vat;
        Validator.Validate(null, o).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ToDefinitions_NormalizesCodesToLowerAndTrimsVatIds()
    {
        var o = Valid(); o.Channels[0].Code = " Meta "; o.Channels[0].VatIds[0] = " IE9692928F ";
        var defs = o.ToDefinitions();
        defs[0].Code.Should().Be("meta");
        defs[0].VatIds.Should().Equal("IE9692928F");
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformanceOptionsValidatorTests"` → compile errors.

- [ ] **Step 3: Implement options + validator**

`MarketingChannelOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingChannelOptions
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<string> VatIds { get; set; } = new();
}
```

`MarketingPerformanceOptions.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingPerformanceOptions
{
    public const string SectionName = "MarketingPerformance";

    /// <summary>How many months (current month included) the scheduled job recomputes. Default 2 = current + previous.</summary>
    public int RecomputeWindowMonths { get; set; } = 2;

    /// <summary>Divisor to derive without-VAT revenue from the stored with-VAT total.</summary>
    public decimal VatRate { get; set; } = 1.21m;

    public string CronExpression { get; set; } = "0 5 * * *";

    /// <summary>Upper bound for a single manual recompute request.</summary>
    public int MaxRecomputeRangeMonths { get; set; } = 60;

    public List<MarketingChannelOptions> Channels { get; set; } = new();

    public IReadOnlyList<MarketingChannelDefinition> ToDefinitions() =>
        Channels.Select(c => new MarketingChannelDefinition
        {
            Code = c.Code.Trim().ToLowerInvariant(),
            Label = c.Label.Trim(),
            VatIds = c.VatIds.Select(v => v.Trim()).Where(v => v.Length > 0).ToList(),
        }).ToList();
}
```

`MarketingPerformanceOptionsValidator.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Configuration;

public class MarketingPerformanceOptionsValidator : IValidateOptions<MarketingPerformanceOptions>
{
    public ValidateOptionsResult Validate(string? name, MarketingPerformanceOptions options)
    {
        var errors = new List<string>();

        if (options.RecomputeWindowMonths < 1)
            errors.Add("MarketingPerformance:RecomputeWindowMonths must be >= 1.");
        if (options.VatRate <= 1m)
            errors.Add("MarketingPerformance:VatRate must be > 1 (e.g. 1.21).");
        if (options.MaxRecomputeRangeMonths < 1)
            errors.Add("MarketingPerformance:MaxRecomputeRangeMonths must be >= 1.");

        var defs = options.ToDefinitions();
        if (defs.Count == 0)
            errors.Add("MarketingPerformance:Channels must contain at least one channel.");

        foreach (var d in defs.Where(d => string.IsNullOrWhiteSpace(d.Code)))
            errors.Add("MarketingPerformance:Channels contains a channel with an empty Code.");
        foreach (var d in defs.Where(d => d.VatIds.Count == 0))
            errors.Add($"MarketingPerformance:Channels['{d.Code}'] has no VatIds.");

        var duplicateCodes = defs.GroupBy(d => d.Code).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var code in duplicateCodes)
            errors.Add($"MarketingPerformance:Channels has duplicate Code '{code}'.");

        var duplicateVatIds = defs.SelectMany(d => d.VatIds)
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.First());
        foreach (var vat in duplicateVatIds)
            errors.Add($"MarketingPerformance: VAT ID '{vat}' is assigned to more than one channel.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
```

- [ ] **Step 4: Module + wiring**

`MarketingPerformanceModule.cs` (services referenced here are created in Tasks 7–10; add each `AddScoped/AddSingleton` line in the task that creates the type so the solution always compiles):

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence.Marketing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance;

public static class MarketingPerformanceModule
{
    public static IServiceCollection AddMarketingPerformanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketingPerformanceOptions>()
            .Bind(configuration.GetSection(MarketingPerformanceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MarketingPerformanceOptions>, MarketingPerformanceOptionsValidator>();

        services.AddScoped<IMarketingPerformanceRepository, MarketingPerformanceRepository>();
        services.AddScoped<IMonthlyRevenueSource, IssuedInvoiceMonthlyRevenueSource>();
        // IMonthlyAdCostSource is registered by the Flexi adapter (FlexiAdapterServiceCollectionExtensions).
        // MediatR handlers and IRecurringJob implementations are discovered by assembly scan.
        return services;
    }
}
```

`ApplicationModule.cs` line 95 — after `services.AddMarketingModule(configuration);` add `services.AddMarketingPerformanceModule(configuration);`.

`appsettings.json` — add after the `MarketingCalendar` section (VAT IDs below are placeholders the owner replaces; validation only checks shape):

```json
  "MarketingPerformance": {
    "RecomputeWindowMonths": 2,
    "VatRate": 1.21,
    "CronExpression": "0 5 * * *",
    "MaxRecomputeRangeMonths": 60,
    "Channels": [
      { "Code": "meta",   "Label": "FB/IG",  "VatIds": ["IE9692928F"] },
      { "Code": "google", "Label": "Google", "VatIds": ["IE6388047V"] },
      { "Code": "sklik",  "Label": "S-Klik", "VatIds": ["CZ26168685"] }
    ]
  },
```

Check `backend/src/Anela.Heblo.API/appsettings.Development.json` and any test `appsettings*.json` used by `WebApplicationFactory` tests: if they override the whole config, add the same section there, otherwise `ValidateOnStart` will fail those tests.

- [ ] **Step 5: Run tests + build** — `dotnet build backend/src/Anela.Heblo.API && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformance"` → PASS. Also run `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Module"` to catch a DI-validation test that boots the container.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingPerformance backend/src/Anela.Heblo.Application/ApplicationModule.cs backend/src/Anela.Heblo.API/appsettings*.json backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceOptionsValidatorTests.cs
git commit -m "feat: marketing performance options, validator and module registration"
```

---

## Task 7: `MarketingMetricsCalculator` and the month DTO

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Contracts/ChannelCostDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Contracts/ChannelInfoDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Contracts/MonthlyMarketingPerformanceDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MarketingMetricsCalculator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingMetricsCalculatorTests.cs`

**Interfaces:**
- Consumes: `MarketingPerformanceMonth`, `MarketingChannelDefinition`, `YearMonth` (Task 4).
- Produces:

```csharp
public class MarketingMetricsCalculator
{
    public MarketingMetricsCalculator(decimal vatRate, IReadOnlyList<MarketingChannelDefinition> channels);
    /// <summary>Row for a month that exists in the table.</summary>
    public MonthlyMarketingPerformanceDto Build(MarketingPerformanceMonth month, MarketingPerformanceMonth? sameMonthLastYear, bool includeWholesale, bool isPartial);
    /// <summary>Row for a month with no data yet (HasData=false, all sums 0, ratios null).</summary>
    public MonthlyMarketingPerformanceDto Empty(YearMonth month, bool isPartial);
    public static decimal? Ratio(decimal numerator, decimal denominator);          // null when denominator == 0
    public static decimal? PercentOfLastYear(decimal current, decimal? lastYear);   // current / lastYear * 100, null when lastYear null/0
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingMetricsCalculatorTests
{
    private static readonly IReadOnlyList<MarketingChannelDefinition> Channels = new[]
    {
        new MarketingChannelDefinition { Code = "meta", Label = "FB/IG", VatIds = new[] { "IE1" } },
        new MarketingChannelDefinition { Code = "google", Label = "Google", VatIds = new[] { "IE2" } },
        new MarketingChannelDefinition { Code = "sklik", Label = "S-Klik", VatIds = new[] { "CZ1" } },
    };

    private static readonly MarketingMetricsCalculator Calc = new(1.21m, Channels);

    // Spreadsheet row 2026-01: FB/IG 386500, Google 124126, S-Klik 14456, Shoptet s DPH 2 586 556, 2354 orders.
    private static MarketingPerformanceMonth Jan2026() => new()
    {
        Year = 2026, Month = 1,
        RetailOrderCount = 2354, RetailRevenueWithVat = 2_586_556m,
        WholesaleOrderCount = 33, WholesaleRevenueWithVat = 285_591m,
        SkippedEurInvoiceCount = 2, IsLocked = true,
        RevenueComputedAt = new DateTime(2026, 9, 1), CostsComputedAt = new DateTime(2026, 9, 1),
        ChannelCosts =
        {
            new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 386_500m, InvoiceCount = 2 },
            new MarketingPerformanceChannelCost { ChannelCode = "google", CostWithoutVat = 124_126m, InvoiceCount = 1 },
            new MarketingPerformanceChannelCost { ChannelCode = "sklik", CostWithoutVat = 14_456m, InvoiceCount = 1 },
        },
    };

    private static MarketingPerformanceMonth Jan2025() => new()
    {
        Year = 2025, Month = 1, RetailOrderCount = 3021, RetailRevenueWithVat = 2_802_542m,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = 583_917m, InvoiceCount = 3 } },
    };

    [Fact]
    public void Build_RetailOnly_MatchesSpreadsheetFormulas()
    {
        var dto = Calc.Build(Jan2026(), Jan2025(), includeWholesale: false, isPartial: false);

        dto.Year.Should().Be(2026);
        dto.Month.Should().Be(1);
        dto.MonthYearDisplay.Should().Be("01/2026");
        dto.Orders.Should().Be(2354);
        dto.RevenueWithVat.Should().Be(2_586_556m);
        dto.RevenueWithoutVat.Should().BeApproximately(2_137_649.59m, 0.01m);
        dto.TotalCost.Should().Be(525_082m);
        dto.Pno.Should().BeApproximately(24.56m, 0.01m);            // 525082 / 2137649.59 * 100
        dto.Roas.Should().BeApproximately(407.11m, 0.01m);          // inverse
        dto.Profit.Should().BeApproximately(1_612_567.59m, 0.01m);
        dto.AvgOrderValue.Should().BeApproximately(908.09m, 0.01m);
        dto.CostPerOrder.Should().BeApproximately(223.06m, 0.01m);
        dto.YoyCostPercent.Should().BeApproximately(89.92m, 0.01m);     // 525082 / 583917
        dto.YoyRevenuePercent.Should().BeApproximately(92.29m, 0.01m);  // 2586556 / 2802542
        dto.YoyOrdersPercent.Should().BeApproximately(77.92m, 0.01m);   // 2354 / 3021
        dto.HasData.Should().BeTrue();
        dto.IsLocked.Should().BeTrue();
        dto.SkippedEurInvoiceCount.Should().Be(2);
    }

    [Fact]
    public void Build_IncludeWholesale_AddsWholesaleToOrdersAndRevenue()
    {
        var dto = Calc.Build(Jan2026(), null, includeWholesale: true, isPartial: false);

        dto.Orders.Should().Be(2387);
        dto.RevenueWithVat.Should().Be(2_872_147m);
        dto.YoyRevenuePercent.Should().BeNull("no prior year supplied");
    }

    [Fact]
    public void Build_ChannelCosts_FollowConfiguredOrderAndFillMissingWithZero()
    {
        var month = Jan2026();
        month.ChannelCosts.RemoveAll(c => c.ChannelCode == "google");

        var dto = Calc.Build(month, null, false, false);

        dto.ChannelCosts.Select(c => c.ChannelCode).Should().Equal("meta", "google", "sklik");
        dto.ChannelCosts.Single(c => c.ChannelCode == "google").CostWithoutVat.Should().Be(0m);
        dto.ChannelCosts.Single(c => c.ChannelCode == "meta").Label.Should().Be("FB/IG");
        dto.TotalCost.Should().Be(400_956m);
    }

    [Fact]
    public void Build_ZeroRevenueOrOrders_YieldsNullRatiosNotExceptions()
    {
        var month = new MarketingPerformanceMonth { Year = 2026, Month = 9, ChannelCosts = { new() { ChannelCode = "meta", CostWithoutVat = 100m } } };

        var dto = Calc.Build(month, null, false, isPartial: true);

        dto.Pno.Should().BeNull();
        dto.Roas.Should().BeNull();
        dto.AvgOrderValue.Should().BeNull();
        dto.CostPerOrder.Should().BeNull();
        dto.Profit.Should().Be(-100m);
        dto.IsPartial.Should().BeTrue();
    }

    [Fact]
    public void Build_ZeroCost_RoasIsNullPnoIsZero()
    {
        var month = new MarketingPerformanceMonth { Year = 2026, Month = 9, RetailOrderCount = 10, RetailRevenueWithVat = 1210m };
        var dto = Calc.Build(month, null, false, false);
        dto.Pno.Should().Be(0m);
        dto.Roas.Should().BeNull();
    }

    [Fact]
    public void Build_LastYearZero_YoyIsNull()
    {
        var lastYear = new MarketingPerformanceMonth { Year = 2025, Month = 1 };
        var dto = Calc.Build(Jan2026(), lastYear, false, false);
        dto.YoyCostPercent.Should().BeNull();
        dto.YoyOrdersPercent.Should().BeNull();
    }

    [Fact]
    public void Empty_HasNoDataAndZeroSums()
    {
        var dto = Calc.Empty(new YearMonth(2026, 10), isPartial: false);
        dto.HasData.Should().BeFalse();
        dto.Orders.Should().Be(0);
        dto.ChannelCosts.Should().HaveCount(3).And.OnlyContain(c => c.CostWithoutVat == 0m);
        dto.Pno.Should().BeNull();
        dto.RevenueComputedAt.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingMetricsCalculatorTests"` → compile errors.

- [ ] **Step 3: Implement DTOs**

`ChannelCostDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

public class ChannelCostDto
{
    [Required] public string ChannelCode { get; set; } = string.Empty;
    [Required] public string Label { get; set; } = string.Empty;
    [Required] public decimal CostWithoutVat { get; set; }
    [Required] public int InvoiceCount { get; set; }
}
```

`ChannelInfoDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

public class ChannelInfoDto
{
    [Required] public string Code { get; set; } = string.Empty;
    [Required] public string Label { get; set; } = string.Empty;
}
```

`MonthlyMarketingPerformanceDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

/// <summary>One month of the "Výkon reklamy" table. Sums come from the snapshot; ratios are derived here.</summary>
public class MonthlyMarketingPerformanceDto
{
    [Required] public int Year { get; set; }
    [Required] public int Month { get; set; }
    /// <summary>"MM/yyyy"</summary>
    [Required] public string MonthYearDisplay { get; set; } = string.Empty;
    /// <summary>False when the snapshot has no row for this month yet.</summary>
    [Required] public bool HasData { get; set; }
    [Required] public bool IsLocked { get; set; }
    /// <summary>True for the current calendar month (incomplete).</summary>
    [Required] public bool IsPartial { get; set; }

    [Required] public int Orders { get; set; }
    [Required] public decimal RevenueWithVat { get; set; }
    [Required] public decimal RevenueWithoutVat { get; set; }
    [Required] public List<ChannelCostDto> ChannelCosts { get; set; } = new();
    [Required] public decimal TotalCost { get; set; }
    /// <summary>Podíl nákladů na obratu, % of revenue without VAT. Null when revenue is 0.</summary>
    public decimal? Pno { get; set; }
    /// <summary>Return on ad spend, %. Null when cost is 0.</summary>
    public decimal? Roas { get; set; }
    /// <summary>Revenue without VAT minus total cost.</summary>
    [Required] public decimal Profit { get; set; }
    public decimal? AvgOrderValue { get; set; }
    public decimal? CostPerOrder { get; set; }
    public decimal? YoyCostPercent { get; set; }
    public decimal? YoyRevenuePercent { get; set; }
    public decimal? YoyOrdersPercent { get; set; }

    [Required] public int SkippedEurInvoiceCount { get; set; }
    public DateTime? RevenueComputedAt { get; set; }
    public DateTime? CostsComputedAt { get; set; }
    public string? LastError { get; set; }
}
```

- [ ] **Step 4: Implement the calculator**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

/// <summary>
/// Pure derivation of the spreadsheet's ratio columns from stored sums.
/// Never stores anything; a changed VAT rate or formula needs no backfill.
/// </summary>
public class MarketingMetricsCalculator
{
    private const decimal Percent = 100m;
    private readonly decimal _vatRate;
    private readonly IReadOnlyList<MarketingChannelDefinition> _channels;

    public MarketingMetricsCalculator(decimal vatRate, IReadOnlyList<MarketingChannelDefinition> channels)
    {
        if (vatRate <= 1m) throw new ArgumentOutOfRangeException(nameof(vatRate), "VAT rate must be > 1, e.g. 1.21.");
        _vatRate = vatRate;
        _channels = channels;
    }

    public MonthlyMarketingPerformanceDto Build(
        MarketingPerformanceMonth month,
        MarketingPerformanceMonth? sameMonthLastYear,
        bool includeWholesale,
        bool isPartial)
    {
        var orders = Orders(month, includeWholesale);
        var revenueWithVat = RevenueWithVat(month, includeWholesale);
        var revenueWithoutVat = revenueWithVat / _vatRate;
        var channelCosts = ChannelCosts(month);
        var totalCost = channelCosts.Sum(c => c.CostWithoutVat);

        return new MonthlyMarketingPerformanceDto
        {
            Year = month.Year,
            Month = month.Month,
            MonthYearDisplay = Display(month.Key),
            HasData = true,
            IsLocked = month.IsLocked,
            IsPartial = isPartial,
            Orders = orders,
            RevenueWithVat = revenueWithVat,
            RevenueWithoutVat = revenueWithoutVat,
            ChannelCosts = channelCosts,
            TotalCost = totalCost,
            Pno = Ratio(totalCost * Percent, revenueWithoutVat),
            Roas = Ratio(revenueWithoutVat * Percent, totalCost),
            Profit = revenueWithoutVat - totalCost,
            AvgOrderValue = Ratio(revenueWithoutVat, orders),
            CostPerOrder = Ratio(totalCost, orders),
            YoyCostPercent = PercentOfLastYear(totalCost, sameMonthLastYear is null ? null : TotalCost(sameMonthLastYear)),
            YoyRevenuePercent = PercentOfLastYear(revenueWithVat, sameMonthLastYear is null ? null : RevenueWithVat(sameMonthLastYear, includeWholesale)),
            YoyOrdersPercent = PercentOfLastYear(orders, sameMonthLastYear is null ? null : Orders(sameMonthLastYear, includeWholesale)),
            SkippedEurInvoiceCount = month.SkippedEurInvoiceCount,
            RevenueComputedAt = month.RevenueComputedAt,
            CostsComputedAt = month.CostsComputedAt,
            LastError = month.LastError,
        };
    }

    public MonthlyMarketingPerformanceDto Empty(YearMonth month, bool isPartial) => new()
    {
        Year = month.Year,
        Month = month.Month,
        MonthYearDisplay = Display(month),
        HasData = false,
        IsPartial = isPartial,
        ChannelCosts = _channels.Select(c => new ChannelCostDto { ChannelCode = c.Code, Label = c.Label }).ToList(),
    };

    public static decimal? Ratio(decimal numerator, decimal denominator) =>
        denominator == 0m ? null : numerator / denominator;

    public static decimal? PercentOfLastYear(decimal current, decimal? lastYear) =>
        lastYear is null || lastYear == 0m ? null : current / lastYear.Value * Percent;

    private static string Display(YearMonth ym) => $"{ym.Month:D2}/{ym.Year:D4}";

    private static int Orders(MarketingPerformanceMonth m, bool includeWholesale) =>
        m.RetailOrderCount + (includeWholesale ? m.WholesaleOrderCount : 0);

    private static decimal RevenueWithVat(MarketingPerformanceMonth m, bool includeWholesale) =>
        m.RetailRevenueWithVat + (includeWholesale ? m.WholesaleRevenueWithVat : 0m);

    private static decimal TotalCost(MarketingPerformanceMonth m) => m.ChannelCosts.Sum(c => c.CostWithoutVat);

    /// <summary>Configured order; a channel with no stored row shows as zero; stored rows for unconfigured codes are appended so nothing disappears silently.</summary>
    private List<ChannelCostDto> ChannelCosts(MarketingPerformanceMonth m)
    {
        var byCode = m.ChannelCosts.ToDictionary(c => c.ChannelCode, StringComparer.OrdinalIgnoreCase);
        var result = _channels.Select(ch =>
        {
            byCode.TryGetValue(ch.Code, out var stored);
            return new ChannelCostDto
            {
                ChannelCode = ch.Code,
                Label = ch.Label,
                CostWithoutVat = stored?.CostWithoutVat ?? 0m,
                InvoiceCount = stored?.InvoiceCount ?? 0,
            };
        }).ToList();

        var known = new HashSet<string>(_channels.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
        result.AddRange(m.ChannelCosts
            .Where(c => !known.Contains(c.ChannelCode))
            .Select(c => new ChannelCostDto { ChannelCode = c.ChannelCode, Label = c.ChannelCode, CostWithoutVat = c.CostWithoutVat, InvoiceCount = c.InvoiceCount }));
        return result;
    }
}
```

- [ ] **Step 5: Run tests** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingMetricsCalculatorTests"` → 7 PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingPerformance backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingMetricsCalculatorTests.cs
git commit -m "feat: marketing metrics calculator and month DTO"
```

---

## Task 8: Channel cost bucketing and the Flexi ad-cost source

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/ChannelCostBucketer.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/MarketingPerformance/FlexiMonthlyAdCostSource.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs:95` (register next to `IReceivedInvoicesClient`)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/ChannelCostBucketerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiMonthlyAdCostSourceTests.cs`

**Interfaces:**
- Consumes: `IReceivedInvoicesClient.SearchByVatIdsAsync` (Task 3), `AdCostInvoice`, `IMonthlyAdCostSource`, `MarketingChannelDefinition` (Task 4).
- Produces:

```csharp
public class ChannelCostBucket { string ChannelCode; decimal CostWithoutVat; int InvoiceCount; }
public class ChannelBucketingResult { List<ChannelCostBucket> Buckets; int SkippedCancelled; List<string> UnmatchedVatIds; }
public static class ChannelCostBucketer { public static ChannelBucketingResult Bucket(IReadOnlyList<AdCostInvoice> invoices, IReadOnlyList<MarketingChannelDefinition> channels); }
public class FlexiMonthlyAdCostSource : IMonthlyAdCostSource  // ctor(IReceivedInvoicesClient, ILogger<FlexiMonthlyAdCostSource>)
```

- [ ] **Step 1: Write the failing bucketer tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class ChannelCostBucketerTests
{
    private static readonly IReadOnlyList<MarketingChannelDefinition> Channels = new[]
    {
        new MarketingChannelDefinition { Code = "meta", Label = "FB/IG", VatIds = new[] { "IE9692928F", "IE0000001X" } },
        new MarketingChannelDefinition { Code = "google", Label = "Google", VatIds = new[] { "IE6388047V" } },
        new MarketingChannelDefinition { Code = "sklik", Label = "S-Klik", VatIds = new[] { "CZ26168685" } },
    };

    private static AdCostInvoice Inv(string vat, decimal amount, bool cancelled = false) =>
        new() { InvoiceNumber = Guid.NewGuid().ToString("N")[..8], SupplierVatId = vat, AmountWithoutVat = amount, IsCancelled = cancelled, AccountingDate = new DateTime(2026, 8, 5) };

    [Fact]
    public void Bucket_SumsPerChannel_AndMultipleVatIdsPerChannel()
    {
        var result = ChannelCostBucketer.Bucket(new[]
        {
            Inv("IE9692928F", 100m), Inv("ie0000001x", 50m), Inv("IE6388047V", 30m), Inv("CZ26168685", 5m),
        }, Channels);

        result.Buckets.Select(b => b.ChannelCode).Should().Equal("meta", "google", "sklik");
        result.Buckets[0].CostWithoutVat.Should().Be(150m);
        result.Buckets[0].InvoiceCount.Should().Be(2);
        result.Buckets[1].CostWithoutVat.Should().Be(30m);
        result.Buckets[2].CostWithoutVat.Should().Be(5m);
    }

    [Fact]
    public void Bucket_ChannelWithoutInvoices_GetsExplicitZeroRow()
    {
        var result = ChannelCostBucketer.Bucket(new[] { Inv("IE9692928F", 100m) }, Channels);
        result.Buckets.Should().HaveCount(3);
        result.Buckets.Single(b => b.ChannelCode == "sklik").Should().BeEquivalentTo(new ChannelCostBucket { ChannelCode = "sklik", CostWithoutVat = 0m, InvoiceCount = 0 });
    }

    [Fact]
    public void Bucket_SkipsCancelled_AndReportsUnmatched()
    {
        var result = ChannelCostBucketer.Bucket(new[]
        {
            Inv("IE9692928F", 100m), Inv("IE9692928F", 999m, cancelled: true), Inv("DE123", 7m),
        }, Channels);

        result.Buckets.Single(b => b.ChannelCode == "meta").CostWithoutVat.Should().Be(100m);
        result.SkippedCancelled.Should().Be(1);
        result.UnmatchedVatIds.Should().Equal("DE123");
    }

    [Fact]
    public void Bucket_NegativeCreditNote_ReducesTheSum()
    {
        var result = ChannelCostBucketer.Bucket(new[] { Inv("IE6388047V", 100m), Inv("IE6388047V", -20m) }, Channels);
        result.Buckets.Single(b => b.ChannelCode == "google").CostWithoutVat.Should().Be(80m);
    }
}
```

- [ ] **Step 2: Write the failing Flexi source test**

```csharp
using Anela.Heblo.Adapters.Flexi.Accounting.MarketingPerformance;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Flexi;

public class FlexiMonthlyAdCostSourceTests
{
    [Fact]
    public async Task GetAsync_QueriesWholeMonthByAccountingDate_AndMapsFields()
    {
        var client = new Mock<IReceivedInvoicesClient>();
        client.Setup(c => c.SearchByVatIdsAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<ReceivedInvoice>
              {
                  new() { InvoiceNumber = "PF1", SupplierVatId = "IE9692928F", AccountingDate = new DateTime(2026, 8, 3), TotalAmountWithoutVat = 1000m, IsCancelled = false },
                  new() { InvoiceNumber = "PF2", SupplierVatId = null, AccountingDate = null, TotalAmountWithoutVat = 5m, IsCancelled = true },
              });
        var source = new FlexiMonthlyAdCostSource(client.Object, NullLogger<FlexiMonthlyAdCostSource>.Instance);

        var result = await source.GetAsync(new YearMonth(2026, 8), new[] { "IE9692928F" }, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().BeEquivalentTo(new AdCostInvoice { InvoiceNumber = "PF1", SupplierVatId = "IE9692928F", AccountingDate = new DateTime(2026, 8, 3), AmountWithoutVat = 1000m, IsCancelled = false });
        result[1].SupplierVatId.Should().BeEmpty();
        result[1].AccountingDate.Should().Be(new YearMonth(2026, 8).Start);
        client.VerifyAll();
    }

    [Fact]
    public async Task GetAsync_NoVatIds_ReturnsEmptyWithoutCallingFlexi()
    {
        var client = new Mock<IReceivedInvoicesClient>(MockBehavior.Strict);
        var source = new FlexiMonthlyAdCostSource(client.Object, NullLogger<FlexiMonthlyAdCostSource>.Instance);

        var result = await source.GetAsync(new YearMonth(2026, 8), Array.Empty<string>(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 3: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ChannelCostBucketer|FullyQualifiedName~FlexiMonthlyAdCostSource"` → compile errors.

- [ ] **Step 4: Implement the bucketer**

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class ChannelCostBucket
{
    public string ChannelCode { get; init; } = string.Empty;
    public decimal CostWithoutVat { get; init; }
    public int InvoiceCount { get; init; }
}

public class ChannelBucketingResult
{
    public List<ChannelCostBucket> Buckets { get; init; } = new();
    public int SkippedCancelled { get; init; }
    public List<string> UnmatchedVatIds { get; init; } = new();
}

/// <summary>Assigns received invoices to channels by supplier DIČ. Pure; one explicit row per configured channel.</summary>
public static class ChannelCostBucketer
{
    public static ChannelBucketingResult Bucket(IReadOnlyList<AdCostInvoice> invoices, IReadOnlyList<MarketingChannelDefinition> channels)
    {
        var channelByVatId = channels
            .SelectMany(ch => ch.VatIds.Select(v => (VatId: v, ch.Code)))
            .ToDictionary(x => x.VatId, x => x.Code, StringComparer.OrdinalIgnoreCase);

        var live = invoices.Where(i => !i.IsCancelled).ToList();
        var skippedCancelled = invoices.Count - live.Count;

        var matched = live
            .Select(i => (Invoice: i, Code: channelByVatId.TryGetValue(i.SupplierVatId, out var code) ? code : null))
            .ToList();

        var buckets = channels.Select(ch =>
        {
            var mine = matched.Where(m => m.Code == ch.Code).Select(m => m.Invoice).ToList();
            return new ChannelCostBucket
            {
                ChannelCode = ch.Code,
                CostWithoutVat = mine.Sum(i => i.AmountWithoutVat),
                InvoiceCount = mine.Count,
            };
        }).ToList();

        var unmatched = matched.Where(m => m.Code is null)
            .Select(m => m.Invoice.SupplierVatId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ChannelBucketingResult { Buckets = buckets, SkippedCancelled = skippedCancelled, UnmatchedVatIds = unmatched };
    }
}
```

- [ ] **Step 5: Implement the Flexi source + register**

`FlexiMonthlyAdCostSource.cs`:

```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Accounting.MarketingPerformance;

/// <summary>Cost step: one Flexi call per month — received invoices by accounting date and supplier DIČ.</summary>
public class FlexiMonthlyAdCostSource : IMonthlyAdCostSource
{
    private readonly IReceivedInvoicesClient _client;
    private readonly ILogger<FlexiMonthlyAdCostSource> _logger;

    public FlexiMonthlyAdCostSource(IReceivedInvoicesClient client, ILogger<FlexiMonthlyAdCostSource> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdCostInvoice>> GetAsync(YearMonth month, IReadOnlyCollection<string> vatIds, CancellationToken cancellationToken)
    {
        if (vatIds.Count == 0)
        {
            _logger.LogWarning("Marketing performance: no VAT IDs configured, skipping Flexi call for {Month}", month);
            return Array.Empty<AdCostInvoice>();
        }

        var invoices = await _client.SearchByVatIdsAsync(month.Start, month.LastDay, vatIds, cancellationToken);

        return invoices.Select(i => new AdCostInvoice
        {
            InvoiceNumber = i.InvoiceNumber,
            SupplierVatId = i.SupplierVatId ?? string.Empty,
            AccountingDate = i.AccountingDate ?? month.Start,
            AmountWithoutVat = i.TotalAmountWithoutVat,
            IsCancelled = i.IsCancelled,
        }).ToList();
    }
}
```

`FlexiAdapterServiceCollectionExtensions.cs` — after line 95 add:

```csharp
        services.AddScoped<IMonthlyAdCostSource, FlexiMonthlyAdCostSource>();
```

with `using Anela.Heblo.Adapters.Flexi.Accounting.MarketingPerformance;` and `using Anela.Heblo.Domain.Features.MarketingPerformance;`.

- [ ] **Step 6: Run tests + build** — `dotnet build backend/src/Anela.Heblo.API && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ChannelCostBucketer|FullyQualifiedName~FlexiMonthlyAdCostSource"` → 6 PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/ChannelCostBucketer.cs backend/src/Adapters/Anela.Heblo.Adapters.Flexi backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/ChannelCostBucketerTests.cs backend/test/Anela.Heblo.Tests/Adapters/Flexi/FlexiMonthlyAdCostSourceTests.cs
git commit -m "feat: channel cost bucketing by supplier DIČ and Flexi monthly ad-cost source"
```

---

## Task 9: Refresh service (per-month two-step upsert, locking, run guard)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MarketingPerformanceRunGuard.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/IMarketingPerformanceRefreshService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MarketingPerformanceRefreshService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/MarketingPerformanceModule.cs` (register)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `IMarketingPerformanceRepository`, `IMonthlyRevenueSource`, `IMonthlyAdCostSource`, `ChannelCostBucketer`, `MarketingPerformanceOptions`, `TimeProvider`.
- Produces:

```csharp
public sealed class MarketingPerformanceRunGuard   // singleton
{
    public bool IsRunning { get; }
    public bool TryBegin();     // false if already running
    public void End();
}

public class MonthRefreshOutcome { YearMonth Month; bool RevenueOk; bool CostsOk; string? Error; int UnmatchedVatIdCount; }
public class RefreshRunResult { List<MonthRefreshOutcome> Months; int LockedMonths; bool AllFailed => Months.Count > 0 && Months.All(m => !m.RevenueOk && !m.CostsOk); }

public interface IMarketingPerformanceRefreshService
{
    /// <summary>Scheduled run: window = [today − (RecomputeWindowMonths−1) months, today]; then locks older months.</summary>
    Task<RefreshRunResult> RefreshWindowAsync(CancellationToken ct);
    /// <summary>Manual run: every month in [from, to], ignoring locks; never locks anything.</summary>
    Task<RefreshRunResult> RecomputeRangeAsync(YearMonth from, YearMonth to, CancellationToken ct);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Marketing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceRefreshServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 5, 0, 0, TimeSpan.FromHours(2));
    private readonly ApplicationDbContext _context;
    private readonly MarketingPerformanceRepository _repo;
    private readonly Mock<IMonthlyRevenueSource> _revenue = new();
    private readonly Mock<IMonthlyAdCostSource> _costs = new();
    private readonly FakeTimeProvider _time = new(Now);

    public MarketingPerformanceRefreshServiceTests()
    {
        _context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _repo = new MarketingPerformanceRepository(_context);
        _revenue.Setup(r => r.GetAsync(It.IsAny<YearMonth>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YearMonth m, CancellationToken _) => new MonthlyRevenueSnapshot { RetailOrderCount = m.Month, RetailRevenueWithVat = m.Month * 1000m, WholesaleOrderCount = 1, WholesaleRevenueWithVat = 500m, SkippedEurInvoiceCount = 2 });
        _costs.Setup(c => c.GetAsync(It.IsAny<YearMonth>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YearMonth m, IReadOnlyCollection<string> _, CancellationToken _) => new List<AdCostInvoice>
            {
                new() { InvoiceNumber = "A", SupplierVatId = "IE1", AmountWithoutVat = 100m * m.Month, AccountingDate = m.Start },
                new() { InvoiceNumber = "B", SupplierVatId = "XX", AmountWithoutVat = 5m, AccountingDate = m.Start }, // unmatched
            });
    }

    public void Dispose() => _context.Dispose();

    private MarketingPerformanceRefreshService Service(int window = 2) => new(
        _repo, _revenue.Object, _costs.Object,
        Options.Create(new MarketingPerformanceOptions
        {
            RecomputeWindowMonths = window,
            Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } }, new MarketingChannelOptions { Code = "google", Label = "Google", VatIds = { "IE2" } } },
        }),
        _time, NullLogger<MarketingPerformanceRefreshService>.Instance);

    [Fact]
    public async Task RefreshWindowAsync_UpsertsCurrentAndPreviousMonth_AndReplacesChannelRows()
    {
        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        result.Months.Select(m => m.Month).Should().Equal(new YearMonth(2026, 8), new YearMonth(2026, 9));
        var sep = (await _repo.GetForUpdateAsync(new YearMonth(2026, 9), CancellationToken.None))!;
        sep.RetailOrderCount.Should().Be(9);
        sep.RetailRevenueWithVat.Should().Be(9000m);
        sep.WholesaleOrderCount.Should().Be(1);
        sep.SkippedEurInvoiceCount.Should().Be(2);
        sep.ChannelCosts.Should().HaveCount(2);
        sep.ChannelCosts.Single(c => c.ChannelCode == "meta").CostWithoutVat.Should().Be(900m);
        sep.ChannelCosts.Single(c => c.ChannelCode == "google").CostWithoutVat.Should().Be(0m);
        sep.RevenueComputedAt.Should().Be(Now.UtcDateTime);
        sep.CostsComputedAt.Should().Be(Now.UtcDateTime);
        sep.LastError.Should().BeNull();
        sep.IsLocked.Should().BeFalse();
        result.Months[1].UnmatchedVatIdCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshWindowAsync_SecondRun_UpdatesInPlaceWithoutDuplicates()
    {
        await Service().RefreshWindowAsync(CancellationToken.None);
        await Service().RefreshWindowAsync(CancellationToken.None);

        _context.MarketingPerformanceMonths.Count().Should().Be(2);
        _context.MarketingPerformanceChannelCosts.Count().Should().Be(4);
    }

    [Fact]
    public async Task RefreshWindowAsync_LocksMonthsOlderThanWindow_AndSkipsThem()
    {
        await _repo.AddAsync(new MarketingPerformanceMonth { Year = 2026, Month = 7 }, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        result.LockedMonths.Should().Be(1);
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 7), CancellationToken.None))!.IsLocked.Should().BeTrue();
        _revenue.Verify(r => r.GetAsync(new YearMonth(2026, 7), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshWindowAsync_FlexiFails_KeepsRevenueAndRecordsError()
    {
        _costs.Setup(c => c.GetAsync(new YearMonth(2026, 9), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Flexi 503"));

        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        var sep = (await _repo.GetForUpdateAsync(new YearMonth(2026, 9), CancellationToken.None))!;
        sep.RetailOrderCount.Should().Be(9);
        sep.RevenueComputedAt.Should().NotBeNull();
        sep.CostsComputedAt.Should().BeNull();
        sep.LastError.Should().Contain("Flexi 503");
        result.Months.Single(m => m.Month == new YearMonth(2026, 9)).CostsOk.Should().BeFalse();
        result.Months.Single(m => m.Month == new YearMonth(2026, 8)).CostsOk.Should().BeTrue();
        result.AllFailed.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshWindowAsync_EverythingFails_AllFailedIsTrue()
    {
        _revenue.Setup(r => r.GetAsync(It.IsAny<YearMonth>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        _costs.Setup(c => c.GetAsync(It.IsAny<YearMonth>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("flexi"));

        var result = await Service().RefreshWindowAsync(CancellationToken.None);

        result.AllFailed.Should().BeTrue();
        (await _repo.GetForUpdateAsync(new YearMonth(2026, 9), CancellationToken.None))!.LastError.Should().Contain("db").And.Contain("flexi");
    }

    [Fact]
    public async Task RecomputeRangeAsync_IgnoresLocks_AndDoesNotLock()
    {
        await _repo.AddAsync(new MarketingPerformanceMonth { Year = 2025, Month = 1, IsLocked = true, RetailOrderCount = 999 }, CancellationToken.None);
        await _repo.SaveChangesAsync(CancellationToken.None);

        var result = await Service().RecomputeRangeAsync(new YearMonth(2025, 1), new YearMonth(2025, 2), CancellationToken.None);

        result.Months.Should().HaveCount(2);
        result.LockedMonths.Should().Be(0);
        var jan = (await _repo.GetForUpdateAsync(new YearMonth(2025, 1), CancellationToken.None))!;
        jan.RetailOrderCount.Should().Be(1);
        jan.IsLocked.Should().BeTrue("recompute preserves the lock flag");
    }

    [Fact]
    public void RunGuard_IsExclusive()
    {
        var guard = new MarketingPerformanceRunGuard();
        guard.TryBegin().Should().BeTrue();
        guard.TryBegin().Should().BeFalse();
        guard.IsRunning.Should().BeTrue();
        guard.End();
        guard.TryBegin().Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformanceRefreshServiceTests"` → compile errors.

- [ ] **Step 3: Implement the run guard**

```csharp
namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

/// <summary>Process-wide "a refresh or recompute is running" flag shared by the scheduled job, the recompute job and the API guard.</summary>
public sealed class MarketingPerformanceRunGuard
{
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public bool TryBegin() => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    public void End() => Volatile.Write(ref _running, 0);
}
```

- [ ] **Step 4: Implement the service**

`IMarketingPerformanceRefreshService.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class MonthRefreshOutcome
{
    public YearMonth Month { get; init; }
    public bool RevenueOk { get; init; }
    public bool CostsOk { get; init; }
    public string? Error { get; init; }
    public int UnmatchedVatIdCount { get; init; }
}

public class RefreshRunResult
{
    public List<MonthRefreshOutcome> Months { get; init; } = new();
    public int LockedMonths { get; init; }
    public bool AllFailed => Months.Count > 0 && Months.All(m => !m.RevenueOk && !m.CostsOk);
}

public interface IMarketingPerformanceRefreshService
{
    Task<RefreshRunResult> RefreshWindowAsync(CancellationToken cancellationToken);
    Task<RefreshRunResult> RecomputeRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken);
}
```

`MarketingPerformanceRefreshService.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class MarketingPerformanceRefreshService : IMarketingPerformanceRefreshService
{
    private readonly IMarketingPerformanceRepository _repository;
    private readonly IMonthlyRevenueSource _revenueSource;
    private readonly IMonthlyAdCostSource _costSource;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarketingPerformanceRefreshService> _logger;

    public MarketingPerformanceRefreshService(
        IMarketingPerformanceRepository repository,
        IMonthlyRevenueSource revenueSource,
        IMonthlyAdCostSource costSource,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<MarketingPerformanceRefreshService> logger)
    {
        _repository = repository;
        _revenueSource = revenueSource;
        _costSource = costSource;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RefreshRunResult> RefreshWindowAsync(CancellationToken cancellationToken)
    {
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var windowStart = current.AddMonths(-(_options.RecomputeWindowMonths - 1));

        var outcomes = new List<MonthRefreshOutcome>();
        foreach (var month in YearMonth.Range(windowStart, current))
        {
            outcomes.Add(await RefreshMonthAsync(month, cancellationToken));
        }

        var locked = await _repository.LockMonthsBeforeAsync(windowStart, cancellationToken);
        if (locked > 0)
        {
            _logger.LogInformation("Marketing performance: locked {Count} month(s) before {Cutoff}", locked, windowStart);
        }

        return new RefreshRunResult { Months = outcomes, LockedMonths = locked };
    }

    public async Task<RefreshRunResult> RecomputeRangeAsync(YearMonth from, YearMonth to, CancellationToken cancellationToken)
    {
        var outcomes = new List<MonthRefreshOutcome>();
        foreach (var month in YearMonth.Range(from, to))
        {
            outcomes.Add(await RefreshMonthAsync(month, cancellationToken));
        }
        return new RefreshRunResult { Months = outcomes, LockedMonths = 0 };
    }

    private async Task<MonthRefreshOutcome> RefreshMonthAsync(YearMonth month, CancellationToken cancellationToken)
    {
        var row = await _repository.GetForUpdateAsync(month, cancellationToken);
        var isNew = row is null;
        row ??= new MarketingPerformanceMonth { Year = month.Year, Month = month.Month };

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var errors = new List<string>();
        var revenueOk = false;
        var costsOk = false;
        var unmatched = 0;

        try
        {
            var revenue = await _revenueSource.GetAsync(month, cancellationToken);
            row.RetailOrderCount = revenue.RetailOrderCount;
            row.RetailRevenueWithVat = revenue.RetailRevenueWithVat;
            row.WholesaleOrderCount = revenue.WholesaleOrderCount;
            row.WholesaleRevenueWithVat = revenue.WholesaleRevenueWithVat;
            row.SkippedEurInvoiceCount = revenue.SkippedEurInvoiceCount;
            row.RevenueComputedAt = now;
            revenueOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Marketing performance: revenue step failed for {Month}", month);
            errors.Add($"Revenue: {ex.Message}");
        }

        try
        {
            var channels = _options.ToDefinitions();
            var vatIds = channels.SelectMany(c => c.VatIds).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var invoices = await _costSource.GetAsync(month, vatIds, cancellationToken);
            var bucketing = ChannelCostBucketer.Bucket(invoices, channels);
            unmatched = bucketing.UnmatchedVatIds.Count;
            if (unmatched > 0)
            {
                _logger.LogWarning("Marketing performance {Month}: {Count} invoice supplier DIČ(s) matched no channel: {VatIds}",
                    month, unmatched, string.Join(", ", bucketing.UnmatchedVatIds));
            }

            row.ChannelCosts.Clear();
            foreach (var bucket in bucketing.Buckets)
            {
                // Never set Id on children added to a tracked parent — EF would issue UPDATE instead of INSERT.
                row.ChannelCosts.Add(new MarketingPerformanceChannelCost
                {
                    ChannelCode = bucket.ChannelCode,
                    CostWithoutVat = bucket.CostWithoutVat,
                    InvoiceCount = bucket.InvoiceCount,
                });
            }
            row.CostsComputedAt = now;
            costsOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Marketing performance: cost step failed for {Month}", month);
            errors.Add($"Costs: {ex.Message}");
        }

        row.LastError = errors.Count == 0 ? null : string.Join(" | ", errors);

        if (isNew)
        {
            await _repository.AddAsync(row, cancellationToken);
        }
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Marketing performance {Month}: revenue={RevenueOk} costs={CostsOk}", month, revenueOk, costsOk);
        return new MonthRefreshOutcome { Month = month, RevenueOk = revenueOk, CostsOk = costsOk, Error = row.LastError, UnmatchedVatIdCount = unmatched };
    }
}
```

Register in `MarketingPerformanceModule.cs`:

```csharp
        services.AddSingleton<MarketingPerformanceRunGuard>();
        services.AddScoped<IMarketingPerformanceRefreshService, MarketingPerformanceRefreshService>();
```

`TimeProvider` is already registered application-wide (other services inject it); if the DI validation test complains, add `services.TryAddSingleton(TimeProvider.System);`.

- [ ] **Step 5: Run tests** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformanceRefreshServiceTests"` → 7 PASS. If `row.ChannelCosts.Clear()` leaves orphans under InMemory (children not deleted), switch to explicit removal: iterate `row.ChannelCosts.ToList()` and call `_context`-free removal via a repository method `RemoveChannelCosts(IEnumerable<MarketingPerformanceChannelCost>)`; the cascade + required FK in Task 5 makes `Clear()` delete on Npgsql, and the `SecondRun` test pins the behaviour.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingPerformance backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceRefreshServiceTests.cs
git commit -m "feat: marketing performance refresh service — per-month revenue/cost upsert, locking, run guard"
```

---

## Task 10: Hangfire jobs — scheduled refresh and parametrised recompute

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Infrastructure/Jobs/MarketingPerformanceRefreshJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Infrastructure/Jobs/MarketingPerformanceRecomputeJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/IMarketingPerformanceRecomputeEnqueuer.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/HangfireMarketingPerformanceRecomputeEnqueuer.cs`
- Modify: `MarketingPerformanceModule.cs` (register enqueuer + recompute job)
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceRefreshJobTests.cs`

**Interfaces:**
- Consumes: `IMarketingPerformanceRefreshService`, `MarketingPerformanceRunGuard` (Task 9), `IRecurringJobStatusChecker`, `RecurringJobMetadata`, Hangfire `IBackgroundJobClient`.
- Produces: job name `marketing-performance-refresh`; `MarketingPerformanceRecomputeJob.RunAsync(int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken ct)`; `string? IMarketingPerformanceRecomputeEnqueuer.Enqueue(YearMonth from, YearMonth to)`.

- [ ] **Step 1: Write the failing job tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class MarketingPerformanceRefreshJobTests
{
    private readonly Mock<IMarketingPerformanceRefreshService> _service = new();
    private readonly MarketingPerformanceRunGuard _guard = new();

    private static Mock<IRecurringJobStatusChecker> StatusChecker(bool enabled)
    {
        var mock = new Mock<IRecurringJobStatusChecker>();
        mock.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(enabled);
        return mock;
    }

    private MarketingPerformanceRefreshJob Job(bool enabled = true, string cron = "0 5 * * *") => new(
        _service.Object, _guard, StatusChecker(enabled).Object,
        Options.Create(new MarketingPerformanceOptions { CronExpression = cron }),
        NullLogger<MarketingPerformanceRefreshJob>.Instance);

    [Fact]
    public void Metadata_UsesAgreedNameAndConfiguredCron()
    {
        var metadata = Job(cron: "30 6 * * *").Metadata;
        metadata.JobName.Should().Be("marketing-performance-refresh");
        metadata.CronExpression.Should().Be("30 6 * * *");
        metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_Disabled_DoesNothing()
    {
        await Job(enabled: false).ExecuteAsync();
        _service.Verify(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Enabled_RefreshesWindow_AndReleasesGuard()
    {
        _service.Setup(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshRunResult { Months = { new MonthRefreshOutcome { Month = new YearMonth(2026, 9), RevenueOk = true, CostsOk = true } } });

        await Job().ExecuteAsync();

        _service.Verify(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()), Times.Once);
        _guard.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AllMonthsFailed_Throws_SoHangfireRetries()
    {
        _service.Setup(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshRunResult { Months = { new MonthRefreshOutcome { Month = new YearMonth(2026, 9), Error = "x" } } });

        var act = () => Job().ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*every month*");
        _guard.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenAnotherRunIsActive_Skips()
    {
        _guard.TryBegin().Should().BeTrue();
        await Job().ExecuteAsync();
        _service.Verify(s => s.RefreshWindowAsync(It.IsAny<CancellationToken>()), Times.Never);
        _guard.End();
    }

    [Fact]
    public async Task RecomputeJob_RunsRangeAndReleasesGuard()
    {
        _service.Setup(s => s.RecomputeRangeAsync(new YearMonth(2023, 1), new YearMonth(2023, 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshRunResult());
        var job = new MarketingPerformanceRecomputeJob(_service.Object, _guard, NullLogger<MarketingPerformanceRecomputeJob>.Instance);

        await job.RunAsync(2023, 1, 2023, 3, CancellationToken.None);

        _service.VerifyAll();
        _guard.IsRunning.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformanceRefreshJobTests"` → compile errors.

- [ ] **Step 3: Implement the scheduled job**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;

/// <summary>
/// Daily snapshot of ad spend (Flexi received invoices by supplier DIČ) and orders/revenue (IssuedInvoices)
/// for the current and previous month (MarketingPerformance:RecomputeWindowMonths). Older months are locked.
/// </summary>
public class MarketingPerformanceRefreshJob : IRecurringJob
{
    public const string Name = "marketing-performance-refresh";
    private const int LockTimeoutSeconds = 600;

    private readonly IMarketingPerformanceRefreshService _service;
    private readonly MarketingPerformanceRunGuard _guard;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<MarketingPerformanceRefreshJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public MarketingPerformanceRefreshJob(
        IMarketingPerformanceRefreshService service,
        MarketingPerformanceRunGuard guard,
        IRecurringJobStatusChecker statusChecker,
        IOptions<MarketingPerformanceOptions> options,
        ILogger<MarketingPerformanceRefreshJob> logger)
    {
        _service = service;
        _guard = guard;
        _statusChecker = statusChecker;
        _logger = logger;
        Metadata = new RecurringJobMetadata
        {
            JobName = Name,
            DisplayName = "Marketing — výkon reklamy (měsíční snapshot)",
            Description = "Denně přepočítá aktuální a předchozí měsíc: náklady na reklamu z přijatých faktur v ABRA Flexi (podle DIČ dodavatele na kanál) a objednávky/tržby z vydaných faktur (CZK, podle DUZP, maloobchod/velkoobchod zvlášť). Starší měsíce uzamkne; ty mění jen ruční přepočet na obrazovce Výkon reklamy.",
            CronExpression = options.Value.CronExpression,
            DefaultIsEnabled = true,
        };
    }

    [DisableConcurrentExecution(LockTimeoutSeconds)]
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken, Metadata.DefaultIsEnabled))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        if (!_guard.TryBegin())
        {
            _logger.LogWarning("Job {JobName}: another marketing performance run is active. Skipping.", Metadata.JobName);
            return;
        }

        try
        {
            var result = await _service.RefreshWindowAsync(cancellationToken);
            _logger.LogInformation("{JobName} complete: {Ok}/{Total} months fully refreshed, {Locked} locked",
                Metadata.JobName, result.Months.Count(m => m.RevenueOk && m.CostsOk), result.Months.Count, result.LockedMonths);

            if (result.AllFailed)
            {
                throw new InvalidOperationException(
                    $"{Metadata.JobName}: every month in the window failed: {string.Join(" | ", result.Months.Select(m => $"{m.Month}: {m.Error}"))}");
            }
        }
        finally
        {
            _guard.End();
        }
    }
}
```

- [ ] **Step 4: Implement the recompute job + enqueuer**

`MarketingPerformanceRecomputeJob.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;

/// <summary>Fire-and-forget Hangfire job behind POST /api/marketing-performance/recompute. Ignores month locks.</summary>
public class MarketingPerformanceRecomputeJob
{
    private readonly IMarketingPerformanceRefreshService _service;
    private readonly MarketingPerformanceRunGuard _guard;
    private readonly ILogger<MarketingPerformanceRecomputeJob> _logger;

    public MarketingPerformanceRecomputeJob(IMarketingPerformanceRefreshService service, MarketingPerformanceRunGuard guard, ILogger<MarketingPerformanceRecomputeJob> logger)
    {
        _service = service;
        _guard = guard;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken cancellationToken)
    {
        var from = new YearMonth(fromYear, fromMonth);
        var to = new YearMonth(toYear, toMonth);

        if (!_guard.TryBegin())
        {
            _logger.LogWarning("Marketing performance recompute {From}..{To} skipped: another run is active", from, to);
            return;
        }

        try
        {
            var result = await _service.RecomputeRangeAsync(from, to, cancellationToken);
            _logger.LogInformation("Marketing performance recompute {From}..{To}: {Ok}/{Total} months fully refreshed",
                from, to, result.Months.Count(m => m.RevenueOk && m.CostsOk), result.Months.Count);
        }
        finally
        {
            _guard.End();
        }
    }
}
```

`IMarketingPerformanceRecomputeEnqueuer.cs` + `HangfireMarketingPerformanceRecomputeEnqueuer.cs`:

```csharp
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public interface IMarketingPerformanceRecomputeEnqueuer
{
    /// <summary>Enqueues the recompute; returns the Hangfire job id or null when enqueueing failed.</summary>
    string? Enqueue(YearMonth from, YearMonth to);
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class HangfireMarketingPerformanceRecomputeEnqueuer : IMarketingPerformanceRecomputeEnqueuer
{
    private readonly IBackgroundJobClient _client;
    private readonly ILogger<HangfireMarketingPerformanceRecomputeEnqueuer> _logger;

    public HangfireMarketingPerformanceRecomputeEnqueuer(IBackgroundJobClient client, ILogger<HangfireMarketingPerformanceRecomputeEnqueuer> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string? Enqueue(YearMonth from, YearMonth to)
    {
        try
        {
            return _client.Enqueue<MarketingPerformanceRecomputeJob>(j => j.RunAsync(from.Year, from.Month, to.Year, to.Month, CancellationToken.None));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue marketing performance recompute {From}..{To}", from, to);
            return null;
        }
    }
}
```

Register in `MarketingPerformanceModule.cs`:

```csharp
        services.AddScoped<MarketingPerformanceRecomputeJob>();
        services.AddScoped<IMarketingPerformanceRecomputeEnqueuer, HangfireMarketingPerformanceRecomputeEnqueuer>();
        // MarketingPerformanceRefreshJob is discovered by the IRecurringJob assembly scan in AddRecurringJobs().
```

`IBackgroundJobClient` is registered by `AddHangfire` in `Program.cs`; check that `RecurringJobSeeder` picks the new job up (it seeds from `IRecurringJob` metadata) — no code change expected.

- [ ] **Step 5: Run tests + build** — `dotnet build backend/src/Anela.Heblo.API && dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformanceRefreshJobTests"` → 6 PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingPerformance backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/MarketingPerformanceRefreshJobTests.cs
git commit -m "feat: marketing performance Hangfire jobs — daily refresh and fire-and-forget recompute"
```

---

## Task 11: Error codes (37XX), contract test bucket, Czech translations

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (insert before `// External Service errors (90XX)`)
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs:101-137`
- Modify: `frontend/src/i18n.ts` (after the Marketing Calendar block, line ~233)

**Interfaces:**
- Produces: `ErrorCodes.MarketingPerformanceInvalidMonthRange = 3701` (BadRequest), `MarketingPerformanceRangeTooLarge = 3702` (BadRequest), `MarketingPerformanceRecomputeAlreadyRunning = 3703` (Conflict), `MarketingPerformanceEnqueueFailed = 3704` (InternalServerError).

- [ ] **Step 1: Add the bucket to the contract test first (it fails until the codes exist)**

In `ErrorHandlingTests.cs` after the `productPricingErrors` line add:

```csharp
        var marketingPerformanceErrors = errorCodes.Where(code => code >= 3700 && code < 3800).ToList(); // 37XX range (Marketing Performance)
```

add to the assertions block:

```csharp
        Assert.True(marketingPerformanceErrors.Count > 0, "Should have Marketing Performance errors in 37XX range");
```

and add `+ marketingPerformanceErrors.Count` to the `categorizedCount` sum (before `+ externalServiceErrors.Count`).

- [ ] **Step 2: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ErrorHandlingTests"` → FAIL "Should have Marketing Performance errors in 37XX range".

- [ ] **Step 3: Add the codes**

In `ErrorCodes.cs` before `// External Service errors (90XX)`:

```csharp
    // Marketing Performance module errors (37XX)
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MarketingPerformanceInvalidMonthRange = 3701,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    MarketingPerformanceRangeTooLarge = 3702,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    MarketingPerformanceRecomputeAlreadyRunning = 3703,
    [HttpStatusCode(HttpStatusCode.InternalServerError)]
    MarketingPerformanceEnqueueFailed = 3704,
```

- [ ] **Step 4: Add Czech messages**

In `frontend/src/i18n.ts` after `MarketingCalendarSyncFailed: ...,`:

```ts
        // Marketing Performance module errors
        MarketingPerformanceInvalidMonthRange: "Neplatné období: zadejte měsíce ve formátu RRRR-MM, počáteční měsíc nesmí být po koncovém a období nesmí být v budoucnosti.",
        MarketingPerformanceRangeTooLarge: "Období pro přepočet je příliš dlouhé (maximum {{maxMonths}} měsíců).",
        MarketingPerformanceRecomputeAlreadyRunning: "Přepočet výkonu reklamy už běží. Počkejte na jeho dokončení.",
        MarketingPerformanceEnqueueFailed: "Přepočet se nepodařilo zařadit do fronty. Zkuste to prosím znovu.",
```

(Check how existing entries interpolate `Params` — if the pattern is `{{paramName}}`, keep `maxMonths`; otherwise match the existing convention.)

- [ ] **Step 5: Run** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ErrorHandlingTests"` → PASS; `cd frontend && npm run lint` → clean.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs frontend/src/i18n.ts
git commit -m "feat: marketing performance error codes (37XX) with Czech translations"
```

---

## Task 12: Read and recompute handlers

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Contracts/MarketingYearSeriesDto.cs`
- Create: `.../UseCases/GetMarketingPerformanceMonths/GetMarketingPerformanceMonthsRequest.cs`, `...Response.cs`, `...Handler.cs`
- Create: `.../UseCases/GetMarketingPerformanceComparison/GetMarketingPerformanceComparisonRequest.cs`, `...Response.cs`, `...Handler.cs`
- Create: `.../UseCases/RecomputeMarketingPerformance/RecomputeMarketingPerformanceRequest.cs`, `...Response.cs`, `...Handler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/GetMarketingPerformanceMonthsHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/GetMarketingPerformanceComparisonHandlerTests.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingPerformance/RecomputeMarketingPerformanceHandlerTests.cs`

**Interfaces:**
- Consumes: `IMarketingPerformanceRepository`, `MarketingMetricsCalculator`, `MarketingPerformanceOptions`, `MarketingPerformanceRunGuard`, `IMarketingPerformanceRecomputeEnqueuer`, `TimeProvider`, `ErrorCodes` 37XX.
- Produces (all classes):

```csharp
// GET months
public class GetMarketingPerformanceMonthsRequest : IRequest<GetMarketingPerformanceMonthsResponse>
{ public string? From; public string? To; public bool IncludeWholesale = false; }   // "yyyy-MM"; default last 36 months ending current month; max 60
public class GetMarketingPerformanceMonthsResponse : BaseResponse
{ List<MonthlyMarketingPerformanceDto> Months; List<ChannelInfoDto> Channels; string From; string To; bool IncludeWholesale; DateTime? LastRefreshAt; decimal VatRate; }

// GET comparison
public class GetMarketingPerformanceComparisonRequest : IRequest<GetMarketingPerformanceComparisonResponse>
{ public int Years = 3; public bool IncludeWholesale = false; }                        // clamped 2..3
public class MarketingYearSeriesDto { int Year; List<MonthlyMarketingPerformanceDto> Months; int YtdOrders; decimal YtdRevenueWithoutVat; decimal YtdTotalCost; decimal? YtdPno; }
public class GetMarketingPerformanceComparisonResponse : BaseResponse
{ List<MarketingYearSeriesDto> Series; int AnchorYear; int CurrentMonth; List<ChannelInfoDto> Channels; bool IncludeWholesale; DateTime? LastRefreshAt; }

// POST recompute
public class RecomputeMarketingPerformanceRequest : IRequest<RecomputeMarketingPerformanceResponse> { public string From; public string To; }
public class RecomputeMarketingPerformanceResponse : BaseResponse { public string? JobId; public int MonthCount; }
```

- [ ] **Step 1: Write the failing months-handler tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class GetMarketingPerformanceMonthsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private readonly Mock<IMarketingPerformanceRepository> _repo = new();

    private static IOptions<MarketingPerformanceOptions> Options() => Microsoft.Extensions.Options.Options.Create(new MarketingPerformanceOptions
    {
        VatRate = 1.21m,
        Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } } },
    });

    private GetMarketingPerformanceMonthsHandler Handler() =>
        new(_repo.Object, Options(), new FakeTimeProvider(Now), NullLogger<GetMarketingPerformanceMonthsHandler>.Instance);

    private static MarketingPerformanceMonth Row(int y, int m, int orders, decimal revenue, decimal cost) => new()
    {
        Year = y, Month = m, RetailOrderCount = orders, RetailRevenueWithVat = revenue,
        WholesaleOrderCount = 5, WholesaleRevenueWithVat = 1210m,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = cost, InvoiceCount = 1 } },
    };

    [Fact]
    public async Task Handle_FillsMissingMonthsAndComputesYoyFromPriorYear()
    {
        _repo.Setup(r => r.GetRangeAsync(new YearMonth(2025, 7), new YearMonth(2026, 8), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth> { Row(2025, 7, 100, 121000m, 1000m), Row(2026, 7, 150, 242000m, 1500m) });
        _repo.Setup(r => r.GetLastComputedAtAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DateTime(2026, 9, 18, 3, 0, 0));

        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest { From = "2026-07", To = "2026-08" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Months.Select(m => (m.Year, m.Month)).Should().Equal((2026, 7), (2026, 8));
        response.Months[0].HasData.Should().BeTrue();
        response.Months[0].YoyOrdersPercent.Should().Be(150m);
        response.Months[0].YoyRevenuePercent.Should().Be(200m);
        response.Months[0].YoyCostPercent.Should().Be(150m);
        response.Months[1].HasData.Should().BeFalse();
        response.Months[1].IsPartial.Should().BeFalse();
        response.Channels.Should().ContainSingle(c => c.Code == "meta" && c.Label == "FB/IG");
        response.LastRefreshAt.Should().Be(new DateTime(2026, 9, 18, 3, 0, 0));
        response.VatRate.Should().Be(1.21m);
    }

    [Fact]
    public async Task Handle_DefaultRange_IsLast36MonthsEndingNow_CurrentMonthPartial()
    {
        _repo.Setup(r => r.GetRangeAsync(new YearMonth(2022, 10), new YearMonth(2026, 9), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth>());

        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest(), CancellationToken.None);

        response.From.Should().Be("2023-10");
        response.To.Should().Be("2026-09");
        response.Months.Should().HaveCount(36);
        response.Months.Last().IsPartial.Should().BeTrue();
        response.Months.First().IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_IncludeWholesale_AddsWholesale()
    {
        _repo.Setup(r => r.GetRangeAsync(It.IsAny<YearMonth>(), It.IsAny<YearMonth>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth> { Row(2026, 7, 150, 242000m, 1500m) });

        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest { From = "2026-07", To = "2026-07", IncludeWholesale = true }, CancellationToken.None);

        response.Months[0].Orders.Should().Be(155);
        response.Months[0].RevenueWithVat.Should().Be(243210m);
        response.IncludeWholesale.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-13", "2026-09", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("2026-09", "2026-01", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("2020-01", "2026-09", ErrorCodes.MarketingPerformanceRangeTooLarge)]
    public async Task Handle_BadRange_ReturnsErrorCode(string from, string to, ErrorCodes expected)
    {
        var response = await Handler().Handle(new GetMarketingPerformanceMonthsRequest { From = from, To = to }, CancellationToken.None);
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Write the failing comparison-handler tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class GetMarketingPerformanceComparisonHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private readonly Mock<IMarketingPerformanceRepository> _repo = new();

    private GetMarketingPerformanceComparisonHandler Handler() => new(
        _repo.Object,
        Microsoft.Extensions.Options.Options.Create(new MarketingPerformanceOptions
        {
            Channels = { new MarketingChannelOptions { Code = "meta", Label = "FB/IG", VatIds = { "IE1" } } },
        }),
        new FakeTimeProvider(Now), NullLogger<GetMarketingPerformanceComparisonHandler>.Instance);

    private static MarketingPerformanceMonth Row(int y, int m, int orders, decimal revenue, decimal cost) => new()
    {
        Year = y, Month = m, RetailOrderCount = orders, RetailRevenueWithVat = revenue,
        ChannelCosts = { new MarketingPerformanceChannelCost { ChannelCode = "meta", CostWithoutVat = cost } },
    };

    [Fact]
    public async Task Handle_ThreeYears_OneSeriesPerYearNewestFirst_TwelveCellsEach_WithYtd()
    {
        _repo.Setup(r => r.GetRangeAsync(new YearMonth(2023, 1), new YearMonth(2026, 12), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth>
             {
                 Row(2024, 6, 10, 1210m, 100m), Row(2025, 6, 20, 2420m, 200m), Row(2026, 6, 30, 3630m, 300m), Row(2026, 9, 5, 605m, 50m),
             });

        var response = await Handler().Handle(new GetMarketingPerformanceComparisonRequest { Years = 3 }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.AnchorYear.Should().Be(2026);
        response.CurrentMonth.Should().Be(9);
        response.Series.Select(s => s.Year).Should().Equal(2026, 2025, 2024);
        response.Series.Should().OnlyContain(s => s.Months.Count == 12);
        var s2026 = response.Series[0];
        s2026.Months[5].HasData.Should().BeTrue();
        s2026.Months[5].YoyOrdersPercent.Should().Be(150m);      // 30 vs 20 in June 2025
        s2026.Months[8].IsPartial.Should().BeTrue();             // September 2026
        s2026.Months[9].HasData.Should().BeFalse();              // October 2026 not yet
        s2026.YtdOrders.Should().Be(35);
        s2026.YtdRevenueWithoutVat.Should().Be(3500m);
        s2026.YtdTotalCost.Should().Be(350m);
        s2026.YtdPno.Should().Be(10m);
        response.Series[2].Months[5].YoyOrdersPercent.Should().BeNull("2023 was not loaded for the oldest series' YoY");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 3)]
    public async Task Handle_ClampsYearsTo2To3(int requested, int expected)
    {
        _repo.Setup(r => r.GetRangeAsync(It.IsAny<YearMonth>(), It.IsAny<YearMonth>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<MarketingPerformanceMonth>());
        var response = await Handler().Handle(new GetMarketingPerformanceComparisonRequest { Years = requested }, CancellationToken.None);
        response.Series.Should().HaveCount(expected);
    }
}
```

- [ ] **Step 3: Write the failing recompute-handler tests**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingPerformance;

public class RecomputeMarketingPerformanceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 10, 0, 0, TimeSpan.FromHours(2));
    private readonly Mock<IMarketingPerformanceRecomputeEnqueuer> _enqueuer = new();
    private readonly MarketingPerformanceRunGuard _guard = new();

    private RecomputeMarketingPerformanceHandler Handler(int maxRange = 60) => new(
        _enqueuer.Object, _guard,
        Microsoft.Extensions.Options.Options.Create(new MarketingPerformanceOptions { MaxRecomputeRangeMonths = maxRange }),
        new FakeTimeProvider(Now), NullLogger<RecomputeMarketingPerformanceHandler>.Instance);

    [Fact]
    public async Task Handle_ValidRange_EnqueuesAndReturnsJobId()
    {
        _enqueuer.Setup(e => e.Enqueue(new YearMonth(2023, 1), new YearMonth(2024, 12))).Returns("hf-42");

        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2023-01", To = "2024-12" }, CancellationToken.None);

        response.Success.Should().BeTrue();
        response.JobId.Should().Be("hf-42");
        response.MonthCount.Should().Be(24);
    }

    [Theory]
    [InlineData("2023-01", "2022-12", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("nope", "2024-12", ErrorCodes.MarketingPerformanceInvalidMonthRange)]
    [InlineData("2026-01", "2026-10", ErrorCodes.MarketingPerformanceInvalidMonthRange)] // future month
    [InlineData("2020-01", "2026-09", ErrorCodes.MarketingPerformanceRangeTooLarge)]
    public async Task Handle_InvalidRange_ReturnsErrorWithoutEnqueueing(string from, string to, ErrorCodes expected)
    {
        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = from, To = to }, CancellationToken.None);
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(expected);
        _enqueuer.Verify(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RangeTooLarge_ReportsMaxInParams()
    {
        var response = await Handler(maxRange: 12).Handle(new RecomputeMarketingPerformanceRequest { From = "2025-01", To = "2026-09" }, CancellationToken.None);
        response.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceRangeTooLarge);
        response.Params.Should().ContainKey("maxMonths").WhoseValue.Should().Be("12");
    }

    [Fact]
    public async Task Handle_WhileRunning_ReturnsAlreadyRunning()
    {
        _guard.TryBegin();
        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2026-01", To = "2026-02" }, CancellationToken.None);
        response.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceRecomputeAlreadyRunning);
        _guard.End();
    }

    [Fact]
    public async Task Handle_EnqueueReturnsNull_ReturnsEnqueueFailed()
    {
        _enqueuer.Setup(e => e.Enqueue(It.IsAny<YearMonth>(), It.IsAny<YearMonth>())).Returns((string?)null);
        var response = await Handler().Handle(new RecomputeMarketingPerformanceRequest { From = "2026-01", To = "2026-02" }, CancellationToken.None);
        response.ErrorCode.Should().Be(ErrorCodes.MarketingPerformanceEnqueueFailed);
    }
}
```

- [ ] **Step 4: Run to verify failure** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformance.*HandlerTests"` → compile errors.

- [ ] **Step 5: Implement shared range parsing**

Create `backend/src/Anela.Heblo.Application/Features/MarketingPerformance/Services/MonthRangeParser.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Services;

public class MonthRangeParseResult
{
    public YearMonth From { get; init; }
    public YearMonth To { get; init; }
    public ErrorCodes? Error { get; init; }
    public Dictionary<string, string>? Params { get; init; }
    public bool IsValid => Error is null;
    public int MonthCount => YearMonth.MonthsBetween(From, To);
}

public static class MonthRangeParser
{
    /// <summary>Parses "yyyy-MM" bounds; rejects from &gt; to, any month after <paramref name="currentMonth"/>, and ranges longer than <paramref name="maxMonths"/>.</summary>
    public static MonthRangeParseResult Parse(string? from, string? to, YearMonth currentMonth, int maxMonths)
    {
        if (!YearMonth.TryParse(from, out var f) || !YearMonth.TryParse(to, out var t) || f > t || t > currentMonth)
        {
            return new MonthRangeParseResult { Error = ErrorCodes.MarketingPerformanceInvalidMonthRange, Params = new() { { "from", from ?? "" }, { "to", to ?? "" } } };
        }
        if (YearMonth.MonthsBetween(f, t) > maxMonths)
        {
            return new MonthRangeParseResult { Error = ErrorCodes.MarketingPerformanceRangeTooLarge, Params = new() { { "maxMonths", maxMonths.ToString() } } };
        }
        return new MonthRangeParseResult { From = f, To = t };
    }
}
```

- [ ] **Step 6: Implement GET months**

Request/Response:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;

public class GetMarketingPerformanceMonthsRequest : IRequest<GetMarketingPerformanceMonthsResponse>
{
    /// <summary>"yyyy-MM"; default = 35 months before the current month.</summary>
    public string? From { get; set; }
    /// <summary>"yyyy-MM"; default = current month.</summary>
    public string? To { get; set; }
    /// <summary>Add wholesale (customer with VAT ID) orders and revenue to the retail figures.</summary>
    public bool IncludeWholesale { get; set; }
}
```

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;

public class GetMarketingPerformanceMonthsResponse : BaseResponse
{
    [Required] public List<MonthlyMarketingPerformanceDto> Months { get; set; } = new();
    [Required] public List<ChannelInfoDto> Channels { get; set; } = new();
    [Required] public string From { get; set; } = string.Empty;
    [Required] public string To { get; set; } = string.Empty;
    [Required] public bool IncludeWholesale { get; set; }
    [Required] public decimal VatRate { get; set; }
    public DateTime? LastRefreshAt { get; set; }

    public GetMarketingPerformanceMonthsResponse() { }
    public GetMarketingPerformanceMonthsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

Handler:

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;

public class GetMarketingPerformanceMonthsHandler : IRequestHandler<GetMarketingPerformanceMonthsRequest, GetMarketingPerformanceMonthsResponse>
{
    private const int DefaultMonths = 36;
    private const int MaxMonths = 60;

    private readonly IMarketingPerformanceRepository _repository;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetMarketingPerformanceMonthsHandler> _logger;

    public GetMarketingPerformanceMonthsHandler(
        IMarketingPerformanceRepository repository,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<GetMarketingPerformanceMonthsHandler> logger)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GetMarketingPerformanceMonthsResponse> Handle(GetMarketingPerformanceMonthsRequest request, CancellationToken cancellationToken)
    {
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var from = request.From ?? current.AddMonths(-(DefaultMonths - 1)).ToString();
        var to = request.To ?? current.ToString();

        var range = MonthRangeParser.Parse(from, to, current, MaxMonths);
        if (!range.IsValid)
        {
            return new GetMarketingPerformanceMonthsResponse(range.Error!.Value, range.Params);
        }

        var channels = _options.ToDefinitions();
        var calculator = new MarketingMetricsCalculator(_options.VatRate, channels);

        // Load one extra year back so YoY ratios have their prior-year cell.
        var rows = await _repository.GetRangeAsync(range.From.AddMonths(-12), range.To, cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key);

        var months = YearMonth.Range(range.From, range.To).Select(ym =>
        {
            var isPartial = ym == current;
            if (!byKey.TryGetValue(ym, out var row))
            {
                return calculator.Empty(ym, isPartial);
            }
            byKey.TryGetValue(ym.AddMonths(-12), out var lastYear);
            return calculator.Build(row, lastYear, request.IncludeWholesale, isPartial);
        }).ToList();

        _logger.LogDebug("Marketing performance months {From}..{To}: {Count} rows, {WithData} with data", range.From, range.To, months.Count, months.Count(m => m.HasData));

        return new GetMarketingPerformanceMonthsResponse
        {
            Months = months,
            Channels = channels.Select(c => new ChannelInfoDto { Code = c.Code, Label = c.Label }).ToList(),
            From = range.From.ToString(),
            To = range.To.ToString(),
            IncludeWholesale = request.IncludeWholesale,
            VatRate = _options.VatRate,
            LastRefreshAt = await _repository.GetLastComputedAtAsync(cancellationToken),
        };
    }
}
```

- [ ] **Step 7: Implement GET comparison**

`MarketingYearSeriesDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.MarketingPerformance.Contracts;

public class MarketingYearSeriesDto
{
    [Required] public int Year { get; set; }
    /// <summary>Always 12 cells, January to December; months without data have HasData = false.</summary>
    [Required] public List<MonthlyMarketingPerformanceDto> Months { get; set; } = new();
    [Required] public int YtdOrders { get; set; }
    [Required] public decimal YtdRevenueWithoutVat { get; set; }
    [Required] public decimal YtdTotalCost { get; set; }
    public decimal? YtdPno { get; set; }
}
```

Request/Response:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;

public class GetMarketingPerformanceComparisonRequest : IRequest<GetMarketingPerformanceComparisonResponse>
{
    /// <summary>Number of calendar years ending with the current one. Clamped to 2..3.</summary>
    public int Years { get; set; } = 3;
    public bool IncludeWholesale { get; set; }
}
```

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;

public class GetMarketingPerformanceComparisonResponse : BaseResponse
{
    /// <summary>Newest year first.</summary>
    [Required] public List<MarketingYearSeriesDto> Series { get; set; } = new();
    [Required] public int AnchorYear { get; set; }
    /// <summary>Current calendar month 1..12 (partial).</summary>
    [Required] public int CurrentMonth { get; set; }
    [Required] public List<ChannelInfoDto> Channels { get; set; } = new();
    [Required] public bool IncludeWholesale { get; set; }
    public DateTime? LastRefreshAt { get; set; }
}
```

Handler:

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Contracts;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;

public class GetMarketingPerformanceComparisonHandler : IRequestHandler<GetMarketingPerformanceComparisonRequest, GetMarketingPerformanceComparisonResponse>
{
    private const int MinYears = 2;
    private const int MaxYears = 3;

    private readonly IMarketingPerformanceRepository _repository;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetMarketingPerformanceComparisonHandler> _logger;

    public GetMarketingPerformanceComparisonHandler(
        IMarketingPerformanceRepository repository,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<GetMarketingPerformanceComparisonHandler> logger)
    {
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GetMarketingPerformanceComparisonResponse> Handle(GetMarketingPerformanceComparisonRequest request, CancellationToken cancellationToken)
    {
        var years = Math.Clamp(request.Years, MinYears, MaxYears);
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var anchorYear = current.Year;
        var oldestYear = anchorYear - years + 1;

        var channels = _options.ToDefinitions();
        var calculator = new MarketingMetricsCalculator(_options.VatRate, channels);

        // One extra year back for the oldest series' YoY is intentionally NOT loaded (spec: oldest series has null YoY).
        var rows = await _repository.GetRangeAsync(new YearMonth(oldestYear, 1), new YearMonth(anchorYear, 12), cancellationToken);
        var byKey = rows.ToDictionary(r => r.Key);

        var series = Enumerable.Range(0, years)
            .Select(i => anchorYear - i)
            .Select(year =>
            {
                var months = Enumerable.Range(1, 12).Select(m =>
                {
                    var ym = new YearMonth(year, m);
                    if (!byKey.TryGetValue(ym, out var row))
                    {
                        return calculator.Empty(ym, ym == current);
                    }
                    byKey.TryGetValue(ym.AddMonths(-12), out var lastYear);
                    return calculator.Build(row, lastYear, request.IncludeWholesale, ym == current);
                }).ToList();

                var ytd = months.Where(m => m.HasData && (year < anchorYear ? m.Month <= current.Month : true)).ToList();
                var ytdRevenue = ytd.Sum(m => m.RevenueWithoutVat);
                var ytdCost = ytd.Sum(m => m.TotalCost);
                return new MarketingYearSeriesDto
                {
                    Year = year,
                    Months = months,
                    YtdOrders = ytd.Sum(m => m.Orders),
                    YtdRevenueWithoutVat = ytdRevenue,
                    YtdTotalCost = ytdCost,
                    YtdPno = MarketingMetricsCalculator.Ratio(ytdCost * 100m, ytdRevenue),
                };
            })
            .ToList();

        _logger.LogDebug("Marketing performance comparison: {Years} years anchored at {Anchor}", years, anchorYear);

        return new GetMarketingPerformanceComparisonResponse
        {
            Series = series,
            AnchorYear = anchorYear,
            CurrentMonth = current.Month,
            Channels = channels.Select(c => new ChannelInfoDto { Code = c.Code, Label = c.Label }).ToList(),
            IncludeWholesale = request.IncludeWholesale,
            LastRefreshAt = await _repository.GetLastComputedAtAsync(cancellationToken),
        };
    }
}
```

YTD for prior years is cut at the current month so the cards compare like with like (Jan–Sep 2025 vs Jan–Sep 2026).

- [ ] **Step 8: Implement POST recompute**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;

public class RecomputeMarketingPerformanceRequest : IRequest<RecomputeMarketingPerformanceResponse>
{
    /// <summary>"yyyy-MM"</summary>
    public string From { get; set; } = string.Empty;
    /// <summary>"yyyy-MM"</summary>
    public string To { get; set; } = string.Empty;
}
```

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;

public class RecomputeMarketingPerformanceResponse : BaseResponse
{
    public string? JobId { get; set; }
    public int MonthCount { get; set; }

    public RecomputeMarketingPerformanceResponse() { }
    public RecomputeMarketingPerformanceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
```

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.Configuration;
using Anela.Heblo.Application.Features.MarketingPerformance.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingPerformance;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;

public class RecomputeMarketingPerformanceHandler : IRequestHandler<RecomputeMarketingPerformanceRequest, RecomputeMarketingPerformanceResponse>
{
    private readonly IMarketingPerformanceRecomputeEnqueuer _enqueuer;
    private readonly MarketingPerformanceRunGuard _guard;
    private readonly MarketingPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecomputeMarketingPerformanceHandler> _logger;

    public RecomputeMarketingPerformanceHandler(
        IMarketingPerformanceRecomputeEnqueuer enqueuer,
        MarketingPerformanceRunGuard guard,
        IOptions<MarketingPerformanceOptions> options,
        TimeProvider timeProvider,
        ILogger<RecomputeMarketingPerformanceHandler> logger)
    {
        _enqueuer = enqueuer;
        _guard = guard;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<RecomputeMarketingPerformanceResponse> Handle(RecomputeMarketingPerformanceRequest request, CancellationToken cancellationToken)
    {
        var current = YearMonth.From(_timeProvider.GetLocalNow().DateTime);
        var range = MonthRangeParser.Parse(request.From, request.To, current, _options.MaxRecomputeRangeMonths);
        if (!range.IsValid)
        {
            return Task.FromResult(new RecomputeMarketingPerformanceResponse(range.Error!.Value, range.Params));
        }

        if (_guard.IsRunning)
        {
            _logger.LogWarning("Marketing performance recompute {From}..{To} rejected: a run is in progress", range.From, range.To);
            return Task.FromResult(new RecomputeMarketingPerformanceResponse(ErrorCodes.MarketingPerformanceRecomputeAlreadyRunning));
        }

        var jobId = _enqueuer.Enqueue(range.From, range.To);
        if (jobId is null)
        {
            return Task.FromResult(new RecomputeMarketingPerformanceResponse(ErrorCodes.MarketingPerformanceEnqueueFailed));
        }

        _logger.LogInformation("Marketing performance recompute {From}..{To} enqueued as Hangfire job {JobId}", range.From, range.To, jobId);
        return Task.FromResult(new RecomputeMarketingPerformanceResponse { JobId = jobId, MonthCount = range.MonthCount });
    }
}
```

- [ ] **Step 9: Run tests** — `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingPerformance"` → all PASS (also the `BaseResponse` reflection contract test in `ErrorHandlingTests`).

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingPerformance backend/test/Anela.Heblo.Tests/Features/MarketingPerformance
git commit -m "feat: marketing performance handlers — months, year comparison, recompute"
```

---

## Task 13: Controller, permission, access matrix

**Files:**
- Modify: `access-matrix.json` (features, menuPaths, seedGroups)
- Regenerate: `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`, `AccessMatrix.generated.cs`, `AccessRoles.generated.cs`, `frontend/src/auth/accessMatrix.generated.ts`, `access-matrix-entra.generated.json`
- Create: `backend/src/Anela.Heblo.API/Controllers/MarketingPerformanceController.cs`

**Interfaces:**
- Produces: `Feature.Marketing_Performance`; roles `marketing.performance.read` / `marketing.performance.write`; routes `GET /api/marketingperformance/months`, `GET /api/marketingperformance/comparison`, `POST /api/marketingperformance/recompute` (route token `[controller]` → `marketingperformance`; the generated client method names become `marketingPerformance_GetMonths`, `marketingPerformance_GetComparison`, `marketingPerformance_Recompute`).

- [ ] **Step 1: Access matrix**

In `access-matrix.json`:
- `features`: after the `Marketing_MarketingCalendar` entry add `{ "key": "Marketing_Performance", "label": "Výkon reklamy", "hasWrite": true }`.
- `menuPaths`: add `{ "path": "/marketing/performance", "requires": [{ "feature": "Marketing_Performance", "level": "Read" }] }`.
- `seedGroups`: append `"marketing.performance.read"` to `Vedeni` roles and `"marketing.performance.read", "marketing.performance.write"` to `Marketer` roles.

Regenerate:

```bash
dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen -- access-matrix.json \
  backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs \
  backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs \
  frontend/src/auth/accessMatrix.generated.ts \
  access-matrix-entra.generated.json
```

Confirm `Feature.Marketing_Performance` exists in `Feature.generated.cs` and `/marketing/performance` in `accessMatrix.generated.ts`.

- [ ] **Step 2: Controller**

```csharp
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceComparison;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.GetMarketingPerformanceMonths;
using Anela.Heblo.Application.Features.MarketingPerformance.UseCases.RecomputeMarketingPerformance;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>Marketing → Výkon reklamy: monthly ad-spend vs. revenue snapshot.</summary>
[FeatureAuthorize(Feature.Marketing_Performance)]
[ApiController]
[Route("api/[controller]")]
public class MarketingPerformanceController : BaseApiController
{
    private readonly IMediator _mediator;

    public MarketingPerformanceController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Monthly rows with derived metrics for a yyyy-MM range (default last 36 months).</summary>
    [HttpGet("months")]
    [ProducesResponseType(typeof(GetMarketingPerformanceMonthsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GetMarketingPerformanceMonthsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetMarketingPerformanceMonthsResponse>> GetMonths(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] bool includeWholesale = false)
    {
        var response = await _mediator.Send(new GetMarketingPerformanceMonthsRequest { From = from, To = to, IncludeWholesale = includeWholesale });
        return HandleResponse(response);
    }

    /// <summary>Year-over-year series (2–3 calendar years, 12 cells each, YTD totals).</summary>
    [HttpGet("comparison")]
    [ProducesResponseType(typeof(GetMarketingPerformanceComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<GetMarketingPerformanceComparisonResponse>> GetComparison(
        [FromQuery] int years = 3,
        [FromQuery] bool includeWholesale = false)
    {
        var response = await _mediator.Send(new GetMarketingPerformanceComparisonRequest { Years = years, IncludeWholesale = includeWholesale });
        return HandleResponse(response);
    }

    /// <summary>Enqueues a recompute of every month in [from, to] regardless of locks. Returns the Hangfire job id.</summary>
    [HttpPost("recompute")]
    [FeatureAuthorize(Feature.Marketing_Performance, AccessLevel.Write)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RecomputeMarketingPerformanceResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RecomputeMarketingPerformanceResponse>> Recompute([FromBody] RecomputeMarketingPerformanceRequest request)
    {
        var response = await _mediator.Send(request);
        if (response.Success)
        {
            return Accepted(response);
        }
        return HandleResponse(response);
    }
}
```

- [ ] **Step 3: Build, smoke the OpenAPI, regenerate the TS client**

```bash
dotnet build backend/src/Anela.Heblo.API
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual
grep -n "marketingPerformance_GetMonths\|marketingPerformance_GetComparison\|marketingPerformance_Recompute" frontend/src/api/generated/api-client.ts
```

Expected: three methods present; `GetMarketingPerformanceMonthsResponse`, `MonthlyMarketingPerformanceDto`, `MarketingYearSeriesDto`, `ChannelInfoDto`, `ChannelCostDto`, `RecomputeMarketingPerformanceRequest` types exported. If the method names differ (NSwag uses `operationId` = `{Controller}_{Action}`), use the names exactly as generated in Task 14.

- [ ] **Step 4: Run the authorization/controller contract tests**

`dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Authorization|FullyQualifiedName~Controller"` → PASS (any test enumerating controllers vs. `access-matrix.json` must see the new feature).

- [ ] **Step 5: Commit**

```bash
git add access-matrix.json access-matrix-entra.generated.json backend/src/Anela.Heblo.Domain/Features/Authorization frontend/src/auth/accessMatrix.generated.ts backend/src/Anela.Heblo.API/Controllers/MarketingPerformanceController.cs frontend/src/api/generated/api-client.ts
git commit -m "feat: marketing performance API controller and Marketing_Performance permission"
```

---

## Task 14: Frontend API hooks

**Files:**
- Modify: `frontend/src/api/client.ts:478-530` (QUERY_KEYS)
- Create: `frontend/src/api/hooks/useMarketingPerformance.ts`
- Test: `frontend/src/api/hooks/__tests__/useMarketingPerformance.test.tsx`

**Interfaces:**
- Consumes: generated client methods from Task 13.
- Produces:

```ts
export const useMarketingPerformanceMonthsQuery = (params: { from?: string; to?: string; includeWholesale: boolean }, enabled = true)
export const useMarketingPerformanceComparisonQuery = (params: { years: number; includeWholesale: boolean }, enabled = true)
export const useRecomputeMarketingPerformanceMutation = ()   // mutate({ from, to })
export type { GetMarketingPerformanceMonthsResponse, GetMarketingPerformanceComparisonResponse, MonthlyMarketingPerformanceDto, MarketingYearSeriesDto, ChannelInfoDto, ChannelCostDto, RecomputeMarketingPerformanceResponse }
```

- [ ] **Step 1: Write the failing hook test**

`frontend/src/api/hooks/__tests__/useMarketingPerformance.test.tsx`:

```tsx
import React from 'react'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useMarketingPerformanceMonthsQuery, useRecomputeMarketingPerformanceMutation } from '../useMarketingPerformance'

const getMonths = jest.fn()
const recompute = jest.fn()
jest.mock('../../client', () => ({
  getAuthenticatedApiClient: () => ({
    marketingPerformance_GetMonths: (...args: unknown[]) => getMonths(...args),
    marketingPerformance_Recompute: (...args: unknown[]) => recompute(...args),
  }),
  QUERY_KEYS: { marketingPerformanceMonths: ['marketing-performance', 'months'], marketingPerformanceComparison: ['marketing-performance', 'comparison'] },
}))

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>{children}</QueryClientProvider>
)

describe('useMarketingPerformance hooks', () => {
  beforeEach(() => jest.clearAllMocks())

  it('passes from/to/includeWholesale to the generated client in order', async () => {
    getMonths.mockResolvedValue({ success: true, months: [], channels: [] })

    const { result } = renderHook(() => useMarketingPerformanceMonthsQuery({ from: '2026-01', to: '2026-09', includeWholesale: true }), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(getMonths).toHaveBeenCalledWith('2026-01', '2026-09', true)
  })

  it('recompute mutation posts the range body', async () => {
    recompute.mockResolvedValue({ success: true, jobId: 'hf-1', monthCount: 2 })

    const { result } = renderHook(() => useRecomputeMarketingPerformanceMutation(), { wrapper })
    result.current.mutate({ from: '2026-01', to: '2026-02' })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(recompute).toHaveBeenCalledWith(expect.objectContaining({ from: '2026-01', to: '2026-02' }))
  })
})
```

- [ ] **Step 2: Run to verify failure** — `cd frontend && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useMarketingPerformance.test.tsx` → module not found.

- [ ] **Step 3: QUERY_KEYS + hooks**

In `client.ts` `QUERY_KEYS` add:

```ts
  marketingPerformanceMonths: ["marketing-performance", "months"] as const,
  marketingPerformanceComparison: ["marketing-performance", "comparison"] as const,
```

`useMarketingPerformance.ts`:

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAuthenticatedApiClient, QUERY_KEYS } from "../client";
import {
  ChannelCostDto,
  ChannelInfoDto,
  GetMarketingPerformanceComparisonResponse,
  GetMarketingPerformanceMonthsResponse,
  MarketingYearSeriesDto,
  MonthlyMarketingPerformanceDto,
  RecomputeMarketingPerformanceRequest,
  RecomputeMarketingPerformanceResponse,
} from "../generated/api-client";

export type {
  ChannelCostDto,
  ChannelInfoDto,
  GetMarketingPerformanceComparisonResponse,
  GetMarketingPerformanceMonthsResponse,
  MarketingYearSeriesDto,
  MonthlyMarketingPerformanceDto,
  RecomputeMarketingPerformanceResponse,
};

const STALE_TIME_MS = 5 * 60 * 1000;
const GC_TIME_MS = 10 * 60 * 1000;

export interface MonthsQueryParams {
  from?: string;
  to?: string;
  includeWholesale: boolean;
}

export interface ComparisonQueryParams {
  years: number;
  includeWholesale: boolean;
}

export const useMarketingPerformanceMonthsQuery = (params: MonthsQueryParams, enabled = true) =>
  useQuery<GetMarketingPerformanceMonthsResponse, Error>({
    queryKey: [...QUERY_KEYS.marketingPerformanceMonths, params.from ?? null, params.to ?? null, params.includeWholesale],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.marketingPerformance_GetMonths(params.from, params.to, params.includeWholesale);
    },
    enabled,
    staleTime: STALE_TIME_MS,
    gcTime: GC_TIME_MS,
  });

export const useMarketingPerformanceComparisonQuery = (params: ComparisonQueryParams, enabled = true) =>
  useQuery<GetMarketingPerformanceComparisonResponse, Error>({
    queryKey: [...QUERY_KEYS.marketingPerformanceComparison, params.years, params.includeWholesale],
    queryFn: async () => {
      const apiClient = getAuthenticatedApiClient();
      return await apiClient.marketingPerformance_GetComparison(params.years, params.includeWholesale);
    },
    enabled,
    staleTime: STALE_TIME_MS,
    gcTime: GC_TIME_MS,
  });

export const useRecomputeMarketingPerformanceMutation = () => {
  const queryClient = useQueryClient();
  return useMutation<RecomputeMarketingPerformanceResponse, Error, { from: string; to: string }>({
    mutationFn: async ({ from, to }) => {
      const apiClient = getAuthenticatedApiClient();
      const body = new RecomputeMarketingPerformanceRequest({ from, to });
      return await apiClient.marketingPerformance_Recompute(body);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.marketingPerformanceMonths });
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.marketingPerformanceComparison });
    },
  });
};
```

If the generated request type is a plain interface rather than a class with a constructor, pass `{ from, to } as RecomputeMarketingPerformanceRequest` instead of `new ...`.

- [ ] **Step 4: Run** — same test command → 2 PASS. `npm run lint` clean.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/api/hooks/useMarketingPerformance.ts frontend/src/api/hooks/__tests__/useMarketingPerformance.test.tsx
git commit -m "feat: marketing performance react-query hooks"
```

---

## Task 15: Shared comparison colours and the metrics module

**Files:**
- Create: `frontend/src/components/charts/comparisonColors.ts`
- Modify: `frontend/src/components/pages/financial-overview/comparisonUtils.ts` (import `YEAR_ALPHAS`, `withYearAlpha` from the shared file; keep its exports so existing imports keep working)
- Create: `frontend/src/components/marketing/performance/metrics.ts`
- Test: `frontend/src/components/marketing/performance/__tests__/metrics.test.ts`
- Test: `frontend/src/components/charts/__tests__/comparisonColors.test.ts`

**Interfaces:**

```ts
// comparisonColors.ts
export const YEAR_ALPHAS = [1, 0.55, 0.3] as const
export type Rgb = [number, number, number]
export const withYearAlpha = (rgb: Rgb, yearIndex: number): string  // rgba(r, g, b, alpha)

// metrics.ts
export type PerformanceMetric = 'totalCost' | 'revenueWithoutVat' | 'orders' | 'pno' | 'roas' | 'avgOrderValue' | 'costPerOrder'
export const PERFORMANCE_METRICS: PerformanceMetric[]
export const METRIC_LABELS: Record<PerformanceMetric, string>
export const METRIC_UNITS: Record<PerformanceMetric, 'czk' | 'percent' | 'count'>
export const METRIC_COLORS: Record<PerformanceMetric, Rgb>
export const CHANNEL_COLORS: Rgb[]                     // cycled per channel index
export const MONTH_LABELS_SHORT: readonly string[]     // Led..Pro
export const getMetricValue = (row: MonthlyMarketingPerformanceDto, metric: PerformanceMetric): number | null
export const formatMetric = (value: number | null | undefined, unit: 'czk' | 'percent' | 'count'): string  // "—" for null
export const formatCzk = (v: number): string
export const formatPercent = (v: number): string      // "24,6 %"
export const formatYoy = (v: number | null | undefined): string  // "+12,3 %" / "−4,0 %" / "—"
```

- [ ] **Step 1: Write the failing tests**

`comparisonColors.test.ts`:

```ts
import { withYearAlpha, YEAR_ALPHAS } from '../comparisonColors'

describe('withYearAlpha', () => {
  it('uses full alpha for the anchor year and fades older years', () => {
    expect(withYearAlpha([34, 197, 94], 0)).toBe('rgba(34, 197, 94, 1)')
    expect(withYearAlpha([34, 197, 94], 1)).toBe(`rgba(34, 197, 94, ${YEAR_ALPHAS[1]})`)
  })
  it('clamps year index to the last alpha', () => {
    expect(withYearAlpha([1, 2, 3], 9)).toBe(`rgba(1, 2, 3, ${YEAR_ALPHAS[2]})`)
  })
})
```

`metrics.test.ts`:

```ts
import { formatCzk, formatMetric, formatPercent, formatYoy, getMetricValue, PERFORMANCE_METRICS } from '../metrics'
import type { MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

const row = {
  year: 2026, month: 1, monthYearDisplay: '01/2026', hasData: true, isLocked: true, isPartial: false,
  orders: 2354, revenueWithVat: 2586556, revenueWithoutVat: 2137649.59, channelCosts: [], totalCost: 525082,
  pno: 24.56, roas: 407.11, profit: 1612567.59, avgOrderValue: 908.09, costPerOrder: 223.06,
  yoyCostPercent: 89.92, yoyRevenuePercent: null, yoyOrdersPercent: undefined, skippedEurInvoiceCount: 0,
} as unknown as MonthlyMarketingPerformanceDto

describe('metrics', () => {
  it('lists the seven selectable metrics in table order', () => {
    expect(PERFORMANCE_METRICS).toEqual(['totalCost', 'revenueWithoutVat', 'orders', 'pno', 'roas', 'avgOrderValue', 'costPerOrder'])
  })

  it('reads each metric from the DTO and returns null for missing ratios', () => {
    expect(getMetricValue(row, 'totalCost')).toBe(525082)
    expect(getMetricValue(row, 'pno')).toBe(24.56)
    expect(getMetricValue({ ...row, pno: null } as MonthlyMarketingPerformanceDto, 'pno')).toBeNull()
  })

  it('formats CZK without decimals using Czech locale', () => {
    expect(formatCzk(2586556).replace(/ /g, ' ')).toBe('2 586 556 Kč')
  })

  it('formats percent with one decimal and a dash for null', () => {
    expect(formatPercent(24.56).replace(/ /g, ' ')).toBe('24,6 %')
    expect(formatMetric(null, 'percent')).toBe('—')
    expect(formatMetric(2354, 'count').replace(/ /g, ' ')).toBe('2 354')
  })

  it('formats YoY as signed delta from 100 %', () => {
    expect(formatYoy(112.34).replace(/ /g, ' ')).toBe('+12,3 %')
    expect(formatYoy(96).replace(/ /g, ' ')).toBe('−4,0 %')
    expect(formatYoy(null)).toBe('—')
  })
})
```

- [ ] **Step 2: Run to verify failure** — `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/performance src/components/charts/__tests__/comparisonColors.test.ts` → module not found.

- [ ] **Step 3: Implement `comparisonColors.ts` and refactor Financial Overview to use it**

```ts
export const YEAR_ALPHAS = [1, 0.55, 0.3] as const

export type Rgb = [number, number, number]

/** Colour for a series at a given year slot (0 = anchor/current year). Hue encodes the metric, alpha the year. */
export const withYearAlpha = ([r, g, b]: Rgb, yearIndex: number): string => {
  const alpha = YEAR_ALPHAS[Math.min(yearIndex, YEAR_ALPHAS.length - 1)]
  return `rgba(${r}, ${g}, ${b}, ${alpha})`
}
```

In `financial-overview/comparisonUtils.ts` replace the local `YEAR_ALPHAS` constant and the body of `getSeriesColor` with:

```ts
import { withYearAlpha, YEAR_ALPHAS } from '../../charts/comparisonColors'
export { YEAR_ALPHAS }
export const getSeriesColor = (metric: ComparisonMetric, yearIndex: number): string =>
  withYearAlpha(METRIC_COLORS[metric], yearIndex)
```

Run `CI=true npx react-scripts test --watchAll=false src/components/pages/financial-overview` — the existing `comparisonUtils.test.ts` must still pass unchanged.

- [ ] **Step 4: Implement `metrics.ts`**

```ts
import type { MonthlyMarketingPerformanceDto } from '../../../api/hooks/useMarketingPerformance'
import type { Rgb } from '../../charts/comparisonColors'

export type PerformanceMetric =
  | 'totalCost'
  | 'revenueWithoutVat'
  | 'orders'
  | 'pno'
  | 'roas'
  | 'avgOrderValue'
  | 'costPerOrder'

export type MetricUnit = 'czk' | 'percent' | 'count'

export const PERFORMANCE_METRICS: PerformanceMetric[] = [
  'totalCost', 'revenueWithoutVat', 'orders', 'pno', 'roas', 'avgOrderValue', 'costPerOrder',
]

export const METRIC_LABELS: Record<PerformanceMetric, string> = {
  totalCost: 'Náklady na reklamu',
  revenueWithoutVat: 'Tržby (bez DPH)',
  orders: 'Objednávky',
  pno: 'PNO',
  roas: 'ROAS',
  avgOrderValue: 'Průměrná objednávka',
  costPerOrder: 'Cena za nákup',
}

export const METRIC_UNITS: Record<PerformanceMetric, MetricUnit> = {
  totalCost: 'czk',
  revenueWithoutVat: 'czk',
  orders: 'count',
  pno: 'percent',
  roas: 'percent',
  avgOrderValue: 'czk',
  costPerOrder: 'czk',
}

export const METRIC_COLORS: Record<PerformanceMetric, Rgb> = {
  totalCost: [239, 68, 68],        // red-500
  revenueWithoutVat: [34, 197, 94], // green-500
  orders: [59, 130, 246],          // blue-500
  pno: [249, 115, 22],             // orange-500
  roas: [168, 85, 247],            // purple-500
  avgOrderValue: [20, 184, 166],   // teal-500
  costPerOrder: [245, 158, 11],    // amber-500
}

/** Stacked channel-cost bars; cycled by channel index (config order). */
export const CHANNEL_COLORS: Rgb[] = [
  [59, 130, 246],  // blue — FB/IG
  [234, 179, 8],   // yellow — Google
  [220, 38, 38],   // red — S-Klik
  [107, 114, 128], // gray — any further channel
]

export const MONTH_LABELS_SHORT = [
  'Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn', 'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro',
] as const

export const getMetricValue = (row: MonthlyMarketingPerformanceDto, metric: PerformanceMetric): number | null => {
  const value = row[metric]
  return value === null || value === undefined ? null : Number(value)
}

const czk = new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', minimumFractionDigits: 0, maximumFractionDigits: 0 })
const count = new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 0 })
const percent = new Intl.NumberFormat('cs-CZ', { minimumFractionDigits: 1, maximumFractionDigits: 1 })

export const EMPTY_VALUE = '—'

export const formatCzk = (value: number): string => czk.format(value)
export const formatCount = (value: number): string => count.format(value)
export const formatPercent = (value: number): string => `${percent.format(value)} %`

export const formatMetric = (value: number | null | undefined, unit: MetricUnit): string => {
  if (value === null || value === undefined || Number.isNaN(value)) return EMPTY_VALUE
  switch (unit) {
    case 'czk':
      return formatCzk(value)
    case 'percent':
      return formatPercent(value)
    case 'count':
      return formatCount(value)
    default: {
      const _exhaustive: never = unit
      throw new Error(`Unhandled unit: ${_exhaustive}`)
    }
  }
}

/** YoY ratio arrives as "this year / last year × 100"; show it as a signed delta ("+12,3 %"). */
export const formatYoy = (ratioPercent: number | null | undefined): string => {
  if (ratioPercent === null || ratioPercent === undefined || Number.isNaN(ratioPercent)) return EMPTY_VALUE
  const delta = ratioPercent - 100
  const sign = delta > 0 ? '+' : delta < 0 ? '−' : ''
  return `${sign}${percent.format(Math.abs(delta))} %`
}
```

- [ ] **Step 5: Run** — both new test files and the financial-overview folder → PASS; `npm run lint` clean.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/charts/comparisonColors.ts frontend/src/components/charts/__tests__/comparisonColors.test.ts frontend/src/components/pages/financial-overview/comparisonUtils.ts frontend/src/components/marketing/performance/metrics.ts frontend/src/components/marketing/performance/__tests__/metrics.test.ts
git commit -m "feat: shared year-comparison colours and marketing performance metric helpers"
```

---

## Task 16: `PerformanceTable`

**Files:**
- Create: `frontend/src/components/marketing/performance/PerformanceTable.tsx`
- Test: `frontend/src/components/marketing/performance/__tests__/PerformanceTable.test.tsx`

**Interfaces:**
- Consumes: `MonthlyMarketingPerformanceDto`, `ChannelInfoDto` (Task 14), formatters from `metrics.ts` (Task 15).
- Produces: `<PerformanceTable months={MonthlyMarketingPerformanceDto[]} channels={ChannelInfoDto[]} />` — newest month first, spreadsheet column order, warning icon (`title` = LastError or "Data zatím nebyla načtena") on rows with `hasData && (lastError || !revenueComputedAt || !costsComputedAt)`, lock icon on `isLocked`, "(probíhá)" suffix on `isPartial`, `data-testid="performance-table"`.

- [ ] **Step 1: Write the failing test**

```tsx
import React from 'react'
import { render, screen, within } from '@testing-library/react'
import { PerformanceTable } from '../PerformanceTable'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

const channels = [{ code: 'meta', label: 'FB/IG' }, { code: 'google', label: 'Google' }] as ChannelInfoDto[]

const month = (overrides: Partial<MonthlyMarketingPerformanceDto>): MonthlyMarketingPerformanceDto =>
  ({
    year: 2026, month: 7, monthYearDisplay: '07/2026', hasData: true, isLocked: false, isPartial: false,
    orders: 1527, revenueWithVat: 1688112, revenueWithoutVat: 1395134, totalCost: 438069, pno: 31.4, roas: 318.5,
    profit: 957065, avgOrderValue: 913.6, costPerOrder: 286.9, yoyCostPercent: 84.6, yoyRevenuePercent: 77.7, yoyOrdersPercent: 83.2,
    skippedEurInvoiceCount: 0, revenueComputedAt: '2026-09-18T03:00:00Z', costsComputedAt: '2026-09-18T03:00:00Z', lastError: null,
    channelCosts: [
      { channelCode: 'meta', label: 'FB/IG', costWithoutVat: 327126, invoiceCount: 2 },
      { channelCode: 'google', label: 'Google', costWithoutVat: 99909, invoiceCount: 1 },
    ],
    ...overrides,
  }) as unknown as MonthlyMarketingPerformanceDto

const norm = (s: string | null) => (s ?? '').replace(/ /g, ' ')

describe('PerformanceTable', () => {
  it('renders one channel column per configured channel, in spreadsheet order, newest month first', () => {
    render(<PerformanceTable channels={channels} months={[month({ month: 6, monthYearDisplay: '06/2026' }), month({})]} />)

    const headers = screen.getAllByRole('columnheader').map((h) => norm(h.textContent))
    expect(headers).toEqual([
      'Měsíc', 'FB/IG', 'Google', 'Náklady celkem', 'Tržby s DPH', 'Tržby bez DPH', 'PNO', 'ROAS',
      'Rozdíl tržby − náklady', 'Objednávky', 'Prům. objednávka', 'Cena za nákup', 'Náklady r/r', 'Tržby r/r', 'Objednávky r/r',
    ])
    const rows = screen.getAllByRole('row').slice(1)
    expect(norm(within(rows[0]).getAllByRole('cell')[0].textContent)).toBe('07/2026')
    expect(norm(within(rows[1]).getAllByRole('cell')[0].textContent)).toBe('06/2026')
  })

  it('formats values in Czech and shows dashes for null ratios', () => {
    render(<PerformanceTable channels={channels} months={[month({ pno: null, roas: null })]} />)
    const cells = screen.getAllByRole('row')[1].querySelectorAll('td')
    expect(norm(cells[1].textContent)).toBe('327 126 Kč')
    expect(norm(cells[3].textContent)).toBe('438 069 Kč')
    expect(norm(cells[6].textContent)).toBe('—')
    expect(norm(cells[9].textContent)).toBe('1 527')
    expect(norm(cells[12].textContent)).toBe('−15,4 %')
  })

  it('flags partial, locked and errored months', () => {
    render(
      <PerformanceTable
        channels={channels}
        months={[
          month({ month: 9, monthYearDisplay: '09/2026', isPartial: true, lastError: 'Costs: Flexi 503', costsComputedAt: null }),
          month({ month: 8, monthYearDisplay: '08/2026', isLocked: true }),
        ]}
      />,
    )
    expect(screen.getByText(/09\/2026/)).toHaveTextContent('(probíhá)')
    expect(screen.getByTitle('Costs: Flexi 503')).toBeInTheDocument()
    expect(screen.getByTitle('Měsíc je uzamčen — mění ho jen ruční přepočet')).toBeInTheDocument()
  })

  it('renders an empty state when no month has data', () => {
    render(<PerformanceTable channels={channels} months={[month({ hasData: false })]} />)
    expect(screen.getByText('Zatím nejsou k dispozici žádná data. Spusťte přepočet nebo počkejte na noční úlohu.')).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run to verify failure** — `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/performance/__tests__/PerformanceTable.test.tsx` → module not found.

- [ ] **Step 3: Implement**

```tsx
import React from 'react'
import { AlertTriangle, Lock } from 'lucide-react'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../api/hooks/useMarketingPerformance'
import { EMPTY_VALUE, formatCount, formatCzk, formatMetric, formatYoy } from './metrics'

interface PerformanceTableProps {
  months: MonthlyMarketingPerformanceDto[]
  channels: ChannelInfoDto[]
}

const LOCK_TITLE = 'Měsíc je uzamčen — mění ho jen ruční přepočet'
const STALE_TITLE = 'Data zatím nebyla načtena'
const EMPTY_TEXT = 'Zatím nejsou k dispozici žádná data. Spusťte přepočet nebo počkejte na noční úlohu.'

const th = 'px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-graphite-muted uppercase tracking-wider whitespace-nowrap'
const td = 'px-3 py-2 text-right text-sm text-gray-900 dark:text-graphite-text whitespace-nowrap tabular-nums'

const hasWarning = (m: MonthlyMarketingPerformanceDto): boolean =>
  m.hasData && (Boolean(m.lastError) || !m.revenueComputedAt || !m.costsComputedAt)

const channelCost = (m: MonthlyMarketingPerformanceDto, code: string): number =>
  m.channelCosts.find((c) => c.channelCode === code)?.costWithoutVat ?? 0

const profitClass = (v: number) => (v >= 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400')

export const PerformanceTable: React.FC<PerformanceTableProps> = ({ months, channels }) => {
  const rows = React.useMemo(
    () => [...months].filter((m) => m.hasData).sort((a, b) => b.year * 100 + b.month - (a.year * 100 + a.month)),
    [months],
  )

  if (rows.length === 0) {
    return (
      <div className="p-6 text-sm text-gray-600 dark:text-graphite-muted text-center" data-testid="performance-table-empty">
        {EMPTY_TEXT}
      </div>
    )
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-gray-200 dark:divide-graphite-border" data-testid="performance-table">
        <thead className="bg-gray-50 dark:bg-graphite-surface-2">
          <tr>
            <th scope="col" className={`${th} text-left`}>Měsíc</th>
            {channels.map((c) => (
              <th key={c.code} scope="col" className={th}>{c.label}</th>
            ))}
            <th scope="col" className={th}>Náklady celkem</th>
            <th scope="col" className={th}>Tržby s DPH</th>
            <th scope="col" className={th}>Tržby bez DPH</th>
            <th scope="col" className={th}>PNO</th>
            <th scope="col" className={th}>ROAS</th>
            <th scope="col" className={th}>Rozdíl tržby − náklady</th>
            <th scope="col" className={th}>Objednávky</th>
            <th scope="col" className={th}>Prům. objednávka</th>
            <th scope="col" className={th}>Cena za nákup</th>
            <th scope="col" className={th}>Náklady r/r</th>
            <th scope="col" className={th}>Tržby r/r</th>
            <th scope="col" className={th}>Objednávky r/r</th>
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-graphite-surface divide-y divide-gray-200 dark:divide-graphite-border">
          {rows.map((m) => (
            <tr key={`${m.year}-${m.month}`} className="hover:bg-gray-50 dark:hover:bg-graphite-surface-2">
              <td className={`${td} text-left font-medium`}>
                <span className="inline-flex items-center gap-1">
                  {m.monthYearDisplay}
                  {m.isPartial && <span className="text-xs text-gray-500 dark:text-graphite-muted">(probíhá)</span>}
                  {m.isLocked && <Lock className="h-3.5 w-3.5 text-gray-400" aria-label={LOCK_TITLE} title={LOCK_TITLE} />}
                  {hasWarning(m) && (
                    <AlertTriangle className="h-3.5 w-3.5 text-amber-500" aria-label={m.lastError ?? STALE_TITLE} title={m.lastError ?? STALE_TITLE} />
                  )}
                </span>
              </td>
              {channels.map((c) => (
                <td key={c.code} className={td}>{formatCzk(channelCost(m, c.code))}</td>
              ))}
              <td className={`${td} font-medium`}>{formatCzk(m.totalCost)}</td>
              <td className={td}>{formatCzk(m.revenueWithVat)}</td>
              <td className={td}>{formatCzk(m.revenueWithoutVat)}</td>
              <td className={td}>{formatMetric(m.pno, 'percent')}</td>
              <td className={td}>{formatMetric(m.roas, 'percent')}</td>
              <td className={`${td} ${profitClass(m.profit)}`}>{formatCzk(m.profit)}</td>
              <td className={td}>{formatCount(m.orders)}</td>
              <td className={td}>{formatMetric(m.avgOrderValue, 'czk')}</td>
              <td className={td}>{formatMetric(m.costPerOrder, 'czk')}</td>
              <td className={td}>{formatYoy(m.yoyCostPercent)}</td>
              <td className={td}>{formatYoy(m.yoyRevenuePercent)}</td>
              <td className={td}>{formatYoy(m.yoyOrdersPercent) || EMPTY_VALUE}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
```

- [ ] **Step 4: Run** — test → 4 PASS. If `getByTitle` fails on the lucide icon, wrap the icon in `<span title=...>` instead of passing `title` to the SVG.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/marketing/performance/PerformanceTable.tsx frontend/src/components/marketing/performance/__tests__/PerformanceTable.test.tsx
git commit -m "feat: marketing performance table"
```

---

## Task 17: Trend chart and year-comparison chart

**Files:**
- Create: `frontend/src/components/marketing/performance/PerformanceTrendChart.tsx`
- Create: `frontend/src/components/marketing/performance/PerformanceComparisonChart.tsx`
- Test: `frontend/src/components/marketing/performance/__tests__/PerformanceTrendChart.test.tsx`
- Test: `frontend/src/components/marketing/performance/__tests__/PerformanceComparisonChart.test.tsx`

**Interfaces:**
- Consumes: `FinancialChart` wrapper (`frontend/src/components/pages/financial-overview/FinancialChart.tsx`, props `{ chartData: ChartData<'bar'>; chartOptions: ChartOptions<'bar'>; title: string }`), `withYearAlpha`, `metrics.ts`.
- Produces:

```tsx
<PerformanceTrendChart months={MonthlyMarketingPerformanceDto[]} channels={ChannelInfoDto[]} metric={PerformanceMetric} />
<PerformanceComparisonChart series={MarketingYearSeriesDto[]} metric={PerformanceMetric} currentMonth={number} />
// both export a pure `build*ChartData` helper used by the tests
export const buildTrendChartData = (months, channels, metric): ChartData<'bar'>
export const buildComparisonChartData = (series, metric): ChartData<'bar'>
```

- [ ] **Step 1: Write the failing tests**

`PerformanceTrendChart.test.tsx`:

```tsx
import React from 'react'
import { render, screen } from '@testing-library/react'
import { buildTrendChartData, PerformanceTrendChart } from '../PerformanceTrendChart'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

jest.mock('react-chartjs-2', () => ({ Chart: () => <canvas data-testid="chart-canvas" /> }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))

const channels = [{ code: 'meta', label: 'FB/IG' }, { code: 'google', label: 'Google' }] as ChannelInfoDto[]
const m = (month: number, hasData = true): MonthlyMarketingPerformanceDto =>
  ({
    year: 2026, month, monthYearDisplay: `0${month}/2026`, hasData, isLocked: false, isPartial: false,
    orders: 10 * month, revenueWithVat: 0, revenueWithoutVat: 1000 * month, totalCost: 100 * month, pno: month, roas: null,
    profit: 0, avgOrderValue: null, costPerOrder: null, skippedEurInvoiceCount: 0,
    channelCosts: [
      { channelCode: 'meta', label: 'FB/IG', costWithoutVat: 60 * month, invoiceCount: 1 },
      { channelCode: 'google', label: 'Google', costWithoutVat: 40 * month, invoiceCount: 1 },
    ],
  }) as unknown as MonthlyMarketingPerformanceDto

describe('buildTrendChartData', () => {
  it('stacks one bar dataset per channel and adds the selected metric as a line on the second axis', () => {
    const data = buildTrendChartData([m(1), m(2)], channels, 'pno')

    expect(data.labels).toEqual(['01/2026', '02/2026'])
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG', 'Google', 'PNO'])
    expect(data.datasets[0]).toMatchObject({ type: 'bar', stack: 'costs', yAxisID: 'y', data: [60, 120] })
    expect(data.datasets[2]).toMatchObject({ type: 'line', yAxisID: 'y1', data: [1, 2] })
  })

  it('renders null for months without data so the line breaks instead of dropping to zero', () => {
    const data = buildTrendChartData([m(1), m(2, false)], channels, 'orders')
    expect(data.datasets[2].data).toEqual([10, null])
  })

  it('does not duplicate cost when the selected metric is totalCost', () => {
    const data = buildTrendChartData([m(1)], channels, 'totalCost')
    expect(data.datasets.map((d) => d.label)).toEqual(['FB/IG', 'Google'])
  })
})

describe('PerformanceTrendChart', () => {
  it('renders with a title', () => {
    render(<PerformanceTrendChart months={[m(1)]} channels={channels} metric="pno" />)
    expect(screen.getByText('Vývoj — PNO')).toBeInTheDocument()
    expect(screen.getByTestId('chart-canvas')).toBeInTheDocument()
  })
})
```

`PerformanceComparisonChart.test.tsx`:

```tsx
import React from 'react'
import { render, screen } from '@testing-library/react'
import { buildComparisonChartData, PerformanceComparisonChart } from '../PerformanceComparisonChart'
import type { MarketingYearSeriesDto, MonthlyMarketingPerformanceDto } from '../../../../api/hooks/useMarketingPerformance'

jest.mock('react-chartjs-2', () => ({ Chart: () => <canvas data-testid="chart-canvas" /> }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))

const cell = (year: number, month: number, orders: number | null): MonthlyMarketingPerformanceDto =>
  ({ year, month, monthYearDisplay: `${month}/${year}`, hasData: orders !== null, orders: orders ?? 0, channelCosts: [] }) as unknown as MonthlyMarketingPerformanceDto

const series = (year: number, values: (number | null)[]): MarketingYearSeriesDto =>
  ({ year, months: values.map((v, i) => cell(year, i + 1, v)), ytdOrders: 0, ytdRevenueWithoutVat: 0, ytdTotalCost: 0 }) as unknown as MarketingYearSeriesDto

describe('buildComparisonChartData', () => {
  it('produces Jan–Dec labels and one dataset per year, anchor year solid, older years faded', () => {
    const data = buildComparisonChartData(
      [series(2026, [5, 6, null, null, null, null, null, null, null, null, null, null]), series(2025, [3, 4, 7, null, null, null, null, null, null, null, null, null])],
      'orders',
    )
    expect(data.labels).toEqual(['Led', 'Úno', 'Bře', 'Dub', 'Kvě', 'Čvn', 'Čvc', 'Srp', 'Zář', 'Říj', 'Lis', 'Pro'])
    expect(data.datasets.map((d) => d.label)).toEqual(['Objednávky 2026', 'Objednávky 2025'])
    expect(data.datasets[0].data).toEqual([5, 6, null, null, null, null, null, null, null, null, null, null])
    expect(String(data.datasets[0].backgroundColor)).toMatch(/, 1\)$/)
    expect(String(data.datasets[1].backgroundColor)).toMatch(/, 0\.55\)$/)
  })
})

describe('PerformanceComparisonChart', () => {
  it('renders with a title', () => {
    render(<PerformanceComparisonChart series={[series(2026, Array(12).fill(null))]} metric="pno" currentMonth={9} />)
    expect(screen.getByText('Meziroční srovnání — PNO')).toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run to verify failure** — `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/performance/__tests__/Performance` → module not found.

- [ ] **Step 3: Implement the trend chart**

```tsx
import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from '../../pages/financial-overview/FinancialChart'
import type { ChannelInfoDto, MonthlyMarketingPerformanceDto } from '../../../api/hooks/useMarketingPerformance'
import { withYearAlpha } from '../../charts/comparisonColors'
import { CHANNEL_COLORS, formatCzk, formatMetric, getMetricValue, METRIC_COLORS, METRIC_LABELS, METRIC_UNITS, type PerformanceMetric } from './metrics'

interface PerformanceTrendChartProps {
  months: MonthlyMarketingPerformanceDto[]
  channels: ChannelInfoDto[]
  metric: PerformanceMetric
}

const COST_STACK = 'costs'
const COST_AXIS = 'y'
const METRIC_AXIS = 'y1'

const byMonthAsc = (a: MonthlyMarketingPerformanceDto, b: MonthlyMarketingPerformanceDto) =>
  a.year * 100 + a.month - (b.year * 100 + b.month)

export const buildTrendChartData = (
  months: MonthlyMarketingPerformanceDto[],
  channels: ChannelInfoDto[],
  metric: PerformanceMetric,
): ChartData<'bar'> => {
  const sorted = [...months].sort(byMonthAsc)
  const labels = sorted.map((m) => m.monthYearDisplay)

  const channelDatasets = channels.map((channel, index) => {
    const color = withYearAlpha(CHANNEL_COLORS[index % CHANNEL_COLORS.length], 0)
    return {
      type: 'bar' as const,
      label: channel.label,
      stack: COST_STACK,
      yAxisID: COST_AXIS,
      data: sorted.map((m) => (m.hasData ? m.channelCosts.find((c) => c.channelCode === channel.code)?.costWithoutVat ?? 0 : 0)),
      backgroundColor: color,
      borderColor: color,
      borderWidth: 1,
    }
  })

  if (metric === 'totalCost') {
    return { labels, datasets: channelDatasets } as ChartData<'bar'>
  }

  const metricColor = withYearAlpha(METRIC_COLORS[metric], 0)
  const metricDataset = {
    type: 'line' as const,
    label: METRIC_LABELS[metric],
    yAxisID: METRIC_AXIS,
    data: sorted.map((m) => (m.hasData ? getMetricValue(m, metric) : null)),
    borderColor: metricColor,
    backgroundColor: metricColor,
    fill: false,
    tension: 0.1,
    borderWidth: 3,
    spanGaps: false,
  }

  return { labels, datasets: [...channelDatasets, metricDataset] } as ChartData<'bar'>
}

export const PerformanceTrendChart: React.FC<PerformanceTrendChartProps> = ({ months, channels, metric }) => {
  const chartData = React.useMemo(() => buildTrendChartData(months, channels, metric), [months, channels, metric])
  const unit = METRIC_UNITS[metric]

  const chartOptions = React.useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'top' as const },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => {
              const value = context.parsed.y
              if (value === null || value === undefined) return `${context.dataset.label}: —`
              const isMetricLine = context.dataset.yAxisID === METRIC_AXIS
              return `${context.dataset.label}: ${isMetricLine ? formatMetric(value, unit) : formatCzk(value)}`
            },
          },
        },
      },
      scales: {
        x: { stacked: true },
        [COST_AXIS]: { stacked: true, position: 'left' as const, beginAtZero: true, ticks: { callback: (v) => formatCzk(Number(v)) } },
        [METRIC_AXIS]: {
          display: metric !== 'totalCost',
          position: 'right' as const,
          beginAtZero: true,
          grid: { drawOnChartArea: false },
          ticks: { callback: (v) => formatMetric(Number(v), unit) },
        },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [metric, unit],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={`Vývoj — ${METRIC_LABELS[metric]}`} />
}
```

- [ ] **Step 4: Implement the comparison chart**

```tsx
import React from 'react'
import type { ChartData, ChartOptions } from 'chart.js'
import { FinancialChart } from '../../pages/financial-overview/FinancialChart'
import type { MarketingYearSeriesDto } from '../../../api/hooks/useMarketingPerformance'
import { withYearAlpha } from '../../charts/comparisonColors'
import { formatMetric, getMetricValue, METRIC_COLORS, METRIC_LABELS, METRIC_UNITS, MONTH_LABELS_SHORT, type PerformanceMetric } from './metrics'

interface PerformanceComparisonChartProps {
  /** Newest year first, as returned by the API. */
  series: MarketingYearSeriesDto[]
  metric: PerformanceMetric
  /** Current (partial) month 1..12; used only for the tooltip hint. */
  currentMonth: number
}

export const buildComparisonChartData = (series: MarketingYearSeriesDto[], metric: PerformanceMetric): ChartData<'bar'> => {
  const datasets = series.map((s, yearIndex) => {
    const color = withYearAlpha(METRIC_COLORS[metric], yearIndex)
    return {
      type: 'line' as const,
      label: `${METRIC_LABELS[metric]} ${s.year}`,
      data: Array.from({ length: 12 }, (_, i) => {
        const cell = s.months.find((m) => m.month === i + 1)
        return cell && cell.hasData ? getMetricValue(cell, metric) : null
      }),
      borderColor: color,
      backgroundColor: color,
      borderWidth: yearIndex === 0 ? 3 : 2,
      tension: 0.1,
      fill: false,
      spanGaps: false,
    }
  })
  return { labels: [...MONTH_LABELS_SHORT], datasets } as ChartData<'bar'>
}

export const PerformanceComparisonChart: React.FC<PerformanceComparisonChartProps> = ({ series, metric, currentMonth }) => {
  const chartData = React.useMemo(() => buildComparisonChartData(series, metric), [series, metric])
  const unit = METRIC_UNITS[metric]

  const chartOptions = React.useMemo<ChartOptions<'bar'>>(
    () => ({
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'top' as const },
        title: { display: false },
        tooltip: {
          callbacks: {
            label: (context) => `${context.dataset.label}: ${formatMetric(context.parsed.y, unit)}`,
            footer: (items) => (items[0]?.dataIndex === currentMonth - 1 ? 'Aktuální měsíc je neúplný' : ''),
          },
        },
      },
      scales: {
        y: { beginAtZero: true, ticks: { callback: (v) => formatMetric(Number(v), unit) } },
      },
      interaction: { intersect: false, mode: 'index' },
    }),
    [unit, currentMonth],
  )

  return <FinancialChart chartData={chartData} chartOptions={chartOptions} title={`Meziroční srovnání — ${METRIC_LABELS[metric]}`} />
}
```

Hovering a month shows every year's value for that month in one tooltip (`mode: 'index'`) — this is the "June 2026 next to June 2025" requirement.

- [ ] **Step 5: Run** — both tests → PASS; `npm run lint` clean.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/marketing/performance
git commit -m "feat: marketing performance trend and year-comparison charts"
```

---

## Task 18: Page, toolbar, recompute dialog, route and sidebar

**Files:**
- Create: `frontend/src/components/marketing/performance/PerformanceToolbar.tsx`
- Create: `frontend/src/components/marketing/performance/RecomputeDialog.tsx`
- Create: `frontend/src/components/marketing/performance/MarketingPerformancePage.tsx`
- Modify: `frontend/src/App.tsx:55` (import) and `:433` (route after `/marketing/calendar`)
- Modify: `frontend/src/components/layout/Sidebar.tsx:183` (item after "Kalendář")
- Test: `frontend/src/components/marketing/performance/__tests__/MarketingPerformancePage.test.tsx`
- Test: `frontend/src/components/marketing/performance/__tests__/RecomputeDialog.test.tsx`

**Interfaces:**
- Consumes: hooks (Task 14), `metrics.ts` (15), `PerformanceTable` (16), charts (17), `usePermissionsContext` (`frontend/src/auth/PermissionsContext.tsx`), `useScreenView` (`frontend/src/telemetry/useScreenView`), `PAGE_CONTAINER_HEIGHT` (`frontend/src/constants/layout`).
- Produces:

```tsx
export type PerformanceViewMode = 'trend' | 'comparison'
export type PerformancePeriod = 12 | 24 | 36
<PerformanceToolbar viewMode period years metric includeWholesale canRecompute lastRefreshAt hasWarnings onViewModeChange onPeriodChange onYearsChange onMetricChange onIncludeWholesaleChange onRecomputeClick />
<RecomputeDialog isOpen onClose />        // owns the mutation; shows job id + link to /recurring-jobs on success
export default MarketingPerformancePage   // route /marketing/performance
```

- [ ] **Step 1: Write the failing page test**

```tsx
import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import MarketingPerformancePage from '../MarketingPerformancePage'

const monthsQuery = jest.fn()
const comparisonQuery = jest.fn()
jest.mock('../../../../api/hooks/useMarketingPerformance', () => ({
  useMarketingPerformanceMonthsQuery: (...args: unknown[]) => monthsQuery(...args),
  useMarketingPerformanceComparisonQuery: (...args: unknown[]) => comparisonQuery(...args),
  useRecomputeMarketingPerformanceMutation: () => ({ mutate: jest.fn(), isPending: false, reset: jest.fn() }),
}))
jest.mock('../../../../auth/PermissionsContext', () => ({ usePermissionsContext: () => ({ hasPermission: (p: string) => p === 'marketing.performance.write' }) }))
jest.mock('../../../../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }))
jest.mock('../../../../hooks/useMediaQuery', () => ({ useIsMobile: () => false }))
jest.mock('react-chartjs-2', () => ({ Chart: () => <canvas data-testid="chart-canvas" /> }))

const month = {
  year: 2026, month: 8, monthYearDisplay: '08/2026', hasData: true, isLocked: false, isPartial: false, orders: 1596,
  revenueWithVat: 1729641, revenueWithoutVat: 1429455, totalCost: 505565, pno: 35.4, roas: 282.7, profit: 923890,
  avgOrderValue: 895.6, costPerOrder: 316.8, skippedEurInvoiceCount: 1, revenueComputedAt: '2026-09-18T03:00:00Z', costsComputedAt: '2026-09-18T03:00:00Z',
  channelCosts: [{ channelCode: 'meta', label: 'FB/IG', costWithoutVat: 371564, invoiceCount: 2 }],
}

describe('MarketingPerformancePage', () => {
  beforeEach(() => {
    jest.clearAllMocks()
    monthsQuery.mockReturnValue({ data: { success: true, months: [month], channels: [{ code: 'meta', label: 'FB/IG' }], lastRefreshAt: '2026-09-18T03:00:00Z' }, isLoading: false, error: null, isRefetching: false })
    comparisonQuery.mockReturnValue({ data: undefined, isLoading: false, error: null })
  })

  it('renders heading, table and trend chart by default', () => {
    render(<MarketingPerformancePage />)
    expect(screen.getByRole('heading', { name: 'Výkon reklamy' })).toBeInTheDocument()
    expect(screen.getByTestId('performance-table')).toBeInTheDocument()
    expect(screen.getByText(/Vývoj — /)).toBeInTheDocument()
  })

  it('wholesale switch re-queries with includeWholesale=true', () => {
    render(<MarketingPerformancePage />)
    fireEvent.click(screen.getByLabelText('včetně velkoobchodu'))
    expect(monthsQuery).toHaveBeenLastCalledWith(expect.objectContaining({ includeWholesale: true }), true)
  })

  it('switching to comparison view enables the comparison query and disables the months query', () => {
    comparisonQuery.mockReturnValue({ data: { success: true, series: [], anchorYear: 2026, currentMonth: 9, channels: [] }, isLoading: false, error: null })
    render(<MarketingPerformancePage />)
    fireEvent.change(screen.getByLabelText('Zobrazení'), { target: { value: 'comparison' } })
    expect(comparisonQuery).toHaveBeenLastCalledWith(expect.objectContaining({ years: 3 }), true)
    expect(monthsQuery).toHaveBeenLastCalledWith(expect.anything(), false)
  })

  it('shows the recompute button only with write permission and opens the dialog', () => {
    render(<MarketingPerformancePage />)
    fireEvent.click(screen.getByRole('button', { name: 'Přepočítat' }))
    expect(screen.getByRole('heading', { name: 'Přepočítat výkon reklamy' })).toBeInTheDocument()
  })

  it('shows the last refresh time in the status line', () => {
    render(<MarketingPerformancePage />)
    expect(screen.getByText(/Poslední aktualizace:/)).toBeInTheDocument()
  })
})
```

`RecomputeDialog.test.tsx`:

```tsx
import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { RecomputeDialog } from '../RecomputeDialog'

const mutate = jest.fn()
let mutationState: Record<string, unknown> = {}
jest.mock('../../../../api/hooks/useMarketingPerformance', () => ({
  useRecomputeMarketingPerformanceMutation: () => ({ mutate, isPending: false, reset: jest.fn(), ...mutationState }),
}))

describe('RecomputeDialog', () => {
  beforeEach(() => { jest.clearAllMocks(); mutationState = {} })

  it('submits the typed range', () => {
    render(<RecomputeDialog isOpen onClose={jest.fn()} />)
    fireEvent.change(screen.getByLabelText('Od (RRRR-MM)'), { target: { value: '2023-01' } })
    fireEvent.change(screen.getByLabelText('Do (RRRR-MM)'), { target: { value: '2024-12' } })
    fireEvent.click(screen.getByRole('button', { name: 'Spustit přepočet' }))
    expect(mutate).toHaveBeenCalledWith({ from: '2023-01', to: '2024-12' }, expect.anything())
  })

  it('blocks submit on a malformed month', () => {
    render(<RecomputeDialog isOpen onClose={jest.fn()} />)
    fireEvent.change(screen.getByLabelText('Od (RRRR-MM)'), { target: { value: '2023-1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Spustit přepočet' }))
    expect(mutate).not.toHaveBeenCalled()
    expect(screen.getByText('Zadejte měsíce ve formátu RRRR-MM.')).toBeInTheDocument()
  })

  it('shows job id and link after success', () => {
    mutationState = { isSuccess: true, data: { success: true, jobId: 'hf-9', monthCount: 24 } }
    render(<RecomputeDialog isOpen onClose={jest.fn()} />)
    expect(screen.getByText(/hf-9/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Naplánované úlohy' })).toHaveAttribute('href', '/recurring-jobs')
  })

  it('renders nothing when closed', () => {
    const { container } = render(<RecomputeDialog isOpen={false} onClose={jest.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })
})
```

- [ ] **Step 2: Run to verify failure** — `cd frontend && CI=true npx react-scripts test --watchAll=false src/components/marketing/performance/__tests__/MarketingPerformancePage.test.tsx src/components/marketing/performance/__tests__/RecomputeDialog.test.tsx` → module not found.

- [ ] **Step 3: Toolbar**

```tsx
import React from 'react'
import { RefreshCw, AlertTriangle } from 'lucide-react'
import { METRIC_LABELS, PERFORMANCE_METRICS, type PerformanceMetric } from './metrics'

export type PerformanceViewMode = 'trend' | 'comparison'
export type PerformancePeriod = 12 | 24 | 36

interface PerformanceToolbarProps {
  viewMode: PerformanceViewMode
  period: PerformancePeriod
  years: number
  metric: PerformanceMetric
  includeWholesale: boolean
  canRecompute: boolean
  lastRefreshAt?: string | Date | null
  hasWarnings: boolean
  isRefetching: boolean
  onViewModeChange: (mode: PerformanceViewMode) => void
  onPeriodChange: (period: PerformancePeriod) => void
  onYearsChange: (years: number) => void
  onMetricChange: (metric: PerformanceMetric) => void
  onIncludeWholesaleChange: (value: boolean) => void
  onRecomputeClick: () => void
}

const select = 'block pl-3 pr-10 py-2 text-base border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm rounded-md'
const label = 'block text-sm font-medium text-gray-700 dark:text-graphite-muted mb-1'

const formatDateTime = (value: string | Date): string =>
  new Intl.DateTimeFormat('cs-CZ', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))

export const PerformanceToolbar: React.FC<PerformanceToolbarProps> = (props) => {
  const {
    viewMode, period, years, metric, includeWholesale, canRecompute, lastRefreshAt, hasWarnings, isRefetching,
    onViewModeChange, onPeriodChange, onYearsChange, onMetricChange, onIncludeWholesaleChange, onRecomputeClick,
  } = props

  return (
    <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark rounded-lg p-4 mb-4">
      <div className="flex flex-col lg:flex-row lg:items-end gap-4">
        <div>
          <label htmlFor="performance-view-mode" className={label}>Zobrazení</label>
          <select id="performance-view-mode" className={`${select} w-52`} value={viewMode} onChange={(e) => onViewModeChange(e.target.value as PerformanceViewMode)}>
            <option value="trend">Vývoj</option>
            <option value="comparison">Meziroční srovnání</option>
          </select>
        </div>

        {viewMode === 'trend' ? (
          <div>
            <label htmlFor="performance-period" className={label}>Období</label>
            <select id="performance-period" className={`${select} w-44`} value={period} onChange={(e) => onPeriodChange(Number(e.target.value) as PerformancePeriod)}>
              <option value={12}>Posledních 12 měsíců</option>
              <option value={24}>Posledních 24 měsíců</option>
              <option value={36}>Posledních 36 měsíců</option>
            </select>
          </div>
        ) : (
          <div>
            <label htmlFor="performance-years" className={label}>Počet roků</label>
            <select id="performance-years" className={`${select} w-32`} value={years} onChange={(e) => onYearsChange(Number(e.target.value))}>
              <option value={2}>2 roky</option>
              <option value={3}>3 roky</option>
            </select>
          </div>
        )}

        <div>
          <label htmlFor="performance-metric" className={label}>Metrika</label>
          <select id="performance-metric" className={`${select} w-52`} value={metric} onChange={(e) => onMetricChange(e.target.value as PerformanceMetric)}>
            {PERFORMANCE_METRICS.map((m) => (
              <option key={m} value={m}>{METRIC_LABELS[m]}</option>
            ))}
          </select>
        </div>

        <label className="inline-flex items-center gap-2 text-sm text-gray-700 dark:text-graphite-text pb-2 cursor-pointer">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
            checked={includeWholesale}
            onChange={(e) => onIncludeWholesaleChange(e.target.checked)}
          />
          včetně velkoobchodu
        </label>

        <div className="flex-1" />

        <div className="flex items-center gap-3 text-xs text-gray-500 dark:text-graphite-muted pb-2">
          {isRefetching && <RefreshCw className="h-3.5 w-3.5 animate-spin" aria-label="Načítání" />}
          {hasWarnings && (
            <span className="inline-flex items-center gap-1 text-amber-600 dark:text-amber-400">
              <AlertTriangle className="h-3.5 w-3.5" /> některé měsíce mají neúplná data
            </span>
          )}
          <span>Poslední aktualizace: {lastRefreshAt ? formatDateTime(lastRefreshAt) : '—'}</span>
        </div>

        {canRecompute && (
          <button
            type="button"
            onClick={onRecomputeClick}
            className="inline-flex items-center px-3 py-2 border border-gray-300 dark:border-graphite-border shadow-sm text-sm font-medium rounded-md text-gray-700 dark:text-graphite-text bg-white dark:bg-graphite-surface-2 hover:bg-gray-50 dark:hover:bg-graphite-surface"
          >
            Přepočítat
          </button>
        )}
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Recompute dialog**

```tsx
import React, { useState } from 'react'
import { Link } from 'react-router-dom'
import { X } from 'lucide-react'
import { useRecomputeMarketingPerformanceMutation } from '../../../api/hooks/useMarketingPerformance'

interface RecomputeDialogProps {
  isOpen: boolean
  onClose: () => void
}

const MONTH_PATTERN = /^\d{4}-(0[1-9]|1[0-2])$/
const FORMAT_ERROR = 'Zadejte měsíce ve formátu RRRR-MM.'
const ORDER_ERROR = 'Počáteční měsíc nesmí být po koncovém.'

const input = 'mt-1 block w-full rounded-md border-gray-300 dark:border-graphite-border dark:bg-graphite-surface-2 dark:text-graphite-text shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm'

const defaultFrom = (): string => {
  const d = new Date()
  d.setMonth(d.getMonth() - 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}
const defaultTo = (): string => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

export const RecomputeDialog: React.FC<RecomputeDialogProps> = ({ isOpen, onClose }) => {
  const [from, setFrom] = useState(defaultFrom)
  const [to, setTo] = useState(defaultTo)
  const [validationError, setValidationError] = useState<string | null>(null)
  const mutation = useRecomputeMarketingPerformanceMutation()

  if (!isOpen) return null

  const submit = () => {
    if (!MONTH_PATTERN.test(from) || !MONTH_PATTERN.test(to)) {
      setValidationError(FORMAT_ERROR)
      return
    }
    if (from > to) {
      setValidationError(ORDER_ERROR)
      return
    }
    setValidationError(null)
    mutation.mutate({ from, to }, {})
  }

  const close = () => {
    mutation.reset()
    onClose()
  }

  // The generated client throws on non-2xx; the thrown SwaggerException carries the BaseResponse body with errorCode.
  const serverError = mutation.error ? ((mutation.error as { result?: { errorCode?: string } }).result?.errorCode ?? mutation.error.message) : null

  return (
    <div className="fixed inset-0 z-50 overflow-y-auto">
      <div className="fixed inset-0 bg-black bg-opacity-50 transition-opacity" onClick={close} />
      <div className="flex min-h-full items-center justify-center p-4">
        <div className="relative bg-white dark:bg-graphite-surface rounded-lg shadow-xl dark:shadow-soft-dark max-w-md w-full p-6">
          <button type="button" onClick={close} aria-label="Zavřít" className="absolute top-4 right-4 text-gray-400 dark:text-graphite-faint hover:text-gray-600 dark:hover:text-graphite-muted">
            <X className="h-5 w-5" />
          </button>
          <h3 className="text-lg font-semibold text-gray-900 dark:text-graphite-text mb-2">Přepočítat výkon reklamy</h3>
          <p className="text-sm text-gray-600 dark:text-graphite-muted mb-4">
            Přepočet znovu načte náklady z ABRA Flexi a tržby z vydaných faktur pro každý měsíc v období, včetně uzamčených měsíců. Běží na pozadí.
          </p>

          {mutation.isSuccess && mutation.data ? (
            <div className="text-sm text-gray-700 dark:text-graphite-text space-y-2">
              <p>Přepočet {mutation.data.monthCount} měsíců byl zařazen do fronty (úloha {mutation.data.jobId}).</p>
              <p>
                Průběh sledujte na stránce <Link to="/recurring-jobs" className="text-indigo-600 dark:text-indigo-400 underline">Naplánované úlohy</Link>.
              </p>
              <div className="flex justify-end pt-2">
                <button type="button" onClick={close} className="px-4 py-2 text-sm font-medium rounded-md bg-indigo-600 text-white hover:bg-indigo-700">Zavřít</button>
              </div>
            </div>
          ) : (
            <form onSubmit={(e) => { e.preventDefault(); submit() }} className="space-y-4">
              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label htmlFor="recompute-from" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted">Od (RRRR-MM)</label>
                  <input id="recompute-from" className={input} value={from} onChange={(e) => setFrom(e.target.value.trim())} placeholder="2023-01" />
                </div>
                <div>
                  <label htmlFor="recompute-to" className="block text-sm font-medium text-gray-700 dark:text-graphite-muted">Do (RRRR-MM)</label>
                  <input id="recompute-to" className={input} value={to} onChange={(e) => setTo(e.target.value.trim())} placeholder="2026-09" />
                </div>
              </div>
              {(validationError || serverError) && (
                <p className="text-sm text-red-600 dark:text-red-400" role="alert">{validationError ?? serverError}</p>
              )}
              <div className="flex justify-end gap-2">
                <button type="button" onClick={close} className="px-4 py-2 text-sm font-medium rounded-md border border-gray-300 dark:border-graphite-border text-gray-700 dark:text-graphite-text">Zrušit</button>
                <button type="submit" disabled={mutation.isPending} className="px-4 py-2 text-sm font-medium rounded-md bg-indigo-600 text-white hover:bg-indigo-700 disabled:opacity-50">
                  Spustit přepočet
                </button>
              </div>
            </form>
          )}
        </div>
      </div>
    </div>
  )
}
```

The page test mocks `useRecomputeMarketingPerformanceMutation` without `isSuccess`, so the form branch renders. `RecomputeDialog.test.tsx` uses a `Link`, so wrap its `render` calls in `<MemoryRouter>` from `react-router-dom` (adjust the test file accordingly).

- [ ] **Step 5: Page**

```tsx
import React, { useMemo, useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import { PAGE_CONTAINER_HEIGHT } from '../../../constants/layout'
import { usePermissionsContext } from '../../../auth/PermissionsContext'
import { useScreenView } from '../../../telemetry/useScreenView'
import {
  useMarketingPerformanceComparisonQuery,
  useMarketingPerformanceMonthsQuery,
} from '../../../api/hooks/useMarketingPerformance'
import { PerformanceToolbar, type PerformancePeriod, type PerformanceViewMode } from './PerformanceToolbar'
import { PerformanceTrendChart } from './PerformanceTrendChart'
import { PerformanceComparisonChart } from './PerformanceComparisonChart'
import { PerformanceTable } from './PerformanceTable'
import { RecomputeDialog } from './RecomputeDialog'
import { formatCount, formatCzk, formatMetric, type PerformanceMetric } from './metrics'

const WRITE_PERMISSION = 'marketing.performance.write'
const DEFAULT_YEARS = 3

const monthKey = (d: Date): string => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`

const rangeForPeriod = (period: PerformancePeriod): { from: string; to: string } => {
  const to = new Date()
  const from = new Date(to.getFullYear(), to.getMonth() - (period - 1), 1)
  return { from: monthKey(from), to: monthKey(to) }
}

const MarketingPerformancePage: React.FC = () => {
  const [viewMode, setViewMode] = useState<PerformanceViewMode>('trend')
  const [period, setPeriod] = useState<PerformancePeriod>(12)
  const [years, setYears] = useState<number>(DEFAULT_YEARS)
  const [metric, setMetric] = useState<PerformanceMetric>('pno')
  const [includeWholesale, setIncludeWholesale] = useState(false)
  const [isRecomputeOpen, setRecomputeOpen] = useState(false)

  useScreenView('Marketing', 'MarketingPerformance')
  const { hasPermission } = usePermissionsContext()
  const canRecompute = hasPermission(WRITE_PERMISSION)

  const range = useMemo(() => rangeForPeriod(period), [period])
  const months = useMarketingPerformanceMonthsQuery({ ...range, includeWholesale }, viewMode === 'trend')
  const comparison = useMarketingPerformanceComparisonQuery({ years, includeWholesale }, viewMode === 'comparison')

  const active = viewMode === 'trend' ? months : comparison
  const monthRows = months.data?.months ?? []
  const channels = (viewMode === 'trend' ? months.data?.channels : comparison.data?.channels) ?? []
  const lastRefreshAt = viewMode === 'trend' ? months.data?.lastRefreshAt : comparison.data?.lastRefreshAt
  const hasWarnings =
    viewMode === 'trend'
      ? monthRows.some((m) => m.hasData && (Boolean(m.lastError) || !m.revenueComputedAt || !m.costsComputedAt))
      : (comparison.data?.series ?? []).some((s) => s.months.some((m) => m.hasData && Boolean(m.lastError)))

  return (
    <div className="flex flex-col w-full" style={{ height: PAGE_CONTAINER_HEIGHT }}>
      <div className="flex-shrink-0 mb-3">
        <h1 className="text-lg font-semibold text-gray-900 dark:text-graphite-text">Výkon reklamy</h1>
        <p className="mt-1 text-gray-600 dark:text-graphite-muted">
          Náklady na reklamu (FB/IG, Google, S-Klik) proti objednávkám a tržbám z e-shopu, po měsících
        </p>
      </div>

      <div className="flex-1 overflow-auto">
        <PerformanceToolbar
          viewMode={viewMode}
          period={period}
          years={years}
          metric={metric}
          includeWholesale={includeWholesale}
          canRecompute={canRecompute}
          lastRefreshAt={lastRefreshAt}
          hasWarnings={hasWarnings}
          isRefetching={Boolean(months.isRefetching)}
          onViewModeChange={setViewMode}
          onPeriodChange={setPeriod}
          onYearsChange={setYears}
          onMetricChange={setMetric}
          onIncludeWholesaleChange={setIncludeWholesale}
          onRecomputeClick={() => setRecomputeOpen(true)}
        />

        {active.isLoading && (
          <div className="flex items-center justify-center py-12">
            <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
            <span className="ml-2 text-gray-600 dark:text-graphite-muted">Načítám data…</span>
          </div>
        )}

        {active.error && (
          <div className="mb-8 p-4 bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-900/50 rounded-lg">
            <div className="flex items-center">
              <AlertTriangle className="w-5 h-5 text-red-500 mr-2" />
              <h3 className="text-red-800 dark:text-red-300 font-medium">Chyba při načítání dat</h3>
            </div>
            <p className="mt-1 text-red-700 dark:text-red-300 text-sm">{active.error.message || 'Neznámá chyba'}</p>
          </div>
        )}

        {viewMode === 'trend' && months.data && (
          <>
            <PerformanceTrendChart months={monthRows} channels={channels} metric={metric} />
            <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark sm:rounded-md mb-8">
              <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-graphite-border">
                <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">Měsíční přehled</h3>
              </div>
              <PerformanceTable months={monthRows} channels={channels} />
            </div>
          </>
        )}

        {viewMode === 'comparison' && comparison.data && (
          <>
            <div className="grid grid-cols-2 md:grid-cols-3 gap-4 mb-6">
              {comparison.data.series.map((s) => (
                <div key={s.year} className="bg-white dark:bg-graphite-surface overflow-hidden shadow dark:shadow-soft-dark rounded-lg p-3">
                  <div className="text-xs font-semibold text-gray-700 dark:text-graphite-text mb-1">{s.year} (YTD)</div>
                  <dl className="space-y-0.5 text-xs">
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">Náklady</dt><dd className="font-medium">{formatCzk(s.ytdTotalCost)}</dd></div>
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">Tržby bez DPH</dt><dd className="font-medium">{formatCzk(s.ytdRevenueWithoutVat)}</dd></div>
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">Objednávky</dt><dd className="font-medium">{formatCount(s.ytdOrders)}</dd></div>
                    <div className="flex justify-between gap-2"><dt className="text-gray-500 dark:text-graphite-muted">PNO</dt><dd className="font-medium">{formatMetric(s.ytdPno, 'percent')}</dd></div>
                  </dl>
                </div>
              ))}
            </div>
            <PerformanceComparisonChart series={comparison.data.series} metric={metric} currentMonth={comparison.data.currentMonth} />
            <div className="bg-white dark:bg-graphite-surface shadow dark:shadow-soft-dark sm:rounded-md mb-8">
              <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-graphite-border">
                <h3 className="text-lg leading-6 font-medium text-gray-900 dark:text-graphite-text">Měsíce podle roku</h3>
              </div>
              <PerformanceTable months={comparison.data.series.flatMap((s) => s.months)} channels={channels} />
            </div>
          </>
        )}
      </div>

      <RecomputeDialog isOpen={isRecomputeOpen} onClose={() => setRecomputeOpen(false)} />
    </div>
  )
}

export default MarketingPerformancePage
```

- [ ] **Step 6: Route + sidebar**

`App.tsx`: add `import MarketingPerformancePage from "./components/marketing/performance/MarketingPerformancePage";` next to line 55 and, after the `/marketing/calendar` route (line 433):

```tsx
                        <Route path="/marketing/performance" element={guard("/marketing/performance", <MarketingPerformancePage />)} />
```

`Sidebar.tsx` line 183, after the "Kalendář" item:

```ts
        { id: "marketing-performance", name: "Výkon reklamy", href: "/marketing/performance", key: "/marketing/performance" },
```

The sidebar hides items whose `key` the user's menu paths do not include, so no extra permission check is needed here.

- [ ] **Step 7: Run tests, lint, build**

```bash
cd frontend
CI=true npx react-scripts test --watchAll=false src/components/marketing/performance src/api/hooks/__tests__/useMarketingPerformance.test.tsx
npm run lint
CI=false npm run build
```

Expected: all PASS, lint clean, build succeeds (the build is stricter than `tsc`; fix any type mismatch between the hooks' generated types and the components here, not in the generated file).

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/marketing/performance frontend/src/App.tsx frontend/src/components/layout/Sidebar.tsx
git commit -m "feat: Výkon reklamy page — toolbar, trend/comparison views, recompute dialog, route and menu"
```

---

## Task 19: E2E scenario

**Files:**
- Modify: `frontend/test/e2e/helpers/e2e-auth-helper.ts` (add `navigateToMarketingPerformance`)
- Create: `frontend/test/e2e/marketing/marketing-performance.spec.ts`

**Interfaces:**
- Consumes: `navigateToApp(page)`, `waitForLoadingComplete(page)` from the helper; staging at `PLAYWRIGHT_BASE_URL`.
- Precondition: the E2E user holds `marketing.performance.read` on staging and at least one month has been recomputed there (Task 21 step 3). Missing data must **throw**, not skip.

- [ ] **Step 1: Helper**

Append to `e2e-auth-helper.ts`, modelled on `navigateToMarketingCalendar`:

```ts
export async function navigateToMarketingPerformance(page: any): Promise<void> {
  await navigateToApp(page);
  await waitForLoadingComplete(page);
  const baseUrl = process.env.PLAYWRIGHT_FRONTEND_URL || process.env.PLAYWRIGHT_BASE_URL || 'https://heblo.stg.anela.cz';
  await page.goto(`${baseUrl}/marketing/performance`);
  await page.getByRole('heading', { name: 'Výkon reklamy', exact: true }).waitFor({ timeout: 15000 });
}
```

- [ ] **Step 2: Spec**

```ts
import { test, expect } from '@playwright/test';
import { navigateToMarketingPerformance } from '../helpers/e2e-auth-helper';

test.describe('Marketing — Výkon reklamy', () => {
  test.beforeEach(async ({ page }) => {
    await navigateToMarketingPerformance(page);
  });

  test('renders the monthly table with data', async ({ page }) => {
    const table = page.getByTestId('performance-table');
    await expect(table).toBeVisible({ timeout: 15000 });
    const rows = table.locator('tbody tr');
    const count = await rows.count();
    if (count === 0) {
      throw new Error('Staging has no marketing performance rows — run the recompute on staging before the nightly suite.');
    }
    await expect(table.getByRole('columnheader', { name: 'PNO', exact: true })).toBeVisible();
  });

  test('wholesale switch re-renders the table', async ({ page }) => {
    const firstRevenue = page.getByTestId('performance-table').locator('tbody tr').first().locator('td').nth(4);
    const before = await firstRevenue.textContent();
    await page.getByLabel('včetně velkoobchodu').check();
    await expect(page.getByTestId('performance-table')).toBeVisible();
    await expect(firstRevenue).not.toHaveText(before ?? '', { timeout: 10000 });
  });

  test('comparison view shows YTD cards and the comparison chart', async ({ page }) => {
    await page.getByLabel('Zobrazení', { exact: true }).selectOption('comparison');
    await expect(page.getByText(/\(YTD\)/).first()).toBeVisible({ timeout: 15000 });
    await expect(page.getByText(/Meziroční srovnání — /)).toBeVisible();
  });
});
```

If the first month has zero wholesale invoices the second test's "not.toHaveText" would be flaky; in that case compare the "Objednávky" column across all rows and assert at least one changed.

- [ ] **Step 3: Run against staging**

`./scripts/run-playwright-tests.sh frontend/test/e2e/marketing/marketing-performance.spec.ts` (after Task 21 deploys to staging). Expected: 3 PASS.

- [ ] **Step 4: Commit**

```bash
git add frontend/test/e2e/marketing/marketing-performance.spec.ts frontend/test/e2e/helpers/e2e-auth-helper.ts
git commit -m "test: e2e scenario for Výkon reklamy"
```

---

## Task 20: Documentation, memory, validation gates

**Files:**
- Create: `docs/features/marketing-performance.md`
- Modify: `memory/context/state.md`
- Modify: `docs/architecture/module-map.md` (add the module line if the file lists modules)

- [ ] **Step 1: Feature doc**

`docs/features/marketing-performance.md`:

```markdown
# Výkon reklamy (Marketing Performance)

Monthly snapshot of ad spend vs. e-shop orders/revenue, replacing the manual `Naklady_reklamy.xlsx`.
Spec: `docs/superpowers/specs/2026-09-18-marketing-performance-design.md`.

## Data
- **Costs**: ABRA Flexi received invoices, one REST call per month (`datUcto` in month, `dic in (…)`), bucketed to channels by supplier DIČ from `MarketingPerformance:Channels`. `storno` skipped, `sumZklCelkem` summed (without VAT).
- **Revenue/orders**: `IssuedInvoices` (Shoptet-synced), CZK only, by `TaxDate`. `Price` is with VAT; without-VAT = ÷ `VatRate` (1.21). Wholesale = `VatPayer` (customer has VAT ID), same rule as Flexi sales query 37. EUR invoices are counted (`SkippedEurInvoiceCount`) and excluded.
- Tables: `MarketingPerformanceMonths`, `MarketingPerformanceChannelCosts` (sums only; PNO/ROAS/averages/YoY derived at read time by `MarketingMetricsCalculator`).

## Job
`marketing-performance-refresh` (Hangfire, default 05:00 daily). Window = current + previous month (`RecomputeWindowMonths`, default 2). Older months are locked; `POST /api/marketingperformance/recompute` (write permission) recomputes any range ≤ 60 months regardless of locks. Per-month failures are recorded in `LastError`; the job fails only if every month failed.

## Screen
Marketing → Výkon reklamy (`/marketing/performance`), permission `Marketing_Performance` (read; write for recompute). Views: Vývoj (stacked channel costs + one metric line), Meziroční srovnání (Jan–Dec, one line per year, hover shows all years for the month). Switch "včetně velkoobchodu" adds wholesale at read time.

## Known discrepancy vs. the spreadsheet
Invoice-based numbers run 5–15 % above the spreadsheet's order counts (which came from Shoptet statistics). Accepted; the app is the definition from now on.

## Operations
- Settings: `MarketingPerformance` section in appsettings (VAT IDs are not secrets). Startup fails on duplicate/missing VAT IDs.
- Backfill: run the recompute 2023-01 → current month once after deploying to each environment.
- Grant `marketing.performance.read/.write` in `/admin/access` — seed groups don't update existing environments.
```

- [ ] **Step 2: Memory state**

Add to `memory/context/state.md` under "Recently Completed": branch `feature/import-advertising-costs`, spec + plan paths, the two facts (`IssuedInvoices.Price` is with-VAT / `PriceC` unused; prod DB is `Heblo_V3`), and what remains (staging migration, permission grant, backfill, nightly E2E).

- [ ] **Step 3: Full validation gates**

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests
cd frontend && npm run lint && CI=false npm run build && CI=true npx react-scripts test --watchAll=false
```

Expected: all green. Fix and re-run until they are; do not mark the task complete on a red gate.

- [ ] **Step 4: Commit**

```bash
git add docs/features/marketing-performance.md memory/context/state.md docs/architecture/module-map.md
git commit -m "docs: Výkon reklamy feature documentation and session state"
```

---

## Task 21: Rollout runbook (manual, owner-driven)

No code. Execute in order; each step has a verification.

- [ ] **Step 1: Fill in the real VAT IDs** in `appsettings.json` (`MarketingPerformance:Channels`) from Task 2's findings; commit (`chore: configure marketing channel VAT IDs`).

- [ ] **Step 2: Staging migration** — with `Database=Heblo_TST` active in `secrets.json`:
  `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`
  Verify: `select count(*) from public."MarketingPerformanceMonths";` → 0 rows, no error.

- [ ] **Step 3: Deploy to staging (PR → main → CI)**, then in `/admin/access` grant `marketing.performance.read` + `.write` to the Marketer group and `.read` to Vedení. Verify the menu item appears for the E2E user.

- [ ] **Step 4: Backfill on staging** — on the page click "Přepočítat", range `2023-01` → current month. Watch `/recurring-jobs` / Hangfire dashboard. Verify: table shows rows from 2023; January 2026 retail orders ≈ 2 354–2 700 and revenue s DPH ≈ 2.6–3.3 M CZK (documented discrepancy band); every channel column non-zero for months where invoices exist.

- [ ] **Step 5: Nightly E2E** — `./scripts/run-playwright-tests.sh frontend/test/e2e/marketing/marketing-performance.spec.ts` → PASS.

- [ ] **Step 6: Production** — repeat steps 2 (`Database=Heblo_V3`, from KV `ConnectionStrings--Production`), 3 and 4. Restore `secrets.json` afterwards.

- [ ] **Step 7: Retire the spreadsheet** — the owner confirms two months side by side, then stops updating `Naklady_reklamy.xlsx`.

---

## Self-review notes

- **Spec coverage**: §3 data model → Tasks 4–5; §4 job (window, locking, partial failure, recompute, SDK change, assumptions) → Tasks 1–3, 9–10; §5 API + error codes → Tasks 11–13; §6 permission → Task 13; §7 frontend → Tasks 14–18; §8 error handling → Tasks 6, 9, 12, 18; §9 testing → every task + Task 19; §10 rollout → Task 21.
- **Type consistency**: `YearMonth` API (Task 4) used identically in 5, 9, 10, 12; `MonthlyMarketingPerformanceDto` field names (Task 7) match `metrics.ts` accessors and `PerformanceTable` (Tasks 15–16); generated client method names (`marketingPerformance_GetMonths/GetComparison/Recompute`) assumed in Task 14 — verify against the actual generated file in Task 13 step 3 and adjust the hook if NSwag names differ.
- **Known judgement calls left to the implementer**: InMemory provider behaviour for `ChannelCosts.Clear()` (Task 9 step 5 has the fallback); whether `RecomputeMarketingPerformanceRequest` is generated as a class or interface (Task 14 step 3).
