# Expedition Gifts Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a configurable gift badge to expedition picking-list PDFs — the badge prints when an order's CZK total meets a threshold — and expose the settings in a new "Nastavení expedice" tabbed page that replaces the existing "Chlazení" page.

**Architecture:** Clean Architecture with MediatR: domain entity → EF Core persistence → application use cases → API controller → frontend form. The expedition pipeline (`ShoptetApiExpeditionListSource`) loads the gift config once per run and stamps `GiftBadgeText` onto each eligible order, which the QuestPDF document renderer reads to print the badge.

**Tech Stack:** .NET 8, EF Core with Npgsql, MediatR, FluentValidation, QuestPDF, SkiaSharp; React 18, React Router v6, TanStack Query, Jest + React Testing Library.

---

## File map

### Backend — New files
| File | Purpose |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/GiftSetting.cs` | Domain entity (singleton, Id=1) |
| `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/IGiftSettingRepository.cs` | Repository interface |
| `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingConfiguration.cs` | EF Core config |
| `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingRepository.cs` | EF Core implementation |
| `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddGiftSettings.cs` | Migration (generated, applied manually) |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs` | DI registration |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/Dto/GiftSettingDto.cs` | DTO class |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs` | Query |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs` | Handler |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs` | Command |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs` | Response |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs` | Handler |
| `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs` | FluentValidation (unit-test only, not registered in DI) |
| `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs` | GET + PUT endpoints |
| `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs` | Handler unit tests |
| `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs` | Handler unit tests |
| `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs` | Validator unit tests |

### Backend — Modified files
| File | Why |
|---|---|
| `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` | Add `DbSet<GiftSetting> GiftSettings` |
| `backend/src/Anela.Heblo.Application/ApplicationModule.cs` | Call `services.AddGiftSettingsModule()` |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs` | Add `GiftBadgeText` to `ExpeditionOrder` |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` | Inject `IGiftSettingRepository`, thread price/currency, compute `GiftBadgeText` |
| `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs` | Render gift badge + add `GenerateGiftIcon()` |
| `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs` | Add gift badge tests |

### Frontend — New files
| File | Purpose |
|---|---|
| `frontend/src/pages/customer/ExpeditionSettingsPage.tsx` | Tabbed shell page (replaces CoolingPage) |
| `frontend/src/components/customer/expeditionSettings/CoolingTab.tsx` | Extracted cooling tab body |
| `frontend/src/components/customer/expeditionSettings/GiftsTab.tsx` | New gifts settings form |
| `frontend/src/api/hooks/useGiftSetting.ts` | React Query GET + PUT hooks |
| `frontend/src/components/customer/expeditionSettings/__tests__/GiftsTab.test.tsx` | Component tests |

### Frontend — Modified files
| File | Why |
|---|---|
| `frontend/src/components/Layout/Sidebar.tsx` | Rename entry, update href |
| `frontend/src/App.tsx` | Add new route + redirect, update import |

### Frontend — Deleted files
| File | Why |
|---|---|
| `frontend/src/pages/customer/CoolingPage.tsx` | Replaced by `ExpeditionSettingsPage.tsx` |

---

## Task 1: Domain entity + repository interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/GiftSetting.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/IGiftSettingRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs` (written later, but verify domain is sound first)

- [ ] **Step 1: Create domain directory and entity**

Create `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/GiftSetting.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.GiftSettings;

public class GiftSetting
{
    public int Id { get; private set; }
    public bool IsEnabled { get; private set; }
    public decimal ThresholdCzk { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; private set; }
    public string? ModifiedBy { get; private set; }

    private GiftSetting() { }

    public static GiftSetting CreateDefault() => new() { Id = 1 };

    public GiftSetting(bool isEnabled, decimal thresholdCzk, string text, string modifiedBy)
    {
        Id = 1;
        IsEnabled = isEnabled;
        ThresholdCzk = thresholdCzk;
        Text = text;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }

    internal void Update(bool isEnabled, decimal thresholdCzk, string text, string modifiedBy)
    {
        IsEnabled = isEnabled;
        ThresholdCzk = thresholdCzk;
        Text = text;
        ModifiedBy = modifiedBy;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 2: Create repository interface**

Create `backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/IGiftSettingRepository.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Logistics.GiftSettings;

public interface IGiftSettingRepository
{
    Task<GiftSetting> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GiftSetting setting, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Verify domain compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --no-incremental -q
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Logistics/GiftSettings/
git commit -m "feat: add GiftSetting domain entity and IGiftSettingRepository"
```

---

## Task 2: Persistence — EF Core config, repository, migration, DbContext, module

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Generate: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddGiftSettings.cs`

- [ ] **Step 1: Create EF Core configuration**

Create `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Logistics.GiftSettings;

public class GiftSettingConfiguration : IEntityTypeConfiguration<GiftSetting>
{
    public void Configure(EntityTypeBuilder<GiftSetting> builder)
    {
        builder.ToTable("GiftSettings", "public");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.IsEnabled).IsRequired();

        builder.Property(e => e.ThresholdCzk)
            .IsRequired()
            .HasColumnType("numeric(18,2)");

        builder.Property(e => e.Text)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ModifiedAt);

        builder.Property(e => e.ModifiedBy).HasMaxLength(256);
    }
}
```

- [ ] **Step 2: Create repository implementation**

Create `backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/GiftSettingRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Logistics.GiftSettings;

public class GiftSettingRepository : IGiftSettingRepository
{
    private readonly ApplicationDbContext _context;

    public GiftSettingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<GiftSetting> GetAsync(CancellationToken cancellationToken = default)
    {
        return await _context.GiftSettings.FirstOrDefaultAsync(cancellationToken)
            ?? GiftSetting.CreateDefault();
    }

    public async Task SaveAsync(GiftSetting setting, CancellationToken cancellationToken = default)
    {
        var existing = await _context.GiftSettings.FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            _context.GiftSettings.Add(setting);
        }
        else
        {
            existing.Update(setting.IsEnabled, setting.ThresholdCzk, setting.Text, setting.ModifiedBy ?? string.Empty);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 3: Register DbSet in ApplicationDbContext**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, add after the `// Carrier Cooling module` section (line ~92):

```csharp
    // Gift Settings module
    public DbSet<GiftSetting> GiftSettings { get; set; } = null!;
```

Also add the using at the top of the file:

```csharp
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
```

- [ ] **Step 4: Verify persistence compiles**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --no-incremental -q
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 5: Generate migration**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
dotnet ef migrations add AddGiftSettings \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API \
  --output-dir Migrations
```

The generated `Up()` method should create a table matching:

```csharp
migrationBuilder.CreateTable(
    name: "GiftSettings",
    schema: "public",
    columns: table => new
    {
        Id = table.Column<int>(type: "integer", nullable: false),
        IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
        ThresholdCzk = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
        Text = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
        ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
        ModifiedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_GiftSettings", x => x.Id);
    });
```

Note: The migration is **generated** but **applied manually** in each environment. Do not run `dotnet ef database update` automatically.

- [ ] **Step 6: Verify solution builds after migration**

```bash
dotnet build backend/Anela.Heblo.sln --no-incremental -q
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Logistics/GiftSettings/
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add GiftSetting persistence — EF config, repository, migration"
```

---

## Task 3: Application — Get use case with tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/Dto/GiftSettingDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.Dto;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class GetGiftSettingHandlerTests
{
    private readonly Mock<IGiftSettingRepository> _repositoryMock = new();
    private readonly GetGiftSettingHandler _sut;

    public GetGiftSettingHandlerTests()
    {
        _sut = new GetGiftSettingHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDefault_WhenNoRowExists()
    {
        _repositoryMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GiftSetting.CreateDefault());

        var result = await _sut.Handle(new GetGiftSettingQuery(), CancellationToken.None);

        result.IsEnabled.Should().BeFalse();
        result.ThresholdCzk.Should().Be(0m);
        result.Text.Should().BeEmpty();
        result.ModifiedAt.Should().BeNull();
        result.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsSavedValues_WhenRowExists()
    {
        var setting = new GiftSetting(true, 1500m, "DÁREK ZDARMA", "user-1");
        _repositoryMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(setting);

        var result = await _sut.Handle(new GetGiftSettingQuery(), CancellationToken.None);

        result.IsEnabled.Should().BeTrue();
        result.ThresholdCzk.Should().Be(1500m);
        result.Text.Should().Be("DÁREK ZDARMA");
        result.ModifiedAt.Should().NotBeNull();
        result.ModifiedBy.Should().Be("user-1");
    }
}
```

- [ ] **Step 2: Run tests to confirm FAIL**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGiftSettingHandlerTests" -q 2>&1 | tail -5
```

Expected: error — `GetGiftSettingHandler` not found.

- [ ] **Step 3: Create DTO**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/Dto/GiftSettingDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.GiftSettings.Dto;

public class GiftSettingDto
{
    public bool IsEnabled { get; set; }
    public decimal ThresholdCzk { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
```

- [ ] **Step 4: Create query**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingQuery.cs`:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.Dto;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;

public class GetGiftSettingQuery : IRequest<GiftSettingDto>
{
}
```

- [ ] **Step 5: Create handler**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/GetGiftSetting/GetGiftSettingHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.Dto;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;

public class GetGiftSettingHandler : IRequestHandler<GetGiftSettingQuery, GiftSettingDto>
{
    private readonly IGiftSettingRepository _repository;

    public GetGiftSettingHandler(IGiftSettingRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<GiftSettingDto> Handle(GetGiftSettingQuery request, CancellationToken cancellationToken)
    {
        var setting = await _repository.GetAsync(cancellationToken);
        return new GiftSettingDto
        {
            IsEnabled = setting.IsEnabled,
            ThresholdCzk = setting.ThresholdCzk,
            Text = setting.Text,
            ModifiedAt = setting.ModifiedAt,
            ModifiedBy = setting.ModifiedBy,
        };
    }
}
```

- [ ] **Step 6: Run tests to confirm PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetGiftSettingHandlerTests" -q 2>&1 | tail -5
```

Expected: Passed: 2, Failed: 0.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GiftSettings/
git add backend/test/Anela.Heblo.Tests/Application/GiftSettings/GetGiftSettingHandlerTests.cs
git commit -m "feat: add GetGiftSetting query handler and DTO"
```

---

## Task 4: Application — Set use case with tests

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class SetGiftSettingHandlerTests
{
    private readonly Mock<IGiftSettingRepository> _repositoryMock = new();
    private readonly SetGiftSettingHandler _sut;

    public SetGiftSettingHandlerTests()
    {
        _sut = new SetGiftSettingHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_SavesSetting_WhenDisabled()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
            ModifiedBy = "user-1",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SavesSetting_WhenEnabledWithValidValues()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = "DÁREK ZDARMA",
            ModifiedBy = "user-1",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeTrue();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithZeroThreshold()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
            ModifiedBy = "user-1",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenEnabledWithEmptyText()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
            ModifiedBy = "user-1",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenTextExceedsMaxLength()
    {
        var command = new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
            ModifiedBy = "user-1",
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.SaveAsync(It.IsAny<GiftSetting>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Write failing validator tests**

Create `backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs`:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Anela.Heblo.Tests.Application.GiftSettings;

public class SetGiftSettingValidatorTests
{
    private readonly SetGiftSettingValidator _validator = new();

    [Fact]
    public void Validator_Passes_WhenDisabled()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = string.Empty,
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_Passes_WhenEnabledWithValidValues()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = "DÁREK ZDARMA",
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validator_Fails_WhenEnabledWithZeroThreshold()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 0,
            Text = "DÁREK ZDARMA",
        });
        result.ShouldHaveValidationErrorFor(x => x.ThresholdCzk);
    }

    [Fact]
    public void Validator_Fails_WhenEnabledWithEmptyText()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = true,
            ThresholdCzk = 1500m,
            Text = string.Empty,
        });
        result.ShouldHaveValidationErrorFor(x => x.Text);
    }

    [Fact]
    public void Validator_Fails_WhenTextExceeds50Chars_EvenWhenDisabled()
    {
        var result = _validator.TestValidate(new SetGiftSettingCommand
        {
            IsEnabled = false,
            ThresholdCzk = 0,
            Text = new string('X', 51),
        });
        result.ShouldHaveValidationErrorFor(x => x.Text);
    }
}
```

- [ ] **Step 3: Run tests to confirm FAIL**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SetGiftSetting" -q 2>&1 | tail -5
```

Expected: Build errors — `SetGiftSettingHandler` not found.

- [ ] **Step 4: Create command**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingCommand.cs`:

```csharp
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public class SetGiftSettingCommand : IRequest<SetGiftSettingResponse>
{
    public bool IsEnabled { get; set; }
    public decimal ThresholdCzk { get; set; }
    public string Text { get; set; } = string.Empty;
    public string ModifiedBy { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Create response**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public class SetGiftSettingResponse : BaseResponse
{
}
```

- [ ] **Step 6: Create handler**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using MediatR;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public class SetGiftSettingHandler : IRequestHandler<SetGiftSettingCommand, SetGiftSettingResponse>
{
    private readonly IGiftSettingRepository _repository;

    public SetGiftSettingHandler(IGiftSettingRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SetGiftSettingResponse> Handle(SetGiftSettingCommand command, CancellationToken cancellationToken)
    {
        if (command.IsEnabled)
        {
            if (command.ThresholdCzk <= 0)
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "ThresholdCzk must be greater than zero when enabled." } },
                };

            if (string.IsNullOrEmpty(command.Text))
                return new SetGiftSettingResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.ValidationError,
                    Params = new Dictionary<string, string> { { "message", "Text is required when enabled." } },
                };
        }

        if (command.Text?.Length > 50)
            return new SetGiftSettingResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string> { { "message", "Text cannot exceed 50 characters." } },
            };

        var setting = new GiftSetting(command.IsEnabled, command.ThresholdCzk, command.Text ?? string.Empty, command.ModifiedBy);
        await _repository.SaveAsync(setting, cancellationToken);
        return new SetGiftSettingResponse();
    }
}
```

- [ ] **Step 7: Create validator (for unit tests only — not registered in DI)**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/SetGiftSettingValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;

public class SetGiftSettingValidator : AbstractValidator<SetGiftSettingCommand>
{
    public SetGiftSettingValidator()
    {
        RuleFor(x => x.Text).MaximumLength(50);

        When(x => x.IsEnabled, () =>
        {
            RuleFor(x => x.ThresholdCzk).GreaterThan(0);
            RuleFor(x => x.Text).NotEmpty();
        });
    }
}
```

- [ ] **Step 8: Run tests to confirm PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SetGiftSetting" -q 2>&1 | tail -5
```

Expected: Passed: 10, Failed: 0.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GiftSettings/UseCases/SetGiftSetting/
git add backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingHandlerTests.cs
git add backend/test/Anela.Heblo.Tests/Application/GiftSettings/SetGiftSettingValidatorTests.cs
git commit -m "feat: add SetGiftSetting command handler and validator"
```

---

## Task 5: Application module + API controller

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs`
- Create: `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Create application module**

Create `backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs`:

```csharp
using Anela.Heblo.Domain.Features.Logistics.GiftSettings;
using Anela.Heblo.Persistence.Logistics.GiftSettings;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.GiftSettings;

public static class GiftSettingsModule
{
    public static IServiceCollection AddGiftSettingsModule(this IServiceCollection services)
    {
        services.AddScoped<IGiftSettingRepository, GiftSettingRepository>();
        return services;
    }
}
```

- [ ] **Step 2: Register module in ApplicationModule.cs**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, add the using and the call after `services.AddCarrierCoolingModule()`:

Add using (top of file):
```csharp
using Anela.Heblo.Application.Features.GiftSettings;
```

Add call (after line `services.AddCarrierCoolingModule();`):
```csharp
        services.AddGiftSettingsModule();
```

- [ ] **Step 3: Create API controller**

Create `backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs`:

```csharp
using System.Security.Claims;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.GetGiftSetting;
using Anela.Heblo.Application.Features.GiftSettings.UseCases.SetGiftSetting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/gift-settings")]
public class GiftSettingsController : BaseApiController
{
    private readonly IMediator _mediator;

    public GiftSettingsController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    [HttpGet]
    public async Task<IActionResult> GetGiftSetting(CancellationToken cancellationToken = default)
    {
        var dto = await _mediator.Send(new GetGiftSettingQuery(), cancellationToken);
        return Ok(dto);
    }

    [HttpPut]
    public async Task<IActionResult> SetGiftSetting(
        [FromBody] SetGiftSettingCommand command,
        CancellationToken cancellationToken = default)
    {
        command.ModifiedBy = GetCurrentUserId();
        var response = await _mediator.Send(command, cancellationToken);
        if (response.Success) return NoContent();
        return BadRequest(response);
    }

    private string GetCurrentUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? User.FindFirst("oid")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no identity claim.");
    }
}
```

- [ ] **Step 4: Build full solution**

```bash
dotnet build backend/Anela.Heblo.sln --no-incremental -q
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 5: Run full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q 2>&1 | tail -10
```

Expected: All tests pass (no new failures).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/GiftSettings/GiftSettingsModule.cs
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git add backend/src/Anela.Heblo.API/Controllers/GiftSettingsController.cs
git commit -m "feat: add GiftSettings module, DI registration, and API controller"
```

---

## Task 6: Expedition — thread price/currency + GiftBadgeText property

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` (part 1 — data model changes)

**Background:** `OrderSummary.Price` (of type `OrderPriceSummary?`) already has `WithVat (decimal?)` and `CurrencyCode (string?)`. The current pipeline tuple type `(string Code, string ShippingGuid)` needs to carry these two extra fields.

- [ ] **Step 1: Add GiftBadgeText to ExpeditionOrder**

In `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs`, add `GiftBadgeText` to `ExpeditionOrder` (after `IsCooled`):

```csharp
    public string? GiftBadgeText { get; set; }
```

The full updated `ExpeditionOrder` class should look like:

```csharp
public class ExpeditionOrder
{
    public string Code { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string Address { get; set; } = null!;
    public string Phone { get; set; } = null!;
    public string? CustomerRemark { get; set; }
    public string? EshopRemark { get; set; }
    public List<ExpeditionOrderItem> Items { get; set; } = new();
    public Cooling CarrierCooling { get; set; } = Cooling.None;
    public string? GiftBadgeText { get; set; }

    public bool IsCooled => Items.Any(i => i.Cooling != Cooling.None && i.Cooling <= CarrierCooling);
}
```

- [ ] **Step 2: Extend ordersByCarrier tuple to carry price and currency**

In `ShoptetApiExpeditionListSource.cs`, update the dictionary type from:
```csharp
var ordersByMethod = new Dictionary<ShippingMethod, List<(string Code, string ShippingGuid)>>();
```
to:
```csharp
var ordersByMethod = new Dictionary<ShippingMethod, List<(string Code, string ShippingGuid, decimal? TotalWithVat, string? CurrencyCode)>>();
```

Also update the `list.Add(...)` call to include price:
```csharp
list.Add((order.Code, shippingGuid, order.Price?.WithVat, order.Price?.CurrencyCode));
```

And update the inner `foreach` that unpacks the tuples:
```csharp
foreach (var (code, shippingGuid, totalWithVat, currencyCode) in sorted)
{
    var detail = await _client.GetExpeditionOrderDetailAsync(code, cancellationToken);
    var expeditionOrder = MapToExpeditionOrder(detail);
    expeditionOrder.CarrierCooling = ResolveCarrierCooling(shippingGuid, coolingMatrix);
    allExpeditionOrders.Add(expeditionOrder);
    processedCodes.Add(code);
}
```

Note: Keep `totalWithVat` and `currencyCode` in scope — they'll be used in Task 7 when wiring `GiftBadgeText`.

- [ ] **Step 3: Build to confirm no regressions**

```bash
dotnet build backend/Anela.Heblo.sln --no-incremental -q
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolData.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
git commit -m "feat: carry order price/currency through expedition pipeline and add GiftBadgeText to ExpeditionOrder"
```

---

## Task 7: Expedition — ResolveGiftBadge + inject IGiftSettingRepository + tests

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs` (part 2)
- Modify: `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`

- [ ] **Step 1: Write failing tests**

Add these test methods to `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`.

First, add a `BuildGiftRepo` helper after `BuildEmptyCoolingRepo()`:

```csharp
private static IGiftSettingRepository BuildGiftRepo(
    bool isEnabled = false, decimal threshold = 0, string text = "")
{
    var setting = isEnabled
        ? new GiftSetting(isEnabled, threshold, text, "test")
        : GiftSetting.CreateDefault();
    var mock = new Mock<IGiftSettingRepository>();
    mock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(setting);
    return mock.Object;
}
```

Update `BuildSource` to accept `IGiftSettingRepository?`:

```csharp
private static ShoptetApiExpeditionListSource BuildSource(
    ShoptetOrderClient client,
    ICarrierCoolingRepository? carrierCooling = null,
    IGiftSettingRepository? giftSettings = null,
    Func<ExpeditionProtocolData, byte[]>? generateDocument = null)
{
    var coolingRepo = carrierCooling ?? BuildEmptyCoolingRepo();
    var giftRepo = giftSettings ?? BuildGiftRepo();
    return new ShoptetApiExpeditionListSource(
        client, TimeProvider.System,
        new Mock<ICatalogRepository>().Object,
        coolingRepo, giftRepo, generateDocument);
}
```

Add the following test methods before the closing `}` of `ShoptetApiExpeditionListSourceTests`:

```csharp
[Theory]
[InlineData(false, 1000, "GIFT", 1500, "CZK", null)]   // disabled → no badge
[InlineData(true, 1000, "GIFT", 999, "CZK", null)]     // below threshold → no badge
[InlineData(true, 1000, "GIFT", null, "CZK", null)]    // null total → no badge
[InlineData(true, 1000, "GIFT", 1500, "EUR", null)]    // non-CZK → no badge
[InlineData(true, 1000, "GIFT", 1000, "CZK", "GIFT")]  // at threshold → badge
[InlineData(true, 1000, "GIFT", 1001, "CZK", "GIFT")]  // above threshold → badge
public void ResolveGiftBadge_ReturnsExpected(
    bool isEnabled, decimal threshold, string text,
    decimal? total, string currency, string? expected)
{
    var setting = isEnabled
        ? new GiftSetting(isEnabled, threshold, text, "test")
        : GiftSetting.CreateDefault();

    var result = ShoptetApiExpeditionListSource.ResolveGiftBadge(total, currency, setting);

    result.Should().Be(expected);
}

[Fact]
public async Task CreatePickingList_AssignsGiftBadge_WhenOrderEligible()
{
    var listResp = new OrderListResponse
    {
        Data = new OrderListData
        {
            Paginator = new Paginator { PageCount = 1 },
            Orders = new List<OrderSummary>
            {
                new()
                {
                    Code = "Z001",
                    Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid },
                    Price = new OrderPriceSummary { WithVat = 2000m, CurrencyCode = "CZK" },
                },
            },
        },
    };

    var capturedData = new List<ExpeditionProtocolData>();
    var client = BuildClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?")) return Json(listResp);
        return Json(DetailFor("Z001"));
    });

    var giftRepo = BuildGiftRepo(isEnabled: true, threshold: 1500m, text: "DÁREK");

    var source = BuildSource(client, giftSettings: giftRepo,
        generateDocument: data => { capturedData.Add(data); return Array.Empty<byte>(); });

    await source.CreatePickingList(DefaultRequest(), null);

    capturedData.Should().HaveCount(1);
    capturedData[0].Orders.Should().HaveCount(1);
    capturedData[0].Orders[0].GiftBadgeText.Should().Be("DÁREK");
}

[Fact]
public async Task CreatePickingList_NoGiftBadge_ForNonCzkOrder()
{
    var listResp = new OrderListResponse
    {
        Data = new OrderListData
        {
            Paginator = new Paginator { PageCount = 1 },
            Orders = new List<OrderSummary>
            {
                new()
                {
                    Code = "Z001",
                    Shipping = new OrderShippingSummary { Guid = ZasilkovnaDoRukyGuid },
                    Price = new OrderPriceSummary { WithVat = 5000m, CurrencyCode = "EUR" },
                },
            },
        },
    };

    var capturedData = new List<ExpeditionProtocolData>();
    var client = BuildClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?")) return Json(listResp);
        return Json(DetailFor("Z001"));
    });

    var source = BuildSource(client,
        giftSettings: BuildGiftRepo(isEnabled: true, threshold: 1500m, text: "DÁREK"),
        generateDocument: data => { capturedData.Add(data); return Array.Empty<byte>(); });

    await source.CreatePickingList(DefaultRequest(), null);

    capturedData[0].Orders[0].GiftBadgeText.Should().BeNull();
}

[Fact]
public async Task CreatePickingList_LoadsGiftSettingOnce_AcrossMultipleBatches()
{
    // 3 orders → greedy batcher splits into 2 batches (Z001 has 10 items, exceeds maxItems on add of Z002)
    var listResp = SinglePageList(
        ("Z001", ZasilkovnaDoRukyGuid),
        ("Z002", ZasilkovnaDoRukyGuid),
        ("Z003", ZasilkovnaDoRukyGuid));

    var client = BuildClient(req =>
    {
        if (req.RequestUri!.PathAndQuery.StartsWith("/api/orders?")) return Json(listResp);
        var code = req.RequestUri.Segments.Last();
        return Json(DetailFor(code, itemCount: code == "Z001" ? 10 : 1));
    });

    var giftRepoMock = new Mock<IGiftSettingRepository>();
    giftRepoMock.Setup(r => r.GetAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(GiftSetting.CreateDefault());

    var source = BuildSource(client, giftSettings: giftRepoMock.Object,
        generateDocument: _ => Array.Empty<byte>());

    await source.CreatePickingList(DefaultRequest(), null);

    giftRepoMock.Verify(r => r.GetAsync(It.IsAny<CancellationToken>()), Times.Once);
}
```

Also add `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` and `using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;` to the test file's usings.

- [ ] **Step 2: Run tests to confirm FAIL**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests" -q 2>&1 | tail -10
```

Expected: Build errors — `IGiftSettingRepository` not found in `BuildSource`.

- [ ] **Step 3: Inject IGiftSettingRepository and add ResolveGiftBadge**

In `ShoptetApiExpeditionListSource.cs`:

1. Add `using Anela.Heblo.Domain.Features.Logistics.GiftSettings;` to the usings.

2. Add field:
```csharp
private readonly IGiftSettingRepository _giftSettings;
```

3. Add parameter to constructor (after `ICarrierCoolingRepository carrierCooling`):
```csharp
IGiftSettingRepository giftSettings,
```

4. In constructor body, add:
```csharp
_giftSettings = giftSettings;
```

5. In `CreatePickingList`, load gift setting once alongside cooling matrix (after the cooling matrix load):
```csharp
// Load gift setting once for the entire run (alongside cooling matrix)
var giftSetting = await _giftSettings.GetAsync(cancellationToken);
```

6. Inside the `foreach (var (code, shippingGuid, totalWithVat, currencyCode) in sorted)` loop, after setting `CarrierCooling`, assign `GiftBadgeText`:
```csharp
expeditionOrder.GiftBadgeText = ResolveGiftBadge(totalWithVat, currencyCode, giftSetting);
```

7. Add the `ResolveGiftBadge` static helper method at the end of the class (alongside `ResolveCarrierCooling`):
```csharp
internal static string? ResolveGiftBadge(
    decimal? totalWithVat,
    string? currencyCode,
    GiftSetting setting)
{
    if (!setting.IsEnabled) return null;
    if (!string.Equals(currencyCode, "CZK", StringComparison.OrdinalIgnoreCase)) return null;
    if (totalWithVat is null || totalWithVat < setting.ThresholdCzk) return null;
    return setting.Text;
}
```

- [ ] **Step 4: Run tests to confirm PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ShoptetApiExpeditionListSourceTests" -q 2>&1 | tail -10
```

Expected: All tests pass (including pre-existing ones).

- [ ] **Step 5: Run full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q 2>&1 | tail -10
```

Expected: All tests pass, no regressions.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs
git add backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs
git commit -m "feat: inject IGiftSettingRepository, add ResolveGiftBadge, stamp GiftBadgeText on expedition orders"
```

---

## Task 8: Expedition — PDF gift badge rendering

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs`

**Background:** The frost badge renders in `ComposeOrderBlock` via `if (order.IsCooled) { headingRow.AutoItem()... }`. The gift badge uses the identical layout pattern in the same `headingRow`, side-by-side. `GiftIconBytes` is generated programmatically with SkiaSharp (same as `FrostIconBytes`), not loaded from a file.

- [ ] **Step 1: Add gift icon constants and static field**

In `ExpeditionProtocolDocument.cs`, add after the frost badge constants (around line 31):

```csharp
    // Gift badge layout constants (same values as frost badge)
    private const float GiftIconSize = 12f;
    private const float GiftBadgePadding = 3f;
    private const float GiftBadgeBorderThickness = 1.5f;
    private const float GiftBadgePaddingLeft = 4f;

    private static readonly byte[] GiftIconBytes = GenerateGiftIcon();
```

- [ ] **Step 2: Add gift badge rendering to ComposeOrderBlock**

In `ComposeOrderBlock`, immediately after the closing `}` of the `if (order.IsCooled)` block (around line 108), add:

```csharp
                if (!string.IsNullOrEmpty(order.GiftBadgeText))
                {
                    headingRow.AutoItem()
                        .PaddingLeft(GiftBadgePaddingLeft)
                        .Border(GiftBadgeBorderThickness)
                        .BorderColor(Colors.Black)
                        .Padding(GiftBadgePadding)
                        .Row(row =>
                        {
                            row.AutoItem().Width(GiftIconSize).Height(GiftIconSize).Image(GiftIconBytes).FitArea();
                            row.AutoItem().PaddingLeft(3).AlignMiddle()
                                .Text(order.GiftBadgeText)
                                .Bold().FontSize(10).FontColor(Colors.Black);
                        });
                }
```

- [ ] **Step 3: Add GenerateGiftIcon method**

Add the following static method to `ExpeditionProtocolDocument.cs`, after `GenerateFrostIcon()` (around line 368):

```csharp
    private static byte[] GenerateGiftIcon()
    {
        const int size = 64;
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = 4f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };

        float cx = size / 2f;

        // Box body
        canvas.DrawRect(6, 30, 52, 28, paint);
        // Lid
        canvas.DrawRect(4, 20, 56, 12, paint);
        // Ribbon — vertical through center
        canvas.DrawLine(cx, 20, cx, 58, paint);
        // Ribbon — horizontal on lid
        canvas.DrawLine(4, 26, 60, 26, paint);
        // Bow — left loop
        canvas.DrawLine(cx, 20, cx - 14, 6, paint);
        canvas.DrawLine(cx - 14, 6, cx - 2, 16, paint);
        // Bow — right loop
        canvas.DrawLine(cx, 20, cx + 14, 6, paint);
        canvas.DrawLine(cx + 14, 6, cx + 2, 16, paint);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
```

- [ ] **Step 4: Build and run PDF document tests**

```bash
dotnet build backend/Anela.Heblo.sln --no-incremental -q
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ExpeditionProtocolDocumentTests" -q 2>&1 | tail -5
```

Expected: Build succeeded, all existing document tests pass.

- [ ] **Step 5: Run full test suite**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q 2>&1 | tail -5
```

Expected: All pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ExpeditionProtocolDocument.cs
git commit -m "feat: render gift badge on expedition PDF next to cooling badge"
```

---

## Task 9: Frontend hook — useGiftSetting.ts

**Files:**
- Create: `frontend/src/api/hooks/useGiftSetting.ts`

- [ ] **Step 1: Create the hook**

Create `frontend/src/api/hooks/useGiftSetting.ts`:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

export interface GiftSettingDto {
  isEnabled: boolean;
  thresholdCzk: number;
  text: string;
  modifiedAt: string | null;
  modifiedBy: string | null;
}

export interface SetGiftSettingRequest {
  isEnabled: boolean;
  thresholdCzk: number;
  text: string;
}

const QUERY_KEYS = {
  setting: ['giftSettings', 'setting'] as const,
};

const getSetting = async (): Promise<GiftSettingDto> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/gift-settings`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new Error(`Failed to fetch gift setting: ${response.status}`);
  }
  return response.json();
};

const setSetting = async (request: SetGiftSettingRequest): Promise<void> => {
  const apiClient = getAuthenticatedApiClient();
  const fullUrl = `${(apiClient as any).baseUrl}/api/gift-settings`;
  const response = await (apiClient as any).http.fetch(fullUrl, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
    },
    body: JSON.stringify(request),
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.params?.message ?? `Failed to save gift setting: ${response.status}`);
  }
};

export const useGiftSetting = () => {
  return useQuery({
    queryKey: QUERY_KEYS.setting,
    queryFn: getSetting,
  });
};

export const useSetGiftSetting = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: setSetting,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.setting });
    },
  });
};
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend
npm run build 2>&1 | tail -10
```

Expected: Compiled successfully (or only pre-existing warnings).

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
git add frontend/src/api/hooks/useGiftSetting.ts
git commit -m "feat: add useGiftSetting React Query hooks for GET/PUT gift-settings"
```

---

## Task 10: Frontend routing — Sidebar rename + new route + redirect

**Files:**
- Modify: `frontend/src/components/Layout/Sidebar.tsx`
- Modify: `frontend/src/App.tsx`

- [ ] **Step 1: Update Sidebar entry**

In `frontend/src/components/Layout/Sidebar.tsx`, find the entry at line ~148:
```typescript
        { id: "chlazeni", name: "Chlazení", href: "/customer/cooling" },
```

Replace with:
```typescript
        { id: "chlazeni", name: "Nastavení expedice", href: "/customer/expedition-settings" },
```

- [ ] **Step 2: Update App.tsx — add import, new route, and redirect**

In `frontend/src/App.tsx`:

1. Replace the import line (line 56):
```typescript
import CoolingPage from "./pages/customer/CoolingPage";
```
with:
```typescript
import ExpeditionSettingsPage from "./pages/customer/ExpeditionSettingsPage";
```

Also add the Navigate import if not already present (check existing imports for Navigate):
```typescript
import { BrowserRouter as Router, Routes, Route, Outlet, Navigate } from "react-router-dom";
```

2. Replace the route at line ~420:
```typescript
                        <Route path="/customer/cooling" element={<CoolingPage />} />
```
with:
```typescript
                        <Route path="/customer/expedition-settings" element={<ExpeditionSettingsPage />} />
                        <Route path="/customer/cooling" element={<Navigate to="/customer/expedition-settings?tab=cooling" replace />} />
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend
npm run build 2>&1 | tail -10
```

Expected: Compiled successfully (will fail on missing `ExpeditionSettingsPage` — that's fine at this step, proceed to Task 11 immediately).

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
git add frontend/src/components/Layout/Sidebar.tsx
git add frontend/src/App.tsx
git commit -m "feat: rename sidebar Chlazení → Nastavení expedice, add expedition-settings route with cooling redirect"
```

---

## Task 11: Frontend pages — CoolingTab + ExpeditionSettingsPage (replace CoolingPage)

**Files:**
- Create: `frontend/src/components/customer/expeditionSettings/CoolingTab.tsx`
- Create: `frontend/src/pages/customer/ExpeditionSettingsPage.tsx`
- Delete: `frontend/src/pages/customer/CoolingPage.tsx`

- [ ] **Step 1: Create CoolingTab — extract cooling body**

Create `frontend/src/components/customer/expeditionSettings/CoolingTab.tsx`:

```tsx
import CarrierCoolingMatrix from '../cooling/CarrierCoolingMatrix';
import WeatherForecastReport from '../cooling/WeatherForecastReport';
import {
  useCarrierCoolingMatrix,
  useSetCarrierCooling,
} from '../../../api/hooks/useCarrierCooling';

function CoolingTab() {
  const { data, isLoading, error } = useCarrierCoolingMatrix();
  const { mutate: setCooling, isPending, variables: savingRow } = useSetCarrierCooling();

  return (
    <>
      <WeatherForecastReport />

      {isLoading && (
        <div className="flex items-center justify-center h-32">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
        </div>
      )}

      {error && (
        <div className="mx-4 p-4 bg-red-50 border border-red-200 rounded-lg text-red-600 text-sm">
          Nepodařilo se načíst nastavení chlazení. Zkuste obnovit stránku.
        </div>
      )}

      {data && (
        <CarrierCoolingMatrix
          groups={data.groups}
          onSetCooling={setCooling}
          isSaving={isPending}
          savingRow={savingRow ?? null}
        />
      )}
    </>
  );
}

export default CoolingTab;
```

- [ ] **Step 2: Create ExpeditionSettingsPage — tabbed shell**

Create `frontend/src/pages/customer/ExpeditionSettingsPage.tsx`:

```tsx
import { useSearchParams } from 'react-router-dom';
import { Settings, Thermometer, Gift } from 'lucide-react';
import { PAGE_CONTAINER_HEIGHT } from '../../constants/layout';
import CoolingTab from '../../components/customer/expeditionSettings/CoolingTab';
import GiftsTab from '../../components/customer/expeditionSettings/GiftsTab';

type Tab = 'cooling' | 'gifts';

function ExpeditionSettingsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const activeTab: Tab = (searchParams.get('tab') as Tab) ?? 'cooling';

  const handleTabChange = (tab: Tab) => {
    setSearchParams({ tab });
  };

  return (
    <div
      className="flex flex-col w-full"
      style={{ height: PAGE_CONTAINER_HEIGHT }}
    >
      <div className="flex-shrink-0 px-4 py-3">
        <h1 className="text-lg font-semibold text-gray-900 flex items-center gap-3">
          <Settings className="h-6 w-6 text-indigo-600" />
          Nastavení expedice
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Konfigurace chlazení a dárků pro expedici.
        </p>
      </div>

      <div className="flex-shrink-0 flex border-b border-gray-200 px-4">
        <button
          onClick={() => handleTabChange('cooling')}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === 'cooling'
              ? 'border-indigo-500 text-indigo-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          <Thermometer className="h-4 w-4" />
          <span>Chlazení</span>
        </button>

        <button
          onClick={() => handleTabChange('gifts')}
          className={`px-4 py-2 text-sm font-medium flex items-center space-x-2 border-b-2 transition-colors ${
            activeTab === 'gifts'
              ? 'border-indigo-500 text-indigo-600'
              : 'border-transparent text-gray-500 hover:text-gray-700'
          }`}
        >
          <Gift className="h-4 w-4" />
          <span>Dárky</span>
        </button>
      </div>

      <div className="flex-1 overflow-y-auto">
        {activeTab === 'cooling' ? <CoolingTab /> : <GiftsTab />}
      </div>
    </div>
  );
}

export default ExpeditionSettingsPage;
```

- [ ] **Step 3: Delete CoolingPage.tsx**

```bash
rm /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend/src/pages/customer/CoolingPage.tsx
```

- [ ] **Step 4: Verify build (will fail if GiftsTab is missing — proceed to Task 12)**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend
npm run build 2>&1 | tail -10
```

If `GiftsTab` is missing, that's expected — continue to Task 12.

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
git rm frontend/src/pages/customer/CoolingPage.tsx
git add frontend/src/components/customer/expeditionSettings/CoolingTab.tsx
git add frontend/src/pages/customer/ExpeditionSettingsPage.tsx
git commit -m "feat: extract CoolingTab, create ExpeditionSettingsPage with tabs, delete CoolingPage"
```

---

## Task 12: Frontend form — GiftsTab with tests

**Files:**
- Create: `frontend/src/components/customer/expeditionSettings/GiftsTab.tsx`
- Create: `frontend/src/components/customer/expeditionSettings/__tests__/GiftsTab.test.tsx`

- [ ] **Step 1: Write failing tests**

Create `frontend/src/components/customer/expeditionSettings/__tests__/GiftsTab.test.tsx`:

```tsx
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import GiftsTab from '../GiftsTab';
import { useGiftSetting, useSetGiftSetting } from '../../../../api/hooks/useGiftSetting';

jest.mock('../../../../api/hooks/useGiftSetting');

const mockUseGiftSetting = useGiftSetting as jest.MockedFunction<typeof useGiftSetting>;
const mockUseSetGiftSetting = useSetGiftSetting as jest.MockedFunction<typeof useSetGiftSetting>;

const createWrapper = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

const defaultMutate = jest.fn();

beforeEach(() => {
  mockUseSetGiftSetting.mockReturnValue({
    mutate: defaultMutate,
    isPending: false,
    isError: false,
    error: null,
  } as any);
});

describe('GiftsTab', () => {
  it('renders loading state', () => {
    mockUseGiftSetting.mockReturnValue({ data: undefined, isLoading: true, error: null } as any);
    render(<GiftsTab />, { wrapper: createWrapper() });
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('renders error state', () => {
    mockUseGiftSetting.mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') } as any);
    render(<GiftsTab />, { wrapper: createWrapper() });
    expect(screen.getByText(/Nepodařilo se načíst/i)).toBeInTheDocument();
  });

  it('renders form with initial values from data', () => {
    mockUseGiftSetting.mockReturnValue({
      data: { isEnabled: true, thresholdCzk: 1500, text: 'DÁREK ZDARMA', modifiedAt: null, modifiedBy: null },
      isLoading: false,
      error: null,
    } as any);

    render(<GiftsTab />, { wrapper: createWrapper() });

    expect((screen.getByRole('spinbutton') as HTMLInputElement).value).toBe('1500');
    expect((screen.getByRole('textbox') as HTMLInputElement).value).toBe('DÁREK ZDARMA');
  });

  it('disables threshold and text inputs when toggle is off', () => {
    mockUseGiftSetting.mockReturnValue({
      data: { isEnabled: false, thresholdCzk: 0, text: '', modifiedAt: null, modifiedBy: null },
      isLoading: false,
      error: null,
    } as any);

    render(<GiftsTab />, { wrapper: createWrapper() });

    expect(screen.getByRole('spinbutton')).toBeDisabled();
    expect(screen.getByRole('textbox')).toBeDisabled();
  });

  it('calls mutate with form values on save', async () => {
    mockUseGiftSetting.mockReturnValue({
      data: { isEnabled: true, thresholdCzk: 1500, text: 'DÁREK ZDARMA', modifiedAt: null, modifiedBy: null },
      isLoading: false,
      error: null,
    } as any);

    render(<GiftsTab />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByRole('button', { name: /Uložit/i }));

    await waitFor(() => {
      expect(defaultMutate).toHaveBeenCalledWith({
        isEnabled: true,
        thresholdCzk: 1500,
        text: 'DÁREK ZDARMA',
      });
    });
  });
});
```

- [ ] **Step 2: Run tests to confirm FAIL**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend
npx react-scripts test --watchAll=false \
  --testPathPattern="GiftsTab.test" 2>&1 | tail -10
```

Expected: FAIL — `GiftsTab` module not found.

- [ ] **Step 3: Create GiftsTab component**

Create `frontend/src/components/customer/expeditionSettings/GiftsTab.tsx`:

```tsx
import { useState, useEffect } from 'react';
import { useGiftSetting, useSetGiftSetting } from '../../../api/hooks/useGiftSetting';

function GiftsTab() {
  const { data, isLoading, error } = useGiftSetting();
  const { mutate: saveSetting, isPending } = useSetGiftSetting();

  const [isEnabled, setIsEnabled] = useState(false);
  const [thresholdCzk, setThresholdCzk] = useState(0);
  const [text, setText] = useState('');

  useEffect(() => {
    if (data) {
      setIsEnabled(data.isEnabled);
      setThresholdCzk(data.thresholdCzk);
      setText(data.text);
    }
  }, [data]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32" role="status">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="mx-4 mt-4 p-4 bg-red-50 border border-red-200 rounded-lg text-red-600 text-sm">
        Nepodařilo se načíst nastavení dárků. Zkuste obnovit stránku.
      </div>
    );
  }

  const handleSave = () => {
    saveSetting({ isEnabled, thresholdCzk, text });
  };

  return (
    <div className="px-4 py-4 max-w-lg space-y-6">
      <p className="text-sm text-gray-500">
        Když je součet objednávky v CZK dosáhne prahu, vytiskne se badge na expediční seznam.
      </p>

      {/* Enable toggle */}
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-gray-700">Aktivní</span>
        <button
          type="button"
          role="switch"
          aria-checked={isEnabled}
          onClick={() => setIsEnabled((v) => !v)}
          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 ${
            isEnabled ? 'bg-indigo-600' : 'bg-gray-200'
          }`}
        >
          <span
            className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
              isEnabled ? 'translate-x-6' : 'translate-x-1'
            }`}
          />
        </button>
      </div>

      {/* Threshold */}
      <div>
        <label htmlFor="threshold" className="block text-sm font-medium text-gray-700 mb-1">
          Práh (CZK)
        </label>
        <input
          id="threshold"
          type="number"
          min={1}
          max={999999}
          value={thresholdCzk}
          onChange={(e) => setThresholdCzk(Number(e.target.value))}
          disabled={!isEnabled}
          className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-indigo-500 disabled:bg-gray-100 disabled:text-gray-400"
        />
      </div>

      {/* Text */}
      <div>
        <label htmlFor="gift-text" className="block text-sm font-medium text-gray-700 mb-1">
          Text badge (max 30 znaků)
        </label>
        <input
          id="gift-text"
          type="text"
          maxLength={30}
          value={text}
          onChange={(e) => setText(e.target.value)}
          disabled={!isEnabled}
          placeholder="DÁREK ZDARMA"
          className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm shadow-sm focus:border-indigo-500 focus:ring-indigo-500 disabled:bg-gray-100 disabled:text-gray-400"
        />
        <p className="mt-1 text-xs text-gray-400">{text.length} / 30</p>
      </div>

      {/* Save button */}
      <button
        type="button"
        onClick={handleSave}
        disabled={isPending}
        className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500 disabled:opacity-50"
      >
        {isPending ? 'Ukládám…' : 'Uložit'}
      </button>
    </div>
  );
}

export default GiftsTab;
```

- [ ] **Step 4: Run tests to confirm PASS**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend
npx react-scripts test --watchAll=false \
  --testPathPattern="GiftsTab.test" 2>&1 | tail -10
```

Expected: Passed: 5, Failed: 0.

- [ ] **Step 5: Full frontend build and lint**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu/frontend
npm run build 2>&1 | tail -10
npm run lint 2>&1 | tail -10
```

Expected: Compiled successfully, no lint errors.

- [ ] **Step 6: Full backend build and tests**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
dotnet build backend/Anela.Heblo.sln --no-incremental -q
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q 2>&1 | tail -5
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: All pass, no formatting issues.

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/conductor/workspaces/Anela.Heblo/yeosu
git add frontend/src/components/customer/expeditionSettings/GiftsTab.tsx
git add frontend/src/components/customer/expeditionSettings/__tests__/GiftsTab.test.tsx
git commit -m "feat: add GiftsTab form with toggle, threshold, text inputs and component tests"
```

---

## Verification checklist

Before calling this feature complete, confirm:

**Backend:**
- [ ] `dotnet build backend/Anela.Heblo.sln -q` — 0 errors
- [ ] `dotnet format backend/Anela.Heblo.sln --verify-no-changes` — clean
- [ ] `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -q` — all pass
- [ ] New tests pass: `ResolveGiftBadge_ReturnsExpected` truth table (6 cases)
- [ ] New tests pass: `CreatePickingList_AssignsGiftBadge_WhenOrderEligible`
- [ ] New tests pass: `CreatePickingList_LoadsGiftSettingOnce_AcrossMultipleBatches`
- [ ] New tests pass: `CreatePickingList_NoGiftBadge_ForNonCzkOrder`
- [ ] New tests pass: `GetGiftSettingHandlerTests` (2 cases)
- [ ] New tests pass: `SetGiftSettingHandlerTests` (5 cases)
- [ ] New tests pass: `SetGiftSettingValidatorTests` (5 cases)

**Frontend:**
- [ ] `npm run build` — clean
- [ ] `npm run lint` — clean
- [ ] New tests pass: `GiftsTab.test.tsx` (5 cases)
- [ ] Navigate to `/customer/cooling` → redirects to `/customer/expedition-settings?tab=cooling`
- [ ] Navigate to `/customer/expedition-settings` → default `Chlazení` tab shows cooling matrix unchanged
- [ ] Switch to `Dárky` tab → form loads, toggle/threshold/text inputs present
- [ ] Enable, set threshold 1500, text `DÁREK ZDARMA`, save, reload → values persist

**PDF (manual, using pattern from memory `pattern_pdf_visual_inspection`):**
- [ ] CZK order over threshold → gift badge appears next to `Objednávka …`
- [ ] CZK order under threshold → no gift badge
- [ ] EUR order over threshold → no gift badge
- [ ] Order with both cooling and gift → both badges side-by-side in same row
