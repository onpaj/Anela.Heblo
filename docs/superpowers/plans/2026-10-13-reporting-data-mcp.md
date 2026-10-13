# Reporting Data over MCP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let company staff query the GA4, Shoptet and Flexi reporting views in `Heblo_V3` through Heblo's MCP server, gated by one read permission per source and enforced in the application layer.

**Architecture:** A new `Reporting` vertical slice in `Anela.Heblo.Application`: a code-defined catalogue of 17 datasets (each wrapping one curated `v_*` view), a pure SQL builder that turns a structured query (columns / filters / sort / limit / offset) into parameterised SQL, and a reader that runs it on the shared `NpgsqlDataSource` inside a read-only transaction with a 10 s statement timeout. Three MediatR handlers (list / describe / query) check the per-source permission; a thin `ReportingMcpTools` class exposes them over MCP.

**Tech Stack:** .NET 8, MediatR, Npgsql (raw ADO.NET, not EF), ModelContextProtocol C# SDK, xUnit + FluentAssertions + Moq, Testcontainers PostgreSQL (`PostgresSharedContainerFixture`).

**Spec:** `docs/superpowers/specs/2026-10-13-reporting-data-mcp-design.md`

## Global Constraints

- Features: `Reporting_WebAnalytics` (GA4), `Reporting_Eshop` (Shoptet), `Reporting_Accounting` (Flexi); read only; role strings `reporting.web_analytics.read`, `reporting.eshop.read`, `reporting.accounting.read`.
- Permission is checked in the handlers (`ICurrentUserService.IsInRole(AccessRoles.For(feature, AccessLevel.Read))`), never only in the MCP tool.
- Only the 17 datasets in spec §4 are reachable. Never `v_posting`, `v_payroll_monthly`, `v_customer_repeat_purchase`, `v_product_pair_monthly`, `v_sync_health`, or any table.
- Identifiers come from the catalogue only, double-quoted; every value is an `NpgsqlParameter` with an explicit `NpgsqlDbType`.
- Execution: raw `NpgsqlConnection` from the DI `NpgsqlDataSource`, `BEGIN TRANSACTION READ ONLY`, `SET LOCAL statement_timeout = '10s'`. Not EF `SqlQuery` (Polly would retry a slow query).
- Limits: `limit` 1–1000 (default 100), `offset` 0–100000, ≤ 20 filters, ≤ 100 `in` values, ≤ 5 sort entries.
- Error codes: `ReportingDatasetNotFound = 3901` (404), `ReportingInvalidQuery = 3902` (400), `ReportingQueryTimeout = 3903` (504); permission failure = existing `Forbidden = 0014`.
- DTOs are classes, never records (`CLAUDE.md`). Every `*Response` inherits `BaseResponse`.
- Before declaring done: `dotnet build`, `dotnet format`, touched tests green; `npm run build` + `npm run lint` in `frontend` (i18n and the generated access-matrix TS change).

## Review Focus

1. **A user with one permission asks about another source** — `ListReportDatasets` must not even reveal that the other datasets exist, and Describe/Query must return `Forbidden`, not data. Pinned in Task 6 (`List_omits_datasets_without_permission`, `Query_without_permission_is_forbidden_and_never_reaches_the_reader`).
2. **Agent sends a month as `"2026-09"` instead of `"2026-09-01"`** — expected to work as the first day of the month. Pinned in Task 4 (`Date_accepts_year_month_shorthand`).
3. **`contains` with `%` or `_` in the search text** (e.g. `50%`) — must match literally, not as a wildcard. Pinned in Task 4 (`Contains_escapes_like_wildcards`).
4. **Paging through a ranked dataset with ties** — rows must not repeat or vanish between pages; the key-column tiebreak guarantees a total order. Pinned in Task 4 (`Key_columns_are_appended_as_tiebreak_once`).
5. **A slow query** — must stop at 10 s with `ReportingQueryTimeout`, not be retried and not hang the MCP call. Pinned in Task 5 (`Statement_timeout_raises_ReportQueryTimeoutException`, using a 1 s timeout override).

---

## File Structure

```
backend/src/Anela.Heblo.Application/Features/Reporting/
  ReportingModule.cs                       DI registration
  ReportAccess.cs                          permission check helper
  Catalog/
    ReportColumnType.cs                    enum Date/Text/Integer/Decimal/Boolean
    ReportColumn.cs                        column definition
    ReportSource.cs                        enum + per-source feature/label/freshness SQL
    ReportDataset.cs                       dataset definition
    ReportDatasetCatalog.cs                lookup + All
    ReportDatasetCatalog.WebAnalytics.cs   5 GA4 datasets
    ReportDatasetCatalog.Eshop.cs          8 Shoptet datasets
    ReportDatasetCatalog.Accounting.cs     4 Flexi datasets
  Query/
    ReportQuery.cs                         ReportQuery, ReportFilter, ReportSort (domain input)
    ReportQueryValidationException.cs
    ReportValueParser.cs                   string -> typed value per column type
    ReportSqlBuilder.cs                    ReportQuery -> ReportSqlCommand
    ReportSqlCommand.cs                    SQL + parameters + output columns
  Data/
    IReportingDataReader.cs
    NpgsqlReportingDataReader.cs
    UnavailableReportingDataReader.cs
    ReportQueryTimeoutException.cs
    ReportFreshness.cs
  Contracts/
    ReportDtos.cs                          DTOs (classes)
  UseCases/
    ReportDatasetMapper.cs                 catalogue -> DTO mapping shared by handlers
    ReportDatasetGate.cs                   dataset lookup + permission check shared by Describe/Query
    ListReportDatasets/ListReportDatasetsRequest.cs, ListReportDatasetsHandler.cs
    DescribeReportDataset/DescribeReportDatasetRequest.cs, DescribeReportDatasetHandler.cs
    QueryReportDataset/QueryReportDatasetRequest.cs, QueryReportDatasetHandler.cs
backend/src/Anela.Heblo.API/MCP/Tools/ReportingMcpTools.cs
backend/test/Anela.Heblo.Tests/Features/Reporting/...
backend/test/Anela.Heblo.Tests/MCP/Tools/ReportingMcpToolsTests.cs
docs/processes/module-reporting.md
```

---

### Task 1: Permissions

**Files:**
- Modify: `access-matrix.json` (features list after `Anela_ProcessDocs`; seed groups)
- Regenerated: `backend/src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs`, `AccessMatrix.generated.cs`, `AccessRoles.generated.cs`, `frontend/src/auth/accessMatrix.generated.ts`, `access-matrix-entra.generated.json`
- Test: `backend/test/Anela.Heblo.Tests/Features/Reporting/ReportingPermissionsTests.cs`

**Interfaces:**
- Produces: `Feature.Reporting_WebAnalytics`, `Feature.Reporting_Eshop`, `Feature.Reporting_Accounting`; `AccessRoles.For(feature, AccessLevel.Read)` returns `reporting.web_analytics.read` / `reporting.eshop.read` / `reporting.accounting.read`.

- [ ] **Step 1: Write the failing test**

```csharp
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportingPermissionsTests
{
    [Theory]
    [InlineData(Feature.Reporting_WebAnalytics, "reporting.web_analytics.read")]
    [InlineData(Feature.Reporting_Eshop, "reporting.eshop.read")]
    [InlineData(Feature.Reporting_Accounting, "reporting.accounting.read")]
    public void Each_source_has_a_read_role(Feature feature, string expectedRole)
    {
        AccessRoles.For(feature, AccessLevel.Read).Should().Be(expectedRole);
    }
}
```

- [ ] **Step 2: Run it — expect a compile failure** (`Feature.Reporting_WebAnalytics` does not exist)

Run: `cd backend && dotnet build test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | tail -5`

- [ ] **Step 3: Add the features to `access-matrix.json`**

In the `features` array, directly after `{ "key": "Anela_ProcessDocs", "label": "Procesní dokumentace" },` add:

```json
    { "key": "Reporting_WebAnalytics", "label": "Data: Google Analytics" },
    { "key": "Reporting_Eshop", "label": "Data: Shoptet objednávky" },
    { "key": "Reporting_Accounting", "label": "Data: Účetnictví (Flexi)" },
```

In `seedGroups`, append to the `roles` arrays (these only affect fresh databases):
- `Spravce`, `Vedeni`: `"reporting.web_analytics.read", "reporting.eshop.read", "reporting.accounting.read"`
- `Marketer`: `"reporting.web_analytics.read", "reporting.eshop.read"`
- `Ucetni`: `"reporting.accounting.read"`
- `Eshop_Administrator`: `"reporting.eshop.read"`

- [ ] **Step 4: Regenerate the access-matrix artifacts**

Run: `cd backend && dotnet run --project tools/Anela.Heblo.AccessMatrixGen -- ../access-matrix.json src/Anela.Heblo.Domain/Features/Authorization/Feature.generated.cs src/Anela.Heblo.Domain/Features/Authorization/AccessMatrix.generated.cs src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs ../frontend/src/auth/accessMatrix.generated.ts ../access-matrix-entra.generated.json`
Expected: the five generated files change; `git diff --stat` shows each with a few added lines and `Feature.generated.cs` lists the three new values.

- [ ] **Step 5: Run the test — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReportingPermissionsTests" -p:UseSharedCompilation=false`

- [ ] **Step 6: Commit**

```bash
git add access-matrix.json access-matrix-entra.generated.json frontend/src/auth/accessMatrix.generated.ts backend/src/Anela.Heblo.Domain/Features/Authorization/*.generated.cs backend/test/Anela.Heblo.Tests/Features/Reporting/ReportingPermissionsTests.cs
git commit -m "feat(reporting): add per-source reporting read permissions"
```

---

### Task 2: Error codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (after the 38XX block, before `// External Service errors (90XX)`)
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs` (range buckets + assertion)
- Modify: `frontend/src/i18n.ts` (Czech translations, after `PricingProductNotInBaseline`)

**Interfaces:**
- Produces: `ErrorCodes.ReportingDatasetNotFound`, `ErrorCodes.ReportingInvalidQuery`, `ErrorCodes.ReportingQueryTimeout`.

- [ ] **Step 1: Add the bucket assertion to `ErrorHandlingTests` (failing test)**

After the `pricingErrors` line add:

```csharp
        var reportingErrors = errorCodes.Where(code => code >= 3900 && code < 4000).ToList(); // 39XX range (Reporting)
```

and next to the other `Assert.True(...)` lines for 37XX/38XX add:

```csharp
        Assert.True(reportingErrors.Count > 0, "Should have Reporting errors in 39XX range");
```

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ErrorHandlingTests" -p:UseSharedCompilation=false`
Expected: FAIL — "Should have Reporting errors in 39XX range".

- [ ] **Step 2: Add the codes**

```csharp
    // Reporting module errors (39XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    ReportingDatasetNotFound = 3901,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    ReportingInvalidQuery = 3902,
    [HttpStatusCode(HttpStatusCode.GatewayTimeout)]
    ReportingQueryTimeout = 3903,

```

- [ ] **Step 3: Add the Czech translations in `frontend/src/i18n.ts`**, after the `PricingProductNotInBaseline` entry:

```ts
        // Reporting module errors (39XX)
        ReportingDatasetNotFound: "Datová sada nebyla nalezena",
        ReportingInvalidQuery: "Neplatný dotaz na datovou sadu",
        ReportingQueryTimeout: "Dotaz trval příliš dlouho, zužte filtr",
```

- [ ] **Step 4: Run the error-code tests — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ErrorHandlingTests|FullyQualifiedName~ErrorCode" -p:UseSharedCompilation=false`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs frontend/src/i18n.ts
git commit -m "feat(reporting): add 39XX reporting error codes"
```

---

### Task 3: Dataset catalogue

**Files:**
- Create: everything under `backend/src/Anela.Heblo.Application/Features/Reporting/Catalog/`
- Test: `backend/test/Anela.Heblo.Tests/Features/Reporting/ReportDatasetCatalogTests.cs`

**Interfaces:**
- Produces:
  - `enum ReportColumnType { Date, Text, Integer, Decimal, Boolean }`
  - `sealed class ReportColumn(string Name, ReportColumnType Type, string Description)` with get-only props `Name`, `Type`, `Description`
  - `enum ReportSource { WebAnalytics, Eshop, Accounting }`
  - `static class ReportSources` — `Feature FeatureFor(ReportSource)`, `string LabelFor(ReportSource)`, `string FreshnessSqlFor(ReportSource)`
  - `sealed class ReportDataset` — `Id`, `Source`, `Schema`, `View`, `Description`, `Grain`, `PeriodColumn`, `IReadOnlyList<ReportColumn> Columns`, `IReadOnlyList<string> KeyColumns`, `IReadOnlyList<(string Column, bool Descending)> DefaultSort`, `IReadOnlyList<string> Caveats`, `ReportColumn? FindColumn(string name)` (case-insensitive)
  - `static class ReportDatasetCatalog` — `IReadOnlyList<ReportDataset> All`, `ReportDataset? Find(string id)` (case-insensitive)

- [ ] **Step 1: Write the failing catalogue tests**

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportDatasetCatalogTests
{
    private static readonly string[] ForbiddenViews =
        ["v_posting", "v_payroll_monthly", "v_customer_repeat_purchase", "v_product_pair_monthly", "v_sync_health"];

    [Fact]
    public void Catalog_holds_the_seventeen_v1_datasets()
    {
        ReportDatasetCatalog.All.Should().HaveCount(17);
        ReportDatasetCatalog.All.Count(d => d.Source == ReportSource.WebAnalytics).Should().Be(5);
        ReportDatasetCatalog.All.Count(d => d.Source == ReportSource.Eshop).Should().Be(8);
        ReportDatasetCatalog.All.Count(d => d.Source == ReportSource.Accounting).Should().Be(4);
    }

    [Fact]
    public void Dataset_ids_are_unique_and_prefixed_by_source()
    {
        ReportDatasetCatalog.All.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        foreach (var d in ReportDatasetCatalog.All)
        {
            var prefix = d.Source switch
            {
                ReportSource.WebAnalytics => "ga4.",
                ReportSource.Eshop => "shoptet.",
                ReportSource.Accounting => "flexi.",
                _ => throw new InvalidOperationException(),
            };
            d.Id.Should().StartWith(prefix);
        }
    }

    [Fact]
    public void Every_referenced_column_exists_in_its_dataset()
    {
        foreach (var d in ReportDatasetCatalog.All)
        {
            d.FindColumn(d.PeriodColumn).Should().NotBeNull($"{d.Id} period column");
            d.FindColumn(d.PeriodColumn)!.Type.Should().Be(ReportColumnType.Date, d.Id);
            d.KeyColumns.Should().NotBeEmpty(d.Id);
            d.KeyColumns.Should().OnlyContain(k => d.FindColumn(k) != null, d.Id);
            d.DefaultSort.Should().NotBeEmpty(d.Id);
            d.DefaultSort.Should().OnlyContain(s => d.FindColumn(s.Column) != null, d.Id);
            d.Columns.Select(c => c.Name).Should().OnlyHaveUniqueItems(d.Id);
            d.Columns.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Description), d.Id);
        }
    }

    [Fact]
    public void Confidential_and_expensive_views_are_never_exposed()
    {
        ReportDatasetCatalog.All.Select(d => d.View).Should().NotContain(ForbiddenViews);
        ReportDatasetCatalog.All.Should().OnlyContain(d => d.View.StartsWith("v_"));
        ReportDatasetCatalog.All.Should().OnlyContain(d =>
            d.Schema == "ga4_agg" || d.Schema == "shoptet_raw" || d.Schema == "flexi_raw");
    }

    [Fact]
    public void Each_source_maps_to_its_own_feature()
    {
        ReportSources.FeatureFor(ReportSource.WebAnalytics).Should().Be(Feature.Reporting_WebAnalytics);
        ReportSources.FeatureFor(ReportSource.Eshop).Should().Be(Feature.Reporting_Eshop);
        ReportSources.FeatureFor(ReportSource.Accounting).Should().Be(Feature.Reporting_Accounting);
    }

    [Fact]
    public void Find_is_case_insensitive_and_returns_null_for_unknown()
    {
        ReportDatasetCatalog.Find("SHOPTET.ORDERS_MONTHLY")!.View.Should().Be("v_order_monthly");
        ReportDatasetCatalog.Find("shoptet.nope").Should().BeNull();
    }
}
```

- [ ] **Step 2: Run — expect compile failure** (namespace `Features.Reporting.Catalog` missing)

Run: `cd backend && dotnet build test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep -c "error CS"`

- [ ] **Step 3: Create the model types**

`Catalog/ReportColumnType.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public enum ReportColumnType
{
    Date,
    Text,
    Integer,
    Decimal,
    Boolean,
}
```

`Catalog/ReportColumn.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public sealed class ReportColumn
{
    public ReportColumn(string name, ReportColumnType type, string description)
    {
        Name = name;
        Type = type;
        Description = description;
    }

    public string Name { get; }
    public ReportColumnType Type { get; }
    public string Description { get; }

    public static ReportColumn Date(string name, string description) => new(name, ReportColumnType.Date, description);
    public static ReportColumn Text(string name, string description) => new(name, ReportColumnType.Text, description);
    public static ReportColumn Int(string name, string description) => new(name, ReportColumnType.Integer, description);
    public static ReportColumn Dec(string name, string description) => new(name, ReportColumnType.Decimal, description);
    public static ReportColumn Bool(string name, string description) => new(name, ReportColumnType.Boolean, description);
}
```

`Catalog/ReportSource.cs`:

```csharp
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public enum ReportSource
{
    WebAnalytics,
    Eshop,
    Accounting,
}

public static class ReportSources
{
    // Each returns (last_sync_at timestamptz, all_ok boolean) from the source's own sync_state.
    private const string Ga4FreshnessSql =
        "SELECT min(last_run_finished_at), bool_and(last_run_status = 'OK') FROM ga4_agg.sync_state";
    private const string ShoptetFreshnessSql =
        "SELECT max(last_run_finished_at), bool_and(last_run_status = 'OK') FROM shoptet_raw.sync_state WHERE entity_name = 'order'";
    private const string FlexiFreshnessSql =
        "SELECT min(last_run_finished_at), bool_and(last_run_status = 'OK') FROM flexi_raw.sync_state";

    public static Feature FeatureFor(ReportSource source) => source switch
    {
        ReportSource.WebAnalytics => Feature.Reporting_WebAnalytics,
        ReportSource.Eshop => Feature.Reporting_Eshop,
        ReportSource.Accounting => Feature.Reporting_Accounting,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static string LabelFor(ReportSource source) => source switch
    {
        ReportSource.WebAnalytics => "Google Analytics 4",
        ReportSource.Eshop => "Shoptet e-shop orders",
        ReportSource.Accounting => "ABRA Flexi accounting",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    public static string FreshnessSqlFor(ReportSource source) => source switch
    {
        ReportSource.WebAnalytics => Ga4FreshnessSql,
        ReportSource.Eshop => ShoptetFreshnessSql,
        ReportSource.Accounting => FlexiFreshnessSql,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };
}
```

`Catalog/ReportDataset.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public sealed class ReportDataset
{
    public required string Id { get; init; }
    public required ReportSource Source { get; init; }
    public required string Schema { get; init; }
    public required string View { get; init; }
    public required string Description { get; init; }
    public required string Grain { get; init; }
    public required string PeriodColumn { get; init; }
    public required IReadOnlyList<ReportColumn> Columns { get; init; }
    public required IReadOnlyList<string> KeyColumns { get; init; }
    public required IReadOnlyList<(string Column, bool Descending)> DefaultSort { get; init; }
    public IReadOnlyList<string> Caveats { get; init; } = [];

    public ReportColumn? FindColumn(string name) =>
        Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
```

`Catalog/ReportDatasetCatalog.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Catalog;

/// <summary>
/// The only relations reachable through the reporting tools. Each entry wraps one curated v_* read
/// view (the same views granted to metabase_ro). Never add raw tables, v_posting,
/// v_payroll_monthly or v_customer_repeat_purchase — see the spec, decision D7.
/// </summary>
public static partial class ReportDatasetCatalog
{
    public static IReadOnlyList<ReportDataset> All { get; } =
        [.. WebAnalytics(), .. Eshop(), .. Accounting()];

    public static ReportDataset? Find(string id) =>
        All.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 4: Create the GA4 datasets** — `Catalog/ReportDatasetCatalog.WebAnalytics.cs`

```csharp
using static Anela.Heblo.Application.Features.Reporting.Catalog.ReportColumn;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public static partial class ReportDatasetCatalog
{
    private const string Ga4Schema = "ga4_agg";
    private const string MonthDesc = "Calendar month (first day of the month).";
    private const string PartialMonth = "The current month is partial — check is_partial_month before comparing it with full months.";

    private static IEnumerable<ReportDataset> WebAnalytics()
    {
        yield return new ReportDataset
        {
            Id = "ga4.traffic_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_traffic",
            Description = "Total anela.cz website traffic per month with month-over-month and year-over-year comparison.",
            Grain = "month",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("sessions", "Sessions in the month."),
                Int("total_users", "GA4's own de-duplicated monthly users (never sum daily users)."),
                Int("new_users", "First-time users in the month."),
                Int("page_views", "Screen/page views."),
                Int("engaged_sessions", "Sessions GA4 counts as engaged."),
                Dec("bounce_rate", "Share of non-engaged sessions, 0–1."),
                Dec("avg_engagement_seconds_per_session", "Average engagement time per session, seconds."),
                Int("days_with_data", "Days of the month present in the data."),
                Int("sessions_prev_month", "Sessions in the previous month."),
                Dec("sessions_mom_pct", "Month-over-month change of sessions, percent."),
                Int("sessions_same_month_last_year", "Sessions in the same month a year earlier."),
                Dec("sessions_yoy_pct", "Year-over-year change of sessions, percent."),
                Int("total_users_same_month_last_year", "Users in the same month a year earlier."),
                Dec("total_users_yoy_pct", "Year-over-year change of users, percent."),
                Bool("is_partial_month", "True for the month still in progress."),
            ],
            KeyColumns = ["month"],
            DefaultSort = [("month", true)],
            Caveats =
            [
                "Matches the GA4 UI. Do not rebuild these totals from ga4.traffic_by_channel_monthly: GA4 totals do not decompose.",
                "History starts 2023-06 (GA4 property creation).",
                PartialMonth,
            ],
        };

        yield return new ReportDataset
        {
            Id = "ga4.traffic_by_channel_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_traffic_by_channel",
            Description = "Sessions per GA4 default channel group (Organic Search, Paid Social, Direct, …) per month.",
            Grain = "month × channel group",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("channel_group", "GA4 default channel group."),
                Dec("sessions", "Sessions from this channel."),
                Dec("channel_users", "Users from this channel (do not sum across channels or days)."),
                Dec("channel_new_users", "New users from this channel."),
                Dec("engaged_sessions", "Engaged sessions from this channel."),
                Dec("pct_of_month_sessions", "Share of the month's sessions, percent."),
                Dec("sessions_prev_month", "Sessions from this channel in the previous month."),
                Dec("sessions_same_month_last_year", "Sessions from this channel a year earlier."),
                Dec("sessions_yoy_pct", "Year-over-year change, percent."),
            ],
            KeyColumns = ["month", "channel_group"],
            DefaultSort = [("month", true), ("sessions", true)],
            Caveats =
            [
                "Use for the shape of acquisition, not for totals: channel sessions sum 0.0–3.8% above the site total. Totals are in ga4.traffic_monthly.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "ga4.landing_pages_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_landing_pages",
            Description = "Landing pages ranked by sessions per month, with previous-month and year-ago rank and sessions.",
            Grain = "month × landing page",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("rank_in_month", "Rank by sessions within the month, 1 = most sessions."),
                Text("landing_page", "Landing page path."),
                Dec("sessions", "Sessions that started on this page."),
                Dec("engaged_sessions", "Engaged sessions that started on this page."),
                Dec("bounce_rate", "Share of non-engaged sessions, 0–1."),
                Dec("sessions_prev_month", "Sessions in the previous month."),
                Int("rank_prev_month", "Rank in the previous month."),
                Dec("sessions_same_month_last_year", "Sessions a year earlier."),
                Int("rank_same_month_last_year", "Rank a year earlier."),
                Dec("sessions_yoy_pct", "Year-over-year change, percent."),
            ],
            KeyColumns = ["month", "landing_page"],
            DefaultSort = [("month", true), ("rank_in_month", false)],
            Caveats =
            [
                "Built from a top-100-pages-per-day capture: the head of the ranking is exact, the sum of sessions is not a site total.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "ga4.articles_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_articles",
            Description = "Pages ranked by page views per month (filter page_path, e.g. contains '/blog', for articles).",
            Grain = "month × page",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("rank_in_month", "Rank by page views within the month, 1 = most viewed."),
                Text("page_path", "Page path."),
                Text("page_title", "Most frequent page title for the path."),
                Dec("page_views", "Page views."),
                Dec("sessions", "Sessions that viewed the page."),
                Dec("page_views_prev_month", "Page views in the previous month."),
                Int("rank_prev_month", "Rank in the previous month."),
                Dec("page_views_same_month_last_year", "Page views a year earlier."),
                Int("rank_same_month_last_year", "Rank a year earlier."),
                Dec("page_views_yoy_pct", "Year-over-year change, percent."),
            ],
            KeyColumns = ["month", "page_path"],
            DefaultSort = [("month", true), ("rank_in_month", false)],
            Caveats = ["Only the top 100 pages per day are captured."],
        };

        yield return new ReportDataset
        {
            Id = "ga4.conversion_monthly",
            Source = ReportSource.WebAnalytics,
            Schema = Ga4Schema,
            View = "v_monthly_conversion",
            Description = "GA4 sessions against GA4 purchase events per month (website conversion trend).",
            Grain = "month",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Int("sessions", "Sessions in the month."),
                Dec("transactions", "GA4 purchase events."),
                Dec("purchase_revenue", "Revenue reported by GA4 purchase events."),
                Dec("conversion_rate_pct", "transactions / sessions, percent."),
                Dec("avg_order_value", "purchase_revenue / transactions."),
                Int("sessions_prev_month", "Sessions in the previous month."),
                Dec("transactions_prev_month", "Purchase events in the previous month."),
                Dec("conversion_rate_pct_prev_month", "Conversion rate in the previous month, percent."),
                Int("sessions_same_month_last_year", "Sessions a year earlier."),
                Dec("transactions_same_month_last_year", "Purchase events a year earlier."),
                Dec("conversion_rate_pct_same_month_last_year", "Conversion rate a year earlier, percent."),
                Dec("transactions_yoy_pct", "Year-over-year change of purchase events, percent."),
                Bool("is_partial_month", "True for the month still in progress."),
            ],
            KeyColumns = ["month"],
            DefaultSort = [("month", true)],
            Caveats =
            [
                "WILL NOT MATCH THE ERP: GA4 purchase events count cancelled and unpaid orders, can double-fire on a thank-you-page reload, and miss phone/e-mail orders and consent-blocked shoppers. Valid as a website trend, never as the company conversion rate — use shoptet.orders_monthly for real orders and revenue.",
                PartialMonth,
            ],
        };
    }
}
```

- [ ] **Step 5: Create the Shoptet datasets** — `Catalog/ReportDatasetCatalog.Eshop.cs`

```csharp
using static Anela.Heblo.Application.Features.Reporting.Catalog.ReportColumn;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public static partial class ReportDatasetCatalog
{
    private const string ShoptetSchema = "shoptet_raw";
    private const string OrderMonthDesc = "Order month (first day, Europe/Prague).";
    private const string ChannelDesc = "Sales channel: eshop_mo (retail e-shop), eshop_vo (wholesale e-shop), prodejna (in-store cash desk).";
    private const string CustomerStatusDesc = "new = first ever non-cancelled order; returning = previous order within 12 months; returning_lapsed = previous order more than 12 months ago; unknown = no identity (cash desk).";
    private const string RevenueCaveat = "Amounts are CZK (foreign-currency orders converted). *_with_vat includes VAT. Cancelled orders are excluded.";
    private const string OrderedNotShipped = "Counts are pieces/orders ORDERED (order date), not packed or shipped.";

    private static IEnumerable<ReportDataset> Eshop()
    {
        yield return new ReportDataset
        {
            Id = "shoptet.orders_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_order_monthly",
            Description = "Orders, customers, revenue and units per month and sales channel — the company's real order numbers.",
            Grain = "month × channel",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Int("orders", "Non-cancelled orders."),
                Int("customers", "Distinct customers."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK (from the order header, net of discounts)."),
                Dec("revenue_without_vat_czk", "Order revenue excl. VAT, CZK."),
                Dec("product_revenue_with_vat_czk", "Revenue from products only, incl. VAT, CZK."),
                Dec("shipping_charged_with_vat_czk", "Shipping charged to customers, incl. VAT, CZK."),
                Dec("payment_fee_with_vat_czk", "Payment-method fees charged, incl. VAT, CZK."),
                Dec("discount_with_vat_czk", "Discounts given, incl. VAT, CZK."),
                Dec("units", "Product pieces ordered."),
                Dec("avg_order_value_with_vat_czk", "Average order value incl. VAT, CZK."),
                Dec("avg_basket_units", "Average pieces per order."),
            ],
            KeyColumns = ["order_month", "channel"],
            DefaultSort = [("order_month", true), ("channel", false)],
            Caveats = [RevenueCaveat, OrderedNotShipped, "History starts 2018-09."],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.orders_by_shipping_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_order_shipping_monthly",
            Description = "Orders and shipping charged per shipping method, month and channel.",
            Grain = "month × channel × shipping method",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("shipping_name", "Shipping method name."),
                Text("shipping_guid", "Shoptet shipping method id."),
                Int("orders", "Non-cancelled orders."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK."),
                Dec("shipping_charged_with_vat_czk", "Shipping charged to customers, incl. VAT, CZK."),
                Dec("avg_shipping_charged_with_vat_czk", "Average shipping charged per order, CZK."),
                Int("orders_with_free_shipping", "Orders with zero shipping charge."),
            ],
            KeyColumns = ["order_month", "channel", "shipping_name", "shipping_guid"],
            DefaultSort = [("order_month", true), ("orders", true)],
            Caveats = [RevenueCaveat],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.orders_by_payment_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_order_payment_monthly",
            Description = "Orders and payment fees per payment method, month and channel.",
            Grain = "month × channel × payment method",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("payment_method_name", "Payment method name."),
                Int("orders", "Non-cancelled orders."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK."),
                Dec("avg_order_value_with_vat_czk", "Average order value incl. VAT, CZK."),
                Dec("payment_fee_with_vat_czk", "Payment-method fees charged, incl. VAT, CZK."),
            ],
            KeyColumns = ["order_month", "channel", "payment_method_name"],
            DefaultSort = [("order_month", true), ("orders", true)],
            Caveats = [RevenueCaveat],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.customer_types_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_customer_type_monthly",
            Description = "Orders, customers and revenue split into new vs returning customers per month and channel.",
            Grain = "month × channel × customer status",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("customer_status", CustomerStatusDesc),
                Int("orders", "Non-cancelled orders."),
                Int("customers", "Distinct customers."),
                Dec("revenue_with_vat_czk", "Order revenue incl. VAT, CZK."),
                Dec("avg_order_value_with_vat_czk", "Average order value incl. VAT, CZK."),
                Dec("units", "Product pieces ordered."),
                Dec("avg_basket_units", "Average pieces per order."),
            ],
            KeyColumns = ["order_month", "channel", "customer_status"],
            DefaultSort = [("order_month", true), ("channel", false), ("customer_status", false)],
            Caveats = [RevenueCaveat, "A customer is identified by e-mail (guest checkouts included)."],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.new_customers_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_new_customers_monthly",
            Description = "Newly acquired customers (first ever order) per month and channel, with first-order value.",
            Grain = "month × channel",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Int("new_customers", "Customers whose first ever non-cancelled order fell in this month."),
                Dec("first_order_revenue_with_vat_czk", "Revenue of those first orders, incl. VAT, CZK."),
                Dec("avg_first_order_value_with_vat_czk", "Average first-order value incl. VAT, CZK."),
                Dec("avg_first_order_units", "Average pieces in a first order."),
            ],
            KeyColumns = ["order_month", "channel"],
            DefaultSort = [("order_month", true), ("channel", false)],
            Caveats = [RevenueCaveat, "A customer is identified by e-mail (guest checkouts included)."],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.product_sales_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_product_sales_monthly",
            Description = "Units and revenue per product variant, month and channel. Filter by product_code or product_name.",
            Grain = "month × channel × product variant",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("product_code", "Product code (e.g. SER004030)."),
                Text("product_name", "Product name."),
                Text("variant_name", "Variant name (size etc.), may be empty."),
                Text("item_type", "product or product-set."),
                Int("orders", "Orders containing the item."),
                Dec("units", "Pieces ordered."),
                Dec("revenue_with_vat_czk", "Item revenue incl. VAT, CZK."),
                Dec("revenue_without_vat_czk", "Item revenue excl. VAT, CZK."),
            ],
            KeyColumns = ["order_month", "channel", "product_code", "variant_name", "item_type"],
            DefaultSort = [("order_month", true), ("revenue_with_vat_czk", true)],
            Caveats =
            [
                RevenueCaveat,
                OrderedNotShipped,
                "Sets are counted as the set (item_type product-set); for the pieces inside sets use shoptet.set_component_units_monthly.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.product_sales_by_customer_type_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_product_sales_by_customer_type_monthly",
            Description = "Units and revenue per product variant split by new vs returning customers, per month and channel.",
            Grain = "month × channel × customer status × product variant",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("customer_status", CustomerStatusDesc),
                Text("product_code", "Product code."),
                Text("product_name", "Product name."),
                Text("variant_name", "Variant name, may be empty."),
                Int("orders", "Orders containing the item."),
                Dec("units", "Pieces ordered."),
                Dec("revenue_with_vat_czk", "Item revenue incl. VAT, CZK."),
            ],
            KeyColumns = ["order_month", "channel", "customer_status", "product_code", "variant_name"],
            DefaultSort = [("order_month", true), ("revenue_with_vat_czk", true)],
            Caveats = [RevenueCaveat, OrderedNotShipped],
        };

        yield return new ReportDataset
        {
            Id = "shoptet.set_component_units_monthly",
            Source = ReportSource.Eshop,
            Schema = ShoptetSchema,
            View = "v_product_set_component_units_monthly",
            Description = "Pieces of each product sold inside product sets, per set, month and channel.",
            Grain = "month × channel × component product × set",
            PeriodColumn = "order_month",
            Columns =
            [
                Date("order_month", OrderMonthDesc),
                Text("channel", ChannelDesc),
                Text("product_code", "Component product code."),
                Text("product_name", "Component product name."),
                Text("variant_name", "Component variant name, may be empty."),
                Text("set_code", "Code of the set the component was sold in."),
                Int("orders", "Orders containing the set."),
                Dec("units", "Component pieces ordered via the set."),
            ],
            KeyColumns = ["order_month", "channel", "product_code", "variant_name", "set_code"],
            DefaultSort = [("order_month", true), ("units", true)],
            Caveats = [OrderedNotShipped, "Cancelled orders are excluded."],
        };
    }
}
```

- [ ] **Step 6: Create the Flexi datasets** — `Catalog/ReportDatasetCatalog.Accounting.cs`

```csharp
using static Anela.Heblo.Application.Features.Reporting.Catalog.ReportColumn;

namespace Anela.Heblo.Application.Features.Reporting.Catalog;

public static partial class ReportDatasetCatalog
{
    private const string FlexiSchema = "flexi_raw";
    private const string CostCenterDesc = "Cost centre (středisko): BUVOL, C, MARKETING, PRODEJNA, SKLAD, VYROBA.";
    private const string NetCostDesc = "Net cost in CZK (debits minus credits), excl. VAT.";
    private const string PostingCountDesc = "Number of ledger postings behind the figure.";
    private const string CostScope = "Costs are accounts of classes 50–56 only (58x contra-costs and 59x income tax excluded) and EXCLUDE personnel/payroll cost, which is not available here.";
    private const string LedgerSource = "Source: ABRA Flexi general ledger, synced nightly; history from 2020-01. The latest month is still being booked.";

    private static IEnumerable<ReportDataset> Accounting()
    {
        yield return new ReportDataset
        {
            Id = "flexi.cost_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_cost_monthly_total",
            Description = "Company operating cost per month and cost centre.",
            Grain = "month × cost centre",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("cost_center", CostCenterDesc),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "cost_center"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats = [CostScope, LedgerSource],
        };

        yield return new ReportDataset
        {
            Id = "flexi.cost_by_account_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_cost_monthly_by_account",
            Description = "Operating cost per month, cost centre and 6-digit account (drill-down of flexi.cost_monthly).",
            Grain = "month × cost centre × account",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("cost_center", CostCenterDesc),
                Text("account", "6-digit account number, e.g. 504000."),
                Text("account_name", "Account name."),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "cost_center", "account"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats =
            [
                CostScope,
                LedgerSource,
                "Goods sold (504) and material consumption (501) are both class 50 — group by account, not by a '50' prefix.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "flexi.marketing_spend_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_marketing_spend_monthly",
            Description = "Marketing spend per month by account, category, cost centre and supplier.",
            Grain = "month × account × category × cost centre × supplier",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("account", "6-digit account number."),
                Text("account_name", "Account name."),
                Text("category", "Marketing category: performance, brand, externiste, darky, trhy, vyrobky-na-reklamu, ostatni."),
                Text("cost_center", CostCenterDesc),
                Text("supplier", "Supplier (contact) name."),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "account", "category", "cost_center", "supplier"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats =
            [
                LedgerSource,
                "Grouped by account, not by accounting template (the ledger carries no předkontace). Graphics, photography, PR and influencers share account 518030 — only supplier tells them apart.",
            ],
        };

        yield return new ReportDataset
        {
            Id = "flexi.ad_spend_monthly",
            Source = ReportSource.Accounting,
            Schema = FlexiSchema,
            View = "v_ad_spend_monthly",
            Description = "Booked ad spend per month and channel (meta, google, sklik), from supplier invoices.",
            Grain = "month × channel × supplier × account",
            PeriodColumn = "month",
            Columns =
            [
                Date("month", MonthDesc),
                Text("channel", "Ad channel: meta, google, sklik."),
                Text("supplier", "Supplier (contact) name."),
                Text("account", "6-digit account number."),
                Text("account_name", "Account name."),
                Dec("net_cost", NetCostDesc),
                Int("posting_count", PostingCountDesc),
            ],
            KeyColumns = ["month", "channel", "supplier", "account"],
            DefaultSort = [("month", true), ("net_cost", true)],
            Caveats =
            [
                LedgerSource,
                "Channel is matched on the supplier label, not on a key. Booking date follows the invoice, which can lag the ad platform's own spend by a month.",
            ],
        };
    }
}
```

Note: `MonthDesc` is declared in the WebAnalytics partial and reused here (same partial class).

- [ ] **Step 7: Run the catalogue tests — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReportDatasetCatalogTests" -p:UseSharedCompilation=false`

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Reporting/Catalog backend/test/Anela.Heblo.Tests/Features/Reporting/ReportDatasetCatalogTests.cs
git commit -m "feat(reporting): dataset catalogue over the curated reporting views"
```

---

### Task 4: Query model, value parser and SQL builder

**Files:**
- Create: `Features/Reporting/Query/ReportQuery.cs`, `ReportQueryValidationException.cs`, `ReportValueParser.cs`, `ReportSqlCommand.cs`, `ReportSqlBuilder.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Reporting/ReportSqlBuilderTests.cs`

**Interfaces:**
- Consumes: `ReportDataset`, `ReportColumn`, `ReportColumnType` (Task 3).
- Produces:
  - `sealed class ReportFilter { string Column; string Op; string? Value; IReadOnlyList<string>? Values }` (init props)
  - `sealed class ReportSort { string Column; string Direction }` (init props; `"asc"`/`"desc"`)
  - `sealed class ReportQuery { IReadOnlyList<string>? Columns; IReadOnlyList<ReportFilter> Filters = []; IReadOnlyList<ReportSort> Sort = []; int? Limit; int? Offset }`
  - `sealed class ReportQueryValidationException(string reason) : Exception`
  - `sealed class ReportSqlCommand { string Sql; IReadOnlyList<NpgsqlParameter> Parameters; IReadOnlyList<ReportColumn> OutputColumns; int Limit }`
  - `static class ReportSqlBuilder` — `ReportSqlCommand Build(ReportDataset dataset, ReportQuery query)`; constants `DefaultLimit = 100`, `MaxLimit = 1000`, `MaxOffset = 100_000`, `MaxFilters = 20`, `MaxInValues = 100`, `MaxSortColumns = 5`.
  - The SQL fetches `Limit + 1` rows; the reader uses the extra row for `hasMore`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;
using FluentAssertions;
using NpgsqlTypes;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportSqlBuilderTests
{
    private static readonly ReportDataset Orders = ReportDatasetCatalog.Find("shoptet.orders_monthly")!;
    private static readonly ReportDataset Articles = ReportDatasetCatalog.Find("ga4.articles_monthly")!;

    private static ReportSqlCommand Build(ReportQuery query, ReportDataset? dataset = null) =>
        ReportSqlBuilder.Build(dataset ?? Orders, query);

    private static ReportFilter F(string column, string op, string? value = null, params string[] values) =>
        new() { Column = column, Op = op, Value = value, Values = values.Length > 0 ? values : null };

    [Fact]
    public void Default_query_selects_all_columns_with_default_sort_and_limit_plus_one()
    {
        var cmd = Build(new ReportQuery());

        cmd.Sql.Should().StartWith("SELECT \"order_month\", \"channel\", \"orders\"");
        cmd.Sql.Should().Contain("FROM \"shoptet_raw\".\"v_order_monthly\"");
        cmd.Sql.Should().NotContain("WHERE");
        cmd.Sql.Should().Contain("ORDER BY \"order_month\" DESC NULLS LAST, \"channel\" ASC NULLS LAST");
        cmd.Sql.Should().EndWith("LIMIT @limit OFFSET @offset");
        cmd.Limit.Should().Be(100);
        cmd.Parameters.Single(p => p.ParameterName == "limit").Value.Should().Be(101);
        cmd.Parameters.Single(p => p.ParameterName == "offset").Value.Should().Be(0);
        cmd.OutputColumns.Should().HaveCount(Orders.Columns.Count);
    }

    [Fact]
    public void Column_projection_keeps_requested_order_and_canonical_names()
    {
        var cmd = Build(new ReportQuery { Columns = ["Revenue_With_Vat_Czk", "order_month"] });

        cmd.Sql.Should().StartWith("SELECT \"revenue_with_vat_czk\", \"order_month\" FROM");
        cmd.OutputColumns.Select(c => c.Name).Should().Equal("revenue_with_vat_czk", "order_month");
    }

    [Theory]
    [InlineData("eq", "=")]
    [InlineData("gt", ">")]
    [InlineData("gte", ">=")]
    [InlineData("lt", "<")]
    [InlineData("lte", "<=")]
    public void Comparison_ops_bind_a_typed_parameter(string op, string sqlOp)
    {
        var cmd = Build(new ReportQuery { Filters = [F("orders", op, "10")] });

        cmd.Sql.Should().Contain($"WHERE \"orders\" {sqlOp} @p0");
        var p = cmd.Parameters.Single(x => x.ParameterName == "p0");
        p.NpgsqlDbType.Should().Be(NpgsqlDbType.Bigint);
        p.Value.Should().Be(10L);
    }

    [Fact]
    public void Neq_is_null_safe()
    {
        Build(new ReportQuery { Filters = [F("channel", "neq", "prodejna")] })
            .Sql.Should().Contain("\"channel\" IS DISTINCT FROM @p0");
    }

    [Fact]
    public void In_binds_an_array_parameter()
    {
        var cmd = Build(new ReportQuery { Filters = [F("channel", "in", null, "eshop_mo", "eshop_vo")] });

        cmd.Sql.Should().Contain("\"channel\" = ANY(@p0)");
        var p = cmd.Parameters.Single(x => x.ParameterName == "p0");
        p.NpgsqlDbType.Should().Be(NpgsqlDbType.Array | NpgsqlDbType.Text);
        p.Value.Should().BeEquivalentTo(new[] { "eshop_mo", "eshop_vo" });
    }

    [Fact]
    public void Between_binds_two_parameters_inclusive()
    {
        var cmd = Build(new ReportQuery { Filters = [F("order_month", "between", null, "2026-01-01", "2026-06-01")] });

        cmd.Sql.Should().Contain("\"order_month\" BETWEEN @p0 AND @p1");
        cmd.Parameters.Single(x => x.ParameterName == "p0").Value.Should().Be(new DateOnly(2026, 1, 1));
        cmd.Parameters.Single(x => x.ParameterName == "p1").Value.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Date_accepts_year_month_shorthand()
    {
        var cmd = Build(new ReportQuery { Filters = [F("order_month", "gte", "2026-09")] });

        var p = cmd.Parameters.Single(x => x.ParameterName == "p0");
        p.NpgsqlDbType.Should().Be(NpgsqlDbType.Date);
        p.Value.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Contains_escapes_like_wildcards()
    {
        var cmd = Build(new ReportQuery { Filters = [F("page_path", "contains", @"50%_off\x")] }, Articles);

        cmd.Sql.Should().Contain("\"page_path\" ILIKE '%' || @p0 || '%' ESCAPE '\\'");
        cmd.Parameters.Single(x => x.ParameterName == "p0").Value.Should().Be(@"50\%\_off\\x");
    }

    [Theory]
    [InlineData("true", "IS NULL")]
    [InlineData("false", "IS NOT NULL")]
    public void IsNull_takes_a_boolean(string value, string expected)
    {
        var cmd = Build(new ReportQuery { Filters = [F("variant_name", "isNull", value)] },
            ReportDatasetCatalog.Find("shoptet.product_sales_monthly")!);

        cmd.Sql.Should().Contain($"\"variant_name\" {expected}");
        cmd.Parameters.Should().NotContain(p => p.ParameterName == "p0");
    }

    [Fact]
    public void Multiple_filters_are_anded_with_increasing_parameter_names()
    {
        var cmd = Build(new ReportQuery { Filters = [F("channel", "eq", "eshop_mo"), F("orders", "gt", "5")] });

        cmd.Sql.Should().Contain("WHERE \"channel\" = @p0 AND \"orders\" > @p1");
    }

    [Fact]
    public void Key_columns_are_appended_as_tiebreak_once()
    {
        var cmd = Build(new ReportQuery { Sort = [new ReportSort { Column = "orders", Direction = "desc" }, new ReportSort { Column = "channel", Direction = "asc" }] });

        cmd.Sql.Should().Contain("ORDER BY \"orders\" DESC NULLS LAST, \"channel\" ASC NULLS LAST, \"order_month\" ASC NULLS LAST LIMIT");
    }

    [Fact]
    public void Limit_and_offset_are_honoured()
    {
        var cmd = Build(new ReportQuery { Limit = 1000, Offset = 2000 });

        cmd.Limit.Should().Be(1000);
        cmd.Parameters.Single(p => p.ParameterName == "limit").Value.Should().Be(1001);
        cmd.Parameters.Single(p => p.ParameterName == "offset").Value.Should().Be(2000);
    }

    public static TheoryData<ReportQuery, string> Invalid => new()
    {
        { new ReportQuery { Columns = ["nope"] }, "Unknown column 'nope'" },
        { new ReportQuery { Columns = [] }, "columns" },
        { new ReportQuery { Filters = [F("nope", "eq", "1")] }, "Unknown column 'nope'" },
        { new ReportQuery { Filters = [F("orders", "like", "1")] }, "Unknown operator 'like'" },
        { new ReportQuery { Filters = [F("orders", "eq", "abc")] }, "not a valid integer" },
        { new ReportQuery { Filters = [F("order_month", "eq", "13/2026")] }, "not a valid date" },
        { new ReportQuery { Filters = [F("orders", "eq")] }, "requires 'value'" },
        { new ReportQuery { Filters = [F("channel", "gt", "a")] }, "not allowed on Text" },
        { new ReportQuery { Filters = [F("orders", "contains", "1")] }, "only allowed on Text" },
        { new ReportQuery { Filters = [F("order_month", "between", null, "2026-01-01")] }, "exactly 2" },
        { new ReportQuery { Filters = [F("channel", "in")] }, "between 1 and 100" },
        { new ReportQuery { Filters = [F("channel", "isNull", "maybe")] }, "true or false" },
        { new ReportQuery { Filters = Enumerable.Repeat(F("orders", "gt", "1"), 21).ToList() }, "at most 20 filters" },
        { new ReportQuery { Sort = [new ReportSort { Column = "orders", Direction = "up" }] }, "asc or desc" },
        { new ReportQuery { Sort = Enumerable.Repeat(new ReportSort { Column = "orders", Direction = "asc" }, 6).ToList() }, "at most 5 sort" },
        { new ReportQuery { Limit = 0 }, "limit" },
        { new ReportQuery { Limit = 1001 }, "limit" },
        { new ReportQuery { Offset = -1 }, "offset" },
        { new ReportQuery { Offset = 100_001 }, "offset" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_queries_are_rejected_with_a_reason(ReportQuery query, string expectedFragment)
    {
        var act = () => Build(query);

        act.Should().Throw<ReportQueryValidationException>().WithMessage($"*{expectedFragment}*");
    }

    [Fact]
    public void Column_names_never_come_from_the_request()
    {
        var cmd = Build(new ReportQuery { Columns = ["ORDERS"], Filters = [F("CHANNEL", "eq", "x\"; DROP TABLE y; --")] });

        cmd.Sql.Should().NotContain("DROP");
        cmd.Sql.Should().Contain("\"orders\"").And.Contain("\"channel\" = @p0");
    }
}
```

- [ ] **Step 2: Run — expect compile failure** (namespace `Features.Reporting.Query` missing)

Run: `cd backend && dotnet build test/Anela.Heblo.Tests -p:UseSharedCompilation=false 2>&1 | grep -m3 "error CS"`

- [ ] **Step 3: Create the query model**

`Query/ReportQuery.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Query;

public sealed class ReportFilter
{
    public required string Column { get; init; }
    public required string Op { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<string>? Values { get; init; }
}

public sealed class ReportSort
{
    public required string Column { get; init; }
    public string Direction { get; init; } = "asc";
}

public sealed class ReportQuery
{
    public IReadOnlyList<string>? Columns { get; init; }
    public IReadOnlyList<ReportFilter> Filters { get; init; } = [];
    public IReadOnlyList<ReportSort> Sort { get; init; } = [];
    public int? Limit { get; init; }
    public int? Offset { get; init; }
}
```

`Query/ReportQueryValidationException.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Query;

/// <summary>The request cannot be turned into a query; the message is shown to the caller.</summary>
public sealed class ReportQueryValidationException : Exception
{
    public ReportQueryValidationException(string reason) : base(reason) { }
}
```

`Query/ReportSqlCommand.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Npgsql;

namespace Anela.Heblo.Application.Features.Reporting.Query;

public sealed class ReportSqlCommand
{
    public required string Sql { get; init; }
    public required IReadOnlyList<NpgsqlParameter> Parameters { get; init; }
    public required IReadOnlyList<ReportColumn> OutputColumns { get; init; }

    /// <summary>Rows the caller asked for; the SQL fetches one more to detect a further page.</summary>
    public required int Limit { get; init; }
}
```

- [ ] **Step 4: Create the value parser** — `Query/ReportValueParser.cs`

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.Reporting.Catalog;
using NpgsqlTypes;

namespace Anela.Heblo.Application.Features.Reporting.Query;

public static class ReportValueParser
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-MM"];

    public static NpgsqlDbType DbTypeFor(ReportColumnType type) => type switch
    {
        ReportColumnType.Date => NpgsqlDbType.Date,
        ReportColumnType.Text => NpgsqlDbType.Text,
        ReportColumnType.Integer => NpgsqlDbType.Bigint,
        ReportColumnType.Decimal => NpgsqlDbType.Numeric,
        ReportColumnType.Boolean => NpgsqlDbType.Boolean,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static object Parse(ReportColumn column, string raw) => column.Type switch
    {
        ReportColumnType.Text => raw,
        ReportColumnType.Date => ParseDate(column, raw),
        ReportColumnType.Integer => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
            ? l
            : throw Invalid(column, raw, "integer"),
        ReportColumnType.Decimal => decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw Invalid(column, raw, "decimal number"),
        ReportColumnType.Boolean => ParseBool(raw) ?? throw Invalid(column, raw, "boolean (true or false)"),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column.Type, null),
    };

    public static bool? ParseBool(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static DateOnly ParseDate(ReportColumn column, string raw) =>
        DateOnly.TryParseExact(raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid(column, raw, "date (yyyy-MM-dd or yyyy-MM)");

    private static ReportQueryValidationException Invalid(ReportColumn column, string raw, string expected) =>
        new($"Value '{raw}' for column '{column.Name}' is not a valid {expected}.");
}
```

Note: `DateOnly.TryParseExact` with format `yyyy-MM` yields the first day of the month.

- [ ] **Step 5: Create the builder** — `Query/ReportSqlBuilder.cs`

```csharp
using System.Text;
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Npgsql;
using NpgsqlTypes;

namespace Anela.Heblo.Application.Features.Reporting.Query;

/// <summary>
/// Turns a structured query into parameterised SQL. Identifiers come only from the catalogue
/// (canonical names, double-quoted); every value is a typed parameter.
/// </summary>
public static class ReportSqlBuilder
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 1000;
    public const int MaxOffset = 100_000;
    public const int MaxFilters = 20;
    public const int MaxInValues = 100;
    public const int MaxSortColumns = 5;

    private static readonly string[] Operators = ["eq", "neq", "gt", "gte", "lt", "lte", "in", "between", "contains", "isNull"];

    public static ReportSqlCommand Build(ReportDataset dataset, ReportQuery query)
    {
        var limit = query.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            throw new ReportQueryValidationException($"limit must be between 1 and {MaxLimit}.");
        var offset = query.Offset ?? 0;
        if (offset is < 0 or > MaxOffset)
            throw new ReportQueryValidationException($"offset must be between 0 and {MaxOffset}.");

        var output = ResolveColumns(dataset, query.Columns);
        var parameters = new List<NpgsqlParameter>();
        var where = BuildWhere(dataset, query.Filters, parameters);
        var orderBy = BuildOrderBy(dataset, query.Sort);

        parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit + 1 });
        parameters.Add(new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = offset });

        var sql = new StringBuilder()
            .Append("SELECT ").Append(string.Join(", ", output.Select(c => Quote(c.Name))))
            .Append(" FROM ").Append(Quote(dataset.Schema)).Append('.').Append(Quote(dataset.View));
        if (where.Length > 0)
            sql.Append(" WHERE ").Append(where);
        sql.Append(" ORDER BY ").Append(orderBy).Append(" LIMIT @limit OFFSET @offset");

        return new ReportSqlCommand
        {
            Sql = sql.ToString(),
            Parameters = parameters,
            OutputColumns = output,
            Limit = limit,
        };
    }

    private static IReadOnlyList<ReportColumn> ResolveColumns(ReportDataset dataset, IReadOnlyList<string>? requested)
    {
        if (requested is null)
            return dataset.Columns;
        if (requested.Count == 0)
            throw new ReportQueryValidationException("columns must not be empty; omit it to return all columns.");
        return requested.Select(name => RequireColumn(dataset, name)).ToList();
    }

    private static string BuildWhere(ReportDataset dataset, IReadOnlyList<ReportFilter> filters, List<NpgsqlParameter> parameters)
    {
        if (filters.Count > MaxFilters)
            throw new ReportQueryValidationException($"Use at most {MaxFilters} filters.");
        return string.Join(" AND ", filters.Select(f => BuildPredicate(dataset, f, parameters)));
    }

    private static string BuildPredicate(ReportDataset dataset, ReportFilter filter, List<NpgsqlParameter> parameters)
    {
        var column = RequireColumn(dataset, filter.Column);
        var op = Operators.FirstOrDefault(o => string.Equals(o, filter.Op, StringComparison.OrdinalIgnoreCase))
            ?? throw new ReportQueryValidationException(
                $"Unknown operator '{filter.Op}'. Allowed: {string.Join(", ", Operators)}.");
        var col = Quote(column.Name);

        switch (op)
        {
            case "isNull":
                var isNull = ReportValueParser.ParseBool(filter.Value)
                    ?? throw new ReportQueryValidationException("isNull requires 'value' true or false.");
                return isNull ? $"{col} IS NULL" : $"{col} IS NOT NULL";

            case "in":
                if (filter.Values is not { Count: >= 1 and <= MaxInValues })
                    throw new ReportQueryValidationException($"in requires 'values' with between 1 and {MaxInValues} items.");
                return $"{col} = ANY({AddArray(column, filter.Values, parameters)})";

            case "between":
                RequireOrderable(column, op);
                if (filter.Values is not { Count: 2 })
                    throw new ReportQueryValidationException("between requires 'values' with exactly 2 items.");
                return $"{col} BETWEEN {Add(column, filter.Values[0], parameters)} AND {Add(column, filter.Values[1], parameters)}";

            case "contains":
                if (column.Type != ReportColumnType.Text)
                    throw new ReportQueryValidationException($"contains is only allowed on Text columns; '{column.Name}' is {column.Type}.");
                var text = RequireValue(filter, op);
                return $"{col} ILIKE '%' || {AddRaw(NpgsqlDbType.Text, EscapeLike(text), parameters)} || '%' ESCAPE '\\'";

            default:
                if (op is "gt" or "gte" or "lt" or "lte")
                    RequireOrderable(column, op);
                var value = Add(column, RequireValue(filter, op), parameters);
                return op switch
                {
                    "eq" => $"{col} = {value}",
                    "neq" => $"{col} IS DISTINCT FROM {value}",
                    "gt" => $"{col} > {value}",
                    "gte" => $"{col} >= {value}",
                    "lt" => $"{col} < {value}",
                    _ => $"{col} <= {value}",
                };
        }
    }

    private static string BuildOrderBy(ReportDataset dataset, IReadOnlyList<ReportSort> sort)
    {
        if (sort.Count > MaxSortColumns)
            throw new ReportQueryValidationException($"Use at most {MaxSortColumns} sort entries.");

        var entries = sort.Count == 0
            ? dataset.DefaultSort.Select(s => (Column: dataset.FindColumn(s.Column)!, s.Descending)).ToList()
            : sort.Select(s => (Column: RequireColumn(dataset, s.Column), Descending: ParseDirection(s.Direction))).ToList();

        var used = entries.Select(e => e.Column.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var key in dataset.KeyColumns.Where(k => used.Add(k)))
            entries.Add((dataset.FindColumn(key)!, false));

        return string.Join(", ", entries.Select(e => $"{Quote(e.Column.Name)} {(e.Descending ? "DESC" : "ASC")} NULLS LAST"));
    }

    private static bool ParseDirection(string direction) => direction.Trim().ToLowerInvariant() switch
    {
        "asc" => false,
        "desc" => true,
        _ => throw new ReportQueryValidationException($"Sort direction '{direction}' must be asc or desc."),
    };

    private static ReportColumn RequireColumn(ReportDataset dataset, string name) =>
        dataset.FindColumn(name) ?? throw new ReportQueryValidationException(
            $"Unknown column '{name}' in dataset '{dataset.Id}'. Columns: {string.Join(", ", dataset.Columns.Select(c => c.Name))}.");

    private static void RequireOrderable(ReportColumn column, string op)
    {
        if (column.Type is ReportColumnType.Text or ReportColumnType.Boolean)
            throw new ReportQueryValidationException($"{op} is not allowed on {column.Type} column '{column.Name}'.");
    }

    private static string RequireValue(ReportFilter filter, string op) =>
        filter.Value ?? throw new ReportQueryValidationException($"{op} requires 'value'.");

    private static string Add(ReportColumn column, string raw, List<NpgsqlParameter> parameters) =>
        AddRaw(ReportValueParser.DbTypeFor(column.Type), ReportValueParser.Parse(column, raw), parameters);

    private static string AddArray(ReportColumn column, IReadOnlyList<string> raws, List<NpgsqlParameter> parameters)
    {
        var values = raws.Select(r => ReportValueParser.Parse(column, r)).ToArray();
        Array typed = column.Type switch
        {
            ReportColumnType.Date => values.Cast<DateOnly>().ToArray(),
            ReportColumnType.Text => values.Cast<string>().ToArray(),
            ReportColumnType.Integer => values.Cast<long>().ToArray(),
            ReportColumnType.Decimal => values.Cast<decimal>().ToArray(),
            _ => values.Cast<bool>().ToArray(),
        };
        return AddRaw(NpgsqlDbType.Array | ReportValueParser.DbTypeFor(column.Type), typed, parameters);
    }

    private static string AddRaw(NpgsqlDbType type, object value, List<NpgsqlParameter> parameters)
    {
        var name = $"p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, type) { Value = value });
        return "@" + name;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
```

- [ ] **Step 6: Run the builder tests — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReportSqlBuilderTests" -p:UseSharedCompilation=false`
If `Default_query_selects_all_columns…` fails on the `ORDER BY` text, compare against `shoptet.orders_monthly`'s `DefaultSort` = `(order_month desc), (channel asc)` and key columns `order_month, channel` — both keys are already used, so no tiebreak is appended.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Reporting/Query backend/test/Anela.Heblo.Tests/Features/Reporting/ReportSqlBuilderTests.cs
git commit -m "feat(reporting): structured query to parameterised SQL builder"
```

---

### Task 5: Data reader (execution + freshness)

**Files:**
- Create: `Features/Reporting/Data/IReportingDataReader.cs`, `NpgsqlReportingDataReader.cs`, `UnavailableReportingDataReader.cs`, `ReportQueryTimeoutException.cs`, `ReportFreshness.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Reporting/Integration/NpgsqlReportingDataReaderIntegrationTests.cs`

**Interfaces:**
- Consumes: `ReportSqlCommand` (Task 4), `ReportSource`, `ReportSources.FreshnessSqlFor` (Task 3).
- Produces:
  - `sealed class ReportRows { IReadOnlyList<IReadOnlyList<object?>> Rows; bool HasMore }`
  - `sealed class ReportFreshness { DateTimeOffset? LastSyncAt; string Status }` — `Status` is `"OK"`, `"FAILED"` or `"UNKNOWN"`
  - `interface IReportingDataReader { Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct); Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct); }`
  - `sealed class ReportQueryTimeoutException : Exception`
  - `NpgsqlReportingDataReader(NpgsqlDataSource dataSource, TimeSpan? statementTimeout = null)` — default 10 s
  - Row values: Date → `DateOnly`, Integer → `long`, Decimal → `decimal`, Text → `string`, Boolean → `bool`, SQL NULL → `null`.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting.Integration;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class NpgsqlReportingDataReaderIntegrationTests
{
    private readonly PostgresSharedContainerFixture _fixture;

    public NpgsqlReportingDataReaderIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    // A stand-in for shoptet_raw.v_order_monthly with the same schema/view names and three months.
    private const string Seed = """
        CREATE SCHEMA shoptet_raw;
        CREATE TABLE shoptet_raw.t (order_month date, channel text, orders bigint, customers bigint,
            revenue_with_vat_czk numeric, revenue_without_vat_czk numeric, product_revenue_with_vat_czk numeric,
            shipping_charged_with_vat_czk numeric, payment_fee_with_vat_czk numeric, discount_with_vat_czk numeric,
            units numeric, avg_order_value_with_vat_czk numeric, avg_basket_units numeric);
        INSERT INTO shoptet_raw.t VALUES
            ('2026-07-01','eshop_mo',10,9,1000.50,826.86,900,50,0,0,30,100.05,3),
            ('2026-08-01','eshop_mo',20,18,2000,1652.89,1800,100,0,0,60,100,3),
            ('2026-09-01','eshop_vo',5,2,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL);
        CREATE VIEW shoptet_raw.v_order_monthly AS SELECT * FROM shoptet_raw.t;
        CREATE TABLE shoptet_raw.sync_state (entity_name text, last_run_finished_at timestamptz, last_run_status text);
        INSERT INTO shoptet_raw.sync_state VALUES ('order', '2026-10-12 23:32:41+00', 'OK'), ('order_backfill', '2026-09-22 21:22:42+00', 'OK');
        CREATE SCHEMA flexi_raw;
        CREATE TABLE flexi_raw.sync_state (entity_name text, last_run_finished_at timestamptz, last_run_status text);
        INSERT INTO flexi_raw.sync_state VALUES ('ledger_entry', '2026-10-13 01:00:21+00', 'OK'), ('contact', '2026-10-13 01:00:22+00', 'FAILED');
        """;

    private async Task<NpgsqlDataSource> CreateAsync(string hint)
    {
        var cs = await _fixture.CreateDatabaseAsync(hint);
        var ds = NpgsqlDataSource.Create(cs);
        await using var cmd = ds.CreateCommand(Seed);
        await cmd.ExecuteNonQueryAsync();
        return ds;
    }

    private static readonly ReportDataset Orders = ReportDatasetCatalog.Find("shoptet.orders_monthly")!;

    [Fact]
    public async Task Returns_typed_rows_and_detects_a_further_page()
    {
        await using var ds = await CreateAsync("rep_rows");
        var command = ReportSqlBuilder.Build(Orders, new ReportQuery
        {
            Columns = ["order_month", "orders", "revenue_with_vat_czk", "channel"],
            Filters = [new ReportFilter { Column = "order_month", Op = "gte", Value = "2026-07" }],
            Limit = 2,
        });

        var result = await new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        result.HasMore.Should().BeTrue();
        result.Rows.Should().HaveCount(2);
        result.Rows[0].Should().Equal(new DateOnly(2026, 9, 1), 5L, null, "eshop_vo");
        result.Rows[1].Should().Equal(new DateOnly(2026, 8, 1), 20L, 2000m, "eshop_mo");
    }

    [Fact]
    public async Task Last_page_reports_no_more_rows()
    {
        await using var ds = await CreateAsync("rep_last");
        var command = ReportSqlBuilder.Build(Orders, new ReportQuery { Limit = 2, Offset = 2 });

        var result = await new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        result.HasMore.Should().BeFalse();
        result.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task In_and_contains_filters_bind_against_a_real_server()
    {
        await using var ds = await CreateAsync("rep_filters");
        var command = ReportSqlBuilder.Build(Orders, new ReportQuery
        {
            Columns = ["channel"],
            Filters =
            [
                new ReportFilter { Column = "channel", Op = "in", Values = ["eshop_mo", "prodejna"] },
                new ReportFilter { Column = "channel", Op = "contains", Value = "SHOP_m" },
            ],
        });

        var result = await new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        result.Rows.Should().HaveCount(2).And.OnlyContain(r => (string)r[0]! == "eshop_mo");
    }

    [Fact]
    public async Task Runs_in_a_read_only_transaction()
    {
        await using var ds = await CreateAsync("rep_readonly");
        var command = new ReportSqlCommand
        {
            Sql = "INSERT INTO shoptet_raw.t (channel) VALUES ('x') RETURNING channel",
            Parameters = [],
            OutputColumns = [ReportColumn.Text("channel", "c")],
            Limit = 1,
        };

        var act = () => new NpgsqlReportingDataReader(ds).QueryAsync(command, default);

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("25006"); // read_only_sql_transaction
    }

    [Fact]
    public async Task Statement_timeout_raises_ReportQueryTimeoutException()
    {
        await using var ds = await CreateAsync("rep_timeout");
        var command = new ReportSqlCommand
        {
            Sql = "SELECT pg_sleep(5)::text AS x",
            Parameters = [],
            OutputColumns = [ReportColumn.Text("x", "x")],
            Limit = 1,
        };

        var act = () => new NpgsqlReportingDataReader(ds, TimeSpan.FromSeconds(1)).QueryAsync(command, default);

        await act.Should().ThrowAsync<ReportQueryTimeoutException>();
    }

    [Fact]
    public async Task Freshness_reads_the_source_sync_state()
    {
        await using var ds = await CreateAsync("rep_fresh");
        var reader = new NpgsqlReportingDataReader(ds);

        var shoptet = await reader.GetFreshnessAsync(ReportSource.Eshop, default);
        var flexi = await reader.GetFreshnessAsync(ReportSource.Accounting, default);

        shoptet.Status.Should().Be("OK");
        shoptet.LastSyncAt.Should().Be(new DateTimeOffset(2026, 10, 12, 23, 32, 41, TimeSpan.Zero));
        flexi.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Freshness_is_unknown_when_the_schema_is_missing()
    {
        await using var ds = await CreateAsync("rep_fresh_missing");

        var ga4 = await new NpgsqlReportingDataReader(ds).GetFreshnessAsync(ReportSource.WebAnalytics, default);

        ga4.Status.Should().Be("UNKNOWN");
        ga4.LastSyncAt.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run — expect compile failure** (namespace `Features.Reporting.Data` missing)

- [ ] **Step 3: Create the contracts**

`Data/ReportFreshness.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Data;

public sealed class ReportFreshness
{
    public const string Ok = "OK";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";

    public DateTimeOffset? LastSyncAt { get; init; }
    public string Status { get; init; } = Unknown;
}

public sealed class ReportRows
{
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public required bool HasMore { get; init; }
}
```

`Data/IReportingDataReader.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;

namespace Anela.Heblo.Application.Features.Reporting.Data;

public interface IReportingDataReader
{
    /// <summary>Runs a built query; throws <see cref="ReportQueryTimeoutException"/> when the statement timeout fires.</summary>
    Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct);

    Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct);
}
```

`Data/ReportQueryTimeoutException.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Data;

public sealed class ReportQueryTimeoutException : Exception
{
    public ReportQueryTimeoutException(Exception inner)
        : base("The reporting query exceeded its statement timeout.", inner) { }
}
```

- [ ] **Step 4: Create the Npgsql reader** — `Data/NpgsqlReportingDataReader.cs`

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;
using Npgsql;

namespace Anela.Heblo.Application.Features.Reporting.Data;

/// <summary>
/// Runs reporting SQL on the shared data source with raw ADO.NET — deliberately not EF, whose
/// Polly execution strategy would retry a slow statement on the shared B1ms core. Every query runs
/// in a READ ONLY transaction with a local statement_timeout.
/// </summary>
public sealed class NpgsqlReportingDataReader : IReportingDataReader
{
    private static readonly TimeSpan DefaultStatementTimeout = TimeSpan.FromSeconds(10);
    private const string QueryCanceled = "57014";
    private const string UndefinedTable = "42P01";
    private const string InvalidSchema = "3F000";

    private readonly NpgsqlDataSource _dataSource;
    private readonly string _timeoutMs;

    public NpgsqlReportingDataReader(NpgsqlDataSource dataSource, TimeSpan? statementTimeout = null)
    {
        _dataSource = dataSource;
        _timeoutMs = ((int)(statementTimeout ?? DefaultStatementTimeout).TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
    }

    public async Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await BeginReadOnlyAsync(connection, ct);

        await using var cmd = new NpgsqlCommand(command.Sql, connection, transaction);
        foreach (var p in command.Parameters)
            cmd.Parameters.Add(p.Clone());

        var rows = new List<IReadOnlyList<object?>>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(ReadRow(reader, command.OutputColumns));
        }
        catch (PostgresException ex) when (ex.SqlState == QueryCanceled)
        {
            throw new ReportQueryTimeoutException(ex);
        }

        await transaction.CommitAsync(ct);

        var hasMore = rows.Count > command.Limit;
        return new ReportRows { Rows = hasMore ? rows.Take(command.Limit).ToList() : rows, HasMore = hasMore };
    }

    public async Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var transaction = await BeginReadOnlyAsync(connection, ct);
        await using var cmd = new NpgsqlCommand(ReportSources.FreshnessSqlFor(source), connection, transaction);
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(1))
                return new ReportFreshness();

            return new ReportFreshness
            {
                LastSyncAt = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
                Status = reader.GetBoolean(1) ? ReportFreshness.Ok : ReportFreshness.Failed,
            };
        }
        catch (PostgresException ex) when (ex.SqlState is UndefinedTable or InvalidSchema)
        {
            return new ReportFreshness();
        }
    }

    private async Task<NpgsqlTransaction> BeginReadOnlyAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var transaction = await connection.BeginTransactionAsync(ct);
        await using var setup = new NpgsqlCommand(
            $"SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = {_timeoutMs}", connection, transaction);
        await setup.ExecuteNonQueryAsync(ct);
        return transaction;
    }

    private static IReadOnlyList<object?> ReadRow(NpgsqlDataReader reader, IReadOnlyList<ReportColumn> columns)
    {
        var row = new object?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            row[i] = reader.IsDBNull(i) ? null : columns[i].Type switch
            {
                ReportColumnType.Date => reader.GetFieldValue<DateOnly>(i),
                ReportColumnType.Integer => Convert.ToInt64(reader.GetValue(i), CultureInfo.InvariantCulture),
                ReportColumnType.Decimal => Convert.ToDecimal(reader.GetValue(i), CultureInfo.InvariantCulture),
                ReportColumnType.Boolean => reader.GetBoolean(i),
                _ => reader.GetValue(i).ToString(),
            };
        }
        return row;
    }
}
```

`_timeoutMs` is an integer we format ourselves, never request input, so interpolating it is safe (`SET LOCAL` does not accept parameters).

`Data/UnavailableReportingDataReader.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Query;

namespace Anela.Heblo.Application.Features.Reporting.Data;

/// <summary>Used when no PostgreSQL data source is registered (in-memory database mode).</summary>
public sealed class UnavailableReportingDataReader : IReportingDataReader
{
    private const string Message = "Reporting data is unavailable: no PostgreSQL data source is configured (in-memory database mode).";

    public Task<ReportRows> QueryAsync(ReportSqlCommand command, CancellationToken ct) =>
        throw new InvalidOperationException(Message);

    public Task<ReportFreshness> GetFreshnessAsync(ReportSource source, CancellationToken ct) =>
        Task.FromResult(new ReportFreshness());
}
```

- [ ] **Step 5: Run the integration tests — expect PASS** (needs Podman: `podman machine start` first)

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~NpgsqlReportingDataReaderIntegrationTests" -p:UseSharedCompilation=false`
If `Runs_in_a_read_only_transaction` gets a different SqlState, print `ex.SqlState` and confirm it is `25006`; anything else means the `SET TRANSACTION READ ONLY` did not apply.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Reporting/Data backend/test/Anela.Heblo.Tests/Features/Reporting/Integration
git commit -m "feat(reporting): read-only, time-boxed Npgsql reader for reporting views"
```

---

### Task 6: Handlers, access check and module registration

**Files:**
- Create: `Features/Reporting/ReportAccess.cs`, `ReportingModule.cs`, `Contracts/ReportDtos.cs`, `UseCases/ReportDatasetMapper.cs`, `UseCases/ReportDatasetGate.cs`, `UseCases/ListReportDatasets/*`, `UseCases/DescribeReportDataset/*`, `UseCases/QueryReportDataset/*`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (add `using` + `services.AddReportingModule();` next to `services.AddProcessDocsModule();`)
- Test: `backend/test/Anela.Heblo.Tests/Features/Reporting/ReportingHandlersTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 3, 4, 5.
- Produces (DTOs, all classes):
  - `ReportFreshnessDto { DateTimeOffset? LastSyncAt; string Status }`
  - `ReportColumnDto { string Name; string Type; string Description }`
  - `ReportDatasetSummaryDto { string Id; string Source; string SourceLabel; string Description; string Grain; ReportFreshnessDto Freshness }`
  - `ReportDatasetDetailDto : ReportDatasetSummaryDto { string PeriodColumn; List<ReportColumnDto> Columns; List<string> DefaultSort; List<string> Caveats }` — `DefaultSort` entries like `"order_month desc"`
  - `ReportFilterDto { string Column; string Op; string? Value; List<string>? Values }`, `ReportSortDto { string Column; string Direction = "asc" }`
  - `ListReportDatasetsRequest : IRequest<ListReportDatasetsResponse>`; response `List<ReportDatasetSummaryDto> Datasets`
  - `DescribeReportDatasetRequest { string Dataset }` → `DescribeReportDatasetResponse { ReportDatasetDetailDto? Dataset }`
  - `QueryReportDatasetRequest { string Dataset; List<string>? Columns; List<ReportFilterDto>? Filters; List<ReportSortDto>? Sort; int? Limit; int? Offset }` → `QueryReportDatasetResponse { string Dataset; List<ReportColumnDto> Columns; List<List<object?>> Rows; int RowCount; bool HasMore; List<string> Caveats; ReportFreshnessDto? Freshness }`
  - Error params: NotFound → `Params["Dataset"]`, `Params["Suggestions"]` (comma-separated, may be empty); InvalidQuery → `Params["Reason"]`; Forbidden → `Params["RequiredRole"]`; Timeout → `Params["Dataset"]`.

- [ ] **Step 1: Write the failing handler tests**

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;
using Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;
using Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Reporting;

public class ReportingHandlersTests
{
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly Mock<IReportingDataReader> _reader = new();

    public ReportingHandlersTests()
    {
        _reader.Setup(r => r.GetFreshnessAsync(It.IsAny<ReportSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportFreshness { LastSyncAt = new DateTimeOffset(2026, 10, 13, 1, 0, 0, TimeSpan.Zero), Status = "OK" });
    }

    private void Grant(Feature feature) =>
        _user.Setup(u => u.IsInRole(AccessRoles.For(feature, AccessLevel.Read))).Returns(true);

    [Fact]
    public async Task List_omits_datasets_without_permission()
    {
        Grant(Feature.Reporting_Eshop);

        var result = await new ListReportDatasetsHandler(_user.Object, _reader.Object)
            .Handle(new ListReportDatasetsRequest(), default);

        result.Success.Should().BeTrue();
        result.Datasets.Should().HaveCount(8).And.OnlyContain(d => d.Id.StartsWith("shoptet."));
        result.Datasets[0].Freshness.Status.Should().Be("OK");
        _reader.Verify(r => r.GetFreshnessAsync(ReportSource.Eshop, It.IsAny<CancellationToken>()), Times.Once);
        _reader.Verify(r => r.GetFreshnessAsync(ReportSource.Accounting, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_is_empty_without_any_permission()
    {
        var result = await new ListReportDatasetsHandler(_user.Object, _reader.Object)
            .Handle(new ListReportDatasetsRequest(), default);

        result.Datasets.Should().BeEmpty();
    }

    [Fact]
    public async Task Describe_returns_columns_caveats_and_default_sort()
    {
        Grant(Feature.Reporting_WebAnalytics);

        var result = await new DescribeReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new DescribeReportDatasetRequest { Dataset = "ga4.conversion_monthly" }, default);

        result.Success.Should().BeTrue();
        result.Dataset!.Columns.Should().Contain(c => c.Name == "conversion_rate_pct" && c.Type == "Decimal");
        result.Dataset.Caveats.Should().Contain(c => c.Contains("WILL NOT MATCH THE ERP"));
        result.Dataset.DefaultSort.Should().Equal("month desc");
        result.Dataset.PeriodColumn.Should().Be("month");
    }

    [Fact]
    public async Task Describe_unknown_dataset_suggests_close_ids()
    {
        Grant(Feature.Reporting_Eshop);

        var result = await new DescribeReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new DescribeReportDatasetRequest { Dataset = "shoptet.order_monthly" }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ReportingDatasetNotFound);
        result.Params!["Suggestions"].Should().Contain("shoptet.orders_monthly");
    }

    [Fact]
    public async Task Describe_without_permission_is_forbidden()
    {
        var result = await new DescribeReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new DescribeReportDatasetRequest { Dataset = "flexi.cost_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Params!["RequiredRole"].Should().Be("reporting.accounting.read");
    }

    [Fact]
    public async Task Query_without_permission_is_forbidden_and_never_reaches_the_reader()
    {
        Grant(Feature.Reporting_WebAnalytics);

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new QueryReportDatasetRequest { Dataset = "flexi.cost_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _reader.Verify(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Query_maps_request_to_sql_and_rows_to_response()
    {
        Grant(Feature.Reporting_Eshop);
        ReportSqlCommand? sent = null;
        _reader.Setup(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()))
            .Callback<ReportSqlCommand, CancellationToken>((c, _) => sent = c)
            .ReturnsAsync(new ReportRows { Rows = [[new DateOnly(2026, 9, 1), 42L]], HasMore = true });

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object).Handle(new QueryReportDatasetRequest
        {
            Dataset = "shoptet.orders_monthly",
            Columns = ["order_month", "orders"],
            Filters = [new ReportFilterDto { Column = "channel", Op = "eq", Value = "eshop_mo" }],
            Sort = [new ReportSortDto { Column = "orders", Direction = "desc" }],
            Limit = 1,
        }, default);

        result.Success.Should().BeTrue();
        sent!.Sql.Should().Contain("WHERE \"channel\" = @p0").And.Contain("ORDER BY \"orders\" DESC");
        result.Columns.Select(c => c.Name).Should().Equal("order_month", "orders");
        result.Rows.Should().ContainSingle().Which.Should().Equal(new DateOnly(2026, 9, 1), 42L);
        result.RowCount.Should().Be(1);
        result.HasMore.Should().BeTrue();
        result.Caveats.Should().NotBeEmpty();
        result.Freshness!.Status.Should().Be("OK");
    }

    [Fact]
    public async Task Query_validation_failure_returns_the_reason()
    {
        Grant(Feature.Reporting_Eshop);

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new QueryReportDatasetRequest { Dataset = "shoptet.orders_monthly", Limit = 5000 }, default);

        result.ErrorCode.Should().Be(ErrorCodes.ReportingInvalidQuery);
        result.Params!["Reason"].Should().Contain("limit");
    }

    [Fact]
    public async Task Query_timeout_maps_to_ReportingQueryTimeout()
    {
        Grant(Feature.Reporting_Eshop);
        _reader.Setup(r => r.QueryAsync(It.IsAny<ReportSqlCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ReportQueryTimeoutException(new Exception("57014")));

        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new QueryReportDatasetRequest { Dataset = "shoptet.orders_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.ReportingQueryTimeout);
    }

    [Fact]
    public async Task Query_unknown_dataset_is_not_found_even_without_permissions()
    {
        var result = await new QueryReportDatasetHandler(_user.Object, _reader.Object)
            .Handle(new QueryReportDatasetRequest { Dataset = "flexi.payroll_monthly" }, default);

        result.ErrorCode.Should().Be(ErrorCodes.ReportingDatasetNotFound);
    }
}
```

The last test documents a deliberate choice: dataset ids are not secret (they are in the docs), so "not found" before "forbidden" leaks nothing, and it keeps the payroll view's absence explicit.

- [ ] **Step 2: Run — expect compile failure**

- [ ] **Step 3: Create the access helper and DTOs**

`ReportAccess.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;

namespace Anela.Heblo.Application.Features.Reporting;

/// <summary>The single permission rule for reporting data, shared by every entry point.</summary>
public static class ReportAccess
{
    public static string RequiredRole(ReportSource source) =>
        AccessRoles.For(ReportSources.FeatureFor(source), AccessLevel.Read);

    public static bool CanRead(ICurrentUserService user, ReportSource source) =>
        user.IsInRole(RequiredRole(source));
}
```

`Contracts/ReportDtos.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Reporting.Contracts;

public class ReportFreshnessDto
{
    public DateTimeOffset? LastSyncAt { get; set; }
    public string Status { get; set; } = "UNKNOWN";
}

public class ReportColumnDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class ReportDatasetSummaryDto
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Grain { get; set; } = string.Empty;
    public ReportFreshnessDto Freshness { get; set; } = new();
}

public class ReportDatasetDetailDto : ReportDatasetSummaryDto
{
    public string PeriodColumn { get; set; } = string.Empty;
    public List<ReportColumnDto> Columns { get; set; } = [];
    public List<string> DefaultSort { get; set; } = [];
    public List<string> Caveats { get; set; } = [];
}

public class ReportFilterDto
{
    public string Column { get; set; } = string.Empty;
    public string Op { get; set; } = string.Empty;
    public string? Value { get; set; }
    public List<string>? Values { get; set; }
}

public class ReportSortDto
{
    public string Column { get; set; } = string.Empty;
    public string Direction { get; set; } = "asc";
}
```

`UseCases/ReportDatasetMapper.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.Data;

namespace Anela.Heblo.Application.Features.Reporting.UseCases;

internal static class ReportDatasetMapper
{
    public static ReportFreshnessDto ToDto(ReportFreshness f) => new() { LastSyncAt = f.LastSyncAt, Status = f.Status };

    public static ReportColumnDto ToDto(ReportColumn c) => new() { Name = c.Name, Type = c.Type.ToString(), Description = c.Description };

    public static ReportDatasetSummaryDto ToSummary(ReportDataset d, ReportFreshness f) => new()
    {
        Id = d.Id,
        Source = d.Source.ToString(),
        SourceLabel = ReportSources.LabelFor(d.Source),
        Description = d.Description,
        Grain = d.Grain,
        Freshness = ToDto(f),
    };

    public static ReportDatasetDetailDto ToDetail(ReportDataset d, ReportFreshness f) => new()
    {
        Id = d.Id,
        Source = d.Source.ToString(),
        SourceLabel = ReportSources.LabelFor(d.Source),
        Description = d.Description,
        Grain = d.Grain,
        Freshness = ToDto(f),
        PeriodColumn = d.PeriodColumn,
        Columns = d.Columns.Select(ToDto).ToList(),
        DefaultSort = d.DefaultSort.Select(s => $"{s.Column} {(s.Descending ? "desc" : "asc")}").ToList(),
        Caveats = d.Caveats.ToList(),
    };
}
```

- [ ] **Step 4: Create the list use case**

`UseCases/ListReportDatasets/ListReportDatasetsRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;

public class ListReportDatasetsRequest : IRequest<ListReportDatasetsResponse>
{
}

public class ListReportDatasetsResponse : BaseResponse
{
    public List<ReportDatasetSummaryDto> Datasets { get; set; } = [];

    public ListReportDatasetsResponse() : base() { }

    public ListReportDatasetsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UseCases/ListReportDatasets/ListReportDatasetsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;

public class ListReportDatasetsHandler : IRequestHandler<ListReportDatasetsRequest, ListReportDatasetsResponse>
{
    private readonly ICurrentUserService _user;
    private readonly IReportingDataReader _reader;

    public ListReportDatasetsHandler(ICurrentUserService user, IReportingDataReader reader)
    {
        _user = user;
        _reader = reader;
    }

    public async Task<ListReportDatasetsResponse> Handle(ListReportDatasetsRequest request, CancellationToken cancellationToken)
    {
        var readable = Enum.GetValues<ReportSource>().Where(s => ReportAccess.CanRead(_user, s)).ToList();

        var freshness = new Dictionary<ReportSource, ReportFreshness>();
        foreach (var source in readable)
            freshness[source] = await _reader.GetFreshnessAsync(source, cancellationToken);

        return new ListReportDatasetsResponse
        {
            Datasets = ReportDatasetCatalog.All
                .Where(d => freshness.ContainsKey(d.Source))
                .Select(d => ReportDatasetMapper.ToSummary(d, freshness[d.Source]))
                .ToList(),
        };
    }
}
```

- [ ] **Step 5: Create the describe use case**

`UseCases/DescribeReportDataset/DescribeReportDatasetRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;

public class DescribeReportDatasetRequest : IRequest<DescribeReportDatasetResponse>
{
    public string Dataset { get; set; } = string.Empty;
}

public class DescribeReportDatasetResponse : BaseResponse
{
    public ReportDatasetDetailDto? Dataset { get; set; }

    public DescribeReportDatasetResponse() : base() { }

    public DescribeReportDatasetResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UseCases/ReportDatasetGate.cs` — the lookup + permission step shared by Describe and Query:

```csharp
using Anela.Heblo.Application.Features.Reporting.Catalog;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using FuzzySharp;

namespace Anela.Heblo.Application.Features.Reporting.UseCases;

internal static class ReportDatasetGate
{
    private const int MaxSuggestions = 3;
    private const int MinSuggestionScore = 60;

    /// <summary>Returns the dataset, or the error code + params explaining why the caller cannot use it.</summary>
    public static (ReportDataset? Dataset, ErrorCodes? Error, Dictionary<string, string>? Params) Resolve(
        string id, ICurrentUserService user)
    {
        var dataset = ReportDatasetCatalog.Find(id);
        if (dataset is null)
        {
            var suggestions = Process.ExtractTop(id ?? string.Empty, ReportDatasetCatalog.All.Select(d => d.Id), limit: MaxSuggestions)
                .Where(r => r.Score >= MinSuggestionScore)
                .Select(r => r.Value);
            return (null, ErrorCodes.ReportingDatasetNotFound, new Dictionary<string, string>
            {
                ["Dataset"] = id ?? string.Empty,
                ["Suggestions"] = string.Join(", ", suggestions),
            });
        }

        if (!ReportAccess.CanRead(user, dataset.Source))
        {
            return (null, ErrorCodes.Forbidden, new Dictionary<string, string>
            {
                ["RequiredRole"] = ReportAccess.RequiredRole(dataset.Source),
            });
        }

        return (dataset, null, null);
    }
}
```

`UseCases/DescribeReportDataset/DescribeReportDatasetHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;

public class DescribeReportDatasetHandler : IRequestHandler<DescribeReportDatasetRequest, DescribeReportDatasetResponse>
{
    private readonly ICurrentUserService _user;
    private readonly IReportingDataReader _reader;

    public DescribeReportDatasetHandler(ICurrentUserService user, IReportingDataReader reader)
    {
        _user = user;
        _reader = reader;
    }

    public async Task<DescribeReportDatasetResponse> Handle(DescribeReportDatasetRequest request, CancellationToken cancellationToken)
    {
        var (dataset, error, parameters) = ReportDatasetGate.Resolve(request.Dataset, _user);
        if (dataset is null)
            return new DescribeReportDatasetResponse(error!.Value, parameters);

        var freshness = await _reader.GetFreshnessAsync(dataset.Source, cancellationToken);
        return new DescribeReportDatasetResponse { Dataset = ReportDatasetMapper.ToDetail(dataset, freshness) };
    }
}
```


- [ ] **Step 6: Create the query use case**

`UseCases/QueryReportDataset/QueryReportDatasetRequest.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;

public class QueryReportDatasetRequest : IRequest<QueryReportDatasetResponse>
{
    public string Dataset { get; set; } = string.Empty;
    public List<string>? Columns { get; set; }
    public List<ReportFilterDto>? Filters { get; set; }
    public List<ReportSortDto>? Sort { get; set; }
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

public class QueryReportDatasetResponse : BaseResponse
{
    public string Dataset { get; set; } = string.Empty;
    public List<ReportColumnDto> Columns { get; set; } = [];
    public List<List<object?>> Rows { get; set; } = [];
    public int RowCount { get; set; }
    public bool HasMore { get; set; }
    public List<string> Caveats { get; set; } = [];
    public ReportFreshnessDto? Freshness { get; set; }

    public QueryReportDatasetResponse() : base() { }

    public QueryReportDatasetResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`UseCases/QueryReportDataset/QueryReportDatasetHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Data;
using Anela.Heblo.Application.Features.Reporting.Query;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;

public class QueryReportDatasetHandler : IRequestHandler<QueryReportDatasetRequest, QueryReportDatasetResponse>
{
    private readonly ICurrentUserService _user;
    private readonly IReportingDataReader _reader;

    public QueryReportDatasetHandler(ICurrentUserService user, IReportingDataReader reader)
    {
        _user = user;
        _reader = reader;
    }

    public async Task<QueryReportDatasetResponse> Handle(QueryReportDatasetRequest request, CancellationToken cancellationToken)
    {
        var (dataset, error, parameters) = ReportDatasetGate.Resolve(request.Dataset, _user);
        if (dataset is null)
            return new QueryReportDatasetResponse(error!.Value, parameters);

        ReportSqlCommand command;
        try
        {
            command = ReportSqlBuilder.Build(dataset, ToQuery(request));
        }
        catch (ReportQueryValidationException ex)
        {
            return new QueryReportDatasetResponse(ErrorCodes.ReportingInvalidQuery,
                new Dictionary<string, string> { ["Reason"] = ex.Message });
        }

        ReportRows rows;
        try
        {
            rows = await _reader.QueryAsync(command, cancellationToken);
        }
        catch (ReportQueryTimeoutException)
        {
            return new QueryReportDatasetResponse(ErrorCodes.ReportingQueryTimeout,
                new Dictionary<string, string> { ["Dataset"] = dataset.Id });
        }

        var freshness = await _reader.GetFreshnessAsync(dataset.Source, cancellationToken);
        return new QueryReportDatasetResponse
        {
            Dataset = dataset.Id,
            Columns = command.OutputColumns.Select(ReportDatasetMapper.ToDto).ToList(),
            Rows = rows.Rows.Select(r => r.ToList()).ToList(),
            RowCount = rows.Rows.Count,
            HasMore = rows.HasMore,
            Caveats = dataset.Caveats.ToList(),
            Freshness = ReportDatasetMapper.ToDto(freshness),
        };
    }

    private static ReportQuery ToQuery(QueryReportDatasetRequest r) => new()
    {
        Columns = r.Columns,
        Filters = (r.Filters ?? []).Select(f => new ReportFilter { Column = f.Column, Op = f.Op, Value = f.Value, Values = f.Values }).ToList(),
        Sort = (r.Sort ?? []).Select(s => new ReportSort { Column = s.Column, Direction = s.Direction }).ToList(),
        Limit = r.Limit,
        Offset = r.Offset,
    };
}
```

- [ ] **Step 7: Register the module**

`ReportingModule.cs`:

```csharp
using Anela.Heblo.Application.Features.Reporting.Data;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Anela.Heblo.Application.Features.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        // NpgsqlDataSource is registered by PersistenceModule except in in-memory mode.
        services.AddSingleton<IReportingDataReader>(sp => sp.GetService<NpgsqlDataSource>() is { } dataSource
            ? new NpgsqlReportingDataReader(dataSource)
            : new UnavailableReportingDataReader());

        // MediatR handlers are automatically registered by AddMediatR scan
        return services;
    }
}
```

In `ApplicationModule.cs` add `using Anela.Heblo.Application.Features.Reporting;` and, right after `services.AddProcessDocsModule();`, add `services.AddReportingModule();`.

- [ ] **Step 8: Run the handler tests — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Reporting&Category!=Integration" -p:UseSharedCompilation=false`

- [ ] **Step 9: Run the response-contract and DI tests** (a reflection test fails any `*Response` not inheriting `BaseResponse`; DI tests resolve every handler)

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~BaseResponse|FullyQualifiedName~DependencyInjection|FullyQualifiedName~Contract" -p:UseSharedCompilation=false`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Reporting backend/src/Anela.Heblo.Application/ApplicationModule.cs backend/test/Anela.Heblo.Tests/Features/Reporting/ReportingHandlersTests.cs
git commit -m "feat(reporting): list/describe/query handlers with per-source permission"
```

---

### Task 7: MCP tools

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/ReportingMcpTools.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs` (add `.WithTools<ReportingMcpTools>()` after `.WithTools<ProcessDocsMcpTools>()`)
- Test: `backend/test/Anela.Heblo.Tests/MCP/Tools/ReportingMcpToolsTests.cs`

**Interfaces:**
- Consumes: the three requests/responses and DTOs from Task 6.
- Produces: MCP tools `ListReportDatasets()`, `DescribeReportDataset(string dataset)`, `QueryReportDataset(string dataset, List<string>? columns, List<ReportFilterDto>? filters, List<ReportSortDto>? sort, int? limit, int? offset)`; each returns JSON (`McpJsonOptions.Default`) or throws `McpException("[<ErrorCode>] …")`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;
using Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;
using Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;
using Anela.Heblo.Application.Shared;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class ReportingMcpToolsTests
{
    private readonly Mock<IMediator> _mediator = new();

    private ReportingMcpTools CreateTools() => new(_mediator.Object);

    [Fact]
    public async Task ListReportDatasets_serializes_the_response()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ListReportDatasetsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListReportDatasetsResponse { Datasets = [new ReportDatasetSummaryDto { Id = "ga4.traffic_monthly" }] });

        var json = await CreateTools().ListReportDatasets();

        Assert.Contains("ga4.traffic_monthly", json);
    }

    [Fact]
    public async Task QueryReportDataset_passes_every_parameter()
    {
        QueryReportDatasetRequest? sent = null;
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<QueryReportDatasetResponse>, CancellationToken>((r, _) => sent = (QueryReportDatasetRequest)r)
            .ReturnsAsync(new QueryReportDatasetResponse { Dataset = "shoptet.orders_monthly", Rows = [[new DateOnly(2026, 9, 1), 42L]], RowCount = 1 });

        var json = await CreateTools().QueryReportDataset(
            "shoptet.orders_monthly",
            columns: ["order_month", "orders"],
            filters: [new ReportFilterDto { Column = "channel", Op = "eq", Value = "eshop_mo" }],
            sort: [new ReportSortDto { Column = "orders", Direction = "desc" }],
            limit: 10,
            offset: 20);

        Assert.Equal("shoptet.orders_monthly", sent!.Dataset);
        Assert.Equal(new[] { "order_month", "orders" }, sent.Columns!);
        Assert.Equal("channel", sent.Filters![0].Column);
        Assert.Equal("desc", sent.Sort![0].Direction);
        Assert.Equal(10, sent.Limit);
        Assert.Equal(20, sent.Offset);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("2026-09-01", doc.RootElement.GetProperty("Rows")[0][0].GetString());
        Assert.Equal(42, doc.RootElement.GetProperty("Rows")[0][1].GetInt64());
    }

    [Fact]
    public async Task Forbidden_becomes_an_McpException_naming_the_role()
    {
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryReportDatasetResponse(ErrorCodes.Forbidden,
                new Dictionary<string, string> { ["RequiredRole"] = "reporting.accounting.read" }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().QueryReportDataset("flexi.cost_monthly"));

        Assert.Contains("[Forbidden]", ex.Message);
        Assert.Contains("reporting.accounting.read", ex.Message);
    }

    [Fact]
    public async Task Not_found_lists_suggestions_and_points_to_ListReportDatasets()
    {
        _mediator.Setup(m => m.Send(It.IsAny<DescribeReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DescribeReportDatasetResponse(ErrorCodes.ReportingDatasetNotFound,
                new Dictionary<string, string> { ["Dataset"] = "shoptet.order", ["Suggestions"] = "shoptet.orders_monthly" }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().DescribeReportDataset("shoptet.order"));

        Assert.Contains("[ReportingDatasetNotFound]", ex.Message);
        Assert.Contains("shoptet.orders_monthly", ex.Message);
        Assert.Contains("ListReportDatasets", ex.Message);
    }

    [Fact]
    public async Task Invalid_query_passes_the_reason_through()
    {
        _mediator.Setup(m => m.Send(It.IsAny<QueryReportDatasetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryReportDatasetResponse(ErrorCodes.ReportingInvalidQuery,
                new Dictionary<string, string> { ["Reason"] = "Unknown column 'nope'" }));

        var ex = await Assert.ThrowsAsync<McpException>(() => CreateTools().QueryReportDataset("shoptet.orders_monthly"));

        Assert.Contains("Unknown column 'nope'", ex.Message);
    }
}
```

- [ ] **Step 2: Run — expect compile failure** (`ReportingMcpTools` missing)

- [ ] **Step 3: Implement the tools**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.API.Infrastructure.Json;
using Anela.Heblo.Application.Features.Reporting.Contracts;
using Anela.Heblo.Application.Features.Reporting.UseCases.DescribeReportDataset;
using Anela.Heblo.Application.Features.Reporting.UseCases.ListReportDatasets;
using Anela.Heblo.Application.Features.Reporting.UseCases.QueryReportDataset;
using Anela.Heblo.Application.Shared;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

/// <summary>
/// Reporting data (GA4, Shoptet, Flexi). Permissions are enforced in the handlers, per source —
/// these tools deliberately do not call EnsureFeatureAccess.
/// </summary>
[McpServerToolType]
public class ReportingMcpTools
{
    private readonly IMediator _mediator;

    public ReportingMcpTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    [Description(
        "List Anela's reporting datasets you are allowed to read: website traffic (Google Analytics 4), e-shop orders, " +
        "customers and product sales (Shoptet), and company costs / marketing / ad spend (ABRA Flexi accounting). All are " +
        "month grain. Call this FIRST for any question about sales, revenue, orders, customers, traffic, conversion or " +
        "costs over time. Returns id, source, description, grain and how fresh the data is; then call DescribeReportDataset.")]
    public async Task<string> ListReportDatasets(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ListReportDatasetsRequest(), cancellationToken);
        EnsureSuccess(response);
        return JsonSerializer.Serialize(response.Datasets, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Describe one reporting dataset: its columns (name, type, meaning), period column, default sort and caveats. " +
        "ALWAYS read the caveats before presenting numbers (e.g. whether amounts include VAT, or why GA4 figures differ " +
        "from the ERP) and repeat the relevant ones to the user.")]
    public async Task<string> DescribeReportDataset(
        [Description("Dataset id exactly as returned by ListReportDatasets, e.g. 'shoptet.orders_monthly'.")] string dataset,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DescribeReportDatasetRequest { Dataset = dataset }, cancellationToken);
        EnsureSuccess(response);
        return JsonSerializer.Serialize(response.Dataset, McpJsonOptions.Default);
    }

    [McpServerTool]
    [Description(
        "Query one reporting dataset. Returns column headers once and rows as value arrays, plus hasMore for paging. " +
        "Filter ops: eq, neq, gt, gte, lt, lte (one 'value'), in (1-100 'values'), between (2 'values', inclusive), " +
        "contains (text, case-insensitive), isNull ('value' true/false). Dates accept yyyy-MM-dd or yyyy-MM. " +
        "Always filter the period column to the months you need. limit 1-1000 (default 100); page with offset while hasMore is true.")]
    public async Task<string> QueryReportDataset(
        [Description("Dataset id from ListReportDatasets.")] string dataset,
        [Description("Optional subset of columns to return, in this order. Omit for all.")] List<string>? columns = null,
        [Description("Optional filters, ANDed together.")] List<ReportFilterDto>? filters = null,
        [Description("Optional sort, e.g. [{\"column\":\"revenue_with_vat_czk\",\"direction\":\"desc\"}]. Omit for the dataset default.")] List<ReportSortDto>? sort = null,
        [Description("Max rows, 1-1000, default 100.")] int? limit = null,
        [Description("Rows to skip for paging, default 0.")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new QueryReportDatasetRequest
        {
            Dataset = dataset,
            Columns = columns,
            Filters = filters,
            Sort = sort,
            Limit = limit,
            Offset = offset,
        }, cancellationToken);
        EnsureSuccess(response);
        return JsonSerializer.Serialize(response, McpJsonOptions.Default);
    }

    private static void EnsureSuccess(BaseResponse response)
    {
        if (response.Success)
            return;

        var code = response.ErrorCode?.ToString() ?? "UNKNOWN_ERROR";
        var p = response.Params ?? new Dictionary<string, string>();
        var detail = response.ErrorCode switch
        {
            ErrorCodes.Forbidden =>
                $"You do not have permission to read this data (requires {p.GetValueOrDefault("RequiredRole")}). Ask an administrator to grant it in Heblo access settings.",
            ErrorCodes.ReportingDatasetNotFound =>
                $"No dataset '{p.GetValueOrDefault("Dataset")}'." +
                (string.IsNullOrEmpty(p.GetValueOrDefault("Suggestions")) ? string.Empty : $" Did you mean: {p["Suggestions"]}?") +
                " Call ListReportDatasets for all ids.",
            ErrorCodes.ReportingInvalidQuery => p.GetValueOrDefault("Reason") ?? "Invalid query.",
            ErrorCodes.ReportingQueryTimeout =>
                "The query took too long. Narrow the period filter or add filters, then retry.",
            _ => response.FullError(),
        };
        throw new McpException($"[{code}] {detail}");
    }
}
```

The serialized response includes `Success`/`ErrorCode`/`Params` from `BaseResponse`; that matches the other tools that serialize whole responses.

- [ ] **Step 4: Register in `McpModule.cs`** — add `.WithTools<ReportingMcpTools>()` after `.WithTools<ProcessDocsMcpTools>()`; no new `using` is needed (same namespace as the other tools).

- [ ] **Step 5: Run the MCP tests — expect PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ReportingMcpToolsTests|FullyQualifiedName~McpModule" -p:UseSharedCompilation=false`
If the `DateOnly` row value serializes as an object instead of `"2026-09-01"`, `McpJsonOptions` lacks a `DateOnly` converter on this runtime — .NET 8 System.Text.Json supports `DateOnly` natively, so first check the test's runtime target.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.API/MCP/Tools/ReportingMcpTools.cs backend/src/Anela.Heblo.API/MCP/McpModule.cs backend/test/Anela.Heblo.Tests/MCP/Tools/ReportingMcpToolsTests.cs
git commit -m "feat(reporting): expose reporting datasets as MCP tools"
```

---

### Task 8: Documentation and full validation

**Files:**
- Modify: `docs/integrations/mcp-server.md` (intro sentence + new "Reporting (3)" section after "Pricing Simulator (6)")
- Create: `docs/processes/module-reporting.md`
- Modify: `docs/architecture/metabase.md` (one line under *Reporting schemas in `Heblo_V3`*)
- Regenerated: `docs/processes/INDEX.md`

- [ ] **Step 1: Add the MCP doc section** to `docs/integrations/mcp-server.md`, after the Pricing Simulator block:

```markdown
**Reporting (3)** — read-only, month-grain reporting data mirrored into `Heblo_V3`. Permission is per **source** and enforced in the MediatR handlers (not in the tool), so it applies to any entry point: `Reporting_WebAnalytics` (GA4, `reporting.web_analytics.read`), `Reporting_Eshop` (Shoptet, `reporting.eshop.read`), `Reporting_Accounting` (Flexi, `reporting.accounting.read`). A caller only sees the datasets of sources they can read.
- `ListReportDatasets` — datasets the caller may read, with source, grain and data freshness (last sync, status)
- `DescribeReportDataset` — columns, period column, default sort and caveats of one dataset
- `QueryReportDataset` — filters (`eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `in`, `between`, `contains`, `isNull`), sort, column projection, `limit` ≤ 1000 and `offset`; runs read-only with a 10 s statement timeout

The catalogue (17 datasets) lives in `Application/Features/Reporting/Catalog/`. Payroll, raw tables, per-customer rows and `v_product_pair_monthly` (17 s per month — needs materialising) are deliberately not exposed.

**Operations:** after deploy, grant the three roles in `/admin/access` (staging first, then production). `seedGroups` only apply to a fresh database.
```

Also change the intro sentence to: "The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, perform batch calculations, user-directory lookups, meeting notes, pricing simulations, and month-grain reporting data (GA4, Shoptet, Flexi)."

- [ ] **Step 2: Create `docs/processes/module-reporting.md`**

```markdown
---
process: module-reporting
kind: module
module: reporting
summary: Read-only access for Claude (Heblo MCP) to the month-grain reporting views over GA4 traffic, Shoptet orders and Flexi costs, gated by one permission per source.
owns: []
verified_at: "0000000"
related: [sync-ga4-aggregates, sync-shoptet-orders, sync-flexi-analytics]
---

# Reporting data (MCP)

## Purpose
Lets staff ask Claude questions like "how did e-shop revenue develop this year vs last?",
"which channels bring traffic?" or "what did we spend on Meta ads per month?" without Metabase.
It reads the same curated `v_*` views that Metabase reads; it never writes.

## Users & screens
No screens (ADR-007: reports are not rendered in the Heblo UI). MCP tools `ListReportDatasets`,
`DescribeReportDataset`, `QueryReportDataset`. Permissions: `Reporting_WebAnalytics` (GA4),
`Reporting_Eshop` (Shoptet), `Reporting_Accounting` (Flexi), read only, checked in the handlers.

## Processes
None of its own. Data is produced by `sync-ga4-aggregates`, `sync-shoptet-orders` and
`sync-flexi-analytics`; freshness shown to the agent comes from their `sync_state` tables.

## Data owned
None. Reads `ga4_agg.v_monthly_*`, `shoptet_raw.v_*` (8 views) and `flexi_raw.v_*` (4 matviews).

## External systems
None directly.

## Dependencies
Reads the reporting schemas through the shared `NpgsqlDataSource` from `PersistenceModule`.

## Known quirks
- `v_product_pair_monthly` is not exposed: as a plain view it takes ~17 s for one month on the B1ms server.
- Payroll (`v_payroll_monthly`), `v_posting`, raw tables and `v_customer_repeat_purchase` (md5 of e-mail per customer) are never exposed.
- Queries run with raw ADO.NET, not EF, so the Polly strategy cannot retry a slow statement.
- A new permission does not reach existing production groups automatically; grant it in `/admin/access`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Reporting/Catalog/ReportDatasetCatalog.cs` — what is exposed
- `backend/src/Anela.Heblo.Application/Features/Reporting/Query/ReportSqlBuilder.cs` — query → SQL
- `backend/src/Anela.Heblo.Application/Features/Reporting/Data/NpgsqlReportingDataReader.cs` — execution
- `backend/src/Anela.Heblo.API/MCP/Tools/ReportingMcpTools.cs` — MCP surface
```

Set `verified_at` to the current short SHA: `git rev-parse --short=9 HEAD`, quoted.

- [ ] **Step 3: Note the second reader in `docs/architecture/metabase.md`** — under the *Reporting schemas in `Heblo_V3`* table add:

```markdown
The same `v_*` views are also read by Heblo's MCP reporting tools (`module-reporting`), gated by a
Heblo permission per source. That path connects as the app's own role, not `metabase_ro`, so the
exposure boundary there is the dataset catalogue in code, not a Postgres grant.
```

- [ ] **Step 4: Regenerate the process index**

Run: `python3 scripts/process-docs/check.py index`
Expected: `docs/processes/INDEX.md` gains a `module-reporting` line; no errors.

- [ ] **Step 5: Full validation**

```bash
cd backend && dotnet build -p:UseSharedCompilation=false && dotnet format --verify-no-changes
cd backend && dotnet test --filter "Category!=Integration" -p:UseSharedCompilation=false
cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~Features.Reporting" -p:UseSharedCompilation=false   # includes Integration; needs podman machine start
cd frontend && npm run build && npm run lint
```

Expected: all green. If `dotnet format --verify-no-changes` fails, run `dotnet format`, review the diff, and commit it.

- [ ] **Step 6: Commit**

```bash
git add docs/integrations/mcp-server.md docs/processes/module-reporting.md docs/processes/INDEX.md docs/architecture/metabase.md
git commit -m "docs(reporting): MCP reporting tools, module doc and ops note"
```

---

## Post-merge rollout (manual, not part of the PR)

1. Deploy to staging. In `/admin/access` grant `reporting.*.read` to the agreed staging groups.
2. From Claude connected to `https://heblo.stg.anela.cz/mcp`: with a user holding only `reporting.eshop.read`, `ListReportDatasets` shows 8 `shoptet.*` datasets; `QueryReportDataset flexi.cost_monthly` returns `[Forbidden]`.
3. Query one dataset per source with a filter on the period column; compare one figure with Metabase.
4. Repeat the grants on production (`https://heblo.anela.cz`).
