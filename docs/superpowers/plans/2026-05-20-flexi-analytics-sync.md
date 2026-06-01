# Flexi Analytics Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a nightly Hangfire job inside Heblo that pulls raw Flexi accounting data into a dedicated `anela_analytics` PostgreSQL database so Metabase can query it.

**Architecture:** A new `Anela.Heblo.Persistence.Analytics` project owns the `AnalyticsDbContext` and EF migrations for the `flexi_raw` schema. Four sync services (ledger, departments, accounting templates, contacts) live in `Anela.Heblo.Adapters.Flexi/Analytics/` and use the existing FlexiBeeSDK clients. A `FlexiAnalyticsSyncJob` (implements `IRecurringJob`) orchestrates them and is auto-discovered by Hangfire's `RecurringJobDiscoveryService`.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, FlexiBeeSDK ≥ 0.1.136, Hangfire, xUnit + Moq + FluentAssertions, Testcontainers.PostgreSql (new, integration test only)

---

## SDK Notes (read before implementing)

- This feature requires **FlexiBeeSDK ≥ 0.1.136** which adds `lastUpdate` filtering to `ILedgerClient`. Bump the `PackageReference` in `Anela.Heblo.Adapters.Flexi.csproj` to `0.1.136` before starting Task 4. Until the new package is published, keep the branch unmerged.
- New `ILedgerClient` overload (added in 0.1.136):
  ```
  GetAsync(DateTime lastUpdateFrom, int limit, int skip, CancellationToken) → Task<IReadOnlyList<LedgerItemFlexiDto>>
  ```
- `LedgerItemFlexiDto` gains in 0.1.136:
  - `DateTime LastUpdate` — populates `last_modified`
  - `string? PeriodRef` — Flexi `obdobi`, e.g. `"code:2025"` — populates `period`
  - `string? DocumentTypeRef` — Flexi `typDokl` — populates `document_type`
  - `string? ContactRef` — Flexi `firma` — populates `contact`
  - `string? AccountingTemplateRef` — Flexi `ucetniPredpis` — populates `accounting_template`
  
  **Verify these property names against the actual 0.1.136 package before implementing Task 4.** If the names differ, adjust the `Map` function accordingly — the column names in the DB are fixed.
- `IDepartmentClient.GetAsync(CancellationToken)` — returns all, no pagination.
- `IAccountingTemplateClient.GetAsync(CancellationToken)` — returns all, no pagination.
- `IContactListClient.GetAsync(IEnumerable<ContactType> contactTypes, int limit, int skip, CancellationToken)` — paginated.
- `LedgerItemFlexiDto.Id` is `int`; `flexi_raw.ledger_entry.flexi_id` is `bigint` (C# `long`).
- Upsert pattern: load existing IDs for the batch → merge in memory → `SaveChangesAsync`. No extra NuGet packages needed.

---

## File Map

### New project: `backend/src/Anela.Heblo.Persistence.Analytics/`
| File | Responsibility |
|------|----------------|
| `Anela.Heblo.Persistence.Analytics.csproj` | EF Core + Npgsql deps only |
| `AnalyticsDbContext.cs` | EF context, `flexi_raw` schema, entity configs |
| `AnalyticsPersistenceModule.cs` | `AddAnalyticsPersistenceServices(string connectionString)` |
| `Entities/LedgerEntry.cs` | `flexi_raw.ledger_entry` EF entity |
| `Entities/Department.cs` | `flexi_raw.department` EF entity |
| `Entities/AccountingTemplate.cs` | `flexi_raw.accounting_template` EF entity |
| `Entities/Contact.cs` | `flexi_raw.contact` EF entity |
| `Entities/SyncState.cs` | `flexi_raw.sync_state` EF entity |
| `Migrations/` | EF migrations (analytics only) |
| `AnalyticsDbContextFactory.cs` | Design-time factory for `dotnet ef` |

### New folder: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/`
| File | Responsibility |
|------|----------------|
| `FlexiAnalyticsSyncOptions.cs` | Config POCO, bound from `FlexiAnalyticsSync` section |
| `SyncResult.cs` | Value type returned by each sync service |
| `ISyncWatermarkRepository.cs` | Interface for reading/writing `sync_state` |
| `SyncWatermarkRepository.cs` | Implementation using `AnalyticsDbContext` |
| `LedgerSyncService.cs` | Date-range paginated ledger sync |
| `DepartmentSyncService.cs` | Full-refresh department sync |
| `AccountingTemplateSyncService.cs` | Full-refresh accounting template sync |
| `ContactSyncService.cs` | Paginated full-refresh contact sync |
| `FlexiAnalyticsSyncService.cs` | Orchestrator: iterates all four services, per-entity error isolation, telemetry |
| `FlexiAnalyticsSyncJob.cs` | `IRecurringJob` implementation, cron `"0 3 * * *"` |

### Modified files
| File | Change |
|------|--------|
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` | Add `<ProjectReference>` to `Anela.Heblo.Persistence.Analytics` |
| `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` | Register analytics services + job when `AnalyticsDatabase:ConnectionString` is present |
| `backend/src/Anela.Heblo.API/appsettings.json` | Add `AnalyticsDatabase` and `FlexiAnalyticsSync` sections |

### New test files (in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/`)
| File | Responsibility |
|------|----------------|
| `Analytics/SyncWatermarkRepositoryTests.cs` | Unit tests against InMemory DbContext |
| `Analytics/LedgerSyncServiceTests.cs` | Unit tests with mocked `ILedgerClient` |
| `Analytics/DepartmentSyncServiceTests.cs` | Unit tests with mocked `IDepartmentClient` |
| `Analytics/AccountingTemplateSyncServiceTests.cs` | Unit tests with mocked `IAccountingTemplateClient` |
| `Analytics/ContactSyncServiceTests.cs` | Unit tests with mocked `IContactListClient` |
| `Analytics/FlexiAnalyticsSyncServiceTests.cs` | Unit tests for orchestration and error isolation |
| `Analytics/Integration/LedgerSyncIntegrationTest.cs` | Testcontainers end-to-end test |

---

## Task 1: Analytics Persistence Project — Entities + DbContext

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Anela.Heblo.Persistence.Analytics.csproj`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Entities/LedgerEntry.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Entities/Department.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Entities/AccountingTemplate.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Entities/Contact.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Entities/SyncState.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsDbContext.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs`
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsDbContextFactory.cs`

- [ ] **Step 1: Create the csproj**

Create `backend/src/Anela.Heblo.Persistence.Analytics/Anela.Heblo.Persistence.Analytics.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.8" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.8" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.4" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add project to solution**

Run from `backend/`:
```bash
dotnet sln add src/Anela.Heblo.Persistence.Analytics/Anela.Heblo.Persistence.Analytics.csproj
```

Expected output: `Project ... added to the solution.`

- [ ] **Step 3: Create entity classes**

Create `backend/src/Anela.Heblo.Persistence.Analytics/Entities/LedgerEntry.cs`:

```csharp
namespace Anela.Heblo.Persistence.Analytics.Entities;

public class LedgerEntry
{
    public long FlexiId { get; set; }
    public string? Code { get; set; }
    public DateOnly EntryDate { get; set; }
    public string? Period { get; set; }
    public string? DocumentType { get; set; }
    public string? AccountDebit { get; set; }
    public string? AccountCredit { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? CostCenter { get; set; }
    public string? Contact { get; set; }
    public string? AccountingTemplate { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
```

Create `backend/src/Anela.Heblo.Persistence.Analytics/Entities/Department.cs`:

```csharp
namespace Anela.Heblo.Persistence.Analytics.Entities;

public class Department
{
    public long FlexiId { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
```

Create `backend/src/Anela.Heblo.Persistence.Analytics/Entities/AccountingTemplate.cs`:

```csharp
namespace Anela.Heblo.Persistence.Analytics.Entities;

public class AccountingTemplate
{
    public long FlexiId { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
```

Create `backend/src/Anela.Heblo.Persistence.Analytics/Entities/Contact.cs`:

```csharp
namespace Anela.Heblo.Persistence.Analytics.Entities;

public class Contact
{
    public long FlexiId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Cin { get; set; }
    public string? Vatin { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string RawPayload { get; set; } = "{}";
    public DateTimeOffset SyncedAt { get; set; }
}
```

Create `backend/src/Anela.Heblo.Persistence.Analytics/Entities/SyncState.cs`:

```csharp
namespace Anela.Heblo.Persistence.Analytics.Entities;

public class SyncState
{
    public string EntityName { get; set; } = "";
    public DateTimeOffset? Watermark { get; set; }
    public DateTimeOffset? LastRunStartedAt { get; set; }
    public DateTimeOffset? LastRunFinishedAt { get; set; }
    public string? LastRunStatus { get; set; }
    public int? LastRunRowsFetched { get; set; }
    public int? LastRunRowsUpserted { get; set; }
    public string? LastErrorMessage { get; set; }
}
```

- [ ] **Step 4: Create AnalyticsDbContext**

Create `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsDbContext.cs`:

```csharp
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Analytics;

public class AnalyticsDbContext : DbContext
{
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AccountingTemplate> AccountingTemplates => Set<AccountingTemplate>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("flexi_raw");

        builder.Entity<LedgerEntry>(e =>
        {
            e.ToTable("ledger_entry");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.EntryDate).HasColumnName("entry_date");
            e.Property(x => x.AccountDebit).HasColumnName("account_debit");
            e.Property(x => x.AccountCredit).HasColumnName("account_credit");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)");
            e.Property(x => x.Currency).HasColumnName("currency");
            e.Property(x => x.CostCenter).HasColumnName("cost_center");
            e.Property(x => x.Period).HasColumnName("period");
            e.Property(x => x.DocumentType).HasColumnName("document_type");
            e.Property(x => x.Contact).HasColumnName("contact");
            e.Property(x => x.AccountingTemplate).HasColumnName("accounting_template");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasIndex(x => x.EntryDate).HasDatabaseName("ix_ledger_entry_entry_date");
            e.HasIndex(x => x.CostCenter).HasDatabaseName("ix_ledger_entry_cost_center");
            e.HasIndex(x => x.AccountDebit).HasDatabaseName("ix_ledger_entry_account_debit");
            e.HasIndex(x => x.AccountCredit).HasDatabaseName("ix_ledger_entry_account_credit");
            e.HasIndex(x => x.LastModified).HasDatabaseName("ix_ledger_entry_last_modified");
        });

        builder.Entity<Department>(e =>
        {
            e.ToTable("department");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<AccountingTemplate>(e =>
        {
            e.ToTable("accounting_template");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<Contact>(e =>
        {
            e.ToTable("contact");
            e.HasKey(x => x.FlexiId);
            e.Property(x => x.FlexiId).HasColumnName("flexi_id").ValueGeneratedNever();
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Cin).HasColumnName("cin");
            e.Property(x => x.Vatin).HasColumnName("vatin");
            e.Property(x => x.LastModified).HasColumnName("last_modified");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
        });

        builder.Entity<SyncState>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => x.EntityName);
            e.Property(x => x.EntityName).HasColumnName("entity_name");
            e.Property(x => x.Watermark).HasColumnName("watermark");
            e.Property(x => x.LastRunStartedAt).HasColumnName("last_run_started_at");
            e.Property(x => x.LastRunFinishedAt).HasColumnName("last_run_finished_at");
            e.Property(x => x.LastRunStatus).HasColumnName("last_run_status");
            e.Property(x => x.LastRunRowsFetched).HasColumnName("last_run_rows_fetched");
            e.Property(x => x.LastRunRowsUpserted).HasColumnName("last_run_rows_upserted");
            e.Property(x => x.LastErrorMessage).HasColumnName("last_error_message");
        });
    }
}
```

- [ ] **Step 5: Create persistence module and design-time factory**

Create `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsPersistenceModule.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Persistence.Analytics;

public static class AnalyticsPersistenceModule
{
    public static IServiceCollection AddAnalyticsPersistenceServices(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseNpgsql(connectionString));
        return services;
    }
}
```

Create `backend/src/Anela.Heblo.Persistence.Analytics/AnalyticsDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Anela.Heblo.Persistence.Analytics;

public class AnalyticsDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql("Host=localhost;Database=anela_analytics;Username=postgres")
            .Options;
        return new AnalyticsDbContext(options);
    }
}
```

- [ ] **Step 6: Verify the project builds**

Run from `backend/`:
```bash
dotnet build src/Anela.Heblo.Persistence.Analytics/Anela.Heblo.Persistence.Analytics.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence.Analytics/
git commit -m "feat(analytics): add Persistence.Analytics project with AnalyticsDbContext and entities"
```

---

## Task 2: EF Core Migration — `flexi_raw` Schema

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence.Analytics/Migrations/` (generated)
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj` (add project reference)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (add `AnalyticsDatabase` section so the startup project can resolve it)

- [ ] **Step 1: Bump SDK version and add project reference in Flexi adapter csproj**

In `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj`:

Change the SDK version from `0.1.135` to `0.1.136` and update the comment:
```xml
    <!-- SDK 0.1.136 required: adds lastUpdateFrom filter to ILedgerClient + LastUpdate field to LedgerItemFlexiDto -->
    <PackageReference Include="Rem.FlexiBeeSDK.Client" Version="0.1.136" />
```

Then add the analytics persistence project reference:
```xml
    <ProjectReference Include="..\..\Anela.Heblo.Persistence.Analytics\Anela.Heblo.Persistence.Analytics.csproj" />
```

- [ ] **Step 2: Add placeholder config so design-time factory works**

In `backend/src/Anela.Heblo.API/appsettings.json`, add after the `"Smartsupp"` section (before the closing `}`):

```json
  "AnalyticsDatabase": {
    "ConnectionString": ""
  },
  "FlexiAnalyticsSync": {
    "Enabled": true,
    "CronExpression": "0 3 * * *",
    "TimeZone": "Europe/Prague",
    "BatchSize": 500,
    "InitialBackfillFrom": "2020-01-01",
    "RequestTimeoutSeconds": 120
  }
```

- [ ] **Step 3: Generate the EF migration**

Run from `backend/`:
```bash
dotnet ef migrations add InitialAnalyticsSchema \
  --project src/Anela.Heblo.Persistence.Analytics \
  --startup-project src/Anela.Heblo.Persistence.Analytics \
  --context AnalyticsDbContext \
  --output-dir Migrations
```

The design-time factory (`AnalyticsDbContextFactory`) handles startup. Expected output: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 4: Verify migration looks correct**

Read `backend/src/Anela.Heblo.Persistence.Analytics/Migrations/<timestamp>_InitialAnalyticsSchema.cs`. Confirm it:
- Creates schema `flexi_raw`
- Creates tables: `ledger_entry`, `department`, `accounting_template`, `contact`, `sync_state`
- Indexes on `ledger_entry` for `entry_date`, `cost_center`, `account_debit`, `account_credit`, `last_modified`
- `amount` column type is `numeric(18,4)`
- `raw_payload` column type is `jsonb`

- [ ] **Step 5: Verify full solution build**

Run from `backend/`:
```bash
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence.Analytics/Migrations/ \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(analytics): add EF migration for flexi_raw schema + project reference"
```

---

## Task 3: SyncWatermarkRepository + Unit Tests

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ISyncWatermarkRepository.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/SyncWatermarkRepository.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/SyncResult.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/SyncWatermarkRepositoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj` (add reference to persistence.analytics)

- [ ] **Step 1: Add test project reference to persistence.analytics and InMemory package**

In `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj`, add inside the existing `<ItemGroup>` with `ProjectReference`:

```xml
      <ProjectReference Include="..\..\src\Anela.Heblo.Persistence.Analytics\Anela.Heblo.Persistence.Analytics.csproj" />
```

Also add a `PackageReference` for InMemory database in the `<ItemGroup>` with packages:

```xml
      <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.8" />
      <PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
```

- [ ] **Step 2: Write the failing test**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/SyncWatermarkRepositoryTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class SyncWatermarkRepositoryTests
{
    private static AnalyticsDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AnalyticsDbContext(options);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenNoExistingState_ReturnsNewStateWithNullWatermark()
    {
        // Arrange
        await using var ctx = CreateInMemoryContext();
        var repo = new SyncWatermarkRepository(ctx);

        // Act
        var state = await repo.GetOrCreateAsync("ledger_entry");

        // Assert
        state.EntityName.Should().Be("ledger_entry");
        state.Watermark.Should().BeNull();
        state.LastRunStatus.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenStateExists_ReturnsSavedState()
    {
        // Arrange
        await using var ctx = CreateInMemoryContext();
        var expectedWatermark = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = expectedWatermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();
        var repo = new SyncWatermarkRepository(ctx);

        // Act
        var state = await repo.GetOrCreateAsync("ledger_entry");

        // Assert
        state.Watermark.Should().Be(expectedWatermark);
        state.LastRunStatus.Should().Be("OK");
    }

    [Fact]
    public async Task SaveAsync_PersistsChangesToDatabase()
    {
        // Arrange
        await using var ctx = CreateInMemoryContext();
        var repo = new SyncWatermarkRepository(ctx);
        var state = await repo.GetOrCreateAsync("department");
        var newWatermark = DateTimeOffset.UtcNow;

        // Act
        state.Watermark = newWatermark;
        state.LastRunStatus = "OK";
        await repo.SaveAsync(state);

        // Assert
        var saved = await ctx.SyncStates.FindAsync("department");
        saved!.Watermark.Should().BeCloseTo(newWatermark, TimeSpan.FromSeconds(1));
        saved.LastRunStatus.Should().Be("OK");
    }
}
```

- [ ] **Step 3: Run the test to confirm it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.SyncWatermarkRepositoryTests" -v normal
```

Expected: Compilation error — `SyncWatermarkRepository` and `ISyncWatermarkRepository` do not exist yet.

- [ ] **Step 4: Create `SyncResult`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/SyncResult.cs`:

```csharp
namespace Anela.Heblo.Adapters.Flexi.Analytics;

public record SyncResult(int RowsFetched, int RowsUpserted, bool IsSuccess);
```

- [ ] **Step 5: Create `ISyncWatermarkRepository`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ISyncWatermarkRepository.cs`:

```csharp
using Anela.Heblo.Persistence.Analytics.Entities;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public interface ISyncWatermarkRepository
{
    Task<SyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default);
    Task SaveAsync(SyncState state, CancellationToken ct = default);
}
```

- [ ] **Step 6: Implement `SyncWatermarkRepository`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/SyncWatermarkRepository.cs`:

```csharp
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class SyncWatermarkRepository : ISyncWatermarkRepository
{
    private readonly AnalyticsDbContext _dbContext;

    public SyncWatermarkRepository(AnalyticsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SyncState> GetOrCreateAsync(string entityName, CancellationToken ct = default)
    {
        var state = await _dbContext.SyncStates.FindAsync([entityName], ct);
        if (state != null)
            return state;

        state = new SyncState { EntityName = entityName };
        _dbContext.SyncStates.Add(state);
        await _dbContext.SaveChangesAsync(ct);
        return state;
    }

    public async Task SaveAsync(SyncState state, CancellationToken ct = default)
    {
        _dbContext.SyncStates.Update(state);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 7: Run tests to confirm they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.SyncWatermarkRepositoryTests" -v normal
```

Expected: `Passed: 3, Failed: 0`

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/SyncWatermarkRepositoryTests.cs
git commit -m "feat(analytics): add SyncWatermarkRepository with unit tests"
```

---

## Task 4: LedgerSyncService + Unit Tests

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncOptions.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs`

Note on ledger sync design: `ILedgerClient.GetAsync` (new overload in SDK 0.1.136) accepts `lastUpdateFrom` as the filter. The watermark stores the max `LastUpdate` seen so far. Each run fetches `lastUpdateFrom = watermark - 1h` (1-hour overlap absorbs clock skew). First run (watermark null) uses `InitialBackfillFrom` converted to a `DateTime`. Paginate with `limit`/`skip` until an empty page. Advance the watermark to `DateTimeOffset.UtcNow` after the last page commits.

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class LedgerSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IOptions<FlexiAnalyticsSyncOptions> DefaultOptions() =>
        Options.Create(new FlexiAnalyticsSyncOptions
        {
            BatchSize = 10,
            InitialBackfillFrom = "2024-01-01"
        });

    private static LedgerSyncService CreateService(
        ILedgerClient ledgerClient,
        AnalyticsDbContext ctx,
        IOptions<FlexiAnalyticsSyncOptions>? opts = null)
    {
        var repo = new SyncWatermarkRepository(ctx);
        return new LedgerSyncService(
            ledgerClient,
            repo,
            ctx,
            opts ?? DefaultOptions(),
            Mock.Of<ILogger<LedgerSyncService>>());
    }

    private static LedgerItemFlexiDto MakeLedgerDto(int id, DateTime accountingDate, double amount = 100.0) =>
        new()
        {
            Id = id,
            AccountingDate = accountingDate,
            LastUpdate = accountingDate.AddHours(1), // SDK 0.1.136
            AmountLocal = amount,
            ParSymbol = $"CODE{id}",
            DebitAccountShowAs = "501000",
            CreditAccountShowAs = "221000",
            CurrencyRef = "code:CZK",
            Description = "Test entry",
            PeriodRef = "code:2025",              // SDK 0.1.136
            DocumentTypeRef = "code:FAP",         // SDK 0.1.136
            ContactRef = "code:ACME",             // SDK 0.1.136
            AccountingTemplateRef = "code:FAP01", // SDK 0.1.136
        };

    [Fact]
    public async Task SyncAsync_WhenNoWatermark_FetchesFromInitialBackfillDate()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var opts = Options.Create(new FlexiAnalyticsSyncOptions
        {
            BatchSize = 10,
            InitialBackfillFrom = "2024-01-01"
        });

        // First call returns one item, second returns empty (end of pagination)
        client.SetupSequence(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto> { MakeLedgerDto(1, new DateTime(2024, 6, 1)) })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx, opts);

        // Act
        var result = await svc.SyncAsync();

        // Assert — lastUpdateFrom should equal InitialBackfillFrom when no watermark
        client.Verify(c => c.GetAsync(
            new DateTime(2024, 1, 1), 10, 0, It.IsAny<CancellationToken>()), Times.Once);
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_WhenWatermarkExists_FetchesFromWatermarkMinus1Hour()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var watermark = new DateTimeOffset(2025, 3, 10, 12, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = watermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();

        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        await svc.SyncAsync();

        // Assert — lastUpdateFrom should be watermark minus 1 hour
        var expectedFrom = watermark.AddHours(-1).UtcDateTime;
        client.Verify(c => c.GetAsync(
            expectedFrom, It.IsAny<int>(), 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_UpsertsRowsAndAdvancesWatermark()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>
            {
                MakeLedgerDto(10, new DateTime(2025, 1, 1)),
                MakeLedgerDto(11, new DateTime(2025, 1, 2))
            })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var entries = await ctx.LedgerEntries.ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.FlexiId == 10);
        entries.Should().Contain(e => e.FlexiId == 11);
        entries.Should().AllSatisfy(e => e.LastModified.Should().NotBeNull());
        entries.Should().AllSatisfy(e => e.Period.Should().Be("code:2025"));
        entries.Should().AllSatisfy(e => e.DocumentType.Should().Be("code:FAP"));
        entries.Should().AllSatisfy(e => e.Contact.Should().Be("code:ACME"));
        entries.Should().AllSatisfy(e => e.AccountingTemplate.Should().Be("code:FAP01"));

        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("OK");
        state.Watermark.Should().NotBeNull();
        state.LastRunRowsFetched.Should().Be(2);
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatusAndKeepsWatermarkUnchanged()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();
        await using var ctx = CreateInMemoryContext();
        var originalWatermark = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ctx.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = originalWatermark,
            LastRunStatus = "OK"
        });
        await ctx.SaveChangesAsync();

        client.Setup(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Flexi unreachable"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        var state = await ctx.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("FAILED");
        state.Watermark.Should().Be(originalWatermark);
        state.LastErrorMessage.Should().Contain("Flexi unreachable");
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.LedgerSyncServiceTests" -v normal 2>&1 | tail -10
```

Expected: Compilation error — `LedgerSyncService` and `FlexiAnalyticsSyncOptions` not found.

- [ ] **Step 3: Create `FlexiAnalyticsSyncOptions`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncOptions.cs`:

```csharp
namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class FlexiAnalyticsSyncOptions
{
    public const string ConfigurationKey = "FlexiAnalyticsSync";

    public bool Enabled { get; set; } = true;
    public string CronExpression { get; set; } = "0 3 * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int BatchSize { get; set; } = 500;
    public string InitialBackfillFrom { get; set; } = "2020-01-01";
    public int RequestTimeoutSeconds { get; set; } = 120;

    public DateTime GetInitialBackfillDateTime() =>
        DateTime.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeLocal).Date;
}
```

- [ ] **Step 4: Implement `LedgerSyncService`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class LedgerSyncService
{
    private const string EntityName = "ledger_entry";

    private readonly ILedgerClient _ledgerClient;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<LedgerSyncService> _logger;

    public LedgerSyncService(
        ILedgerClient ledgerClient,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<LedgerSyncService> logger)
    {
        _ledgerClient = ledgerClient;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);

        // Use watermark − 1h to absorb clock skew / slow Flexi updates.
        // First run (null watermark): fall back to InitialBackfillFrom.
        var lastUpdateFrom = state.Watermark.HasValue
            ? state.Watermark.Value.AddHours(-1).UtcDateTime
            : _options.GetInitialBackfillDateTime();

        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation(
            "FlexiAnalyticsSync.EntityStarted {EntityName} watermark={Watermark} lastUpdateFrom={LastUpdateFrom}",
            EntityName, state.Watermark, lastUpdateFrom);

        var totalFetched = 0;
        var totalUpserted = 0;

        try
        {
            var skip = 0;
            while (true)
            {
                // SDK 0.1.136: GetAsync(lastUpdateFrom, limit, skip, ct)
                var batch = await _ledgerClient.GetAsync(
                    lastUpdateFrom, _options.BatchSize, skip, ct);

                if (batch.Count == 0)
                    break;

                var entries = batch.Select(Map).ToList();
                var upserted = await UpsertBatchAsync(entries, ct);
                totalFetched += batch.Count;
                totalUpserted += upserted;
                skip += batch.Count;

                if (batch.Count < _options.BatchSize)
                    break;
            }

            state.Watermark = DateTimeOffset.UtcNow;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalUpserted;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.EntityCompleted {EntityName} rowsFetched={RowsFetched} rowsUpserted={RowsUpserted}",
                EntityName, totalFetched, totalUpserted);
        }
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex, "FlexiAnalyticsSync.EntityFailed {EntityName}", EntityName);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalUpserted, state.LastRunStatus == "OK");
    }

    private async Task<int> UpsertBatchAsync(List<LedgerEntry> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.LedgerEntries
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var entry in incoming)
        {
            if (existing.TryGetValue(entry.FlexiId, out var existingEntry))
            {
                existingEntry.EntryDate = entry.EntryDate;
                existingEntry.Code = entry.Code;
                existingEntry.AccountDebit = entry.AccountDebit;
                existingEntry.AccountCredit = entry.AccountCredit;
                existingEntry.Amount = entry.Amount;
                existingEntry.Currency = entry.Currency;
                existingEntry.CostCenter = entry.CostCenter;
                existingEntry.Period = entry.Period;
                existingEntry.DocumentType = entry.DocumentType;
                existingEntry.Contact = entry.Contact;
                existingEntry.AccountingTemplate = entry.AccountingTemplate;
                existingEntry.Description = entry.Description;
                existingEntry.LastModified = entry.LastModified;
                existingEntry.RawPayload = entry.RawPayload;
                existingEntry.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.LedgerEntries.Add(entry);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
        return incoming.Count;
    }

    private static LedgerEntry Map(LedgerItemFlexiDto dto) => new()
    {
        FlexiId = dto.Id,
        Code = dto.ParSymbol,
        EntryDate = DateOnly.FromDateTime(dto.AccountingDate),
        AccountDebit = dto.DebitAccountShowAs,
        AccountCredit = dto.CreditAccountShowAs,
        Amount = (decimal)dto.AmountLocal,
        Currency = dto.CurrencyRef,
        CostCenter = dto.Department?.Code,
        Description = dto.Description,
        LastModified = new DateTimeOffset(dto.LastUpdate, TimeSpan.Zero), // SDK 0.1.136
        Period = dto.PeriodRef,                   // SDK 0.1.136
        DocumentType = dto.DocumentTypeRef,       // SDK 0.1.136
        Contact = dto.ContactRef,                 // SDK 0.1.136
        AccountingTemplate = dto.AccountingTemplateRef, // SDK 0.1.136
        RawPayload = JsonSerializer.Serialize(dto),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.LedgerSyncServiceTests" -v normal
```

Expected: `Passed: 4, Failed: 0`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs
git commit -m "feat(analytics): add LedgerSyncService with lastUpdate-based incremental sync and tests"
```

---

## Task 5: DepartmentSyncService + Unit Tests

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;
using Rem.FlexiBeeSDK.Model.Accounting.Departments;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class DepartmentSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DepartmentSyncService CreateService(
        IDepartmentClient client,
        AnalyticsDbContext ctx)
    {
        var repo = new SyncWatermarkRepository(ctx);
        var opts = Options.Create(new FlexiAnalyticsSyncOptions { BatchSize = 100 });
        return new DepartmentSyncService(client, repo, ctx, opts, Mock.Of<ILogger<DepartmentSyncService>>());
    }

    [Fact]
    public async Task SyncAsync_UpsertsAllDepartments()
    {
        // Arrange
        var client = new Mock<IDepartmentClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DepartmentFlexiDto>
            {
                new() { Id = 1, Code = "PROD", Name = "Produkce", LastUpdate = DateTime.UtcNow },
                new() { Id = 2, Code = "LOG", Name = "Logistika", LastUpdate = DateTime.UtcNow },
            });

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(2);
        var departments = await ctx.Departments.ToListAsync();
        departments.Should().HaveCount(2);
        departments.Should().Contain(d => d.Code == "PROD" && d.Name == "Produkce");
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatus()
    {
        // Arrange
        var client = new Mock<IDepartmentClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("timeout"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        var state = await ctx.SyncStates.FindAsync("department");
        state!.LastRunStatus.Should().Be("FAILED");
        state.LastErrorMessage.Should().Contain("timeout");
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.DepartmentSyncServiceTests" -v normal 2>&1 | tail -5
```

Expected: Compilation error — `DepartmentSyncService` not found.

- [ ] **Step 3: Implement `DepartmentSyncService`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments;
using Rem.FlexiBeeSDK.Model.Accounting.Departments;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class DepartmentSyncService
{
    private const string EntityName = "department";

    private readonly IDepartmentClient _client;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<DepartmentSyncService> _logger;

    public DepartmentSyncService(
        IDepartmentClient client,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<DepartmentSyncService> logger)
    {
        _client = client;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);
        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation("FlexiAnalyticsSync.EntityStarted {EntityName}", EntityName);

        var totalFetched = 0;

        try
        {
            var dtos = await _client.GetAsync(ct);
            var entities = dtos.Select(Map).ToList();
            totalFetched = entities.Count;
            await UpsertAllAsync(entities, ct);

            state.Watermark = DateTimeOffset.UtcNow;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalFetched;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.EntityCompleted {EntityName} rows={Rows}", EntityName, totalFetched);
        }
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex, "FlexiAnalyticsSync.EntityFailed {EntityName}", EntityName);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalFetched, state.LastRunStatus == "OK");
    }

    private async Task UpsertAllAsync(List<Department> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.Departments
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var dept in incoming)
        {
            if (existing.TryGetValue(dept.FlexiId, out var existingDept))
            {
                existingDept.Code = dept.Code;
                existingDept.Name = dept.Name;
                existingDept.LastModified = dept.LastModified;
                existingDept.RawPayload = dept.RawPayload;
                existingDept.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.Departments.Add(dept);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static Department Map(DepartmentFlexiDto dto) => new()
    {
        FlexiId = dto.Id,
        Code = dto.Code ?? "",
        Name = dto.Name,
        LastModified = dto.LastUpdate == default ? null : new DateTimeOffset(dto.LastUpdate, TimeSpan.Zero),
        RawPayload = JsonSerializer.Serialize(dto),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.DepartmentSyncServiceTests" -v normal
```

Expected: `Passed: 2, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs
git commit -m "feat(analytics): add DepartmentSyncService with tests"
```

---

## Task 6: AccountingTemplateSyncService + Unit Tests

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Model.Accounting.AccountingTemplates;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class AccountingTemplateSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static AccountingTemplateSyncService CreateService(
        IAccountingTemplateClient client,
        AnalyticsDbContext ctx)
    {
        var repo = new SyncWatermarkRepository(ctx);
        var opts = Options.Create(new FlexiAnalyticsSyncOptions { BatchSize = 100 });
        return new AccountingTemplateSyncService(client, repo, ctx, opts,
            Mock.Of<ILogger<AccountingTemplateSyncService>>());
    }

    [Fact]
    public async Task SyncAsync_UpsertsAllTemplates()
    {
        // Arrange
        var client = new Mock<IAccountingTemplateClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AccountingTemplateFlexiDto>
            {
                new() { Id = 1, Code = "FAP", Name = "Faktura přijatá", LastUpdate = DateTime.UtcNow },
                new() { Id = 2, Code = "FAV", Name = "Faktura vydaná", LastUpdate = DateTime.UtcNow },
            });

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var templates = await ctx.AccountingTemplates.ToListAsync();
        templates.Should().HaveCount(2);
        templates.Should().Contain(t => t.Code == "FAP" && t.Name == "Faktura přijatá");
    }

    [Fact]
    public async Task SyncAsync_OnClientError_RecordsFailedStatus()
    {
        // Arrange
        var client = new Mock<IAccountingTemplateClient>();
        await using var ctx = CreateInMemoryContext();

        client.Setup(c => c.GetAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("server error"));

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        var state = await ctx.SyncStates.FindAsync("accounting_template");
        state!.LastRunStatus.Should().Be("FAILED");
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.AccountingTemplateSyncServiceTests" 2>&1 | tail -5
```

Expected: Compilation error.

- [ ] **Step 3: Implement `AccountingTemplateSyncService`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Model.Accounting.AccountingTemplates;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class AccountingTemplateSyncService
{
    private const string EntityName = "accounting_template";

    private readonly IAccountingTemplateClient _client;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<AccountingTemplateSyncService> _logger;

    public AccountingTemplateSyncService(
        IAccountingTemplateClient client,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<AccountingTemplateSyncService> logger)
    {
        _client = client;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);
        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation("FlexiAnalyticsSync.EntityStarted {EntityName}", EntityName);

        var totalFetched = 0;

        try
        {
            var dtos = await _client.GetAsync(ct);
            var entities = dtos.Select(Map).ToList();
            totalFetched = entities.Count;
            await UpsertAllAsync(entities, ct);

            state.Watermark = DateTimeOffset.UtcNow;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalFetched;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.EntityCompleted {EntityName} rows={Rows}", EntityName, totalFetched);
        }
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex, "FlexiAnalyticsSync.EntityFailed {EntityName}", EntityName);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalFetched, state.LastRunStatus == "OK");
    }

    private async Task UpsertAllAsync(List<AccountingTemplate> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.AccountingTemplates
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var tmpl in incoming)
        {
            if (existing.TryGetValue(tmpl.FlexiId, out var existingTmpl))
            {
                existingTmpl.Code = tmpl.Code;
                existingTmpl.Name = tmpl.Name;
                existingTmpl.Description = tmpl.Description;
                existingTmpl.LastModified = tmpl.LastModified;
                existingTmpl.RawPayload = tmpl.RawPayload;
                existingTmpl.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.AccountingTemplates.Add(tmpl);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static AccountingTemplate Map(AccountingTemplateFlexiDto dto) => new()
    {
        FlexiId = dto.Id,
        Code = dto.Code ?? "",
        Name = dto.Name,
        Description = dto.Description,
        LastModified = dto.LastUpdate == default ? null : new DateTimeOffset(dto.LastUpdate, TimeSpan.Zero),
        RawPayload = JsonSerializer.Serialize(dto),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.AccountingTemplateSyncServiceTests" -v normal
```

Expected: `Passed: 2, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs
git commit -m "feat(analytics): add AccountingTemplateSyncService with tests"
```

---

## Task 7: ContactSyncService + Unit Tests

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs`

Note: `IContactListClient.GetAsync` takes `IEnumerable<ContactType> contactTypes, int limit, int skip, CancellationToken`. Use `ContactType.All` and loop until empty page. `ContactFlexiDto.Id` is `int?` — skip contacts without an Id.

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class ContactSyncServiceTests
{
    private static AnalyticsDbContext CreateInMemoryContext() =>
        new(new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ContactSyncService CreateService(IContactListClient client, AnalyticsDbContext ctx,
        int batchSize = 100)
    {
        var repo = new SyncWatermarkRepository(ctx);
        var opts = Options.Create(new FlexiAnalyticsSyncOptions { BatchSize = batchSize });
        return new ContactSyncService(client, repo, ctx, opts, Mock.Of<ILogger<ContactSyncService>>());
    }

    [Fact]
    public async Task SyncAsync_UpsertsAllContacts_SkipsContactsWithNullId()
    {
        // Arrange
        var client = new Mock<IContactListClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetAsync(
                It.IsAny<IEnumerable<ContactType>>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactFlexiDto>
            {
                new() { Id = 1, Code = "ACME", Name = "Acme s.r.o.", Cin = "12345678" },
                new() { Id = null, Code = "NOID", Name = "No ID contact" }, // should be skipped
            })
            .ReturnsAsync(new List<ContactFlexiDto>());

        var svc = CreateService(client.Object, ctx);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var contacts = await ctx.Contacts.ToListAsync();
        contacts.Should().HaveCount(1);
        contacts[0].Code.Should().Be("ACME");
    }

    [Fact]
    public async Task SyncAsync_PaginatesUntilEmptyPage()
    {
        // Arrange
        var client = new Mock<IContactListClient>();
        await using var ctx = CreateInMemoryContext();

        client.SetupSequence(c => c.GetAsync(
                It.IsAny<IEnumerable<ContactType>>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContactFlexiDto>
            {
                new() { Id = 1, Code = "C1", Name = "Contact 1" },
                new() { Id = 2, Code = "C2", Name = "Contact 2" },
            })
            .ReturnsAsync(new List<ContactFlexiDto> { new() { Id = 3, Code = "C3", Name = "Contact 3" } })
            .ReturnsAsync(new List<ContactFlexiDto>());

        var svc = CreateService(client.Object, ctx, batchSize: 2);

        // Act
        var result = await svc.SyncAsync();

        // Assert
        var contacts = await ctx.Contacts.ToListAsync();
        contacts.Should().HaveCount(3);
        result.RowsFetched.Should().Be(3);
    }
}
```

- [ ] **Step 2: Run to confirm failure**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.ContactSyncServiceTests" 2>&1 | tail -5
```

Expected: Compilation error.

- [ ] **Step 3: Implement `ContactSyncService`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Contacts;
using Rem.FlexiBeeSDK.Model.Contacts;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class ContactSyncService
{
    private const string EntityName = "contact";

    private readonly IContactListClient _client;
    private readonly ISyncWatermarkRepository _watermarkRepo;
    private readonly AnalyticsDbContext _dbContext;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<ContactSyncService> _logger;

    public ContactSyncService(
        IContactListClient client,
        ISyncWatermarkRepository watermarkRepo,
        AnalyticsDbContext dbContext,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<ContactSyncService> logger)
    {
        _client = client;
        _watermarkRepo = watermarkRepo;
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var state = await _watermarkRepo.GetOrCreateAsync(EntityName, ct);
        state.LastRunStartedAt = DateTimeOffset.UtcNow;
        state.LastRunStatus = "RUNNING";
        await _watermarkRepo.SaveAsync(state, ct);

        _logger.LogInformation("FlexiAnalyticsSync.EntityStarted {EntityName}", EntityName);

        var totalFetched = 0;

        try
        {
            var contactTypes = new[] { ContactType.All };
            var skip = 0;
            while (true)
            {
                var batch = await _client.GetAsync(contactTypes, _options.BatchSize, skip, ct);
                if (batch.Count == 0)
                    break;

                var entities = batch
                    .Where(dto => dto.Id.HasValue)
                    .Select(Map)
                    .ToList();

                await UpsertBatchAsync(entities, ct);
                totalFetched += entities.Count;
                skip += batch.Count;

                if (batch.Count < _options.BatchSize)
                    break;
            }

            state.Watermark = DateTimeOffset.UtcNow;
            state.LastRunStatus = "OK";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastRunRowsFetched = totalFetched;
            state.LastRunRowsUpserted = totalFetched;
            state.LastErrorMessage = null;

            _logger.LogInformation(
                "FlexiAnalyticsSync.EntityCompleted {EntityName} rows={Rows}", EntityName, totalFetched);
        }
        catch (Exception ex)
        {
            state.LastRunStatus = "FAILED";
            state.LastRunFinishedAt = DateTimeOffset.UtcNow;
            state.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            _logger.LogError(ex, "FlexiAnalyticsSync.EntityFailed {EntityName}", EntityName);
        }

        await _watermarkRepo.SaveAsync(state, ct);
        return new SyncResult(totalFetched, totalFetched, state.LastRunStatus == "OK");
    }

    private async Task UpsertBatchAsync(List<Contact> incoming, CancellationToken ct)
    {
        var ids = incoming.Select(x => x.FlexiId).ToHashSet();
        var existing = await _dbContext.Contacts
            .Where(x => ids.Contains(x.FlexiId))
            .ToDictionaryAsync(x => x.FlexiId, ct);

        foreach (var contact in incoming)
        {
            if (existing.TryGetValue(contact.FlexiId, out var existingContact))
            {
                existingContact.Code = contact.Code;
                existingContact.Name = contact.Name;
                existingContact.Cin = contact.Cin;
                existingContact.Vatin = contact.Vatin;
                existingContact.LastModified = contact.LastModified;
                existingContact.RawPayload = contact.RawPayload;
                existingContact.SyncedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _dbContext.Contacts.Add(contact);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static Contact Map(ContactFlexiDto dto) => new()
    {
        FlexiId = dto.Id!.Value,
        Code = dto.Code,
        Name = dto.Name,
        Cin = dto.CIN,
        Vatin = dto.VATIN,
        LastModified = dto.LastUpdate,
        RawPayload = JsonSerializer.Serialize(dto),
        SyncedAt = DateTimeOffset.UtcNow,
    };
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.ContactSyncServiceTests" -v normal
```

Expected: `Passed: 2, Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs
git commit -m "feat(analytics): add ContactSyncService with paginated fetch and tests"
```

---

## Task 8: FlexiAnalyticsSyncService (Orchestrator) + Unit Tests

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/IEntitySyncService.cs` (shared interface)
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncService.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/FlexiAnalyticsSyncServiceTests.cs`

Each concrete sync service implements `IEntitySyncService` so the orchestrator can be tested with mocks.

- [ ] **Step 1: Create `IEntitySyncService`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/IEntitySyncService.cs`:

```csharp
namespace Anela.Heblo.Adapters.Flexi.Analytics;

public interface IEntitySyncService
{
    Task<SyncResult> SyncAsync(CancellationToken ct = default);
}
```

Then add `: IEntitySyncService` to each service's class declaration in Tasks 4–7 (the `SyncAsync` signature already matches):

- `LedgerSyncService : IEntitySyncService`
- `DepartmentSyncService : IEntitySyncService`
- `AccountingTemplateSyncService : IEntitySyncService`
- `ContactSyncService : IEntitySyncService`

- [ ] **Step 2: Write failing tests**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/FlexiAnalyticsSyncServiceTests.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class FlexiAnalyticsSyncServiceTests
{
    [Fact]
    public async Task SyncAllAsync_CallsAllFourEntitySyncs()
    {
        // Arrange
        var ledger = new Mock<IEntitySyncService>();
        var dept = new Mock<IEntitySyncService>();
        var tmpl = new Mock<IEntitySyncService>();
        var contact = new Mock<IEntitySyncService>();

        ledger.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(10, 10, true));
        dept.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(5, 5, true));
        tmpl.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(3, 3, true));
        contact.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(20, 20, true));

        var svc = new FlexiAnalyticsSyncService(
            ledger.Object, dept.Object, tmpl.Object, contact.Object,
            Mock.Of<ILogger<FlexiAnalyticsSyncService>>());

        // Act
        await svc.SyncAllAsync();

        // Assert
        ledger.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        dept.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        tmpl.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        contact.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAllAsync_WhenOneSyncReturnsFailure_ContinuesWithOtherSyncs()
    {
        // Arrange
        var ledger = new Mock<IEntitySyncService>();
        var dept = new Mock<IEntitySyncService>();
        var tmpl = new Mock<IEntitySyncService>();
        var contact = new Mock<IEntitySyncService>();

        ledger.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(0, 0, false)); // ledger fails
        dept.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(5, 5, true));
        tmpl.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(3, 3, true));
        contact.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(20, 20, true));

        var svc = new FlexiAnalyticsSyncService(
            ledger.Object, dept.Object, tmpl.Object, contact.Object,
            Mock.Of<ILogger<FlexiAnalyticsSyncService>>());

        // Act — should not throw even though ledger failed
        var act = () => svc.SyncAllAsync();
        await act.Should().NotThrowAsync();

        // Assert — other syncs still ran
        dept.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        tmpl.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        contact.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 3: Run to confirm failure**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.FlexiAnalyticsSyncServiceTests" 2>&1 | tail -5
```

Expected: Compilation error — `FlexiAnalyticsSyncService` and `IEntitySyncService` not found.

- [ ] **Step 4: Add `: IEntitySyncService` to each service class declaration**

In `LedgerSyncService.cs`, change:
```csharp
public class LedgerSyncService
```
to:
```csharp
public class LedgerSyncService : IEntitySyncService
```

Repeat for `DepartmentSyncService`, `AccountingTemplateSyncService`, `ContactSyncService`.

- [ ] **Step 5: Implement `FlexiAnalyticsSyncService`**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncService.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class FlexiAnalyticsSyncService
{
    private readonly IEntitySyncService _ledgerSync;
    private readonly IEntitySyncService _departmentSync;
    private readonly IEntitySyncService _accountingTemplateSync;
    private readonly IEntitySyncService _contactSync;
    private readonly ILogger<FlexiAnalyticsSyncService> _logger;

    public FlexiAnalyticsSyncService(
        IEntitySyncService ledgerSync,
        IEntitySyncService departmentSync,
        IEntitySyncService accountingTemplateSync,
        IEntitySyncService contactSync,
        ILogger<FlexiAnalyticsSyncService> logger)
    {
        _ledgerSync = ledgerSync;
        _departmentSync = departmentSync;
        _accountingTemplateSync = accountingTemplateSync;
        _contactSync = contactSync;
        _logger = logger;
    }

    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("FlexiAnalyticsSync.JobStarted {JobId}", jobId);
        var sw = Stopwatch.StartNew();

        var syncs = new (string Name, IEntitySyncService Svc)[]
        {
            ("ledger_entry", _ledgerSync),
            ("department", _departmentSync),
            ("accounting_template", _accountingTemplateSync),
            ("contact", _contactSync),
        };

        var succeeded = 0;
        var failed = 0;

        foreach (var (name, svc) in syncs)
        {
            try
            {
                var result = await svc.SyncAsync(ct);
                if (result.IsSuccess)
                    succeeded++;
                else
                    failed++;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "FlexiAnalyticsSync.EntityFailed {EntityName} (unhandled)", name);
            }
        }

        _logger.LogInformation(
            "FlexiAnalyticsSync.JobCompleted {JobId} succeeded={Succeeded} failed={Failed} ms={Ms}",
            jobId, succeeded, failed, sw.ElapsedMilliseconds);
    }
}
```

- [ ] **Step 6: Update DI registration to use keyed services**

Because `FlexiAnalyticsSyncService` now takes 4 `IEntitySyncService` parameters (all the same interface), DI can't distinguish between them automatically. Update `AddFlexiAnalyticsSync` in `FlexiAdapterServiceCollectionExtensions.cs` to register the orchestrator manually:

```csharp
    private static IServiceCollection AddFlexiAnalyticsSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FlexiAnalyticsSyncOptions>(
            configuration.GetSection(FlexiAnalyticsSyncOptions.ConfigurationKey));
        services.AddScoped<ISyncWatermarkRepository, SyncWatermarkRepository>();
        services.AddScoped<LedgerSyncService>();
        services.AddScoped<DepartmentSyncService>();
        services.AddScoped<AccountingTemplateSyncService>();
        services.AddScoped<ContactSyncService>();
        services.AddScoped<FlexiAnalyticsSyncService>(sp => new FlexiAnalyticsSyncService(
            sp.GetRequiredService<LedgerSyncService>(),
            sp.GetRequiredService<DepartmentSyncService>(),
            sp.GetRequiredService<AccountingTemplateSyncService>(),
            sp.GetRequiredService<ContactSyncService>(),
            sp.GetRequiredService<ILogger<FlexiAnalyticsSyncService>>()));
        services.AddScoped<IRecurringJob, FlexiAnalyticsSyncJob>();
        return services;
    }
```

- [ ] **Step 7: Run tests to confirm they pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics.FlexiAnalyticsSyncServiceTests" -v normal
```

Expected: `Passed: 2, Failed: 0`

- [ ] **Step 8: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/FlexiAnalyticsSyncServiceTests.cs
git commit -m "feat(analytics): add FlexiAnalyticsSyncService orchestrator with entity isolation"
```

---

## Task 9: FlexiAnalyticsSyncJob

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncJob.cs`

No separate unit test: the job's only logic is an enable-check guard and a delegate call. The orchestrator is tested in Task 8.

- [ ] **Step 1: Create the job**

Create `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncJob.cs`:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Flexi.Analytics;

[DisableConcurrentExecution(timeoutInSeconds: 0)]
public class FlexiAnalyticsSyncJob : IRecurringJob
{
    private readonly FlexiAnalyticsSyncService _syncService;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly FlexiAnalyticsSyncOptions _options;
    private readonly ILogger<FlexiAnalyticsSyncJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "flexi-analytics-sync",
        DisplayName = "Flexi Analytics Sync",
        Description = "Pulls raw Flexi accounting ledger and dimension data into the anela_analytics database for Metabase BI",
        CronExpression = "0 3 * * *",
        TimeZoneId = "Europe/Prague",
        DefaultIsEnabled = true,
    };

    public FlexiAnalyticsSyncJob(
        FlexiAnalyticsSyncService syncService,
        IRecurringJobStatusChecker statusChecker,
        IOptions<FlexiAnalyticsSyncOptions> options,
        ILogger<FlexiAnalyticsSyncJob> logger)
    {
        _syncService = syncService;
        _statusChecker = statusChecker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Job {JobName} is disabled via configuration (FlexiAnalyticsSync:Enabled=false). Skipping.",
                Metadata.JobName);
            return;
        }

        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled in the database. Skipping.", Metadata.JobName);
            return;
        }

        try
        {
            await _syncService.SyncAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobName} failed", Metadata.JobName);
            throw; // Re-throw so Hangfire records the failure
        }
    }
}
```

- [ ] **Step 2: Verify the project builds**

```bash
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncJob.cs
git commit -m "feat(analytics): add FlexiAnalyticsSyncJob (nightly 03:00 Prague)"
```

---

## Task 10: DI Registration, Configuration, Project Wiring

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (config sections already added in Task 2 — verify correct)

- [ ] **Step 1: Update `FlexiAdapterServiceCollectionExtensions.cs`**

Add the following to the bottom of `AddFlexiAdapter`, immediately before `return services;`:

```csharp
        // Analytics sync — registered only when the analytics DB connection string is configured
        var analyticsConnectionString = configuration["AnalyticsDatabase:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(analyticsConnectionString))
        {
            services.AddAnalyticsPersistenceServices(analyticsConnectionString);
            services.AddFlexiAnalyticsSync(configuration);
        }
```

Also add a private static helper method at the bottom of the class:

```csharp
    private static IServiceCollection AddFlexiAnalyticsSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FlexiAnalyticsSyncOptions>(
            configuration.GetSection(FlexiAnalyticsSyncOptions.ConfigurationKey));
        services.AddScoped<ISyncWatermarkRepository, SyncWatermarkRepository>();
        services.AddScoped<LedgerSyncService>();
        services.AddScoped<DepartmentSyncService>();
        services.AddScoped<AccountingTemplateSyncService>();
        services.AddScoped<ContactSyncService>();
        services.AddScoped<FlexiAnalyticsSyncService>();
        services.AddScoped<IRecurringJob, FlexiAnalyticsSyncJob>();
        return services;
    }
```

Add the required using at the top of `FlexiAdapterServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Analytics;
```

- [ ] **Step 2: Verify `appsettings.json` has the correct sections**

Confirm `backend/src/Anela.Heblo.API/appsettings.json` contains (added in Task 2):

```json
  "AnalyticsDatabase": {
    "ConnectionString": ""
  },
  "FlexiAnalyticsSync": {
    "Enabled": true,
    "CronExpression": "0 3 * * *",
    "TimeZone": "Europe/Prague",
    "BatchSize": 500,
    "InitialBackfillFrom": "2020-01-01",
    "RequestTimeoutSeconds": 120
  }
```

The real `AnalyticsDatabase:ConnectionString` goes in Azure Key Vault / user secrets. The empty string in `appsettings.json` means the analytics sync is skipped in local dev unless overridden.

- [ ] **Step 3: Verify full solution build**

```bash
cd backend && dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run all analytics unit tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Analytics" -v normal
```

Expected: All pass.

- [ ] **Step 5: Run the full test suite to check for regressions**

```bash
cd backend && dotnet test
```

Expected: No regressions. Previously-passing tests still pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs
git commit -m "feat(analytics): wire FlexiAnalyticsSyncJob + services into DI"
```

---

## Task 11: Integration Test (Testcontainers)

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/Integration/LedgerSyncIntegrationTest.cs`

The `Testcontainers.PostgreSql` package was added to the test project in Task 3. This test spins up a real Postgres container, applies EF migrations, runs `LedgerSyncService.SyncAsync` with mocked Flexi client, and asserts database state.

- [ ] **Step 1: Write the integration test**

Create `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/Integration/LedgerSyncIntegrationTest.cs`:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Analytics.Entities;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Model.Accounting.Ledger;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics.Integration;

[Trait("Category", "Integration")]
public class LedgerSyncIntegrationTest : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithDatabase("anela_analytics")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private AnalyticsDbContext _dbContext = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AnalyticsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new AnalyticsDbContext(options);
        await _dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SyncAsync_FirstRun_PopulatesLedgerTable()
    {
        // Arrange
        var client = new Mock<ILedgerClient>();

        client.SetupSequence(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>
            {
                new() { Id = 100, AccountingDate = new DateTime(2025, 3, 1), LastUpdate = new DateTime(2025, 3, 1, 8, 0, 0), AmountLocal = 5000.0, ParSymbol = "DOC001", DebitAccountShowAs = "501000", CreditAccountShowAs = "321000", CurrencyRef = "code:CZK" },
                new() { Id = 101, AccountingDate = new DateTime(2025, 3, 2), LastUpdate = new DateTime(2025, 3, 2, 9, 0, 0), AmountLocal = 3000.0, ParSymbol = "DOC002", DebitAccountShowAs = "502000", CreditAccountShowAs = "221000", CurrencyRef = "code:CZK" },
            })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var repo = new SyncWatermarkRepository(_dbContext);
        var opts = Options.Create(new FlexiAnalyticsSyncOptions
        {
            BatchSize = 10,
            InitialBackfillFrom = "2025-01-01"
        });
        var svc = new LedgerSyncService(client.Object, repo, _dbContext, opts,
            Mock.Of<ILogger<LedgerSyncService>>());

        // Act
        var result = await svc.SyncAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RowsFetched.Should().Be(2);

        var entries = await _dbContext.LedgerEntries.ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.FlexiId == 100 && e.Amount == 5000m);
        entries.Should().Contain(e => e.FlexiId == 101);
        // last_modified is now populated from SDK 0.1.136 LastUpdate field
        entries.Should().AllSatisfy(e => e.LastModified.Should().NotBeNull());

        var state = await _dbContext.SyncStates.FindAsync("ledger_entry");
        state!.LastRunStatus.Should().Be("OK");
        state.Watermark.Should().NotBeNull();
        state.LastRunRowsFetched.Should().Be(2);
    }

    [Fact]
    public async Task SyncAsync_SecondRun_UpsertsExistingRows()
    {
        // Arrange — pre-populate one row
        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            FlexiId = 200,
            EntryDate = new DateOnly(2025, 4, 1),
            Amount = 1000m,
            Currency = "code:CZK",
            RawPayload = "{}",
            SyncedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        _dbContext.SyncStates.Add(new SyncState
        {
            EntityName = "ledger_entry",
            Watermark = DateTimeOffset.UtcNow.AddDays(-1),
            LastRunStatus = "OK"
        });
        await _dbContext.SaveChangesAsync();

        var client = new Mock<ILedgerClient>();
        client.SetupSequence(c => c.GetAsync(
                It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItemFlexiDto>
            {
                // Same ID, updated amount — Flexi re-posted this entry
                new() { Id = 200, AccountingDate = new DateTime(2025, 4, 1), LastUpdate = DateTime.UtcNow, AmountLocal = 9999.0, ParSymbol = "DOC200", DebitAccountShowAs = "501000", CreditAccountShowAs = "221000", CurrencyRef = "code:CZK" },
            })
            .ReturnsAsync(new List<LedgerItemFlexiDto>());

        var repo = new SyncWatermarkRepository(_dbContext);
        var opts = Options.Create(new FlexiAnalyticsSyncOptions { BatchSize = 10, InitialBackfillFrom = "2024-01-01" });
        var svc = new LedgerSyncService(client.Object, repo, _dbContext, opts,
            Mock.Of<ILogger<LedgerSyncService>>());

        // Act
        await svc.SyncAsync();

        // Assert — upserted, not duplicated
        var entries = await _dbContext.LedgerEntries.Where(e => e.FlexiId == 200).ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].Amount.Should().Be(9999m);
    }
}
```

- [ ] **Step 2: Run the integration test**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ \
  --filter "Category=Integration" -v normal
```

This requires Docker to be running. Expected: `Passed: 2, Failed: 0`

If Docker is unavailable locally, the test can be skipped with `--filter "Category!=Integration"`.

- [ ] **Step 3: Verify full test suite including integration tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests/ -v normal
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/Integration/
git commit -m "test(analytics): add Testcontainers integration test for LedgerSyncService"
```

---

## Task 12: Final Validation

- [ ] **Step 1: Full build**

```bash
cd backend && dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 2: Full test run**

```bash
cd backend && dotnet test --filter "Category!=Integration"
```

Expected: All tests pass, 0 failures.

- [ ] **Step 3: Format check**

```bash
cd backend && dotnet format --verify-no-changes
```

Fix any formatting issues if this fails.

- [ ] **Step 4: Verify `ApplicationDbContext` migrations are untouched**

```bash
cd backend && git diff origin/main -- src/Anela.Heblo.Persistence/Migrations/
```

Expected: No output (no changes to main DB migrations).

- [ ] **Step 5: Verify `appsettings.json` diff looks correct**

```bash
cd backend && git diff origin/main -- src/Anela.Heblo.API/appsettings.json
```

Expected: Only the `AnalyticsDatabase` and `FlexiAnalyticsSync` sections added.

- [ ] **Step 6: Final commit**

```bash
git add -u
git commit -m "chore(analytics): format and final validation"
```

---

## Post-Deploy Verification (Manual)

After deploying to staging:

1. Add `AnalyticsDatabase:ConnectionString` to Key Vault with value: `Host=<azure-pg-server>;Database=anela_analytics;Username=heblo_analytics_rw;Password=<secret>`

2. Run the EF migration against the analytics DB:
   ```bash
   dotnet ef database update \
     --project backend/src/Anela.Heblo.Persistence.Analytics \
     --startup-project backend/src/Anela.Heblo.Persistence.Analytics \
     --context AnalyticsDbContext \
     --connection "Host=<azure-pg-server>;Database=anela_analytics;..."
   ```

3. Trigger `FlexiAnalyticsSyncJob` manually from the Hangfire dashboard.

4. Verify via `psql`:
   ```sql
   SELECT entity_name, last_run_status, last_run_rows_upserted, watermark
   FROM flexi_raw.sync_state;
   ```
   Expected: 4 rows, all `last_run_status = 'OK'`, non-null watermarks.

5. Sample query:
   ```sql
   SELECT cost_center, SUM(amount) AS total
   FROM flexi_raw.ledger_entry
   WHERE entry_date >= '2025-01-01'
   GROUP BY cost_center
   ORDER BY total DESC
   LIMIT 10;
   ```
   Expected: Non-empty result.
