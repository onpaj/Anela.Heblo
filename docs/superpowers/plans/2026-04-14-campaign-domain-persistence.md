# Campaign Domain + Persistence — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the Campaign domain model (entities, enums, platform DTOs, repository + client interfaces) and full EF Core persistence layer (configurations, upsert repository, DbSet registration, DI wiring, migration) so all subsequent sub-plans have a stable, tested foundation.

**Architecture:** Clean Architecture / Vertical Slice. Domain types in `Domain/Features/Campaigns/`. EF configurations + repository in `Persistence/Campaigns/`. `ApplicationDbContext.OnModelCreating` already calls `ApplyConfigurationsFromAssembly` — only DbSet properties and DI registration need touching in existing files.

**Tech Stack:** .NET 8, EF Core 8 + Npgsql, PostgreSQL ("public" schema), xUnit, FluentAssertions, EF InMemory (repository tests)

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdPlatform.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdCampaign.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdAdSet.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Ad.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdDailyMetric.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdSyncLog.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/MetaCampaignDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/MetaAdSetDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/MetaAdDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/MetaInsightDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/GoogleCampaignDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/GoogleAdGroupDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/GoogleAdDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/GoogleMetricDto.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/IMetaAdsClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/IGoogleAdsClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/Campaigns/ICampaignRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Campaigns/AdCampaignConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Campaigns/AdAdSetConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Campaigns/AdConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Campaigns/AdDailyMetricConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Campaigns/AdSyncLogConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Campaigns/CampaignRepository.cs`
- `backend/test/Anela.Heblo.Tests/Campaigns/AdDailyMetricTests.cs`
- `backend/test/Anela.Heblo.Tests/Campaigns/CampaignRepositoryTests.cs`

**Modify:**
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add 5 DbSet properties
- `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — register `ICampaignRepository`

---

### Task 1: Domain Enums and Entities

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdPlatform.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdCampaign.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdAdSet.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/Ad.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdDailyMetric.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/AdSyncLog.cs`

- [ ] **Step 1: Create `AdPlatform.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public enum AdPlatform
{
    Meta = 1,
    Google = 2
}
```

- [ ] **Step 2: Create `AdCampaign.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdCampaign
{
    public Guid Id { get; set; }
    public AdPlatform Platform { get; set; }
    public string PlatformCampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public decimal? DailyBudget { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime SyncedAt { get; set; }

    public ICollection<AdAdSet> AdSets { get; set; } = new List<AdAdSet>();
}
```

- [ ] **Step 3: Create `AdAdSet.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdAdSet
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public AdPlatform Platform { get; set; }
    public string PlatformAdSetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TargetingDescription { get; set; }

    public AdCampaign Campaign { get; set; } = null!;
    public ICollection<Ad> Ads { get; set; } = new List<Ad>();
}
```

- [ ] **Step 4: Create `Ad.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public class Ad
{
    public Guid Id { get; set; }
    public Guid AdSetId { get; set; }
    public AdPlatform Platform { get; set; }
    public string PlatformAdId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CreativePreviewUrl { get; set; }

    public AdAdSet AdSet { get; set; } = null!;
    public ICollection<AdDailyMetric> DailyMetrics { get; set; } = new List<AdDailyMetric>();
}
```

- [ ] **Step 5: Create `AdDailyMetric.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdDailyMetric
{
    public Guid Id { get; set; }
    public Guid AdId { get; set; }
    public DateOnly Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Spend { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionValue { get; set; }
    public decimal CTR { get; set; }
    public decimal CPC { get; set; }
    public decimal ROAS { get; set; }

    public Ad Ad { get; set; } = null!;

    public static AdDailyMetric Compute(
        Guid id,
        Guid adId,
        DateOnly date,
        long impressions,
        long clicks,
        decimal spend,
        int conversions,
        decimal conversionValue)
    {
        return new AdDailyMetric
        {
            Id = id,
            AdId = adId,
            Date = date,
            Impressions = impressions,
            Clicks = clicks,
            Spend = spend,
            Conversions = conversions,
            ConversionValue = conversionValue,
            CTR = impressions > 0 ? Math.Round((decimal)clicks / impressions, 6) : 0m,
            CPC = clicks > 0 ? Math.Round(spend / clicks, 6) : 0m,
            ROAS = spend > 0 ? Math.Round(conversionValue / spend, 6) : 0m
        };
    }
}
```

- [ ] **Step 6: Create `AdSyncLog.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public class AdSyncLog
{
    public Guid Id { get; set; }
    public AdPlatform Platform { get; set; }
    public DateTime SyncStarted { get; set; }
    public DateTime? SyncCompleted { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int RecordsProcessed { get; set; }

    public static AdSyncLog StartNew(AdPlatform platform) =>
        new AdSyncLog
        {
            Id = Guid.NewGuid(),
            Platform = platform,
            SyncStarted = DateTime.UtcNow,
            Status = "Running",
            RecordsProcessed = 0
        };

    public void MarkSucceeded(int recordsProcessed)
    {
        Status = "Succeeded";
        SyncCompleted = DateTime.UtcNow;
        RecordsProcessed = recordsProcessed;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = "Failed";
        SyncCompleted = DateTime.UtcNow;
        ErrorMessage = errorMessage;
    }
}
```

- [ ] **Step 7: Verify domain project builds**

```bash
cd /path/to/repo && dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: Build succeeded, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Campaigns/
git commit -m "feat(campaigns): add domain entities AdCampaign, AdAdSet, Ad, AdDailyMetric, AdSyncLog"
```

---

### Task 2: Platform DTOs and Client Interfaces

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/Dtos/` (8 files)
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/IMetaAdsClient.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/IGoogleAdsClient.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Campaigns/ICampaignRepository.cs`

- [ ] **Step 1: Create Meta DTOs**

`Dtos/MetaCampaignDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class MetaCampaignDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public decimal? DailyBudget { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
}
```

`Dtos/MetaAdSetDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class MetaAdSetDto
{
    public string Id { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TargetingDescription { get; set; }
}
```

`Dtos/MetaAdDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class MetaAdDto
{
    public string Id { get; set; } = string.Empty;
    public string AdSetId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CreativePreviewUrl { get; set; }
}
```

`Dtos/MetaInsightDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class MetaInsightDto
{
    public string AdId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal Spend { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionValue { get; set; }
}
```

- [ ] **Step 2: Create Google DTOs**

`Dtos/GoogleCampaignDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class GoogleCampaignDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AdvertisingChannelType { get; set; } = string.Empty;
    public decimal? DailyBudgetMicros { get; set; }
}
```

`Dtos/GoogleAdGroupDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class GoogleAdGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
```

`Dtos/GoogleAdDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class GoogleAdDto
{
    public string Id { get; set; } = string.Empty;
    public string AdGroupId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
```

`Dtos/GoogleMetricDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class GoogleMetricDto
{
    public string AdId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public decimal CostMicros { get; set; }
    public int Conversions { get; set; }
    public decimal ConversionsValue { get; set; }
}
```

- [ ] **Step 3: Create client interfaces**

`IMetaAdsClient.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns.Dtos;

namespace Anela.Heblo.Domain.Features.Campaigns;

public interface IMetaAdsClient
{
    Task<IReadOnlyList<MetaCampaignDto>> GetCampaignsAsync(CancellationToken ct);
    Task<IReadOnlyList<MetaAdSetDto>> GetAdSetsAsync(string campaignId, CancellationToken ct);
    Task<IReadOnlyList<MetaAdDto>> GetAdsAsync(string adSetId, CancellationToken ct);
    Task<IReadOnlyList<MetaInsightDto>> GetInsightsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
```

`IGoogleAdsClient.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns.Dtos;

namespace Anela.Heblo.Domain.Features.Campaigns;

public interface IGoogleAdsClient
{
    Task<IReadOnlyList<GoogleCampaignDto>> GetCampaignsAsync(CancellationToken ct);
    Task<IReadOnlyList<GoogleAdGroupDto>> GetAdGroupsAsync(string campaignId, CancellationToken ct);
    Task<IReadOnlyList<GoogleAdDto>> GetAdsAsync(string adGroupId, CancellationToken ct);
    Task<IReadOnlyList<GoogleMetricDto>> GetMetricsAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
```

- [ ] **Step 4: Create `ICampaignRepository.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Campaigns;

public interface ICampaignRepository
{
    // Upsert (used by sync handlers)
    Task UpsertCampaignsAsync(IEnumerable<AdCampaign> campaigns, CancellationToken ct);
    Task UpsertAdSetsAsync(IEnumerable<AdAdSet> adSets, CancellationToken ct);
    Task UpsertAdsAsync(IEnumerable<Ad> ads, CancellationToken ct);
    Task UpsertDailyMetricsAsync(IEnumerable<AdDailyMetric> metrics, CancellationToken ct);
    Task LogSyncStartedAsync(AdSyncLog log, CancellationToken ct);
    Task UpdateSyncLogAsync(AdSyncLog log, CancellationToken ct);

    // Query (implemented in plan 4)
    Task<CampaignDashboardDto> GetDashboardAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct);
    Task<IReadOnlyList<CampaignSummaryDto>> GetCampaignListAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct);
    Task<CampaignDetailDto> GetCampaignDetailAsync(Guid campaignId, DateOnly from, DateOnly to, CancellationToken ct);
}
```

- [ ] **Step 5: Create query result DTOs (stubs for compile)**

`Dtos/CampaignDashboardDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class CampaignDashboardDto
{
    public decimal TotalSpend { get; set; }
    public int TotalConversions { get; set; }
    public decimal AvgRoas { get; set; }
    public decimal AvgCpc { get; set; }
    public IReadOnlyList<DailySpendDto> SpendOverTime { get; set; } = Array.Empty<DailySpendDto>();
}
```

`Dtos/DailySpendDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class DailySpendDto
{
    public DateOnly Date { get; set; }
    public decimal MetaSpend { get; set; }
    public decimal GoogleSpend { get; set; }
}
```

`Dtos/CampaignSummaryDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class CampaignSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public int Conversions { get; set; }
    public decimal Roas { get; set; }
}
```

`Dtos/CampaignDetailDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class CampaignDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AdPlatform Platform { get; set; }
    public string Status { get; set; } = string.Empty;
    public IReadOnlyList<AdSetDetailDto> AdSets { get; set; } = Array.Empty<AdSetDetailDto>();
}
```

`Dtos/AdSetDetailDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class AdSetDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public IReadOnlyList<AdSummaryDto> Ads { get; set; } = Array.Empty<AdSummaryDto>();
}
```

`Dtos/AdSummaryDto.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Campaigns.Dtos;

public class AdSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CreativePreviewUrl { get; set; }
    public decimal Spend { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public int Conversions { get; set; }
}
```

- [ ] **Step 6: Verify domain builds**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Campaigns/
git commit -m "feat(campaigns): add platform DTOs and domain interfaces (IMetaAdsClient, IGoogleAdsClient, ICampaignRepository)"
```

---

### Task 3: Unit Tests for Domain Logic

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Campaigns/AdDailyMetricTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using FluentAssertions;

namespace Anela.Heblo.Tests.Campaigns;

public class AdDailyMetricTests
{
    [Fact]
    public void Compute_WithNonZeroImpressions_CalculatesCtr()
    {
        var metric = AdDailyMetric.Compute(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 1, 1),
            impressions: 1000, clicks: 50, spend: 100m,
            conversions: 5, conversionValue: 200m);

        metric.CTR.Should().Be(0.05m);
    }

    [Fact]
    public void Compute_WithZeroImpressions_ReturnsCtrZero()
    {
        var metric = AdDailyMetric.Compute(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 1, 1),
            impressions: 0, clicks: 0, spend: 0m,
            conversions: 0, conversionValue: 0m);

        metric.CTR.Should().Be(0m);
        metric.CPC.Should().Be(0m);
        metric.ROAS.Should().Be(0m);
    }

    [Fact]
    public void Compute_WithNonZeroClicks_CalculatesCpc()
    {
        var metric = AdDailyMetric.Compute(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 1, 1),
            impressions: 1000, clicks: 50, spend: 100m,
            conversions: 5, conversionValue: 200m);

        metric.CPC.Should().Be(2m);
    }

    [Fact]
    public void Compute_WithNonZeroSpend_CalculatesRoas()
    {
        var metric = AdDailyMetric.Compute(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2024, 1, 1),
            impressions: 1000, clicks: 50, spend: 100m,
            conversions: 5, conversionValue: 200m);

        metric.ROAS.Should().Be(2m);
    }

    [Fact]
    public void AdSyncLog_StartNew_SetsRunningStatus()
    {
        var log = AdSyncLog.StartNew(AdPlatform.Meta);

        log.Status.Should().Be("Running");
        log.Platform.Should().Be(AdPlatform.Meta);
        log.SyncCompleted.Should().BeNull();
    }

    [Fact]
    public void AdSyncLog_MarkSucceeded_SetsSucceededStatus()
    {
        var log = AdSyncLog.StartNew(AdPlatform.Google);
        log.MarkSucceeded(42);

        log.Status.Should().Be("Succeeded");
        log.RecordsProcessed.Should().Be(42);
        log.SyncCompleted.Should().NotBeNull();
    }

    [Fact]
    public void AdSyncLog_MarkFailed_SetsFailedStatusWithMessage()
    {
        var log = AdSyncLog.StartNew(AdPlatform.Meta);
        log.MarkFailed("API timeout");

        log.Status.Should().Be("Failed");
        log.ErrorMessage.Should().Be("API timeout");
        log.SyncCompleted.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL (domain code not yet meeting tests)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Campaigns.AdDailyMetricTests" -v minimal
```

Expected: Tests pass (domain entity Compute factory method is already defined in Task 1).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Campaigns/AdDailyMetricTests.cs
git commit -m "test(campaigns): add unit tests for AdDailyMetric computed fields and AdSyncLog state transitions"
```

---

### Task 4: EF Core Configurations

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Campaigns/AdCampaignConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Campaigns/AdAdSetConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Campaigns/AdConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Campaigns/AdDailyMetricConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Campaigns/AdSyncLogConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create `AdCampaignConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdCampaignConfiguration : IEntityTypeConfiguration<AdCampaign>
{
    public void Configure(EntityTypeBuilder<AdCampaign> builder)
    {
        builder.ToTable("AdCampaigns", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlatformCampaignId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Objective).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(8).IsRequired();
        builder.Property(x => x.DailyBudget).HasColumnType("numeric(18,4)");

        builder.HasIndex(x => new { x.Platform, x.PlatformCampaignId }).IsUnique();

        builder.HasMany(x => x.AdSets)
            .WithOne(x => x.Campaign)
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 2: Create `AdAdSetConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdAdSetConfiguration : IEntityTypeConfiguration<AdAdSet>
{
    public void Configure(EntityTypeBuilder<AdAdSet> builder)
    {
        builder.ToTable("AdAdSets", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlatformAdSetId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TargetingDescription).HasMaxLength(1024);

        builder.HasIndex(x => new { x.Platform, x.PlatformAdSetId }).IsUnique();

        builder.HasMany(x => x.Ads)
            .WithOne(x => x.AdSet)
            .HasForeignKey(x => x.AdSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Create `AdConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdConfiguration : IEntityTypeConfiguration<Ad>
{
    public void Configure(EntityTypeBuilder<Ad> builder)
    {
        builder.ToTable("Ads", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PlatformAdId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreativePreviewUrl).HasMaxLength(2048);

        builder.HasIndex(x => new { x.Platform, x.PlatformAdId }).IsUnique();

        builder.HasMany(x => x.DailyMetrics)
            .WithOne(x => x.Ad)
            .HasForeignKey(x => x.AdId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Create `AdDailyMetricConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdDailyMetricConfiguration : IEntityTypeConfiguration<AdDailyMetric>
{
    public void Configure(EntityTypeBuilder<AdDailyMetric> builder)
    {
        builder.ToTable("AdDailyMetrics", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Spend).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ConversionValue).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CTR).HasColumnType("numeric(12,6)");
        builder.Property(x => x.CPC).HasColumnType("numeric(12,6)");
        builder.Property(x => x.ROAS).HasColumnType("numeric(12,6)");

        builder.HasIndex(x => new { x.AdId, x.Date }).IsUnique();
    }
}
```

- [ ] **Step 5: Create `AdSyncLogConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Campaigns;

public class AdSyncLogConfiguration : IEntityTypeConfiguration<AdSyncLog>
{
    public void Configure(EntityTypeBuilder<AdSyncLog> builder)
    {
        builder.ToTable("AdSyncLogs", "public");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(4096);

        builder.HasIndex(x => new { x.Platform, x.SyncStarted });
    }
}
```

- [ ] **Step 6: Add DbSets to `ApplicationDbContext.cs`**

In `ApplicationDbContext.cs`, find the `// Grid Layouts module` block and add after it:

```csharp
// Campaigns module
public DbSet<AdCampaign> AdCampaigns { get; set; } = null!;
public DbSet<AdAdSet> AdAdSets { get; set; } = null!;
public DbSet<Ad> Ads { get; set; } = null!;
public DbSet<AdDailyMetric> AdDailyMetrics { get; set; } = null!;
public DbSet<AdSyncLog> AdSyncLogs { get; set; } = null!;
```

Also add the using at the top:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
```

- [ ] **Step 7: Verify persistence builds**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Campaigns/
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(campaigns): add EF Core configurations and DbSets for campaign entities"
```

---

### Task 5: Campaign Repository (Upsert Side)

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Campaigns/CampaignRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

- [ ] **Step 1: Write failing repository tests first**

`backend/test/Anela.Heblo.Tests/Campaigns/CampaignRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Campaigns;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Campaigns;

public class CampaignRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly CampaignRepository _repository;

    public CampaignRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new CampaignRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task UpsertCampaignsAsync_NewCampaign_InsertsRecord()
    {
        var campaign = new AdCampaign
        {
            Id = Guid.NewGuid(),
            Platform = AdPlatform.Meta,
            PlatformCampaignId = "123456",
            Name = "Test Campaign",
            Status = "ACTIVE",
            Objective = "CONVERSIONS",
            Currency = "CZK",
            CreatedAt = DateTime.UtcNow,
            SyncedAt = DateTime.UtcNow
        };

        await _repository.UpsertCampaignsAsync(new[] { campaign }, CancellationToken.None);

        var saved = await _context.AdCampaigns.FirstOrDefaultAsync(x => x.PlatformCampaignId == "123456");
        saved.Should().NotBeNull();
        saved!.Name.Should().Be("Test Campaign");
    }

    [Fact]
    public async Task UpsertCampaignsAsync_ExistingCampaign_UpdatesRecord()
    {
        var id = Guid.NewGuid();
        var original = new AdCampaign
        {
            Id = id,
            Platform = AdPlatform.Meta,
            PlatformCampaignId = "123456",
            Name = "Old Name",
            Status = "ACTIVE",
            Objective = "REACH",
            Currency = "CZK",
            CreatedAt = DateTime.UtcNow,
            SyncedAt = DateTime.UtcNow
        };
        _context.AdCampaigns.Add(original);
        await _context.SaveChangesAsync();

        var updated = new AdCampaign
        {
            Id = id,
            Platform = AdPlatform.Meta,
            PlatformCampaignId = "123456",
            Name = "New Name",
            Status = "PAUSED",
            Objective = "REACH",
            Currency = "CZK",
            CreatedAt = original.CreatedAt,
            SyncedAt = DateTime.UtcNow
        };

        await _repository.UpsertCampaignsAsync(new[] { updated }, CancellationToken.None);

        var saved = await _context.AdCampaigns.FirstAsync(x => x.Id == id);
        saved.Name.Should().Be("New Name");
        saved.Status.Should().Be("PAUSED");
    }

    [Fact]
    public async Task LogSyncStartedAsync_PersistsSyncLog()
    {
        var log = AdSyncLog.StartNew(AdPlatform.Meta);

        await _repository.LogSyncStartedAsync(log, CancellationToken.None);

        var saved = await _context.AdSyncLogs.FirstOrDefaultAsync(x => x.Id == log.Id);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be("Running");
    }
}
```

- [ ] **Step 2: Run tests — expect compilation failure (CampaignRepository not yet created)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Campaigns.CampaignRepositoryTests" -v minimal
```

Expected: Compilation error — `CampaignRepository` not found.

- [ ] **Step 3: Implement `CampaignRepository.cs`**

```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Campaigns;

public class CampaignRepository : ICampaignRepository
{
    private readonly ApplicationDbContext _context;

    public CampaignRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task UpsertCampaignsAsync(IEnumerable<AdCampaign> campaigns, CancellationToken ct)
    {
        foreach (var campaign in campaigns)
        {
            var existing = await _context.AdCampaigns
                .FirstOrDefaultAsync(x => x.Platform == campaign.Platform && x.PlatformCampaignId == campaign.PlatformCampaignId, ct);

            if (existing is null)
            {
                await _context.AdCampaigns.AddAsync(campaign, ct);
            }
            else
            {
                existing.Name = campaign.Name;
                existing.Status = campaign.Status;
                existing.Objective = campaign.Objective;
                existing.DailyBudget = campaign.DailyBudget;
                existing.Currency = campaign.Currency;
                existing.SyncedAt = campaign.SyncedAt;
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpsertAdSetsAsync(IEnumerable<AdAdSet> adSets, CancellationToken ct)
    {
        foreach (var adSet in adSets)
        {
            var existing = await _context.AdAdSets
                .FirstOrDefaultAsync(x => x.Platform == adSet.Platform && x.PlatformAdSetId == adSet.PlatformAdSetId, ct);

            if (existing is null)
            {
                await _context.AdAdSets.AddAsync(adSet, ct);
            }
            else
            {
                existing.Name = adSet.Name;
                existing.Status = adSet.Status;
                existing.TargetingDescription = adSet.TargetingDescription;
                existing.CampaignId = adSet.CampaignId;
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpsertAdsAsync(IEnumerable<Ad> ads, CancellationToken ct)
    {
        foreach (var ad in ads)
        {
            var existing = await _context.Ads
                .FirstOrDefaultAsync(x => x.Platform == ad.Platform && x.PlatformAdId == ad.PlatformAdId, ct);

            if (existing is null)
            {
                await _context.Ads.AddAsync(ad, ct);
            }
            else
            {
                existing.Name = ad.Name;
                existing.Status = ad.Status;
                existing.CreativePreviewUrl = ad.CreativePreviewUrl;
                existing.AdSetId = ad.AdSetId;
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpsertDailyMetricsAsync(IEnumerable<AdDailyMetric> metrics, CancellationToken ct)
    {
        foreach (var metric in metrics)
        {
            var existing = await _context.AdDailyMetrics
                .FirstOrDefaultAsync(x => x.AdId == metric.AdId && x.Date == metric.Date, ct);

            if (existing is null)
            {
                await _context.AdDailyMetrics.AddAsync(metric, ct);
            }
            else
            {
                existing.Impressions = metric.Impressions;
                existing.Clicks = metric.Clicks;
                existing.Spend = metric.Spend;
                existing.Conversions = metric.Conversions;
                existing.ConversionValue = metric.ConversionValue;
                existing.CTR = metric.CTR;
                existing.CPC = metric.CPC;
                existing.ROAS = metric.ROAS;
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task LogSyncStartedAsync(AdSyncLog log, CancellationToken ct)
    {
        await _context.AdSyncLogs.AddAsync(log, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateSyncLogAsync(AdSyncLog log, CancellationToken ct)
    {
        _context.AdSyncLogs.Update(log);
        await _context.SaveChangesAsync(ct);
    }

    // Query methods implemented in plan 4 (campaign-query-api)
    public Task<CampaignDashboardDto> GetDashboardAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in campaign-query-api plan");

    public Task<IReadOnlyList<CampaignSummaryDto>> GetCampaignListAsync(DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in campaign-query-api plan");

    public Task<CampaignDetailDto> GetCampaignDetailAsync(Guid campaignId, DateOnly from, DateOnly to, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in campaign-query-api plan");
}
```

- [ ] **Step 4: Register in `PersistenceModule.cs`**

Add after `// Grid Layouts repositories`:
```csharp
// Campaigns repositories
services.AddScoped<ICampaignRepository, CampaignRepository>();
```

Add using at top:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Persistence.Campaigns;
```

- [ ] **Step 5: Run tests — expect PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Campaigns" -v minimal
```

Expected: All 10 tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Campaigns/CampaignRepository.cs
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git add backend/test/Anela.Heblo.Tests/Campaigns/CampaignRepositoryTests.cs
git commit -m "feat(campaigns): add CampaignRepository upsert implementation and DI registration"
```

---

### Task 6: EF Migration

**Files:** Migration files auto-generated by EF tooling in `Anela.Heblo.Persistence/Migrations/`

- [ ] **Step 1: Add migration**

```bash
cd backend && dotnet ef migrations add AddCampaigns \
  --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: Migration files created in `Persistence/Migrations/`.

- [ ] **Step 2: Verify migration SQL looks correct**

```bash
cd backend && dotnet ef migrations script \
  --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj \
  --idempotent 2>&1 | grep -A 5 "AdCampaigns"
```

Expected: `CREATE TABLE "AdCampaigns"` SQL visible.

- [ ] **Step 3: Full build check**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit migration**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(campaigns): add EF migration AddCampaigns"
```
