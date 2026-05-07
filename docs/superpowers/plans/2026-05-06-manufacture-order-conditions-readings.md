# Manufacture Order Conditions Readings — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Silently capture inner/outer temperature and humidity from HomeAssistant when a manufacture order transitions to SemiProductManufactured or Completed, and display those readings read-only on the order detail page.

**Architecture:** A new domain port `IConditionsReadingProvider` keeps the domain and application layers agnostic of HomeAssistant. The implementation lives in a new `Anela.Heblo.Adapters.HomeAssistant` adapter project following the established typed-HttpClient pattern. Both confirmation workflows (`ConfirmSemiProductManufactureWorkflow`, `ConfirmProductCompletionWorkflow`) capture a snapshot before `SaveChangesAsync`; failures never block the transition.

**Tech Stack:** .NET 8, C#, EF Core (PostgreSQL), AutoMapper, xUnit + Moq + FluentAssertions, React 18, TypeScript, Jest + React Testing Library, OpenAPI-generated TypeScript client.

---

## File Map

### New backend files
| File | Responsibility |
|---|---|
| `Domain/Features/Manufacture/Conditions/IConditionsReadingProvider.cs` | Port interface — only reference inside domain/application |
| `Domain/Features/Manufacture/Conditions/ConditionsSnapshot.cs` | Value type returned by the provider |
| `Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs` | Enum: Live / Partial / Unavailable |
| `Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs` | Entity, child of ManufactureOrder |
| `Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs` | EF configuration |
| `Adapters/Anela.Heblo.Adapters.HomeAssistant/*.cs` | Settings + provider + service collection extension |
| `test/Anela.Heblo.Adapters.HomeAssistant.Tests/*.cs` | Adapter unit tests |

### Modified backend files
| File | Change |
|---|---|
| `Domain/Features/Manufacture/ManufactureOrder.cs` | Add `ConditionsReadings` collection |
| `Persistence/Manufacture/ManufactureOrderConfiguration.cs` | Configure navigation |
| `Persistence/Manufacture/ManufactureOrderRepository.cs` | Include ConditionsReadings in GetOrderByIdAsync |
| `Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs` | Inject provider, capture snapshot |
| `Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` | Inject provider, capture snapshot |
| `Application/Features/Manufacture/UseCases/GetManufactureOrders/GetManufactureOrdersResponse.cs` | Add `ConditionsReadings` to `ManufactureOrderDto`, add `ManufactureOrderConditionsReadingDto` |
| `Application/Features/Manufacture/ManufactureOrderMappingProfile.cs` | Add mapping for ConditionsReading |
| `API/Program.cs` | Register `AddHomeAssistantAdapter` |
| `API/appsettings.json` | Add `HomeAssistant:` section with placeholders |

### New frontend files
| File | Responsibility |
|---|---|
| `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx` | Read-only table of two readings |
| `frontend/src/components/manufacture/detail/__tests__/ConditionsReadingsSection.test.tsx` | Jest tests |

### Modified frontend files
| File | Change |
|---|---|
| `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx` | Import and render ConditionsReadingsSection |

---

## Task 1: Domain types — port, value object, entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/IConditionsReadingProvider.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`

- [ ] **Step 1: Create the ConditionsReadingSource enum**

Create `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsReadingSource.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture.Conditions;

public enum ConditionsReadingSource
{
    Live = 1,
    Partial = 2,
    Unavailable = 3,
}
```

- [ ] **Step 2: Create ConditionsSnapshot**

Create `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ConditionsSnapshot.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture.Conditions;

public sealed record ConditionsSnapshot(
    decimal? InnerTemperature,
    decimal? InnerHumidity,
    decimal? OuterTemperature,
    decimal? OuterHumidity,
    DateTime RecordedAt,
    ConditionsReadingSource Source);
```

- [ ] **Step 3: Create IConditionsReadingProvider**

Create `backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/IConditionsReadingProvider.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Manufacture.Conditions;

public interface IConditionsReadingProvider
{
    Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Create ManufactureOrderConditionsReading entity**

Create `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Conditions;

namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderConditionsReading
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public ManufactureOrder ManufactureOrder { get; set; } = null!;
    public ManufactureOrderState Stage { get; set; }
    public decimal? InnerTemperature { get; set; }
    public decimal? InnerHumidity { get; set; }
    public decimal? OuterTemperature { get; set; }
    public decimal? OuterHumidity { get; set; }
    public DateTime RecordedAt { get; set; }
    public ConditionsReadingSource Source { get; set; }
}
```

- [ ] **Step 5: Add navigation to ManufactureOrder**

In `backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs`, add to the Collections section (after `public List<ManufactureOrderNote> Notes { get; set; } = new();`):

```csharp
public List<ManufactureOrderConditionsReading> ConditionsReadings { get; set; } = new();
```

- [ ] **Step 6: Build to verify no compilation errors**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: Build succeeded with 0 error(s).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Conditions/ \
        backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrderConditionsReading.cs \
        backend/src/Anela.Heblo.Domain/Features/Manufacture/ManufactureOrder.cs
git commit -m "feat(manufacture): add IConditionsReadingProvider port and ManufactureOrderConditionsReading entity"
```

---

## Task 2: EF configuration and migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs`

- [ ] **Step 1: Create EF configuration for ConditionsReading**

Create `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConditionsReadingConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Anela.Heblo.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Manufacture;

public class ManufactureOrderConditionsReadingConfiguration : IEntityTypeConfiguration<ManufactureOrderConditionsReading>
{
    public void Configure(EntityTypeBuilder<ManufactureOrderConditionsReading> builder)
    {
        builder.ToTable("ManufactureOrderConditionsReadings", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Stage)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.InnerTemperature)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.InnerHumidity)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.OuterTemperature)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.OuterHumidity)
            .HasColumnType("numeric(5,2)")
            .IsRequired(false);

        builder.Property(x => x.RecordedAt)
            .IsRequired()
            .AsUtcTimestamp();

        builder.Property(x => x.Source)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(x => x.ManufactureOrderId)
            .HasDatabaseName("IX_ManufactureOrderConditionsReadings_ManufactureOrderId");

        builder.HasIndex(x => new { x.ManufactureOrderId, x.Stage })
            .IsUnique()
            .HasDatabaseName("IX_ManufactureOrderConditionsReadings_ManufactureOrderId_Stage");
    }
}
```

- [ ] **Step 2: Add navigation configuration to ManufactureOrderConfiguration**

In `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderConfiguration.cs`, add inside `Configure` after the existing `builder.HasMany(x => x.Notes)` block:

```csharp
builder.HasMany(x => x.ConditionsReadings)
    .WithOne(x => x.ManufactureOrder)
    .HasForeignKey(x => x.ManufactureOrderId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3: Include ConditionsReadings in the detail query**

In `backend/src/Anela.Heblo.Persistence/Manufacture/ManufactureOrderRepository.cs`, in `GetOrderByIdAsync` add `.Include(x => x.ConditionsReadings)` after the existing includes:

The method currently reads:
```csharp
return await _context.ManufactureOrders
    .Include(x => x.SemiProduct)
    .Include(x => x.Products)
    .Include(x => x.Notes.OrderByDescending(n => n.CreatedAt))
    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
```

Change it to:
```csharp
return await _context.ManufactureOrders
    .Include(x => x.SemiProduct)
    .Include(x => x.Products)
    .Include(x => x.Notes.OrderByDescending(n => n.CreatedAt))
    .Include(x => x.ConditionsReadings)
    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
```

- [ ] **Step 4: Generate EF migration**

```bash
cd backend
dotnet ef migrations add AddManufactureOrderConditionsReadings \
    --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
    --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 5: Apply migration to dev database**

```bash
dotnet ef database update \
    --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
    --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: `Done.`

- [ ] **Step 6: Verify table and index in DB**

Connect to the dev PostgreSQL database and confirm:
```sql
\d "public"."ManufactureOrderConditionsReadings"
\di "public"."IX_ManufactureOrderConditionsReadings*"
```

Expected: table with columns Id, ManufactureOrderId, Stage, InnerTemperature, InnerHumidity, OuterTemperature, OuterHumidity, RecordedAt, Source. Two indexes visible.

- [ ] **Step 7: Build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```

Expected: Build succeeded.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Manufacture/ \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(manufacture): add ManufactureOrderConditionsReadings table, EF config and migration"
```

---

## Task 3: HomeAssistant adapter — test project, implementation, DI wiring

**Files:**
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`
- Create: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj`
- Create: `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsReadingProviderTests.cs`
- Modify: `backend/src/Anela.Heblo.API/Program.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Create the adapter project**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/Anela.Heblo.Adapters.HomeAssistant.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Anela.Heblo.Adapters.HomeAssistant</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Anela.Heblo.Domain\Anela.Heblo.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create HomeAssistantSettings**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantSettings.cs`:

```csharp
namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantSettings
{
    public static string ConfigurationKey => "HomeAssistant";

    public string BaseUrl { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string InnerTemperatureEntityId { get; set; } = null!;
    public string InnerHumidityEntityId { get; set; } = null!;
    public string OuterTemperatureEntityId { get; set; } = null!;
    public string OuterHumidityEntityId { get; set; } = null!;
    public int RequestTimeoutSeconds { get; set; } = 3;
}
```

- [ ] **Step 3: Create the test project**

Create `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/Anela.Heblo.Adapters.HomeAssistant.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Adapters\Anela.Heblo.Adapters.HomeAssistant\Anela.Heblo.Adapters.HomeAssistant.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Write failing tests for the provider**

Create `backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/HomeAssistantConditionsReadingProviderTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Anela.Heblo.Adapters.HomeAssistant;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Adapters.HomeAssistant.Tests;

public class HomeAssistantConditionsReadingProviderTests
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HomeAssistantSettings _settings;

    public HomeAssistantConditionsReadingProviderTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _settings = new HomeAssistantSettings
        {
            BaseUrl = "http://ha.test:8123",
            AccessToken = "test-token",
            InnerTemperatureEntityId = "sensor.inner_temp",
            InnerHumidityEntityId = "sensor.inner_humidity",
            OuterTemperatureEntityId = "sensor.outer_temp",
            OuterHumidityEntityId = "sensor.outer_humidity",
            RequestTimeoutSeconds = 5,
        };
    }

    private HomeAssistantConditionsReadingProvider CreateProvider()
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri(_settings.BaseUrl),
            Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds),
        };
        var options = Options.Create(_settings);
        return new HomeAssistantConditionsReadingProvider(httpClient, options, NullLogger<HomeAssistantConditionsReadingProvider>.Instance);
    }

    private void SetupSensorResponse(string entityId, string stateValue, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(new { state = stateValue, entity_id = entityId });
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery.Contains(entityId)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private void SetupSensorFailure(string entityId, HttpStatusCode status)
    {
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.PathAndQuery.Contains(entityId)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent("error"),
            });
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_AllSensorsReturnNumericValues_ReturnsLiveSourceWithAllValues()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Live);
        result.InnerTemperature.Should().Be(21.5m);
        result.InnerHumidity.Should().Be(55.0m);
        result.OuterTemperature.Should().Be(18.2m);
        result.OuterHumidity.Should().Be(72.3m);
        result.RecordedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturns404_ReturnsPartialSourceWithNullForThatSensor()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "21.5");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.NotFound);
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().Be(21.5m);
        result.InnerHumidity.Should().Be(55.0m);
        result.OuterTemperature.Should().BeNull();
        result.OuterHumidity.Should().Be(72.3m);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_AllSensorsReturn500_ReturnsUnavailableWithAllNulls()
    {
        // Arrange
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.InternalServerError);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.InternalServerError);
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
        result.InnerTemperature.Should().BeNull();
        result.InnerHumidity.Should().BeNull();
        result.OuterTemperature.Should().BeNull();
        result.OuterHumidity.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturnsUnavailableState_ThatValueIsNull()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "unavailable");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().BeNull();
        result.InnerHumidity.Should().Be(55.0m);
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturnsUnknownState_ThatValueIsNull()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "unknown");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_OneSensorReturnsNonNumericState_ThatValueIsNull()
    {
        // Arrange
        SetupSensorResponse("sensor.inner_temp", "error_text");
        SetupSensorResponse("sensor.inner_humidity", "55.0");
        SetupSensorResponse("sensor.outer_temp", "18.2");
        SetupSensorResponse("sensor.outer_humidity", "72.3");
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Partial);
        result.InnerTemperature.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_401Unauthorized_ReturnsUnavailable()
    {
        // Arrange
        SetupSensorFailure("sensor.inner_temp", HttpStatusCode.Unauthorized);
        SetupSensorFailure("sensor.inner_humidity", HttpStatusCode.Unauthorized);
        SetupSensorFailure("sensor.outer_temp", HttpStatusCode.Unauthorized);
        SetupSensorFailure("sensor.outer_humidity", HttpStatusCode.Unauthorized);
        var provider = CreateProvider();

        // Act
        var result = await provider.GetCurrentSnapshotAsync(CancellationToken.None);

        // Assert
        result.Source.Should().Be(ConditionsReadingSource.Unavailable);
        result.InnerTemperature.Should().BeNull();
        result.OuterTemperature.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSnapshotAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = CreateProvider();

        // Act
        var act = async () => await provider.GetCurrentSnapshotAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 5: Run tests — all should FAIL (class doesn't exist yet)**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/ --no-build 2>&1 | tail -5
```

Expected: Build errors because `HomeAssistantConditionsReadingProvider` does not exist yet. This confirms the tests are real.

Actually run the build to see the error:

```bash
dotnet build backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/
```

Expected: Error CS0246 — type or namespace `HomeAssistantConditionsReadingProvider` not found.

- [ ] **Step 6: Implement HomeAssistantConditionsReadingProvider**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantConditionsReadingProvider.cs`:

```csharp
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.HomeAssistant;

public class HomeAssistantConditionsReadingProvider : IConditionsReadingProvider
{
    private readonly HttpClient _httpClient;
    private readonly HomeAssistantSettings _settings;
    private readonly ILogger<HomeAssistantConditionsReadingProvider> _logger;

    public HomeAssistantConditionsReadingProvider(
        HttpClient httpClient,
        IOptions<HomeAssistantSettings> options,
        ILogger<HomeAssistantConditionsReadingProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<ConditionsSnapshot> GetCurrentSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tasks = new[]
        {
            FetchSensorValueAsync(_settings.InnerTemperatureEntityId, ct),
            FetchSensorValueAsync(_settings.InnerHumidityEntityId, ct),
            FetchSensorValueAsync(_settings.OuterTemperatureEntityId, ct),
            FetchSensorValueAsync(_settings.OuterHumidityEntityId, ct),
        };

        var values = await Task.WhenAll(tasks);

        var innerTemp = values[0];
        var innerHumidity = values[1];
        var outerTemp = values[2];
        var outerHumidity = values[3];

        var nonNullCount = values.Count(v => v.HasValue);
        var source = nonNullCount == 4 ? ConditionsReadingSource.Live
            : nonNullCount == 0 ? ConditionsReadingSource.Unavailable
            : ConditionsReadingSource.Partial;

        return new ConditionsSnapshot(
            InnerTemperature: innerTemp,
            InnerHumidity: innerHumidity,
            OuterTemperature: outerTemp,
            OuterHumidity: outerHumidity,
            RecordedAt: DateTime.UtcNow,
            Source: source);
    }

    private async Task<decimal?> FetchSensorValueAsync(string entityId, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/states/{entityId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "HomeAssistant returned {StatusCode} for entity {EntityId}",
                    response.StatusCode, entityId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("state", out var stateProp))
            {
                _logger.LogWarning("HomeAssistant response for {EntityId} has no 'state' field", entityId);
                return null;
            }

            var stateStr = stateProp.GetString();
            if (string.IsNullOrEmpty(stateStr) ||
                stateStr.Equals("unavailable", StringComparison.OrdinalIgnoreCase) ||
                stateStr.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("HomeAssistant entity {EntityId} has non-numeric state: {State}", entityId, stateStr);
                return null;
            }

            if (!decimal.TryParse(stateStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                _logger.LogWarning("HomeAssistant entity {EntityId} returned unparseable state: {State}", entityId, stateStr);
                return null;
            }

            return value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch HomeAssistant entity {EntityId}", entityId);
            return null;
        }
    }
}
```

- [ ] **Step 7: Create service collection extension**

Create `backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/HomeAssistantAdapterServiceCollectionExtensions.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.HomeAssistant;

public static class HomeAssistantAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddHomeAssistantAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(HomeAssistantSettings.ConfigurationKey);
        services.Configure<HomeAssistantSettings>(section);

        var settings = section.Get<HomeAssistantSettings>() ?? new HomeAssistantSettings();

        services.AddHttpClient<HomeAssistantConditionsReadingProvider>(client =>
        {
            client.BaseAddress = new Uri(settings.BaseUrl ?? "http://localhost:8123");
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.AccessToken);
        });

        services.AddTransient<IConditionsReadingProvider>(
            sp => sp.GetRequiredService<HomeAssistantConditionsReadingProvider>());

        return services;
    }
}
```

- [ ] **Step 8: Run the tests — all should now PASS**

```bash
dotnet test backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/
```

Expected: Test run succeeded. 8 passed.

- [ ] **Step 9: Register adapter in Program.cs**

In `backend/src/Anela.Heblo.API/Program.cs`, add after the last `builder.Services.Add*Adapter` line (e.g., after `AddSendGridAdapter`):

```csharp
builder.Services.AddHomeAssistantAdapter(builder.Configuration);
```

Also add the using at the top if needed: `using Anela.Heblo.Adapters.HomeAssistant;`

Also add the adapter project reference to `backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`:
```xml
<ProjectReference Include="..\Adapters\Anela.Heblo.Adapters.HomeAssistant\Anela.Heblo.Adapters.HomeAssistant.csproj" />
```

- [ ] **Step 10: Add placeholder config in appsettings.json**

In `backend/src/Anela.Heblo.API/appsettings.json`, add the `HomeAssistant` section (place it near the other adapter configs):

```json
"HomeAssistant": {
  "BaseUrl": "-- stored in secrets.json --",
  "AccessToken": "-- stored in secrets.json --",
  "InnerTemperatureEntityId": "sensor.workshop_inner_temperature",
  "InnerHumidityEntityId": "sensor.workshop_inner_humidity",
  "OuterTemperatureEntityId": "sensor.workshop_outer_temperature",
  "OuterHumidityEntityId": "sensor.workshop_outer_humidity",
  "RequestTimeoutSeconds": 3
}
```

Also add to `secrets.json` (dev only):
```json
"HomeAssistant:BaseUrl": "https://your-ha-instance:8123",
"HomeAssistant:AccessToken": "your-long-lived-access-token"
```

- [ ] **Step 11: Build the full API project**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: Build succeeded.

- [ ] **Step 12: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.HomeAssistant/ \
        backend/test/Anela.Heblo.Adapters.HomeAssistant.Tests/ \
        backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj \
        backend/src/Anela.Heblo.API/Program.cs \
        backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(manufacture): add HomeAssistant adapter with IConditionsReadingProvider implementation"
```

---

## Task 4: Inject IConditionsReadingProvider into UpdateManufactureOrderStatusHandler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs`

The hook point is `UpdateManufactureOrderStatusHandler`. It loads the tracked `ManufactureOrder`, mutates state fields, then calls `await _repository.UpdateOrderAsync(order, cancellationToken)` at line 119. We add conditions capture between those two operations.

Current constructor (4 params): `(IManufactureOrderRepository, TimeProvider, ILogger<…>, IHttpContextAccessor)`. After change: same 4 plus `IConditionsReadingProvider` as 5th.

- [ ] **Step 1: Write failing tests for conditions capture in the status handler**

Create `backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs`:

```csharp
using System.Security.Claims;
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrderStatus;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class UpdateManufactureOrderStatusHandlerConditionsTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<IConditionsReadingProvider> _conditionsProviderMock;
    private readonly Mock<ILogger<UpdateManufactureOrderStatusHandler>> _loggerMock;

    public UpdateManufactureOrderStatusHandlerConditionsTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _loggerMock = new Mock<ILogger<UpdateManufactureOrderStatusHandler>>();
        _conditionsProviderMock = new Mock<IConditionsReadingProvider>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        var claims = new List<Claim> { new(ClaimTypes.Name, "Test User") };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var httpContext = new Mock<HttpContext>();
        httpContext.Setup(x => x.User).Returns(principal);
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext.Object);
    }

    private UpdateManufactureOrderStatusHandler CreateHandler() =>
        new UpdateManufactureOrderStatusHandler(
            _repositoryMock.Object,
            TimeProvider.System,
            _loggerMock.Object,
            _httpContextAccessorMock.Object,
            _conditionsProviderMock.Object);

    private ManufactureOrder CreateOrderInState(ManufactureOrderState state) =>
        new ManufactureOrder
        {
            Id = 1,
            OrderNumber = "MO-2026-001",
            State = state,
            StateChangedAt = DateTime.UtcNow,
            StateChangedByUser = "previous-user",
            CreatedByUser = "creator",
            CreatedDate = DateTime.UtcNow.AddDays(-1),
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
        };

    [Fact]
    public async Task Handle_TransitionToSemiProductManufactured_CapturesConditionsReadingWithLiveSource()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var snapshot = new ConditionsSnapshot(
            InnerTemperature: 21.5m,
            InnerHumidity: 55.0m,
            OuterTemperature: 18.2m,
            OuterHumidity: 72.3m,
            RecordedAt: DateTime.UtcNow,
            Source: ConditionsReadingSource.Live);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test reason",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        order.ConditionsReadings.Should().HaveCount(1);
        var reading = order.ConditionsReadings.Single();
        reading.Stage.Should().Be(ManufactureOrderState.SemiProductManufactured);
        reading.InnerTemperature.Should().Be(21.5m);
        reading.InnerHumidity.Should().Be(55.0m);
        reading.OuterTemperature.Should().Be(18.2m);
        reading.OuterHumidity.Should().Be(72.3m);
        reading.Source.Should().Be(ConditionsReadingSource.Live);
    }

    [Fact]
    public async Task Handle_TransitionToCompleted_CapturesConditionsReadingWithCorrectStage()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.SemiProductManufactured);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var snapshot = new ConditionsSnapshot(21m, 50m, 15m, 65m, DateTime.UtcNow, ConditionsReadingSource.Live);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.Completed,
            ChangeReason = "Done",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reading = order.ConditionsReadings.Single();
        reading.Stage.Should().Be(ManufactureOrderState.Completed);
    }

    [Fact]
    public async Task Handle_ConditionsProviderReturnsUnavailable_ReadingPersistedWithNullValuesTransitionSucceeds()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var snapshot = new ConditionsSnapshot(null, null, null, null, DateTime.UtcNow, ConditionsReadingSource.Unavailable);
        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var reading = order.ConditionsReadings.Single();
        reading.InnerTemperature.Should().BeNull();
        reading.Source.Should().Be(ConditionsReadingSource.Unavailable);
    }

    [Fact]
    public async Task Handle_ConditionsProviderThrows_ReadingPersistedAsUnavailableTransitionSucceeds()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        _conditionsProviderMock.Setup(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.SemiProductManufactured,
            ChangeReason = "Test",
        };
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        order.ConditionsReadings.Should().HaveCount(1);
        var reading = order.ConditionsReadings.Single();
        reading.Source.Should().Be(ConditionsReadingSource.Unavailable);
        reading.InnerTemperature.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TransitionToDraft_DoesNotCaptureConditionsReading()
    {
        // Arrange
        var order = CreateOrderInState(ManufactureOrderState.Planned);
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        _repositoryMock.Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new UpdateManufactureOrderStatusRequest
        {
            Id = 1,
            NewState = ManufactureOrderState.Draft,
            ChangeReason = "Reset",
        };
        var handler = CreateHandler();

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert — provider never called, no reading added
        _conditionsProviderMock.Verify(x => x.GetCurrentSnapshotAsync(It.IsAny<CancellationToken>()), Times.Never);
        order.ConditionsReadings.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests — they should FAIL (provider not in constructor)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~UpdateManufactureOrderStatusHandlerConditionsTests"
```

Expected: Build error — constructor has 4 parameters, test provides 5.

- [ ] **Step 3: Inject IConditionsReadingProvider into the handler**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/UpdateManufactureOrderStatusHandler.cs`:

1. Add using: `using Anela.Heblo.Domain.Features.Manufacture.Conditions;`
2. Add field: `private readonly IConditionsReadingProvider _conditionsProvider;`
3. Add `IConditionsReadingProvider conditionsProvider` as the 5th constructor parameter and assign it.
4. Locate `order.State = request.NewState;` and the subsequent `await _repository.UpdateOrderAsync(order, cancellationToken);`. Insert conditions capture between them:

```csharp
if (request.NewState is ManufactureOrderState.SemiProductManufactured or ManufactureOrderState.Completed)
{
    var reading = await CaptureConditionsReadingAsync(order, request.NewState, cancellationToken);
    order.ConditionsReadings.Add(reading);
}
```

Add the helper method:

```csharp
private async Task<ManufactureOrderConditionsReading> CaptureConditionsReadingAsync(
    ManufactureOrder order,
    ManufactureOrderState stage,
    CancellationToken ct)
{
    try
    {
        var snapshot = await _conditionsProvider.GetCurrentSnapshotAsync(ct);
        return new ManufactureOrderConditionsReading
        {
            ManufactureOrderId = order.Id,
            Stage = stage,
            InnerTemperature = snapshot.InnerTemperature,
            InnerHumidity = snapshot.InnerHumidity,
            OuterTemperature = snapshot.OuterTemperature,
            OuterHumidity = snapshot.OuterHumidity,
            RecordedAt = snapshot.RecordedAt,
            Source = snapshot.Source,
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to capture conditions reading for order {OrderId}, stage {Stage}", order.Id, stage);
        return new ManufactureOrderConditionsReading
        {
            ManufactureOrderId = order.Id,
            Stage = stage,
            RecordedAt = DateTime.UtcNow,
            Source = ConditionsReadingSource.Unavailable,
        };
    }
}
```

The persistence call is `await _repository.UpdateOrderAsync(order, cancellationToken)` on line 119 of the handler. Insert the conditions capture **between** `order.State = request.NewState;` and that `UpdateOrderAsync` call.

- [ ] **Step 4: Run tests — all should PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~UpdateManufactureOrderStatusHandlerConditionsTests"
```

Expected: 5 passed.

- [ ] **Step 5: Run full manufacture test suite to confirm no regressions**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~Manufacture"
```

Expected: All pass. The existing `UpdateManufactureOrderStatusHandlerTests` will also need `IConditionsReadingProvider` mocked — mirror the pattern from the new test file (use `Mock<IConditionsReadingProvider>` and pass `.Object` as the 5th constructor arg with a default `ReturnsAsync` for `Live` snapshot).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrderStatus/ \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerConditionsTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Manufacture/UpdateManufactureOrderStatusHandlerTests.cs
git commit -m "feat(manufacture): capture ambient conditions reading on state transitions to SemiProductManufactured and Completed"
```

---

## Task 5: API — extend GetManufactureOrder response with conditions readings

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrders/GetManufactureOrdersResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs`

- [ ] **Step 1: Add ConditionsReadings to ManufactureOrderDto and add the reading DTO**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOrders/GetManufactureOrdersResponse.cs`:

Add to `ManufactureOrderDto` class (after `public List<ManufactureOrderNoteDto> Notes { get; set; } = new();`):

```csharp
public List<ManufactureOrderConditionsReadingDto> ConditionsReadings { get; set; } = new();
```

Add a new class at the end of the file (before the closing namespace brace if any, or just below):

```csharp
public class ManufactureOrderConditionsReadingDto
{
    public int Id { get; set; }
    public ManufactureOrderState Stage { get; set; }
    public decimal? InnerTemperature { get; set; }
    public decimal? InnerHumidity { get; set; }
    public decimal? OuterTemperature { get; set; }
    public decimal? OuterHumidity { get; set; }
    public DateTime RecordedAt { get; set; }
    public int Source { get; set; }
}
```

Note: DTO is a class, not a record (project rule — OpenAPI generators mishandle record parameter order).

- [ ] **Step 2: Add AutoMapper mapping**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureOrderMappingProfile.cs`, add inside the constructor:

```csharp
CreateMap<ManufactureOrderConditionsReading, ManufactureOrderConditionsReadingDto>()
    .ForMember(dest => dest.Source, opt => opt.MapFrom(src => (int)src.Source));
```

Also add the using at the top of the file:
```csharp
using Anela.Heblo.Domain.Features.Manufacture.Conditions;
```

- [ ] **Step 3: Build to verify mapping and DTO compile**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded.

- [ ] **Step 4: Run existing GetManufactureOrder handler tests to confirm they still pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~GetManufactureOrderHandlerTests"
```

Expected: All pass (the AutoMapper mock in the test returns a pre-configured DTO, so adding a new mapped field doesn't break existing tests).

- [ ] **Step 5: Regenerate TypeScript OpenAPI client**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: Build successful. The generated file `frontend/src/api/generated/api-client.ts` now contains `ManufactureOrderConditionsReadingDto` and `conditionsReadings` on `ManufactureOrderDto`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ \
        frontend/src/api/generated/
git commit -m "feat(manufacture): add ConditionsReadings to ManufactureOrderDto with AutoMapper mapping"
```

---

## Task 6: Frontend — ConditionsReadingsSection component

**Files:**
- Create: `frontend/src/components/manufacture/detail/__tests__/ConditionsReadingsSection.test.tsx`
- Create: `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx`
- Modify: `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`

- [ ] **Step 1: Write failing tests for ConditionsReadingsSection**

Create `frontend/src/components/manufacture/detail/__tests__/ConditionsReadingsSection.test.tsx`:

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import "@testing-library/jest-dom";
import ConditionsReadingsSection from "../ConditionsReadingsSection";
import { ManufactureOrderConditionsReadingDto, ManufactureOrderState } from "../../../../api/generated/api-client";

const mockReading = (
  stage: ManufactureOrderState,
  overrides: Partial<ManufactureOrderConditionsReadingDto> = {}
): ManufactureOrderConditionsReadingDto => ({
  id: 1,
  stage,
  innerTemperature: 21.5,
  innerHumidity: 55.0,
  outerTemperature: 18.2,
  outerHumidity: 72.3,
  recordedAt: "2026-05-06T10:30:00Z",
  source: 1, // Live
  ...overrides,
});

describe("ConditionsReadingsSection", () => {
  it("renders section heading", () => {
    render(<ConditionsReadingsSection readings={[]} />);
    expect(screen.getByText(/Podmínky/i)).toBeInTheDocument();
  });

  it("renders em-dashes when no readings provided", () => {
    render(<ConditionsReadingsSection readings={[]} />);
    const dashes = screen.getAllByText("—");
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  it("renders temperature and humidity values when readings present", () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText("21.5")).toBeInTheDocument();
    expect(screen.getByText("55.0")).toBeInTheDocument();
    expect(screen.getByText("18.2")).toBeInTheDocument();
    expect(screen.getByText("72.3")).toBeInTheDocument();
  });

  it("renders null cell as em-dash", () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured, { innerTemperature: null }),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("shows HA nedostupný badge when source is Unavailable (3)", () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured, { source: 3 }),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText(/HA nedostupný/i)).toBeInTheDocument();
  });

  it("shows Částečné badge when source is Partial (2)", () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured, { source: 2 }),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText(/Částečné/i)).toBeInTheDocument();
  });

  it("shows no badge when source is Live (1)", () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured, { source: 1 }),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.queryByText(/HA nedostupný/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Částečné/i)).not.toBeInTheDocument();
  });

  it("renders both stages when two readings present", () => {
    const readings = [
      mockReading(ManufactureOrderState.SemiProductManufactured),
      mockReading(ManufactureOrderState.Completed, { id: 2, innerTemperature: 22.0 }),
    ];
    render(<ConditionsReadingsSection readings={readings} />);
    expect(screen.getByText("22.0")).toBeInTheDocument();
    expect(screen.getByText("21.5")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run tests — should FAIL (component doesn't exist)**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="ConditionsReadingsSection" 2>&1 | tail -10
```

Expected: Test file found, fails because `ConditionsReadingsSection` module cannot be resolved.

- [ ] **Step 3: Implement ConditionsReadingsSection**

Create `frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx`:

```tsx
import React from "react";
import {
  ManufactureOrderConditionsReadingDto,
  ManufactureOrderState,
} from "../../../api/generated/api-client";

const STAGE_LABELS: Partial<Record<ManufactureOrderState, string>> = {
  [ManufactureOrderState.SemiProductManufactured]: "Polotovar",
  [ManufactureOrderState.Completed]: "Dokončeno",
};

const ALL_STAGES = [
  ManufactureOrderState.SemiProductManufactured,
  ManufactureOrderState.Completed,
];

const SourceBadge: React.FC<{ source: number }> = ({ source }) => {
  if (source === 3) {
    return (
      <span className="ml-1 rounded bg-red-100 px-1.5 py-0.5 text-xs font-medium text-red-700">
        HA nedostupný
      </span>
    );
  }
  if (source === 2) {
    return (
      <span className="ml-1 rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-700">
        Částečné
      </span>
    );
  }
  return null;
};

const ValueCell: React.FC<{ value: number | null | undefined }> = ({ value }) =>
  value == null ? <span>—</span> : <span>{value.toFixed(1)}</span>;

interface Props {
  readings: ManufactureOrderConditionsReadingDto[];
}

const ConditionsReadingsSection: React.FC<Props> = ({ readings }) => {
  const byStage = new Map(readings.map((r) => [r.stage, r]));

  return (
    <div className="mt-4">
      <h3 className="mb-2 text-sm font-semibold text-gray-700">Podmínky výroby</h3>
      <table className="w-full text-sm border-collapse">
        <thead>
          <tr className="border-b text-left text-xs font-medium text-gray-500 uppercase">
            <th className="pb-1 pr-3">Fáze</th>
            <th className="pb-1 pr-3">T vnitřní (°C)</th>
            <th className="pb-1 pr-3">RH vnitřní (%)</th>
            <th className="pb-1 pr-3">T venkovní (°C)</th>
            <th className="pb-1 pr-3">RH venkovní (%)</th>
            <th className="pb-1">Zaznamenáno</th>
          </tr>
        </thead>
        <tbody>
          {ALL_STAGES.map((stage) => {
            const reading = byStage.get(stage);
            return (
              <tr key={stage} className="border-b last:border-0">
                <td className="py-1.5 pr-3 font-medium text-gray-700">
                  {STAGE_LABELS[stage]}
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.innerTemperature} />
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.innerHumidity} />
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.outerTemperature} />
                </td>
                <td className="py-1.5 pr-3">
                  <ValueCell value={reading?.outerHumidity} />
                </td>
                <td className="py-1.5">
                  {reading ? (
                    <>
                      <span>{new Date(reading.recordedAt).toLocaleString("cs-CZ")}</span>
                      <SourceBadge source={reading.source} />
                    </>
                  ) : (
                    <span>—</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};

export default ConditionsReadingsSection;
```

- [ ] **Step 4: Run tests — all should PASS**

```bash
cd frontend && npx react-scripts test --watchAll=false --testPathPattern="ConditionsReadingsSection" 2>&1 | tail -10
```

Expected: Test Suites: 1 passed, 1 total. Tests: 8 passed.

- [ ] **Step 5: Wire ConditionsReadingsSection into ManufactureOrderDetail.tsx**

In `frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx`:

1. Add import at the top (with other detail imports):
```tsx
import ConditionsReadingsSection from "../detail/ConditionsReadingsSection";
```

2. Find where `<BasicInfoSection />` is rendered in the `"info"` tab content. After the `<SemiProductSection />` component (or at the bottom of the info tab panel, before the closing tag), add:

```tsx
<ConditionsReadingsSection
  readings={order?.conditionsReadings ?? []}
/>
```

Replace `order` with whatever the local variable name is for the fetched order data (typically `order.data` or `data?.order` depending on the query hook — read the existing usage in the file).

- [ ] **Step 6: Build frontend to verify TypeScript compiles**

```bash
cd frontend && npm run build 2>&1 | tail -20
```

Expected: Compiled successfully.

- [ ] **Step 7: Run lint**

```bash
cd frontend && npm run lint 2>&1 | tail -10
```

Expected: No warnings or errors.

- [ ] **Step 8: Commit**

```bash
git add frontend/src/components/manufacture/detail/ConditionsReadingsSection.tsx \
        frontend/src/components/manufacture/detail/__tests__/ConditionsReadingsSection.test.tsx \
        frontend/src/components/manufacture/pages/ManufactureOrderDetail.tsx
git commit -m "feat(manufacture): add ConditionsReadingsSection component to order detail page"
```

---

## Task 7: Final verification

- [ ] **Step 1: Run all backend tests**

```bash
dotnet test backend/ 2>&1 | tail -15
```

Expected: All test suites pass.

- [ ] **Step 2: Run all frontend tests**

```bash
cd frontend && npx react-scripts test --watchAll=false 2>&1 | tail -10
```

Expected: All suites pass.

- [ ] **Step 3: Full backend build and format**

```bash
dotnet build backend/ && dotnet format backend/ --verify-no-changes
```

Expected: Build succeeded. Format: no changes needed.

- [ ] **Step 4: Manual end-to-end smoke test**

1. Configure `secrets.json` with real HomeAssistant URL and access token for the four sensor entity IDs.
2. Start the app (`docker compose up` or `dotnet run`).
3. Open a manufacture order in Draft/Planned state. Advance to `SemiProductManufactured`. On the detail page, the Podmínky výroby table should show one row with current sensor values and a timestamp.
4. Advance to `Completed`. The table should show two rows.
5. Break the access token (e.g., set to `"invalid"` in secrets). Create a new order and advance through both stages. Both rows should show `—` cells and the red `HA nedostupný` badge. Transitions should still succeed.
6. Restore the valid token.

- [ ] **Step 5: Final commit (if any cleanup needed)**

```bash
git add -p  # stage only remaining changes
git commit -m "chore(manufacture): final cleanup and verification"
```
