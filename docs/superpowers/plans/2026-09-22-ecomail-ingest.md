# Ecomail Ingest Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pull Ecomail newsletter and automation statistics into the primary Heblo database on a schedule, so Cluster B items #4/#5/#6 can be answered with SQL instead of live API calls.

**Architecture:** A new `Anela.Heblo.Adapters.Ecomail` adapter wraps the Ecomail v2 REST API. An `Ecomail` application module owns a sync service and an `IRecurringJob` that runs every 6 hours. Campaign rows are upserted with their lifetime stats (final for a one-off send); automation rows are written as **append-only dated snapshots** of cumulative counters, because per-month automation conversions exist nowhere else. Presentation (API endpoints + React pages) is explicitly **out of scope** — it is a separate plan, blocked on questions for Bára.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, Polly 8, Hangfire (via `IRecurringJob`), xUnit + FluentAssertions + Moq.

**Spec:** `CLUSTER-B-FINDINGS.md` (repo root of this worktree) — §8 measured API behaviour, §9 the storage decision and table shapes.

## Global Constraints

- **Target framework `net8.0`**, `Nullable` and `ImplicitUsings` enabled — matches every other adapter.
- **Every `DateTime` property mapped with `.AsUtcTimestamp()`** (`Anela.Heblo.Persistence.Extensions`). A global converter forces `Kind=Unspecified`, so a `timestamp with time zone` column fails every write. A migration fixing exactly this shipped on 2026-09-22 (`FixMarketingPerformanceTimestampKind`) — do not reintroduce it.
- **No Application type may be named `*Response` unless it inherits `BaseResponse`** — a reflection contract test fails in CI otherwise. The sync result type is therefore `EcomailSyncReport`.
- **DTOs crossing the OpenAPI boundary are classes, never records.** Nothing in this plan crosses it; internal types may be records.
- **Options validators are registered manually** — there is no `AddValidatorsFromAssembly`. Register `IValidateOptions<T>` explicitly, as `MarketingPerformanceModule` does.
- **Tables are PascalCase in the `public` schema**: `builder.ToTable("EcomailCampaigns", "public")`. The `*_raw` snake_case convention belongs to the abandoned `anela_analytics` database — do not use it.
- **Migrations are applied manually** after deploy. Generating the migration is in scope; running it against any live database is not.
- **The API key lives in Azure Key Vault as `Ecomail--ApiKey`**, never in `appsettings*.json` and never in App Service settings.
- **Ecomail API limits:** 1000 req/min, 429 + `Retry-After`; `per_page` maximum is **50** (51+ returns 422).

---

## File Structure

| File | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailCampaign.cs` | Campaign entity + the reportable-selection rule |
| `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailPipeline.cs` | Automation dimension entity |
| `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailAutomationSnapshot.cs` | Append-only cumulative observation |
| `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailAutomationMonth.cs` | Per-month automation sums |
| `backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailRepository.cs` | Persistence port |
| `backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailApiClient.cs` | API port + its transport DTOs |
| `backend/src/Anela.Heblo.Persistence/Ecomail/*Configuration.cs` | EF mappings (4 files) |
| `backend/src/Anela.Heblo.Persistence/Ecomail/EcomailRepository.cs` | Repository implementation |
| `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/` | Options, HTTP client, DI registration |
| `backend/src/Anela.Heblo.Application/Features/Ecomail/` | Sync service, recurring job, module |
| `backend/test/Anela.Heblo.Tests/Ecomail/` | All tests for the above |

Four tasks. Each ends with a green build and a commit.

---

### Task 1: Entities, EF mappings and migration

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailCampaign.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailPipeline.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailAutomationSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailAutomationMonth.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Ecomail/EcomailCampaignConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Ecomail/EcomailPipelineConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Ecomail/EcomailAutomationSnapshotConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Ecomail/EcomailAutomationMonthConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (add four `DbSet`s near the existing ones, ~line 51)
- Test: `backend/test/Anela.Heblo.Tests/Ecomail/EcomailModelMappingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `EcomailCampaign`, `EcomailPipeline`, `EcomailAutomationSnapshot`, `EcomailAutomationMonth` (all in namespace `Anela.Heblo.Domain.Features.Ecomail`), and `EcomailCampaign.IsReportable` / `EcomailCampaign.ReportableTypes`.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Ecomail/EcomailModelMappingTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailModelMappingTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ecomail-model-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData(typeof(EcomailCampaign), "EcomailCampaigns")]
    [InlineData(typeof(EcomailPipeline), "EcomailPipelines")]
    [InlineData(typeof(EcomailAutomationSnapshot), "EcomailAutomationSnapshots")]
    [InlineData(typeof(EcomailAutomationMonth), "EcomailAutomationMonths")]
    public void maps_each_entity_to_its_pascal_case_table_in_public_schema(Type entity, string table)
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(entity);

        entityType.Should().NotBeNull($"{entity.Name} must be registered on ApplicationDbContext");
        entityType!.GetTableName().Should().Be(table);
        entityType.GetSchema().Should().Be("public");
    }

    [Fact]
    public void snapshot_is_unique_per_pipeline_and_capture_date()
    {
        using var context = CreateContext();

        var indexes = context.Model.FindEntityType(typeof(EcomailAutomationSnapshot))!.GetIndexes();

        indexes.Should().ContainSingle(i =>
                i.IsUnique &&
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { "PipelineId", "CapturedOn" }),
            "one snapshot per automation per day is what makes the job safe to re-run");
    }

    [Fact]
    public void automation_month_is_unique_per_pipeline_and_month()
    {
        using var context = CreateContext();

        var indexes = context.Model.FindEntityType(typeof(EcomailAutomationMonth))!.GetIndexes();

        indexes.Should().ContainSingle(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "PipelineId", "Year", "Month" }));
    }

    [Fact]
    public void every_datetime_column_is_timestamp_without_time_zone()
    {
        using var context = CreateContext();
        var entities = new[]
        {
            typeof(EcomailCampaign), typeof(EcomailPipeline),
            typeof(EcomailAutomationSnapshot), typeof(EcomailAutomationMonth)
        };

        foreach (var entity in entities)
        {
            var dateTimeProps = context.Model.FindEntityType(entity)!.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?));

            foreach (var prop in dateTimeProps)
            {
                prop.GetColumnType().Should().Be("timestamp",
                    $"{entity.Name}.{prop.Name} must use AsUtcTimestamp() or Postgres rejects every write");
            }
        }
    }

    [Theory]
    [InlineData(3, "email", true)]
    [InlineData(3, "ab", true)]
    [InlineData(3, "variation", false)]   // an A/B arm, already inside its parent's totals
    [InlineData(3, "sms", false)]         // not email at all
    [InlineData(0, "email", false)]       // draft
    public void reportable_selects_only_sent_email_and_ab_campaigns(int status, string type, bool expected)
    {
        var campaign = new EcomailCampaign { Status = status, CampaignType = type };

        campaign.IsReportable.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailModelMappingTests" -p:UseSharedCompilation=false
```

Expected: FAIL — `EcomailCampaign` does not exist (compile error).

> If `dotnet test` hangs at 0% CPU, another worktree is running it concurrently. Build first, then re-run with `--no-build`.

- [ ] **Step 3: Create the four entities**

`backend/src/Anela.Heblo.Domain/Features/Ecomail/EcomailCampaign.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>
/// One Ecomail campaign. Id is Ecomail's own campaign id (natural key, not generated).
/// Stats are lifetime totals, which for a one-off send are final within days — so they live
/// here as columns rather than in a snapshot table. Counts only; rates are derived at read time.
/// </summary>
public class EcomailCampaign
{
    /// <summary>Campaign types that represent a real newsletter send. See CLUSTER-B-FINDINGS.md §8.6.</summary>
    public static readonly string[] ReportableTypes = { "email", "ab" };

    /// <summary>Ecomail status code 3 = sent.</summary>
    public const int SentStatus = 3;

    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? FromEmail { get; set; }

    /// <summary>"email" | "ab" | "variation" | "sms". String, not enum: Ecomail may add more.</summary>
    public string CampaignType { get; set; } = string.Empty;

    public int Status { get; set; }

    /// <summary>Set on every sent non-variation campaign, without exception across 250 campaigns.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>Set on "variation" rows, pointing at the owning "ab" campaign.</summary>
    public int? ParentId { get; set; }

    public int Recipients { get; set; }

    public int Inject { get; set; }
    public int Delivery { get; set; }
    public int Open { get; set; }
    public int TotalOpen { get; set; }
    public int Click { get; set; }
    public int TotalClick { get; set; }
    public int Unsub { get; set; }
    public int Bounce { get; set; }
    public int Spam { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }

    public DateTime SyncedAt { get; set; }

    /// <summary>
    /// True when this row is a newsletter that should appear in reporting: a sent campaign that is
    /// neither an A/B arm (its numbers are a subset of its parent's) nor SMS.
    /// </summary>
    public bool IsReportable =>
        Status == SentStatus && ReportableTypes.Contains(CampaignType);
}
```

`EcomailPipeline.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>An Ecomail automation ("pipeline"). Id is Ecomail's own id.</summary>
public class EcomailPipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ListId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}
```

`EcomailAutomationSnapshot.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>
/// What an automation's cumulative counters read on a given day. Append-only: never recomputed,
/// never locked, because this is an observation rather than a derived aggregate.
///
/// This table is the entire reason the ingest exists. Ecomail exposes only lifetime totals for an
/// automation, and its date-filter parameters on /pipelines/{id}/stats are a no-op
/// (CLUSTER-B-FINDINGS.md §8.3). Monthly conversions are therefore the delta between two rows here,
/// and a day not captured is a hole that can never be backfilled.
/// </summary>
public class EcomailAutomationSnapshot
{
    public int Id { get; set; }

    public int PipelineId { get; set; }

    /// <summary>Date the counters were read, in Europe/Prague. Maps to a Postgres `date`.</summary>
    public DateOnly CapturedOn { get; set; }

    public int Triggered { get; set; }
    public int Ended { get; set; }
    public int Send { get; set; }
    public int Open { get; set; }
    public int Click { get; set; }
    public int Unsub { get; set; }
    public int Bounce { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }
}
```

`EcomailAutomationMonth.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>
/// Per-automation, per-month event sums, read from /pipelines/{id}/stats-detail date windows —
/// the one filter Ecomail honours. Retroactively backfillable, so unlike the snapshot table this
/// one is recomputable. Sums only; rates are derived at read time.
///
/// Carries no conversions column on purpose: stats-detail has no conversion event
/// (CLUSTER-B-FINDINGS.md §8.5). Monthly conversions come from snapshot deltas instead.
/// </summary>
public class EcomailAutomationMonth
{
    public int Id { get; set; }

    public int PipelineId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    public int Send { get; set; }
    public int Open { get; set; }
    public int Click { get; set; }
    public int Unsub { get; set; }

    public DateTime ComputedAt { get; set; }

    /// <summary>True once the month has left the recompute window; only an explicit recompute touches it.</summary>
    public bool IsLocked { get; set; }

    public string? LastError { get; set; }
}
```

- [ ] **Step 4: Create the four EF configurations**

`backend/src/Anela.Heblo.Persistence/Ecomail/EcomailCampaignConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailCampaignConfiguration : IEntityTypeConfiguration<EcomailCampaign>
{
    public void Configure(EntityTypeBuilder<EcomailCampaign> builder)
    {
        builder.ToTable("EcomailCampaigns", "public");

        builder.HasKey(x => x.Id);
        // Ecomail's id is the natural key — never generate one.
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FromEmail).HasMaxLength(320).IsRequired(false);
        builder.Property(x => x.CampaignType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).IsRequired();

        builder.Property(x => x.SentAt).IsRequired(false).AsUtcTimestamp();
        builder.Property(x => x.SyncedAt).IsRequired().AsUtcTimestamp();

        builder.Property(x => x.ConversionsValue).HasPrecision(18, 2);

        // The reporting read path filters on these two together.
        builder.HasIndex(x => new { x.Status, x.CampaignType });
        builder.HasIndex(x => x.SentAt);
        builder.HasIndex(x => x.ParentId);

        builder.Ignore(x => x.IsReportable);
    }
}
```

`EcomailPipelineConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailPipelineConfiguration : IEntityTypeConfiguration<EcomailPipeline>
{
    public void Configure(EntityTypeBuilder<EcomailPipeline> builder)
    {
        builder.ToTable("EcomailPipelines", "public");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired(false).AsUtcTimestamp();
        builder.Property(x => x.UpdatedAt).IsRequired(false).AsUtcTimestamp();
        builder.Property(x => x.SyncedAt).IsRequired().AsUtcTimestamp();
    }
}
```

`EcomailAutomationSnapshotConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailAutomationSnapshotConfiguration : IEntityTypeConfiguration<EcomailAutomationSnapshot>
{
    public void Configure(EntityTypeBuilder<EcomailAutomationSnapshot> builder)
    {
        builder.ToTable("EcomailAutomationSnapshots", "public");

        builder.HasKey(x => x.Id);

        // DateOnly maps to Postgres `date` — no AsUtcTimestamp needed, and no timezone hazard.
        builder.Property(x => x.CapturedOn).HasColumnType("date").IsRequired();

        builder.Property(x => x.ConversionsValue).HasPrecision(18, 2);

        // Makes the job safe to re-run inside a day: the second write is rejected, not duplicated.
        builder.HasIndex(x => new { x.PipelineId, x.CapturedOn }).IsUnique();
    }
}
```

`EcomailAutomationMonthConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailAutomationMonthConfiguration : IEntityTypeConfiguration<EcomailAutomationMonth>
{
    public void Configure(EntityTypeBuilder<EcomailAutomationMonth> builder)
    {
        builder.ToTable("EcomailAutomationMonths", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ComputedAt).IsRequired().AsUtcTimestamp();
        builder.Property(x => x.LastError).HasMaxLength(2000).IsRequired(false);

        builder.HasIndex(x => new { x.PipelineId, x.Year, x.Month }).IsUnique();
    }
}
```

- [ ] **Step 5: Register the DbSets**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, add alongside the existing `DbSet` declarations (around line 51). Configurations are picked up automatically by the existing `ApplyConfigurationsFromAssembly` call at line 210 — do not add anything to `OnModelCreating`.

```csharp
    public DbSet<EcomailCampaign> EcomailCampaigns { get; set; } = null!;
    public DbSet<EcomailPipeline> EcomailPipelines { get; set; } = null!;
    public DbSet<EcomailAutomationSnapshot> EcomailAutomationSnapshots { get; set; } = null!;
    public DbSet<EcomailAutomationMonth> EcomailAutomationMonths { get; set; } = null!;
```

Add `using Anela.Heblo.Domain.Features.Ecomail;` to the file's usings.

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailModelMappingTests" -p:UseSharedCompilation=false
```

Expected: PASS, 8 tests.

- [ ] **Step 7: Generate the migration**

```bash
cd backend/src/Anela.Heblo.Persistence
dotnet ef migrations add AddEcomailIngestTables --startup-project ../Anela.Heblo.API
```

Run from the `Anela.Heblo.Persistence` directory — `DesignTimeDbContextFactory` resolves configuration via `../Anela.Heblo.API` relative to the current directory.

Open the generated `*_AddEcomailIngestTables.cs` and confirm: four `CreateTable` calls, all `timestamp without time zone` (never `timestamptz`), `CapturedOn` typed `date`, and the two unique indexes. **Do not apply it to any database** — migrations here are manual and deliberate.

- [ ] **Step 8: Verify the build**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Ecomail \
        backend/src/Anela.Heblo.Persistence/Ecomail \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Persistence/Migrations \
        backend/test/Anela.Heblo.Tests/Ecomail
git commit -m "feat: add Ecomail ingest entities and migration"
```

---

### Task 2: Ecomail API client

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailApiClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/Anela.Heblo.Adapters.Ecomail.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailOptions.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailOptionsValidator.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailApiClient.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/EcomailAdapterServiceCollectionExtensions.cs`
- Modify: `backend/Anela.Heblo.sln` (add the project), `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj` (project reference)
- Test: `backend/test/Anela.Heblo.Tests/Ecomail/EcomailApiClientTests.cs`

**Interfaces:**
- Consumes: `EcomailCampaign`, `EcomailPipeline` from Task 1 (namespace only — the client returns DTOs, not entities).
- Produces:

```csharp
public interface IEcomailApiClient
{
    Task<IReadOnlyList<EcomailCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default);
    Task<EcomailStatsDto?> GetCampaignStatsAsync(int campaignId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EcomailPipelineDto>> GetPipelinesAsync(CancellationToken cancellationToken = default);
    Task<EcomailStatsDto?> GetPipelineStatsAsync(int pipelineId, CancellationToken cancellationToken = default);
    Task<int> GetPipelineEventCountAsync(int pipelineId, string eventName, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Ecomail/EcomailApiClientTests.cs`. The payloads below are trimmed captures of real responses from the `anela` account.

```csharp
using System.Net;
using System.Text;
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Domain.Features.Ecomail;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailApiClientTests
{
    private const string CampaignsPage = """
    [
      {"id":264,"title":"Plet v lete_0726","subject":"Co slunce pleti bere","from_email":"info@newsletter.anela.cz",
       "sent_at":"2026-07-26 05:33:17","scheduled_at":"2026-07-26 07:30:00","recipients":12715,"status":3,
       "campaign_type":"ab","parent_id":null},
      {"id":266,"title":"Plet v lete_0726","subject":"Co slunce pleti bere","from_email":"info@newsletter.anela.cz",
       "sent_at":null,"scheduled_at":"2026-07-26 07:30:00","recipients":1271,"status":3,
       "campaign_type":"variation","parent_id":264}
    ]
    """;

    private const string CampaignStats = """
    {"stats":{"inject":12715,"delivery":12707,"delivery_rate":99.94,"open":3248,"total_open":4100,
      "open_rate":25.56,"click":49,"total_click":73,"click_rate":0.39,"bounce":8,"bounce_rate":0.06,
      "spam":0,"spam_rate":0,"unsub":33,"unsub_rate":0.26,"conversions":42,"conversions_value":72302}}
    """;

    private const string Pipelines = """
    [{"id":14720,"name":"Opusteny kosik_2025","list_id":1,
      "created_at":"2025-09-08 22:04:03","updated_at":"2026-04-03 20:04:04"}]
    """;

    private const string StatsDetail = """
    {"next_page_url":null,"total":403,"per_page":1,"subscribers":{"foo@bar.cz":{"open":2}}}
    """;

    private static (EcomailApiClient client, List<HttpRequestMessage> requests) CreateClient(
        params (HttpStatusCode status, string body)[] responses)
    {
        var requests = new List<HttpRequestMessage>();
        var queue = new Queue<(HttpStatusCode, string)>(responses);
        var handler = new Mock<HttpMessageHandler>();

        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request);
                var (status, body) = queue.Count > 0 ? queue.Dequeue() : (HttpStatusCode.OK, "[]");
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler.Object) { BaseAddress = new Uri("https://api2.ecomailapp.cz") });

        var options = Options.Create(new EcomailOptions { ApiKey = "test-key" });
        return (new EcomailApiClient(options, factory.Object, NullLogger<EcomailApiClient>.Instance), requests);
    }

    [Fact]
    public async Task sends_the_api_key_in_the_key_header()
    {
        var (client, requests) = CreateClient((HttpStatusCode.OK, "[]"));

        await client.GetPipelinesAsync();

        requests.Single().Headers.GetValues("key").Should().ContainSingle().Which.Should().Be("test-key");
    }

    [Fact]
    public async Task requests_campaigns_with_per_page_50_because_51_is_rejected()
    {
        var (client, requests) = CreateClient((HttpStatusCode.OK, "[]"));

        await client.GetCampaignsAsync();

        requests[0].RequestUri!.Query.Should().Contain("per_page=50");
    }

    [Fact]
    public async Task pages_campaigns_until_a_short_page_arrives()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 50).Select(i =>
            $$"""{"id":{{i}},"title":"t","subject":"s","status":3,"campaign_type":"email","recipients":1}""")) + "]";
        var (client, requests) = CreateClient(
            (HttpStatusCode.OK, fullPage),
            (HttpStatusCode.OK, CampaignsPage));   // 2 items — short, so paging stops

        var campaigns = await client.GetCampaignsAsync();

        campaigns.Should().HaveCount(52);
        requests.Should().HaveCount(2);
        requests[1].RequestUri!.Query.Should().Contain("page=2");
    }

    [Fact]
    public async Task parses_campaign_type_parent_id_and_null_sent_at()
    {
        var (client, _) = CreateClient((HttpStatusCode.OK, CampaignsPage));

        var campaigns = await client.GetCampaignsAsync();

        var parent = campaigns.Single(c => c.Id == 264);
        parent.CampaignType.Should().Be("ab");
        parent.ParentId.Should().BeNull();
        parent.SentAt.Should().Be(new DateTime(2026, 7, 26, 5, 33, 17));
        parent.Recipients.Should().Be(12715);

        var variation = campaigns.Single(c => c.Id == 266);
        variation.CampaignType.Should().Be("variation");
        variation.ParentId.Should().Be(264);
        variation.SentAt.Should().BeNull("variations never carry sent_at");
    }

    [Fact]
    public async Task parses_stats_including_conversions_value()
    {
        var (client, _) = CreateClient((HttpStatusCode.OK, CampaignStats));

        var stats = await client.GetCampaignStatsAsync(264);

        stats.Should().NotBeNull();
        stats!.Inject.Should().Be(12715);
        stats.Open.Should().Be(3248);
        stats.Unsub.Should().Be(33);
        stats.Conversions.Should().Be(42);
        stats.ConversionsValue.Should().Be(72302m);
    }

    [Fact]
    public async Task parses_pipelines()
    {
        var (client, _) = CreateClient((HttpStatusCode.OK, Pipelines));

        var pipelines = await client.GetPipelinesAsync();

        pipelines.Should().ContainSingle();
        pipelines[0].Id.Should().Be(14720);
        pipelines[0].Name.Should().Be("Opusteny kosik_2025");
        pipelines[0].ListId.Should().Be(1);
    }

    [Fact]
    public async Task event_count_reads_total_and_requests_a_single_row()
    {
        var (client, requests) = CreateClient((HttpStatusCode.OK, StatsDetail));

        var count = await client.GetPipelineEventCountAsync(
            31762, "open", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        count.Should().Be(403);

        var query = requests.Single().RequestUri!.Query;
        query.Should().Contain("event=open");
        query.Should().Contain("from_date=2026-08-01");
        query.Should().Contain("to_date=2026-08-31");
        query.Should().Contain("per_page=1", "only .total is read, so never pull subscriber rows");
    }

    [Fact]
    public async Task event_count_returns_zero_when_total_is_null()
    {
        // Ecomail answers an unknown event name with {"total":null} rather than an error.
        var (client, _) = CreateClient((HttpStatusCode.OK, """{"total":null}"""));

        var count = await client.GetPipelineEventCountAsync(
            1, "conversion", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        count.Should().Be(0);
    }

    [Fact]
    public async Task stats_returns_null_when_the_campaign_is_gone()
    {
        var (client, _) = CreateClient((HttpStatusCode.NotFound, """{"message":"Not Found!"}"""));

        var stats = await client.GetCampaignStatsAsync(999999);

        stats.Should().BeNull("a deleted campaign must not fail the whole sync");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailApiClientTests" -p:UseSharedCompilation=false
```

Expected: FAIL — `Anela.Heblo.Adapters.Ecomail` does not exist.

- [ ] **Step 3: Create the adapter project**

`backend/src/Adapters/Anela.Heblo.Adapters.Ecomail/Anela.Heblo.Adapters.Ecomail.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net8.0</TargetFramework>
        <Nullable>enable</Nullable>
        <ImplicitUsings>enable</ImplicitUsings>
        <RootNamespace>Anela.Heblo.Adapters.Ecomail</RootNamespace>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
        <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
        <PackageReference Include="Polly" Version="8.4.1" />
        <PackageReference Include="Polly.Extensions" Version="8.4.1" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
    </ItemGroup>

</Project>
```

Register it:

```bash
cd backend && dotnet sln add src/Adapters/Anela.Heblo.Adapters.Ecomail/Anela.Heblo.Adapters.Ecomail.csproj
dotnet add src/Anela.Heblo.API/Anela.Heblo.API.csproj reference \
  src/Adapters/Anela.Heblo.Adapters.Ecomail/Anela.Heblo.Adapters.Ecomail.csproj
dotnet add test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj reference \
  src/Adapters/Anela.Heblo.Adapters.Ecomail/Anela.Heblo.Adapters.Ecomail.csproj
```

- [ ] **Step 4: Define the port and its DTOs**

`backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailApiClient.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Ecomail;

/// <summary>Read-only access to the Ecomail v2 API. Nothing here sends, triggers or deletes.</summary>
public interface IEcomailApiClient
{
    Task<IReadOnlyList<EcomailCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default);

    /// <summary>Lifetime stats. Null when the campaign no longer exists.</summary>
    Task<EcomailStatsDto?> GetCampaignStatsAsync(int campaignId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EcomailPipelineDto>> GetPipelinesAsync(CancellationToken cancellationToken = default);

    /// <summary>Cumulative lifetime stats for an automation. Null when it no longer exists.</summary>
    Task<EcomailStatsDto?> GetPipelineStatsAsync(int pipelineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unique subscribers with <paramref name="eventName"/> inside the window, read from
    /// stats-detail's `total`. This is the only date filter Ecomail actually honours.
    /// Returns 0 when Ecomail answers `{"total":null}`.
    /// </summary>
    Task<int> GetPipelineEventCountAsync(
        int pipelineId, string eventName, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default);
}

public class EcomailCampaignDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? FromEmail { get; set; }
    public string CampaignType { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime? SentAt { get; set; }
    public int? ParentId { get; set; }
    public int Recipients { get; set; }
}

public class EcomailPipelineDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ListId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Shared stats shape: /campaigns/{id}/stats and /pipelines/{id}/stats return the same fields.</summary>
public class EcomailStatsDto
{
    public int Inject { get; set; }
    public int Delivery { get; set; }
    public int Open { get; set; }
    public int TotalOpen { get; set; }
    public int Click { get; set; }
    public int TotalClick { get; set; }
    public int Unsub { get; set; }
    public int Bounce { get; set; }
    public int Spam { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }

    // Automation-only; absent on campaigns and left at 0 there.
    public int Triggered { get; set; }
    public int Ended { get; set; }
    public int Send { get; set; }
}
```

- [ ] **Step 5: Create options and validator**

`EcomailOptions.cs`:

```csharp
namespace Anela.Heblo.Adapters.Ecomail;

public class EcomailOptions
{
    public const string SectionName = "Ecomail";

    /// <summary>From Key Vault secret `Ecomail--ApiKey`. Empty disables the whole module.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api2.ecomailapp.cz";

    public int HttpTimeoutSeconds { get; set; } = 60;

    /// <summary>Every 6 hours — the full pull is ~400 calls and "current newsletter" numbers go stale overnight.</summary>
    public string CronExpression { get; set; } = "0 */6 * * *";

    public string TimeZone { get; set; } = "Europe/Prague";

    /// <summary>How many months of automation event windows the job recomputes. 2 = current + previous.</summary>
    public int RecomputeWindowMonths { get; set; } = 2;

    /// <summary>First month of automation history to backfill. The account's oldest send is 2024-11.</summary>
    public DateOnly BackfillFrom { get; set; } = new(2024, 11, 1);
}
```

`EcomailOptionsValidator.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Ecomail;

public class EcomailOptionsValidator : IValidateOptions<EcomailOptions>
{
    public ValidateOptionsResult Validate(string? name, EcomailOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("Ecomail:BaseUrl must be an absolute URL.");
        }

        if (options.HttpTimeoutSeconds is < 1 or > 600)
        {
            errors.Add("Ecomail:HttpTimeoutSeconds must be between 1 and 600.");
        }

        if (options.RecomputeWindowMonths < 1)
        {
            errors.Add("Ecomail:RecomputeWindowMonths must be >= 1.");
        }

        // ApiKey is deliberately not required: an empty key disables the module rather than
        // breaking startup for every developer who has no Ecomail credentials.
        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
```

- [ ] **Step 6: Implement the client**

`EcomailApiClient.cs`:

```csharp
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Ecomail;

public class EcomailApiClient : IEcomailApiClient
{
    public const string HttpClientName = "Ecomail";

    /// <summary>Ecomail rejects per_page above 50 with a 422.</summary>
    private const int PageSize = 50;

    private static readonly ResiliencePipeline DefaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<EcomailThrottledException>(),
        })
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private readonly EcomailOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EcomailApiClient> _logger;
    private readonly ResiliencePipeline _pipeline;

    public EcomailApiClient(
        IOptions<EcomailOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<EcomailApiClient> logger,
        ResiliencePipeline? pipeline = null)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pipeline = pipeline ?? DefaultPipeline;
    }

    public async Task<IReadOnlyList<EcomailCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<EcomailCampaignDto>();

        for (var page = 1; ; page++)
        {
            var url = $"/campaigns?per_page={PageSize}&page={page}";
            var batch = await GetAsync<List<EcomailCampaignDto>>(url, cancellationToken) ?? new List<EcomailCampaignDto>();

            all.AddRange(batch);

            // A short page means the last page. Ecomail returns a bare array with no total.
            if (batch.Count < PageSize)
            {
                return all;
            }
        }
    }

    public async Task<EcomailStatsDto?> GetCampaignStatsAsync(int campaignId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<StatsEnvelope>($"/campaigns/{campaignId}/stats", cancellationToken);
        return envelope?.Stats;
    }

    public async Task<IReadOnlyList<EcomailPipelineDto>> GetPipelinesAsync(CancellationToken cancellationToken = default)
        => await GetAsync<List<EcomailPipelineDto>>("/pipelines", cancellationToken) ?? new List<EcomailPipelineDto>();

    public async Task<EcomailStatsDto?> GetPipelineStatsAsync(int pipelineId, CancellationToken cancellationToken = default)
    {
        var envelope = await GetAsync<StatsEnvelope>($"/pipelines/{pipelineId}/stats", cancellationToken);
        return envelope?.Stats;
    }

    public async Task<int> GetPipelineEventCountAsync(
        int pipelineId, string eventName, DateOnly fromDate, DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        // per_page=1 because only `total` is read — never page through subscribers.
        var url = $"/pipelines/{pipelineId}/stats-detail" +
                  $"?event={Uri.EscapeDataString(eventName)}" +
                  $"&from_date={fromDate:yyyy-MM-dd}&to_date={toDate:yyyy-MM-dd}&per_page=1";

        var detail = await GetAsync<StatsDetailEnvelope>(url, cancellationToken);
        return detail?.Total ?? 0;
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        return await _pipeline.ExecuteAsync(async ct =>
        {
            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("key", _options.ApiKey);

            using var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _logger.LogWarning("Ecomail throttled {Url}; retrying after {RetryAfter}", url, retryAfter);
                throw new EcomailThrottledException(retryAfter);
            }

            // A deleted campaign must not fail the whole sync.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Ecomail returned {Status} for {Url}; skipping", response.StatusCode, url);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }, cancellationToken);
    }

    private sealed class StatsEnvelope
    {
        public EcomailStatsDto? Stats { get; set; }
    }

    private sealed class StatsDetailEnvelope
    {
        public int? Total { get; set; }
    }
}

public sealed class EcomailThrottledException : Exception
{
    public EcomailThrottledException(TimeSpan retryAfter)
        : base($"Ecomail throttled the request; retry after {retryAfter}.")
        => RetryAfter = retryAfter;

    public TimeSpan RetryAfter { get; }
}
```

> `sent_at` arrives as `"2026-07-26 05:33:17"` — a space, not `T`. `System.Text.Json` parses this into `DateTime` only via the invariant fallback, so if Step 7 shows a parse failure, add a `JsonConverter<DateTime?>` using `DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)` and apply it to `EcomailCampaignDto.SentAt`, `EcomailPipelineDto.CreatedAt` and `.UpdatedAt`.

- [ ] **Step 7: Create the DI registration**

`EcomailAdapterServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Ecomail;

public static class EcomailAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddEcomailAdapter(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EcomailOptions>()
            .Bind(configuration.GetSection(EcomailOptions.SectionName))
            .ValidateOnStart();
        // No AddValidatorsFromAssembly in this project — validators are registered by hand.
        services.AddSingleton<IValidateOptions<EcomailOptions>, EcomailOptionsValidator>();

        services.AddHttpClient(EcomailApiClient.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<EcomailOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
        });

        services.AddScoped<IEcomailApiClient, EcomailApiClient>();

        return services;
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailApiClientTests" -p:UseSharedCompilation=false
```

Expected: PASS, 9 tests.

- [ ] **Step 9: Verify the build and commit**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
git add backend/src/Adapters/Anela.Heblo.Adapters.Ecomail \
        backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailApiClient.cs \
        backend/Anela.Heblo.sln backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/test/Anela.Heblo.Tests
git commit -m "feat: add Ecomail API client adapter"
```

---

### Task 3: Repository and sync service

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Ecomail/EcomailRepository.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Ecomail/Services/IEcomailSyncService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Ecomail/Services/EcomailSyncService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Ecomail/Services/EcomailSyncReport.cs`
- Test: `backend/test/Anela.Heblo.Tests/Ecomail/EcomailSyncServiceTests.cs`

**Interfaces:**
- Consumes: `IEcomailApiClient`, `EcomailCampaignDto`, `EcomailPipelineDto`, `EcomailStatsDto` (Task 2); all four entities (Task 1).
- Produces:

```csharp
public interface IEcomailSyncService
{
    Task<EcomailSyncReport> SyncAllAsync(CancellationToken cancellationToken = default);
}

public sealed record EcomailSyncReport(
    int CampaignsUpserted, int PipelinesUpserted,
    int SnapshotsWritten, int AutomationMonthsComputed,
    IReadOnlyList<string> Errors)
{
    public bool IsFullSuccess => Errors.Count == 0;
}
```

> Named `*Report`, never `*Response`: a reflection contract test fails CI for any Application type ending in `Response` that does not inherit `BaseResponse`.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Ecomail/EcomailSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Ecomail;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailSyncServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 3, 0, 0, DateTimeKind.Unspecified);

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"ecomail-sync-{Guid.NewGuid()}")
            .Options);

    private static EcomailStatsDto Stats(int conversions = 0, decimal value = 0) => new()
    {
        Inject = 100, Delivery = 99, Open = 25, TotalOpen = 40,
        Click = 5, TotalClick = 7, Unsub = 2, Bounce = 1, Spam = 0,
        Conversions = conversions, ConversionsValue = value,
        Triggered = 500, Ended = 480, Send = 450,
    };

    private static EcomailSyncService CreateService(
        ApplicationDbContext context,
        Mock<IEcomailApiClient> api,
        EcomailOptions? options = null)
    {
        var opts = options ?? new EcomailOptions
        {
            ApiKey = "k",
            RecomputeWindowMonths = 2,
            BackfillFrom = new DateOnly(2026, 8, 1),
        };

        return new EcomailSyncService(
            api.Object,
            new EcomailRepository(context),
            Options.Create(opts),
            new FakeTimeProvider(Now),
            NullLogger<EcomailSyncService>.Instance);
    }

    private static Mock<IEcomailApiClient> ApiWith(
        IEnumerable<EcomailCampaignDto>? campaigns = null,
        IEnumerable<EcomailPipelineDto>? pipelines = null)
    {
        var api = new Mock<IEcomailApiClient>();
        api.Setup(a => a.GetCampaignsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((campaigns ?? Array.Empty<EcomailCampaignDto>()).ToList());
        api.Setup(a => a.GetPipelinesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((pipelines ?? Array.Empty<EcomailPipelineDto>()).ToList());
        api.Setup(a => a.GetCampaignStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(4, 6057m));
        api.Setup(a => a.GetPipelineStatsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(150, 314708.90m));
        api.Setup(a => a.GetPipelineEventCountAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);
        return api;
    }

    private static EcomailCampaignDto Campaign(int id, string type, int status = 3) => new()
    {
        Id = id, Title = $"c{id}", Subject = "s", CampaignType = type, Status = status,
        Recipients = 1000, SentAt = new DateTime(2026, 8, 15, 6, 0, 0),
    };

    [Fact]
    public async Task stores_every_campaign_including_variations_and_drafts()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[]
        {
            Campaign(264, "ab"), Campaign(266, "variation"),
            Campaign(300, "email"), Campaign(301, "email", status: 0), Campaign(302, "sms"),
        });

        await CreateService(context, api).SyncAllAsync();

        context.EcomailCampaigns.Should().HaveCount(5,
            "raw rows stay complete for traceability; the reporting rule filters at read time");
    }

    [Fact]
    public async Task fetches_stats_only_for_reportable_campaigns()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[]
        {
            Campaign(264, "ab"), Campaign(266, "variation"),
            Campaign(301, "email", status: 0), Campaign(302, "sms"),
        });

        await CreateService(context, api).SyncAllAsync();

        api.Verify(a => a.GetCampaignStatsAsync(264, It.IsAny<CancellationToken>()), Times.Once);
        api.Verify(a => a.GetCampaignStatsAsync(266, It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(a => a.GetCampaignStatsAsync(301, It.IsAny<CancellationToken>()), Times.Never);
        api.Verify(a => a.GetCampaignStatsAsync(302, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task rerunning_on_the_same_day_updates_rather_than_duplicates()
    {
        using var context = CreateContext();
        var api = ApiWith(
            campaigns: new[] { Campaign(264, "ab") },
            pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        var service = CreateService(context, api);
        await service.SyncAllAsync();
        await service.SyncAllAsync();

        context.EcomailCampaigns.Should().ContainSingle();
        context.EcomailAutomationSnapshots.Should().ContainSingle(
            "one snapshot per automation per day, so a 6-hourly job stays idempotent");
    }

    [Fact]
    public async Task writes_the_cumulative_counters_into_the_snapshot()
    {
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        await CreateService(context, api).SyncAllAsync();

        var snapshot = context.EcomailAutomationSnapshots.Single();
        snapshot.PipelineId.Should().Be(14720);
        snapshot.CapturedOn.Should().Be(DateOnly.FromDateTime(Now));
        snapshot.Conversions.Should().Be(150);
        snapshot.ConversionsValue.Should().Be(314708.90m);
        snapshot.Triggered.Should().Be(500);
    }

    [Fact]
    public async Task computes_automation_months_across_the_backfill_range()
    {
        using var context = CreateContext();
        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });

        await CreateService(context, api).SyncAllAsync();

        // BackfillFrom 2026-08 through the current month 2026-09 = 2 months.
        var months = context.EcomailAutomationMonths.OrderBy(m => m.Month).ToList();
        months.Should().HaveCount(2);
        months[0].Year.Should().Be(2026);
        months[0].Month.Should().Be(8);
        months[0].Open.Should().Be(11);
        months[0].Send.Should().Be(11);
    }

    [Fact]
    public async Task does_not_recompute_a_locked_month()
    {
        using var context = CreateContext();
        context.EcomailAutomationMonths.Add(new EcomailAutomationMonth
        {
            PipelineId = 14720, Year = 2026, Month = 8,
            Send = 999, Open = 999, IsLocked = true, ComputedAt = Now.AddMonths(-1),
        });
        await context.SaveChangesAsync();

        var api = ApiWith(pipelines: new[] { new EcomailPipelineDto { Id = 14720, Name = "Kosik" } });
        await CreateService(context, api).SyncAllAsync();

        var august = context.EcomailAutomationMonths.Single(m => m.Month == 8);
        august.Open.Should().Be(999, "a locked month is only touched by an explicit recompute");
    }

    [Fact]
    public async Task one_failing_campaign_does_not_abort_the_run()
    {
        using var context = CreateContext();
        var api = ApiWith(campaigns: new[] { Campaign(264, "ab"), Campaign(300, "email") });
        api.Setup(a => a.GetCampaignStatsAsync(264, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var report = await CreateService(context, api).SyncAllAsync();

        report.IsFullSuccess.Should().BeFalse();
        report.Errors.Should().ContainSingle().Which.Should().Contain("264");
        context.EcomailCampaigns.Single(c => c.Id == 300).Open.Should().Be(25,
            "the healthy campaign is still persisted");
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTime _now;
        public FakeTimeProvider(DateTime now) => _now = now;
        public override DateTimeOffset GetUtcNow() => new(_now, TimeSpan.Zero);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailSyncServiceTests" -p:UseSharedCompilation=false
```

Expected: FAIL — `EcomailSyncService` does not exist.

- [ ] **Step 3: Define and implement the repository**

`backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Ecomail;

public interface IEcomailRepository
{
    Task<Dictionary<int, EcomailCampaign>> GetCampaignsByIdAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<int, EcomailPipeline>> GetPipelinesByIdAsync(CancellationToken cancellationToken = default);

    void AddCampaign(EcomailCampaign campaign);
    void AddPipeline(EcomailPipeline pipeline);

    Task<bool> SnapshotExistsAsync(int pipelineId, DateOnly capturedOn, CancellationToken cancellationToken = default);
    void AddSnapshot(EcomailAutomationSnapshot snapshot);

    Task<Dictionary<(int PipelineId, int Year, int Month), EcomailAutomationMonth>> GetAutomationMonthsAsync(
        CancellationToken cancellationToken = default);
    void AddAutomationMonth(EcomailAutomationMonth month);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

`backend/src/Anela.Heblo.Persistence/Ecomail/EcomailRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Ecomail;

public class EcomailRepository : IEcomailRepository
{
    private readonly ApplicationDbContext _context;

    public EcomailRepository(ApplicationDbContext context) => _context = context;

    // Tracked on purpose: the sync service mutates the returned entities in place.
    public async Task<Dictionary<int, EcomailCampaign>> GetCampaignsByIdAsync(CancellationToken cancellationToken = default)
        => await _context.EcomailCampaigns.ToDictionaryAsync(c => c.Id, cancellationToken);

    public async Task<Dictionary<int, EcomailPipeline>> GetPipelinesByIdAsync(CancellationToken cancellationToken = default)
        => await _context.EcomailPipelines.ToDictionaryAsync(p => p.Id, cancellationToken);

    public void AddCampaign(EcomailCampaign campaign) => _context.EcomailCampaigns.Add(campaign);

    public void AddPipeline(EcomailPipeline pipeline) => _context.EcomailPipelines.Add(pipeline);

    public Task<bool> SnapshotExistsAsync(int pipelineId, DateOnly capturedOn, CancellationToken cancellationToken = default)
        => _context.EcomailAutomationSnapshots
            .AnyAsync(s => s.PipelineId == pipelineId && s.CapturedOn == capturedOn, cancellationToken);

    public void AddSnapshot(EcomailAutomationSnapshot snapshot) => _context.EcomailAutomationSnapshots.Add(snapshot);

    public async Task<Dictionary<(int, int, int), EcomailAutomationMonth>> GetAutomationMonthsAsync(
        CancellationToken cancellationToken = default)
        => await _context.EcomailAutomationMonths
            .ToDictionaryAsync(m => (m.PipelineId, m.Year, m.Month), cancellationToken);

    public void AddAutomationMonth(EcomailAutomationMonth month) => _context.EcomailAutomationMonths.Add(month);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 4: Implement the sync service**

`backend/src/Anela.Heblo.Application/Features/Ecomail/Services/EcomailSyncReport.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Ecomail.Services;

/// <summary>
/// Outcome of one sync run. Named Report, not Response: a reflection contract test fails CI for any
/// Application type ending in "Response" that does not inherit BaseResponse.
/// </summary>
public sealed record EcomailSyncReport(
    int CampaignsUpserted,
    int PipelinesUpserted,
    int SnapshotsWritten,
    int AutomationMonthsComputed,
    IReadOnlyList<string> Errors)
{
    public bool IsFullSuccess => Errors.Count == 0;
}
```

`IEcomailSyncService.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Ecomail.Services;

public interface IEcomailSyncService
{
    Task<EcomailSyncReport> SyncAllAsync(CancellationToken cancellationToken = default);
}
```

`EcomailSyncService.cs`:

```csharp
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Domain.Features.Ecomail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Ecomail.Services;

public class EcomailSyncService : IEcomailSyncService
{
    /// <summary>stats-detail events worth storing per month. There is no conversion event.</summary>
    private static readonly string[] MonthlyEvents = { "send", "open", "click", "unsub" };

    private readonly IEcomailApiClient _api;
    private readonly IEcomailRepository _repository;
    private readonly EcomailOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EcomailSyncService> _logger;

    public EcomailSyncService(
        IEcomailApiClient api,
        IEcomailRepository repository,
        IOptions<EcomailOptions> options,
        TimeProvider timeProvider,
        ILogger<EcomailSyncService> logger)
    {
        _api = api;
        _repository = repository;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<EcomailSyncReport> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var now = _timeProvider.GetUtcNow().DateTime;
        var today = DateOnly.FromDateTime(now);

        var campaigns = await SyncCampaignsAsync(now, errors, cancellationToken);
        var (pipelineIds, pipelines) = await SyncPipelinesAsync(now, errors, cancellationToken);
        var snapshots = await SyncSnapshotsAsync(pipelineIds, today, errors, cancellationToken);
        var months = await SyncAutomationMonthsAsync(pipelineIds, today, now, errors, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        return new EcomailSyncReport(campaigns, pipelines, snapshots, months, errors);
    }

    private async Task<int> SyncCampaignsAsync(DateTime now, List<string> errors, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetCampaignsByIdAsync(cancellationToken);
        var remote = await _api.GetCampaignsAsync(cancellationToken);
        var count = 0;

        foreach (var dto in remote)
        {
            if (!existing.TryGetValue(dto.Id, out var entity))
            {
                entity = new EcomailCampaign { Id = dto.Id };
                _repository.AddCampaign(entity);
            }

            entity.Title = dto.Title;
            entity.Subject = dto.Subject;
            entity.FromEmail = dto.FromEmail;
            entity.CampaignType = dto.CampaignType;
            entity.Status = dto.Status;
            entity.SentAt = dto.SentAt;
            entity.ParentId = dto.ParentId;
            entity.Recipients = dto.Recipients;
            entity.SyncedAt = now;
            count++;

            // Stats cost one call each. Variations are subsets of their parent, drafts have no
            // stats, SMS is not a newsletter — none of them are worth a call.
            if (!entity.IsReportable)
            {
                continue;
            }

            try
            {
                var stats = await _api.GetCampaignStatsAsync(dto.Id, cancellationToken);
                if (stats is not null)
                {
                    ApplyStats(entity, stats);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Filter on the token, not the exception type: a TaskCanceledException from an
                // HTTP timeout is an OperationCanceledException and must be caught here.
                _logger.LogWarning(ex, "Ecomail campaign {CampaignId} stats failed", dto.Id);
                errors.Add($"campaign {dto.Id}: {ex.Message}");
            }
        }

        return count;
    }

    private static void ApplyStats(EcomailCampaign entity, EcomailStatsDto stats)
    {
        entity.Inject = stats.Inject;
        entity.Delivery = stats.Delivery;
        entity.Open = stats.Open;
        entity.TotalOpen = stats.TotalOpen;
        entity.Click = stats.Click;
        entity.TotalClick = stats.TotalClick;
        entity.Unsub = stats.Unsub;
        entity.Bounce = stats.Bounce;
        entity.Spam = stats.Spam;
        entity.Conversions = stats.Conversions;
        entity.ConversionsValue = stats.ConversionsValue;
    }

    private async Task<(List<int> Ids, int Count)> SyncPipelinesAsync(
        DateTime now, List<string> errors, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetPipelinesByIdAsync(cancellationToken);
        var ids = new List<int>();
        var count = 0;

        try
        {
            foreach (var dto in await _api.GetPipelinesAsync(cancellationToken))
            {
                if (!existing.TryGetValue(dto.Id, out var entity))
                {
                    entity = new EcomailPipeline { Id = dto.Id };
                    _repository.AddPipeline(entity);
                }

                entity.Name = dto.Name;
                entity.ListId = dto.ListId;
                entity.CreatedAt = dto.CreatedAt;
                entity.UpdatedAt = dto.UpdatedAt;
                entity.SyncedAt = now;

                ids.Add(dto.Id);
                count++;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ecomail pipelines listing failed");
            errors.Add($"pipelines: {ex.Message}");
        }

        return (ids, count);
    }

    private async Task<int> SyncSnapshotsAsync(
        List<int> pipelineIds, DateOnly today, List<string> errors, CancellationToken cancellationToken)
    {
        var written = 0;

        foreach (var pipelineId in pipelineIds)
        {
            try
            {
                // The job runs every 6h; only the first run of a day writes a row.
                if (await _repository.SnapshotExistsAsync(pipelineId, today, cancellationToken))
                {
                    continue;
                }

                var stats = await _api.GetPipelineStatsAsync(pipelineId, cancellationToken);
                if (stats is null)
                {
                    continue;
                }

                _repository.AddSnapshot(new EcomailAutomationSnapshot
                {
                    PipelineId = pipelineId,
                    CapturedOn = today,
                    Triggered = stats.Triggered,
                    Ended = stats.Ended,
                    Send = stats.Send,
                    Open = stats.Open,
                    Click = stats.Click,
                    Unsub = stats.Unsub,
                    Bounce = stats.Bounce,
                    Conversions = stats.Conversions,
                    ConversionsValue = stats.ConversionsValue,
                });
                written++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Ecomail pipeline {PipelineId} snapshot failed", pipelineId);
                errors.Add($"pipeline {pipelineId} snapshot: {ex.Message}");
            }
        }

        return written;
    }

    private async Task<int> SyncAutomationMonthsAsync(
        List<int> pipelineIds, DateOnly today, DateTime now, List<string> errors, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetAutomationMonthsAsync(cancellationToken);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var computed = 0;

        foreach (var pipelineId in pipelineIds)
        {
            for (var month = _options.BackfillFrom; month <= currentMonth; month = month.AddMonths(1))
            {
                existing.TryGetValue((pipelineId, month.Year, month.Month), out var entity);

                if (entity is { IsLocked: true })
                {
                    continue;
                }

                // Months outside the recompute window are computed once, then frozen.
                var monthsBack = ((currentMonth.Year - month.Year) * 12) + currentMonth.Month - month.Month;
                var isInsideWindow = monthsBack < _options.RecomputeWindowMonths;

                if (entity is not null && !isInsideWindow)
                {
                    entity.IsLocked = true;
                    continue;
                }

                try
                {
                    var counts = new Dictionary<string, int>();
                    var to = month.AddMonths(1).AddDays(-1);

                    foreach (var eventName in MonthlyEvents)
                    {
                        counts[eventName] = await _api.GetPipelineEventCountAsync(
                            pipelineId, eventName, month, to, cancellationToken);
                    }

                    if (entity is null)
                    {
                        entity = new EcomailAutomationMonth
                        {
                            PipelineId = pipelineId, Year = month.Year, Month = month.Month,
                        };
                        _repository.AddAutomationMonth(entity);
                    }

                    entity.Send = counts["send"];
                    entity.Open = counts["open"];
                    entity.Click = counts["click"];
                    entity.Unsub = counts["unsub"];
                    entity.ComputedAt = now;
                    entity.IsLocked = !isInsideWindow;
                    entity.LastError = null;
                    computed++;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex,
                        "Ecomail pipeline {PipelineId} month {Year}-{Month} failed", pipelineId, month.Year, month.Month);
                    errors.Add($"pipeline {pipelineId} {month:yyyy-MM}: {ex.Message}");

                    if (entity is not null)
                    {
                        entity.LastError = ex.Message;
                    }
                }
            }
        }

        return computed;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailSyncServiceTests" -p:UseSharedCompilation=false
```

Expected: PASS, 7 tests.

- [ ] **Step 6: Verify the build and commit**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
git add backend/src/Anela.Heblo.Domain/Features/Ecomail/IEcomailRepository.cs \
        backend/src/Anela.Heblo.Persistence/Ecomail/EcomailRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Ecomail \
        backend/test/Anela.Heblo.Tests/Ecomail
git commit -m "feat: add Ecomail sync service and repository"
```

---

### Task 4: Recurring job, module wiring and configuration

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Ecomail/Infrastructure/Jobs/EcomailSyncJob.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Ecomail/EcomailModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (add `AddEcomailModule` beside `AddMarketingPerformanceModule`, ~line 102)
- Modify: `backend/src/Anela.Heblo.API/Program.cs` (add `using` + `AddEcomailAdapter`, beside the other adapter registrations)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (add the `Ecomail` section)
- Test: `backend/test/Anela.Heblo.Tests/Ecomail/EcomailSyncJobTests.cs`

**Interfaces:**
- Consumes: `IEcomailSyncService`, `EcomailSyncReport` (Task 3); `EcomailOptions`, `AddEcomailAdapter` (Task 2).
- Produces: `EcomailSyncJob : IRecurringJob` with `Metadata.JobName == "ecomail-sync"`.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Ecomail/EcomailSyncJobTests.cs`:

```csharp
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Application.Features.Ecomail.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Ecomail.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Ecomail;

public class EcomailSyncJobTests
{
    private static EcomailSyncJob CreateJob(Mock<IEcomailSyncService> service, EcomailOptions options)
        => new(service.Object, Options.Create(options), NullLogger<EcomailSyncJob>.Instance);

    private static Mock<IEcomailSyncService> SucceedingService()
    {
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(5, 2, 2, 4, Array.Empty<string>()));
        return service;
    }

    [Fact]
    public void exposes_metadata_driven_by_configuration()
    {
        var options = new EcomailOptions { ApiKey = "k", CronExpression = "0 */6 * * *", TimeZone = "Europe/Prague" };

        var job = CreateJob(SucceedingService(), options);

        job.Metadata.JobName.Should().Be("ecomail-sync");
        job.Metadata.CronExpression.Should().Be("0 */6 * * *");
        job.Metadata.TimeZoneId.Should().Be("Europe/Prague");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void is_disabled_by_default_when_no_api_key_is_configured()
    {
        var job = CreateJob(SucceedingService(), new EcomailOptions { ApiKey = "" });

        job.Metadata.DefaultIsEnabled.Should().BeFalse(
            "a developer without Ecomail credentials must not get a failing scheduled job");
    }

    [Fact]
    public async Task skips_execution_without_an_api_key()
    {
        var service = SucceedingService();
        var job = CreateJob(service, new EcomailOptions { ApiKey = "" });

        await job.ExecuteAsync();

        service.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task runs_the_sync_when_configured()
    {
        var service = SucceedingService();
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        await job.ExecuteAsync();

        service.Verify(s => s.SyncAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task throws_when_every_part_of_the_run_failed()
    {
        var service = new Mock<IEcomailSyncService>();
        service.Setup(s => s.SyncAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EcomailSyncReport(0, 0, 0, 0, new[] { "pipelines: boom" }));
        var job = CreateJob(service, new EcomailOptions { ApiKey = "k" });

        var act = () => job.ExecuteAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*boom*", "a silent failure is how a dead pipeline goes unnoticed for months");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~EcomailSyncJobTests" -p:UseSharedCompilation=false
```

Expected: FAIL — `EcomailSyncJob` does not exist.

- [ ] **Step 3: Implement the job**

`backend/src/Anela.Heblo.Application/Features/Ecomail/Infrastructure/Jobs/EcomailSyncJob.cs`:

```csharp
using Anela.Heblo.Adapters.Ecomail;
using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Ecomail.Infrastructure.Jobs;

public sealed class EcomailSyncJob : IRecurringJob
{
    private readonly IEcomailSyncService _syncService;
    private readonly EcomailOptions _options;
    private readonly ILogger<EcomailSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; }

    public EcomailSyncJob(
        IEcomailSyncService syncService,
        IOptions<EcomailOptions> options,
        ILogger<EcomailSyncJob> logger)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new RecurringJobMetadata
        {
            JobName = "ecomail-sync",
            DisplayName = "Ecomail Sync",
            Description = "Pulls Ecomail campaign and automation statistics, and snapshots cumulative automation counters.",
            CronExpression = _options.CronExpression,
            DefaultIsEnabled = !string.IsNullOrWhiteSpace(_options.ApiKey),
            TimeZoneId = _options.TimeZone,
        };
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogInformation("Job {JobName} has no API key configured. Skipping execution.", Metadata.JobName);
            return;
        }

        _logger.LogInformation("Job {JobName} started.", Metadata.JobName);

        var report = await _syncService.SyncAllAsync(cancellationToken);

        _logger.LogInformation(
            "Job {JobName} finished. Campaigns={Campaigns} Pipelines={Pipelines} Snapshots={Snapshots} Months={Months} Errors={Errors}",
            Metadata.JobName, report.CampaignsUpserted, report.PipelinesUpserted,
            report.SnapshotsWritten, report.AutomationMonthsComputed, report.Errors.Count);

        // Nothing landed and something broke — surface it to Hangfire rather than reporting success.
        if (!report.IsFullSuccess &&
            report.CampaignsUpserted == 0 && report.SnapshotsWritten == 0 && report.AutomationMonthsComputed == 0)
        {
            throw new InvalidOperationException(
                $"Ecomail sync produced no data: {string.Join(" | ", report.Errors)}");
        }
    }
}
```

- [ ] **Step 4: Create the application module**

`backend/src/Anela.Heblo.Application/Features/Ecomail/EcomailModule.cs`:

```csharp
using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Ecomail;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Ecomail;

public static class EcomailModule
{
    public static IServiceCollection AddEcomailModule(this IServiceCollection services)
    {
        services.TryAddSingletonTimeProvider();
        services.AddScoped<IEcomailRepository, EcomailRepository>();
        services.AddScoped<IEcomailSyncService, EcomailSyncService>();
        // IRecurringJob implementations are discovered by assembly scan — EcomailSyncJob needs no
        // explicit registration, matching MarketingPerformanceModule.
        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
```

> Check first whether `TimeProvider` is already registered in `ApplicationModule.cs`. If it is, delete `TryAddSingletonTimeProvider` and the call to it, and let the existing registration stand.

- [ ] **Step 5: Wire the module and adapter**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, beside line 102:

```csharp
        services.AddEcomailModule();
```

with `using Anela.Heblo.Application.Features.Ecomail;` added to the usings.

In `backend/src/Anela.Heblo.API/Program.cs`, add `using Anela.Heblo.Adapters.Ecomail;` beside the other adapter usings (lines 3–21), and register the adapter alongside the others:

```csharp
builder.Services.AddEcomailAdapter(builder.Configuration);
```

- [ ] **Step 6: Add configuration**

In `backend/src/Anela.Heblo.API/appsettings.json`, beside the `FlexiAnalyticsSync` section:

```json
  "Ecomail": {
    "ApiKey": "",
    "BaseUrl": "https://api2.ecomailapp.cz",
    "HttpTimeoutSeconds": 60,
    "CronExpression": "0 */6 * * *",
    "TimeZone": "Europe/Prague",
    "RecomputeWindowMonths": 2,
    "BackfillFrom": "2024-11-01"
  },
```

The key itself is **never** committed and **never** an App Service setting. Set it in Key Vault:

```bash
az keyvault secret set --vault-name kv-heblo-stg --name "Ecomail--ApiKey" --value "<key>"
```

Restart the Web App afterwards — Key Vault is read once at startup.

- [ ] **Step 7: Run the full test suite**

```bash
cd backend && dotnet build
dotnet test --no-build -p:UseSharedCompilation=false --filter "Category!=Integration"
```

Expected: PASS, including `ApplicationStartupTests` (which resolves the whole container and will catch a missing registration) and `ReflectionValidationTests` (which enforces the `*Response`/`BaseResponse` rule).

- [ ] **Step 8: Verify formatting and commit**

```bash
cd backend && dotnet format --verify-no-changes
git add backend/src/Anela.Heblo.Application backend/src/Anela.Heblo.API \
        backend/test/Anela.Heblo.Tests/Ecomail
git commit -m "feat: schedule the Ecomail sync job every six hours"
```

---

## Out of scope

Deliberately not in this plan, and each needs its own:

- **Read API + React pages** for #4/#5/#6. Blocked on Bára confirming what UR means and which pipelines count as reportable automations — though neither blocks the ingest, because only counts are stored.
- **The A/B and month-bucketing read rules.** They belong in a SQL view or query handler, not in the ingest.
- **The proxy-open caveat** (§8.7) — a presentation concern.
- **Removing the dead `anela_analytics` sync** (§9). Unrelated cleanup; flag it, don't fold it in.

## Self-review

**Spec coverage.** §9.2's four tables → Task 1. §8.4's `stats-detail` access path → Task 2 (`GetPipelineEventCountAsync`) and Task 3 (`MonthlyEvents`). §8.5's snapshot requirement → Task 1's entity and Task 3's `SyncSnapshotsAsync`. §8.6's selection rule → `EcomailCampaign.IsReportable`, tested in both Task 1 and Task 3. §9.1's `MarketingPerformance` idiom — recompute window, `IsLocked`, `LastError`, counts-not-rates — → Task 3's `SyncAutomationMonthsAsync`. §9.3's cadence decision → Task 4's cron.

**Placeholder scan.** No TBDs; every code step carries the code. Two conditional branches are called out explicitly with the check that resolves them (the `sent_at` date-format converter in Task 2 Step 6, the `TimeProvider` registration in Task 4 Step 4), rather than left vague.

**Type consistency.** `EcomailStatsDto` is shared by both stats endpoints, with `Triggered`/`Ended`/`Send` left at 0 for campaigns — consistent between Task 2's client and Task 3's consumer. `IEcomailRepository` names match between Task 3's interface, implementation and the test's direct `EcomailRepository` construction. `EcomailSyncReport`'s five-argument shape is identical in Task 3 and Task 4's test. `EcomailOptions` lives in the adapter assembly and is consumed by the Application layer — the existing `FlexiAnalyticsSyncOptions` does the same, so the dependency direction is already established.

**One known gap:** Task 3's tests construct `EcomailRepository` (Persistence) directly rather than mocking `IEcomailRepository`, which makes them integration-ish and dependent on the EF InMemory provider. That is deliberate — it exercises the real tracking behaviour that a mock would hide — but note that InMemory does not enforce unique indexes, so `rerunning_on_the_same_day_updates_rather_than_duplicates` proves the service's own guard, not the database constraint.
