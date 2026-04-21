# Campaign Query API — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the query side of the campaign feature — dashboard, campaign list, and campaign detail handlers — plus the `CampaignsController` exposing 4 REST endpoints, and complete the `CampaignRepository` query methods.

**Architecture:** Query handlers follow the same MediatR pattern as all other features. The controller inherits from `BaseApiController` and uses `HandleResponse<T>()`. Repository query methods use raw EF Core LINQ with `GroupBy` and projections — no AutoMapper, no stored procedures.

**Tech Stack:** .NET 8, MediatR, EF Core 8 + LINQ, `BaseApiController`

**Prerequisite:** `campaign-domain-persistence` plan must be completed (all entities, `ICampaignRepository`, migrations done).

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/GetCampaignDashboard/GetCampaignDashboardRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/GetCampaignDashboard/GetCampaignDashboardHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/GetCampaignList/GetCampaignListRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/GetCampaignList/GetCampaignListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/GetCampaignDetail/GetCampaignDetailRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/GetCampaignDetail/GetCampaignDetailHandler.cs`
- `backend/src/Anela.Heblo.API/Controllers/CampaignsController.cs`
- `backend/test/Anela.Heblo.Tests/Campaigns/GetCampaignDashboardHandlerTests.cs`

**Modify:**
- `backend/src/Anela.Heblo.Persistence/Campaigns/CampaignRepository.cs` — implement query methods

---

### Task 1: Complete Repository Query Methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Campaigns/CampaignRepository.cs`

- [ ] **Step 1: Write failing query tests**

`backend/test/Anela.Heblo.Tests/Campaigns/GetCampaignDashboardHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDashboard;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Anela.Heblo.Tests.Campaigns;

public class GetCampaignDashboardHandlerTests
{
    private readonly ICampaignRepository _repository = Substitute.For<ICampaignRepository>();

    [Fact]
    public async Task Handle_ReturnsDashboardFromRepository()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 31);

        var expected = new CampaignDashboardDto
        {
            TotalSpend = 1500m,
            TotalConversions = 75,
            AvgRoas = 2.5m,
            AvgCpc = 1.2m,
            SpendOverTime = new[] { new DailySpendDto { Date = from, MetaSpend = 50m, GoogleSpend = 30m } }
        };

        _repository.GetDashboardAsync(from, to, null, Arg.Any<CancellationToken>())
            .Returns(expected);

        var handler = new GetCampaignDashboardHandler(_repository);
        var request = new GetCampaignDashboardRequest { From = from, To = to, Platform = null };
        var result = await handler.Handle(request, CancellationToken.None);

        result.TotalSpend.Should().Be(1500m);
        result.TotalConversions.Should().Be(75);
        result.AvgRoas.Should().Be(2.5m);
    }
}
```

- [ ] **Step 2: Run test — expect compilation failure**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetCampaignDashboardHandlerTests" -v minimal
```

Expected: `GetCampaignDashboardRequest` not found.

- [ ] **Step 3: Implement query methods in `CampaignRepository.cs`**

Replace the three `throw new NotImplementedException(...)` stubs with the actual implementations:

```csharp
public async Task<CampaignDashboardDto> GetDashboardAsync(
    DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct)
{
    var query = _context.AdDailyMetrics
        .Include(m => m.Ad)
            .ThenInclude(a => a.AdSet)
                .ThenInclude(s => s.Campaign)
        .Where(m => m.Date >= from && m.Date <= to);

    if (platform.HasValue)
    {
        query = query.Where(m => m.Ad.Platform == platform.Value);
    }

    var rows = await query.ToListAsync(ct);

    var totalSpend = rows.Sum(m => m.Spend);
    var totalConversions = rows.Sum(m => m.Conversions);
    var totalConversionValue = rows.Sum(m => m.ConversionValue);
    var totalClicks = rows.Sum(m => m.Clicks);

    var avgRoas = totalSpend > 0 ? Math.Round(totalConversionValue / totalSpend, 4) : 0m;
    var avgCpc = totalClicks > 0 ? Math.Round(totalSpend / totalClicks, 4) : 0m;

    var spendOverTime = rows
        .GroupBy(m => m.Date)
        .OrderBy(g => g.Key)
        .Select(g => new DailySpendDto
        {
            Date = g.Key,
            MetaSpend = g.Where(m => m.Ad.Platform == AdPlatform.Meta).Sum(m => m.Spend),
            GoogleSpend = g.Where(m => m.Ad.Platform == AdPlatform.Google).Sum(m => m.Spend)
        })
        .ToList();

    return new CampaignDashboardDto
    {
        TotalSpend = totalSpend,
        TotalConversions = totalConversions,
        AvgRoas = avgRoas,
        AvgCpc = avgCpc,
        SpendOverTime = spendOverTime
    };
}

public async Task<IReadOnlyList<CampaignSummaryDto>> GetCampaignListAsync(
    DateOnly from, DateOnly to, AdPlatform? platform, CancellationToken ct)
{
    var query = _context.AdCampaigns.AsQueryable();

    if (platform.HasValue)
    {
        query = query.Where(c => c.Platform == platform.Value);
    }

    var campaigns = await query
        .Select(c => new
        {
            Campaign = c,
            Metrics = c.AdSets
                .SelectMany(s => s.Ads)
                .SelectMany(a => a.DailyMetrics)
                .Where(m => m.Date >= from && m.Date <= to)
        })
        .ToListAsync(ct);

    return campaigns.Select(x => new CampaignSummaryDto
    {
        Id = x.Campaign.Id,
        Name = x.Campaign.Name,
        Platform = x.Campaign.Platform,
        Status = x.Campaign.Status,
        Spend = x.Metrics.Sum(m => m.Spend),
        Impressions = x.Metrics.Sum(m => m.Impressions),
        Clicks = x.Metrics.Sum(m => m.Clicks),
        Conversions = x.Metrics.Sum(m => m.Conversions),
        Roas = x.Metrics.Sum(m => m.Spend) > 0
            ? Math.Round(x.Metrics.Sum(m => m.ConversionValue) / x.Metrics.Sum(m => m.Spend), 4)
            : 0m
    }).ToList();
}

public async Task<CampaignDetailDto> GetCampaignDetailAsync(
    Guid campaignId, DateOnly from, DateOnly to, CancellationToken ct)
{
    var campaign = await _context.AdCampaigns
        .Include(c => c.AdSets)
            .ThenInclude(s => s.Ads)
                .ThenInclude(a => a.DailyMetrics.Where(m => m.Date >= from && m.Date <= to))
        .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

    if (campaign is null)
    {
        throw new KeyNotFoundException($"Campaign {campaignId} not found");
    }

    return new CampaignDetailDto
    {
        Id = campaign.Id,
        Name = campaign.Name,
        Platform = campaign.Platform,
        Status = campaign.Status,
        AdSets = campaign.AdSets.Select(s => new AdSetDetailDto
        {
            Id = s.Id,
            Name = s.Name,
            Status = s.Status,
            Ads = s.Ads.Select(a => new AdSummaryDto
            {
                Id = a.Id,
                Name = a.Name,
                Status = a.Status,
                CreativePreviewUrl = a.CreativePreviewUrl,
                Spend = a.DailyMetrics.Sum(m => m.Spend),
                Impressions = a.DailyMetrics.Sum(m => m.Impressions),
                Clicks = a.DailyMetrics.Sum(m => m.Clicks),
                Conversions = a.DailyMetrics.Sum(m => m.Conversions)
            }).ToList()
        }).ToList()
    };
}
```

Also add the missing usings at the top of `CampaignRepository.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using Microsoft.EntityFrameworkCore;
```

- [ ] **Step 4: Build persistence**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Campaigns/CampaignRepository.cs
git commit -m "feat(campaigns): implement CampaignRepository query methods (dashboard, list, detail)"
```

---

### Task 2: Query Handlers

**Files:**
- Create: `GetCampaignDashboard/GetCampaignDashboardRequest.cs`
- Create: `GetCampaignDashboard/GetCampaignDashboardHandler.cs`
- Create: `GetCampaignList/GetCampaignListRequest.cs`
- Create: `GetCampaignList/GetCampaignListHandler.cs`
- Create: `GetCampaignDetail/GetCampaignDetailRequest.cs`
- Create: `GetCampaignDetail/GetCampaignDetailHandler.cs`

All in `backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/`

- [ ] **Step 1: Create dashboard handler**

`GetCampaignDashboard/GetCampaignDashboardRequest.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDashboard;

public class GetCampaignDashboardRequest : IRequest<CampaignDashboardDto>
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public AdPlatform? Platform { get; set; }
}
```

`GetCampaignDashboard/GetCampaignDashboardHandler.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDashboard;

public class GetCampaignDashboardHandler : IRequestHandler<GetCampaignDashboardRequest, CampaignDashboardDto>
{
    private readonly ICampaignRepository _repository;

    public GetCampaignDashboardHandler(ICampaignRepository repository)
    {
        _repository = repository;
    }

    public Task<CampaignDashboardDto> Handle(
        GetCampaignDashboardRequest request,
        CancellationToken cancellationToken)
    {
        return _repository.GetDashboardAsync(request.From, request.To, request.Platform, cancellationToken);
    }
}
```

- [ ] **Step 2: Create campaign list handler**

`GetCampaignList/GetCampaignListRequest.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignList;

public class GetCampaignListRequest : IRequest<IReadOnlyList<CampaignSummaryDto>>
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public AdPlatform? Platform { get; set; }
}
```

`GetCampaignList/GetCampaignListHandler.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignList;

public class GetCampaignListHandler : IRequestHandler<GetCampaignListRequest, IReadOnlyList<CampaignSummaryDto>>
{
    private readonly ICampaignRepository _repository;

    public GetCampaignListHandler(ICampaignRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<CampaignSummaryDto>> Handle(
        GetCampaignListRequest request,
        CancellationToken cancellationToken)
    {
        return _repository.GetCampaignListAsync(request.From, request.To, request.Platform, cancellationToken);
    }
}
```

- [ ] **Step 3: Create campaign detail handler**

`GetCampaignDetail/GetCampaignDetailRequest.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDetail;

public class GetCampaignDetailRequest : IRequest<CampaignDetailDto>
{
    public Guid CampaignId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}
```

`GetCampaignDetail/GetCampaignDetailHandler.cs`:
```csharp
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;

namespace Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDetail;

public class GetCampaignDetailHandler : IRequestHandler<GetCampaignDetailRequest, CampaignDetailDto>
{
    private readonly ICampaignRepository _repository;

    public GetCampaignDetailHandler(ICampaignRepository repository)
    {
        _repository = repository;
    }

    public Task<CampaignDetailDto> Handle(
        GetCampaignDetailRequest request,
        CancellationToken cancellationToken)
    {
        return _repository.GetCampaignDetailAsync(
            request.CampaignId, request.From, request.To, cancellationToken);
    }
}
```

- [ ] **Step 4: Run dashboard handler test — expect PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "GetCampaignDashboardHandlerTests" -v minimal
```

Expected: 1 test passes.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Campaigns/UseCases/
git commit -m "feat(campaigns): add GetCampaignDashboard, GetCampaignList, GetCampaignDetail handlers"
```

---

### Task 3: Campaigns Controller

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/CampaignsController.cs`

- [ ] **Step 1: Create `CampaignsController.cs`**

```csharp
using Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDashboard;
using Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignDetail;
using Anela.Heblo.Application.Features.Campaigns.UseCases.GetCampaignList;
using Anela.Heblo.Application.Features.Campaigns.UseCases.SyncMetaAds;
using Anela.Heblo.Application.Features.Campaigns.UseCases.SyncGoogleAds;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.Campaigns.Dtos;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CampaignsController : BaseApiController
{
    private readonly IMediator _mediator;

    public CampaignsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Returns aggregated dashboard metrics for the given date range and optional platform filter.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(CampaignDashboardDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CampaignDashboardDto>> GetDashboard(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] AdPlatform? platform,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetCampaignDashboardRequest { From = from, To = to, Platform = platform },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns a list of campaigns with aggregated metrics for the given date range.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CampaignSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CampaignSummaryDto>>> GetCampaigns(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] AdPlatform? platform,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetCampaignListRequest { From = from, To = to, Platform = platform },
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns campaign detail including ad sets and ads with metrics.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CampaignDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CampaignDetailDto>> GetCampaignDetail(
        Guid id,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _mediator.Send(
                new GetCampaignDetailRequest { CampaignId = id, From = from, To = to },
                cancellationToken);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Triggers a manual sync of both Meta and Google Ads data.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TriggerSync(CancellationToken cancellationToken)
    {
        var metaResult = await _mediator.Send(new SyncMetaAdsRequest(), cancellationToken);
        var googleResult = await _mediator.Send(new SyncGoogleAdsRequest(), cancellationToken);

        return Ok(new
        {
            meta = metaResult ? "succeeded" : "failed",
            google = googleResult ? "succeeded" : "failed"
        });
    }
}
```

- [ ] **Step 2: Full solution build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Format check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: No formatting issues.

- [ ] **Step 4: Run all campaign tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Campaigns" -v minimal
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/CampaignsController.cs
git commit -m "feat(campaigns): add CampaignsController with dashboard, list, detail, and manual sync endpoints"
```

---

### Task 4: Regenerate TypeScript API Client

- [ ] **Step 1: Rebuild to regenerate OpenAPI spec**

```bash
dotnet build backend/Anela.Heblo.sln
```

- [ ] **Step 2: Regenerate TypeScript client**

Follow the process in `docs/development/api-client-generation.md` to regenerate the TypeScript client. Typically:

```bash
cd frontend && npm run generate-api-client
```

Expected: New types for `CampaignDashboardDto`, `CampaignSummaryDto`, `CampaignDetailDto`, `AdPlatform` appear in the generated client.

- [ ] **Step 3: Commit regenerated client**

```bash
git add frontend/src/api/
git commit -m "feat(campaigns): regenerate TypeScript API client with campaign endpoints"
```
