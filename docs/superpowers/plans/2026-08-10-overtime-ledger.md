# Overtime Ledger (Evidence přesčasů) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Month-to-month overtime tracking per employee computed from Logeto, with manual adjustments, explicit month close, and an auto-generated Excel — replacing the hand-maintained internal Excel.

**Architecture:** New vertical slice `Features/Attendance/Overtime/` (spec: `docs/superpowers/specs/2026-08-10-overtime-ledger-design.md`). Frozen monthly statements: open months are recomputed live from Logeto; closing freezes numbers, chains the balance, locks adjustments, regenerates the Excel. Admin UI in Heblo; employees read the generated Excel.

**Tech Stack:** .NET 8, MediatR + MVC controller, EF Core (PostgreSQL, InMemory for tests), FluentValidation, xUnit + Moq + FluentAssertions, ClosedXML (new package), Microsoft Graph (existing `GraphApiHelpers`), React + TanStack Query v5 + Tailwind.

## Global Constraints

- DTOs are **classes, never C# records** (OpenAPI generator breaks on records). Internal domain types may be records.
- Every `*Response` class in `Anela.Heblo.Application` MUST inherit `Anela.Heblo.Application.Shared.BaseResponse` (reflection-asserted by `ErrorHandlingTests`).
- Validators are registered **explicitly per request type** (`IValidator<>` + `ValidationBehavior<,>` both) — there is no `AddValidatorsFromAssembly`.
- New error codes use the **34XX** range and require: `[HttpStatusCode(...)]` attribute on each member, a bucket entry in `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs`, and Czech strings under `errors:` in `frontend/src/i18n.ts` keyed by enum member name.
- EF conventions: `ToTable(name, "public")`, enums stored via `HasConversion<string>()`, `DateTime` columns `timestamp without time zone`, explicit `HasMaxLength`.
- Hours are `decimal`, rounded to 2 decimal places with `Math.Round(x, 2, MidpointRounding.AwayFromZero)`.
- FE: hardcoded Czech UI strings (matches existing pages), Tailwind + `lucide-react`, TanStack Query v5, `useScreenView` telemetry on the page.
- Test commands: backend `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "<Filter>"` from repo root (build first if another worktree may run tests). FE: `cd frontend && CI=true npx react-scripts test --watchAll=false <path>`.
- Commit after every task. No attribution footers (disabled globally).
- Validation gates before completion: `dotnet build` + `dotnet format`, `cd frontend && npm run build && npm run lint`.

## File Structure

```
backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime/
  OvertimeEmployee.cs, OvertimeMonthlyStatement.cs, OvertimeAdjustment.cs,
  OvertimeAdjustmentType.cs, OvertimeStatementStatus.cs,
  IOvertimeEmployeeRepository.cs, IOvertimeStatementRepository.cs, IOvertimeAdjustmentRepository.cs,
  IContractHoursProvider.cs
backend/src/Anela.Heblo.Persistence/Attendance/
  OvertimeEmployeeConfiguration.cs, OvertimeMonthlyStatementConfiguration.cs, OvertimeAdjustmentConfiguration.cs,
  OvertimeEmployeeRepository.cs, OvertimeStatementRepository.cs, OvertimeAdjustmentRepository.cs
backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/
  OvertimeModule.cs, OvertimeOptions.cs,
  Services/CzechHolidays.cs, Services/WorkingDaysCalculator.cs,
  Services/ConfigurationContractHoursProvider.cs,
  Services/OvertimeCalculationService.cs,
  Services/OvertimeExcelBuilder.cs, Services/IOvertimeReportPublisher.cs, Services/GraphOvertimeReportPublisher.cs,
  Contracts/OvertimeEmployeeDto.cs, Contracts/OvertimeStatementDto.cs, Contracts/OvertimeAdjustmentDto.cs,
  UseCases/GetOvertimeEmployees/*, UseCases/UpsertOvertimeEmployee/*,
  UseCases/GetMonthlyStatements/*, UseCases/SetStatementReviewed/*,
  UseCases/CreateAdjustment/*, UseCases/DeleteAdjustment/*,
  UseCases/CloseMonth/*, UseCases/ExportOvertimeReport/*
backend/src/Anela.Heblo.API/Controllers/OvertimeController.cs
backend/test/Anela.Heblo.Tests/Application/Overtime/*
frontend/src/api/hooks/useOvertime.ts, frontend/src/pages/OvertimePage.tsx,
frontend/src/components/dialogs/CloseOvertimeMonthDialog.tsx
Modified: access-matrix.json, ErrorCodes.cs, ErrorHandlingTests.cs, i18n.ts,
ApplicationDbContext.cs, ApplicationModule.cs, appsettings.json, App.tsx, Sidebar.tsx, client.ts
```

---

### Task 1: Permission, error codes, i18n groundwork

**Files:**
- Modify: `access-matrix.json` (repo root)
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Modify: `backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs:74-133`
- Modify: `frontend/src/i18n.ts` (inside `errors:` object)

**Interfaces:**
- Produces: `Feature.Attendance_Overtime` enum member (generated), roles `attendance.overtime.read`/`.write`, menu path `/overtime`, `ErrorCodes` members 3401–3408.

- [ ] **Step 1: Add feature to access-matrix.json**

In `features[]` (keep alphabetical-ish placement near other entries):

```json
{ "key": "Attendance_Overtime", "label": "Evidence přesčasů", "hasWrite": true },
```

In `menuPaths[]`:

```json
{ "path": "/overtime", "requires": [ { "feature": "Attendance_Overtime", "level": "Read" } ] },
```

In `seedGroups[]`, add to the `"Spravce"` group's `roles` array: `"attendance.overtime.read", "attendance.overtime.write"`.

- [ ] **Step 2: Regenerate the access matrix**

Run: `dotnet run --project backend/tools/Anela.Heblo.AccessMatrixGen`
Expected: regenerates `Feature.generated.cs`, `AccessRoles.generated.cs`, `AccessMatrix.generated.cs`, `frontend/src/auth/accessMatrix.generated.ts`, `access-matrix.generated.json`, `access-matrix-entra.generated.json` — verify `Attendance_Overtime` appears in `Feature.generated.cs` and `"/overtime"` in `accessMatrix.generated.ts`.

- [ ] **Step 3: Add error codes**

In `ErrorCodes.cs`, after the LabelIdentification (33XX) block:

```csharp
    // Overtime ledger module errors (34XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    OvertimeEmployeeNotFound = 3401,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    OvertimeMonthAlreadyClosed = 3402,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    OvertimeAdjustmentNotFound = 3403,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    OvertimeAdjustmentMonthClosed = 3404,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    OvertimeMonthNotReviewed = 3405,
    [HttpStatusCode(HttpStatusCode.BadRequest)]
    OvertimeContractHoursMissing = 3406,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    OvertimePreviousMonthOpen = 3407,
    [HttpStatusCode(HttpStatusCode.ServiceUnavailable)]
    OvertimeExportPublishFailed = 3408,
```

- [ ] **Step 4: Update ErrorHandlingTests bucket**

In `ErrorHandlingTests.cs`, add alongside the existing per-module locals (~line 99):

```csharp
        var overtimeErrors = allErrorCodes.Count(e => (int)e >= 3400 && (int)e <= 3499);
```

and include `+ overtimeErrors` in the sum assertion (~line 133).

- [ ] **Step 5: Add Czech error strings**

In `frontend/src/i18n.ts` inside `errors:` (after the LabelIdentification entries):

```ts
        // Overtime ledger module errors
        OvertimeEmployeeNotFound: "Zaměstnanec nenalezen v evidenci přesčasů",
        OvertimeMonthAlreadyClosed: "Měsíc {year}/{month} je již uzavřen",
        OvertimeAdjustmentNotFound: "Korekce nenalezena",
        OvertimeAdjustmentMonthClosed: "Korekci nelze měnit — měsíc je uzavřen",
        OvertimeMonthNotReviewed: "Někteří zaměstnanci nejsou zkontrolováni: {names}",
        OvertimeContractHoursMissing: "Chybí denní úvazek pro: {names}",
        OvertimePreviousMonthOpen: "Nelze uzavřít {year}/{month} — existuje neuzavřený starší měsíc",
        OvertimeExportPublishFailed: "Nahrání reportu na SharePoint selhalo",
```

- [ ] **Step 6: Run the gate test**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~ErrorHandlingTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add access-matrix.json backend/src/Anela.Heblo.Domain/Features/Authorization/ backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs backend/test/Anela.Heblo.Tests/ErrorHandlingTests.cs frontend/src/i18n.ts frontend/src/auth/accessMatrix.generated.ts access-matrix.generated.json access-matrix-entra.generated.json
git commit -m "feat: add overtime ledger permission, error codes and translations"
```

---

### Task 2: Domain entities, EF persistence, migration

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime/OvertimeEmployee.cs`, `OvertimeMonthlyStatement.cs`, `OvertimeAdjustment.cs`, `OvertimeAdjustmentType.cs`, `OvertimeStatementStatus.cs`, `IOvertimeEmployeeRepository.cs`, `IOvertimeStatementRepository.cs`, `IOvertimeAdjustmentRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Attendance/` — 3 configurations + 3 repositories
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeModule.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (3 DbSets), `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (`services.AddOvertimeModule(configuration);` after `AddAttendanceModule`)
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeRepositoryTests.cs`

**Interfaces:**
- Produces (used by all later tasks):
  - `OvertimeEmployee { int Id; Guid PersonId; string DisplayName; decimal BaselineHours; DateOnly BaselineDate; bool IsActive; }` — public setters (simple persistence class, matches slice DTO style).
  - `OvertimeMonthlyStatement { int Id; Guid PersonId; int Year; int Month; OvertimeStatementStatus Status; decimal RequiredHours; decimal WorkedHours; decimal VacationHours; decimal SickHours; decimal DoctorHours; decimal CompTimeHours; decimal OtherAbsenceHours; decimal DeltaHours; decimal BalanceAfter; bool IsReviewed; DateTime? ClosedAtUtc; string? ClosedBy; }`
  - `OvertimeAdjustment { int Id; Guid PersonId; int Year; int Month; OvertimeAdjustmentType Type; decimal Hours; string Note; DateTime CreatedAtUtc; string CreatedBy; }`
  - `enum OvertimeStatementStatus { Open, Closed }`, `enum OvertimeAdjustmentType { Payout, PurchaseDeduction, Correction, SportBenefit, Other }`
  - `IOvertimeEmployeeRepository { Task<IReadOnlyList<OvertimeEmployee>> GetAllAsync(CancellationToken ct); Task<OvertimeEmployee?> GetByPersonIdAsync(Guid personId, CancellationToken ct); Task UpsertAsync(OvertimeEmployee employee, CancellationToken ct); }`
  - `IOvertimeStatementRepository { Task<IReadOnlyList<OvertimeMonthlyStatement>> GetByMonthAsync(int year, int month, CancellationToken ct); Task<OvertimeMonthlyStatement?> GetLatestClosedAsync(Guid personId, CancellationToken ct); Task<IReadOnlyList<OvertimeMonthlyStatement>> GetAllClosedAsync(CancellationToken ct); Task<bool> AnyOpenBeforeAsync(int year, int month, CancellationToken ct); Task AddAsync(OvertimeMonthlyStatement statement, CancellationToken ct); Task SaveChangesAsync(CancellationToken ct); }`
  - `IOvertimeAdjustmentRepository { Task<IReadOnlyList<OvertimeAdjustment>> GetByMonthAsync(int year, int month, CancellationToken ct); Task<OvertimeAdjustment?> GetByIdAsync(int id, CancellationToken ct); Task AddAsync(OvertimeAdjustment adjustment, CancellationToken ct); Task DeleteAsync(OvertimeAdjustment adjustment, CancellationToken ct); }`
  - Repositories call `SaveChangesAsync` inside `UpsertAsync`/`AddAsync`/`DeleteAsync` (project convention); `IOvertimeStatementRepository.SaveChangesAsync` exists so handlers can mutate tracked statements and persist.

- [ ] **Step 1: Write failing repository tests**

`backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeRepositoryTests.cs` — InMemory pattern (fresh DB per class, `IDisposable`):

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Attendance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public OvertimeRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"OvertimeTestDb_{Guid.NewGuid()}")
            .Options;
        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task EmployeeUpsert_InsertsThenUpdates()
    {
        var repo = new OvertimeEmployeeRepository(_context);
        await repo.UpsertAsync(new OvertimeEmployee
        {
            PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m,
            BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        await repo.UpsertAsync(new OvertimeEmployee
        {
            PersonId = Person, DisplayName = "Pepina H.", BaselineHours = 3.0m,
            BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        var all = await repo.GetAllAsync(CancellationToken.None);
        all.Should().HaveCount(1);
        all[0].DisplayName.Should().Be("Pepina H.");
        all[0].BaselineHours.Should().Be(3.0m);
    }

    [Fact]
    public async Task GetLatestClosed_ReturnsNewestClosedStatement_IgnoringOpen()
    {
        var repo = new OvertimeStatementRepository(_context);
        await repo.AddAsync(Statement(2026, 9, OvertimeStatementStatus.Closed, balanceAfter: 5m), CancellationToken.None);
        await repo.AddAsync(Statement(2026, 10, OvertimeStatementStatus.Closed, balanceAfter: 8m), CancellationToken.None);
        await repo.AddAsync(Statement(2026, 11, OvertimeStatementStatus.Open, balanceAfter: 0m), CancellationToken.None);

        var latest = await repo.GetLatestClosedAsync(Person, CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.Month.Should().Be(10);
        latest.BalanceAfter.Should().Be(8m);
    }

    [Fact]
    public async Task AnyOpenBefore_DetectsOlderOpenMonth()
    {
        var repo = new OvertimeStatementRepository(_context);
        await repo.AddAsync(Statement(2026, 9, OvertimeStatementStatus.Open, 0m), CancellationToken.None);

        (await repo.AnyOpenBeforeAsync(2026, 10, CancellationToken.None)).Should().BeTrue();
        (await repo.AnyOpenBeforeAsync(2026, 9, CancellationToken.None)).Should().BeFalse();
    }

    private static OvertimeMonthlyStatement Statement(int year, int month, OvertimeStatementStatus status, decimal balanceAfter) => new()
    {
        PersonId = Person, Year = year, Month = month, Status = status, BalanceAfter = balanceAfter
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~OvertimeRepositoryTests"`
Expected: FAIL — compilation errors (types don't exist).

- [ ] **Step 3: Create domain types**

`OvertimeStatementStatus.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

public enum OvertimeStatementStatus
{
    Open,
    Closed
}
```

`OvertimeAdjustmentType.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

public enum OvertimeAdjustmentType
{
    Payout,
    PurchaseDeduction,
    Correction,
    SportBenefit,
    Other
}
```

`OvertimeEmployee.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>
/// A Logeto person tracked in the overtime ledger, with the baseline balance
/// (seeded from the legacy Excel) from which all deltas accumulate.
/// </summary>
public class OvertimeEmployee
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal BaselineHours { get; set; }

    /// <summary>Logeto data before this date is never computed.</summary>
    public DateOnly BaselineDate { get; set; }

    public bool IsActive { get; set; } = true;
}
```

`OvertimeMonthlyStatement.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>
/// One person-month of the overtime ledger. While Open, hour fields are a cache of the
/// live Logeto computation; on close they freeze and become the audit record.
/// </summary>
public class OvertimeMonthlyStatement
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public OvertimeStatementStatus Status { get; set; } = OvertimeStatementStatus.Open;

    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }

    /// <summary>Previous balance + delta + month's adjustments; written on close.</summary>
    public decimal BalanceAfter { get; set; }

    public bool IsReviewed { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosedBy { get; set; }
}
```

`OvertimeAdjustment.cs`:

```csharp
namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>Manual ledger move (payout, purchase deduction, correction, …), bound to an open month.</summary>
public class OvertimeAdjustment
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public OvertimeAdjustmentType Type { get; set; }

    /// <summary>Signed; negative reduces the balance. May be 0 for SportBenefit notes.</summary>
    public decimal Hours { get; set; }

    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
```

Repository interfaces exactly as in the **Interfaces** block above, each in its own file, namespace `Anela.Heblo.Domain.Features.Attendance.Overtime`.

- [ ] **Step 4: Create EF configurations**

`backend/src/Anela.Heblo.Persistence/Attendance/OvertimeEmployeeConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeEmployeeConfiguration : IEntityTypeConfiguration<OvertimeEmployee>
{
    public void Configure(EntityTypeBuilder<OvertimeEmployee> builder)
    {
        builder.ToTable("OvertimeEmployees", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.PersonId).IsUnique();
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.BaselineHours).HasPrecision(8, 2);
    }
}
```

`OvertimeMonthlyStatementConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeMonthlyStatementConfiguration : IEntityTypeConfiguration<OvertimeMonthlyStatement>
{
    public void Configure(EntityTypeBuilder<OvertimeMonthlyStatement> builder)
    {
        builder.ToTable("OvertimeMonthlyStatements", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PersonId, e.Year, e.Month }).IsUnique();
        builder.Property(e => e.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.RequiredHours).HasPrecision(8, 2);
        builder.Property(e => e.WorkedHours).HasPrecision(8, 2);
        builder.Property(e => e.VacationHours).HasPrecision(8, 2);
        builder.Property(e => e.SickHours).HasPrecision(8, 2);
        builder.Property(e => e.DoctorHours).HasPrecision(8, 2);
        builder.Property(e => e.CompTimeHours).HasPrecision(8, 2);
        builder.Property(e => e.OtherAbsenceHours).HasPrecision(8, 2);
        builder.Property(e => e.DeltaHours).HasPrecision(8, 2);
        builder.Property(e => e.BalanceAfter).HasPrecision(8, 2);
        builder.Property(e => e.ClosedAtUtc).HasColumnType("timestamp without time zone");
        builder.Property(e => e.ClosedBy).HasMaxLength(200);
    }
}
```

`OvertimeAdjustmentConfiguration.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeAdjustmentConfiguration : IEntityTypeConfiguration<OvertimeAdjustment>
{
    public void Configure(EntityTypeBuilder<OvertimeAdjustment> builder)
    {
        builder.ToTable("OvertimeAdjustments", "public");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.PersonId, e.Year, e.Month });
        builder.Property(e => e.Type).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Hours).HasPrecision(8, 2);
        builder.Property(e => e.Note).IsRequired().HasMaxLength(500);
        builder.Property(e => e.CreatedAtUtc).IsRequired().HasColumnType("timestamp without time zone");
        builder.Property(e => e.CreatedBy).IsRequired().HasMaxLength(200);
    }
}
```

- [ ] **Step 5: Create repositories**

`backend/src/Anela.Heblo.Persistence/Attendance/OvertimeEmployeeRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeEmployeeRepository : IOvertimeEmployeeRepository
{
    private readonly ApplicationDbContext _context;

    public OvertimeEmployeeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OvertimeEmployee>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeEmployees.OrderBy(e => e.DisplayName).ToListAsync(cancellationToken);
    }

    public async Task<OvertimeEmployee?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeEmployees.FirstOrDefaultAsync(e => e.PersonId == personId, cancellationToken);
    }

    public async Task UpsertAsync(OvertimeEmployee employee, CancellationToken cancellationToken = default)
    {
        var existing = await _context.OvertimeEmployees
            .FirstOrDefaultAsync(e => e.PersonId == employee.PersonId, cancellationToken);

        if (existing is null)
        {
            _context.OvertimeEmployees.Add(employee);
        }
        else
        {
            existing.DisplayName = employee.DisplayName;
            existing.BaselineHours = employee.BaselineHours;
            existing.BaselineDate = employee.BaselineDate;
            existing.IsActive = employee.IsActive;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

`OvertimeStatementRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeStatementRepository : IOvertimeStatementRepository
{
    private readonly ApplicationDbContext _context;

    public OvertimeStatementRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OvertimeMonthlyStatement>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .Where(s => s.Year == year && s.Month == month)
            .ToListAsync(cancellationToken);
    }

    public async Task<OvertimeMonthlyStatement?> GetLatestClosedAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .Where(s => s.PersonId == personId && s.Status == OvertimeStatementStatus.Closed)
            .OrderByDescending(s => s.Year).ThenByDescending(s => s.Month)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OvertimeMonthlyStatement>> GetAllClosedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .Where(s => s.Status == OvertimeStatementStatus.Closed)
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyOpenBeforeAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeMonthlyStatements
            .AnyAsync(s => s.Status == OvertimeStatementStatus.Open
                           && (s.Year < year || (s.Year == year && s.Month < month)), cancellationToken);
    }

    public async Task AddAsync(OvertimeMonthlyStatement statement, CancellationToken cancellationToken = default)
    {
        _context.OvertimeMonthlyStatements.Add(statement);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _context.SaveChangesAsync(cancellationToken);
}
```

`OvertimeAdjustmentRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Attendance;

public class OvertimeAdjustmentRepository : IOvertimeAdjustmentRepository
{
    private readonly ApplicationDbContext _context;

    public OvertimeAdjustmentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OvertimeAdjustment>> GetByMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeAdjustments
            .Where(a => a.Year == year && a.Month == month)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<OvertimeAdjustment?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.OvertimeAdjustments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task AddAsync(OvertimeAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        _context.OvertimeAdjustments.Add(adjustment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(OvertimeAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        _context.OvertimeAdjustments.Remove(adjustment);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 6: Register DbSets and module**

`ApplicationDbContext.cs` — add with the other DbSets:

```csharp
    public DbSet<Anela.Heblo.Domain.Features.Attendance.Overtime.OvertimeEmployee> OvertimeEmployees { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Attendance.Overtime.OvertimeMonthlyStatement> OvertimeMonthlyStatements { get; set; } = null!;
    public DbSet<Anela.Heblo.Domain.Features.Attendance.Overtime.OvertimeAdjustment> OvertimeAdjustments { get; set; } = null!;
```

(Configurations are auto-discovered via `ApplyConfigurationsFromAssembly` — nothing else needed.)

`backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeModule.cs` (validators/behaviors get added here by later tasks):

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Persistence.Attendance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Attendance.Overtime;

public static class OvertimeModule
{
    public static IServiceCollection AddOvertimeModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IOvertimeEmployeeRepository, OvertimeEmployeeRepository>();
        services.AddScoped<IOvertimeStatementRepository, OvertimeStatementRepository>();
        services.AddScoped<IOvertimeAdjustmentRepository, OvertimeAdjustmentRepository>();

        return services;
    }
}
```

In `ApplicationModule.cs`, after `services.AddAttendanceModule(configuration);`:

```csharp
        services.AddOvertimeModule(configuration);
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~OvertimeRepositoryTests"`
Expected: PASS (3 tests).

- [ ] **Step 8: Add the migration**

```bash
dotnet ef migrations add AddOvertimeLedger \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: migration creates tables `OvertimeEmployees`, `OvertimeMonthlyStatements`, `OvertimeAdjustments`. Do NOT run `database update` (migrations are applied manually per environment).

- [ ] **Step 9: Build and commit**

Run: `dotnet build backend/Anela.Heblo.sln` (or the sln at repo root) — Expected: success.

```bash
git add backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime backend/src/Anela.Heblo.Persistence backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/src/Anela.Heblo.Application/ApplicationModule.cs backend/test/Anela.Heblo.Tests/Application/Overtime
git commit -m "feat: add overtime ledger entities, repositories and migration"
```

---

### Task 3: Czech holidays + working-days calculator

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/CzechHolidays.cs`, `Services/WorkingDaysCalculator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/WorkingDaysCalculatorTests.cs`

**Interfaces:**
- Produces: `static class CzechHolidays { static bool IsPublicHoliday(DateOnly date); }`, `static class WorkingDaysCalculator { static int CountWorkingDays(DateOnly from, DateOnly toInclusive); }` — used by Task 5.

No new NuGet dependency — Nager.Date requires a paid license key for current .NET versions; the Czech holiday set is 13 fixed dates + 2 Easter-derived days, trivially computed.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.Overtime;

public class WorkingDaysCalculatorTests
{
    [Theory]
    [InlineData(2026, 1, 1)]   // Nový rok
    [InlineData(2026, 4, 3)]   // Velký pátek (Easter 2026-04-05)
    [InlineData(2026, 4, 6)]   // Velikonoční pondělí
    [InlineData(2026, 5, 1)]   // Svátek práce
    [InlineData(2026, 5, 8)]   // Den vítězství
    [InlineData(2026, 7, 5)]   // Cyril a Metoděj
    [InlineData(2026, 7, 6)]   // Jan Hus
    [InlineData(2026, 9, 28)]  // Den české státnosti
    [InlineData(2026, 10, 28)] // Vznik ČSR
    [InlineData(2026, 11, 17)] // Den boje za svobodu
    [InlineData(2026, 12, 24)]
    [InlineData(2026, 12, 25)]
    [InlineData(2026, 12, 26)]
    public void IsPublicHoliday_ReturnsTrue_ForCzechHolidays(int y, int m, int d)
        => CzechHolidays.IsPublicHoliday(new DateOnly(y, m, d)).Should().BeTrue();

    [Fact]
    public void IsPublicHoliday_ReturnsFalse_ForOrdinaryDay()
        => CzechHolidays.IsPublicHoliday(new DateOnly(2026, 8, 11)).Should().BeFalse();

    [Fact]
    public void CountWorkingDays_July2026_Is22()
    {
        // July 2026: 23 weekdays; 6.7. (Monday, Jan Hus) is a holiday; 5.7. falls on Sunday → 22.
        WorkingDaysCalculator.CountWorkingDays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31))
            .Should().Be(22);
    }

    [Fact]
    public void CountWorkingDays_August2026_Is21()
    {
        // August 2026: no holidays, 21 weekdays.
        WorkingDaysCalculator.CountWorkingDays(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))
            .Should().Be(21);
    }

    [Fact]
    public void CountWorkingDays_PartialRange_CountsFromStart()
    {
        // 2026-08-17 (Mon) .. 2026-08-31 (Mon) = 11 weekdays, no holidays.
        WorkingDaysCalculator.CountWorkingDays(new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 31))
            .Should().Be(11);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~WorkingDaysCalculatorTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`CzechHolidays.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>Czech public holidays (zákon č. 245/2000 Sb.). Fixed dates plus Good Friday
/// and Easter Monday derived from the Gregorian Easter (Meeus/Jones/Butcher algorithm).</summary>
public static class CzechHolidays
{
    private static readonly (int Month, int Day)[] FixedHolidays =
    {
        (1, 1), (5, 1), (5, 8), (7, 5), (7, 6), (9, 28), (10, 28), (11, 17), (12, 24), (12, 25), (12, 26)
    };

    public static bool IsPublicHoliday(DateOnly date)
    {
        if (FixedHolidays.Any(h => h.Month == date.Month && h.Day == date.Day))
        {
            return true;
        }

        var easterSunday = EasterSunday(date.Year);
        return date == easterSunday.AddDays(-2)   // Velký pátek
            || date == easterSunday.AddDays(1);   // Velikonoční pondělí
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
```

`WorkingDaysCalculator.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public static class WorkingDaysCalculator
{
    /// <summary>Weekdays (Mon–Fri) in [from, toInclusive] that are not Czech public holidays.</summary>
    public static int CountWorkingDays(DateOnly from, DateOnly toInclusive)
    {
        var count = 0;
        for (var day = from; day <= toInclusive; day = day.AddDays(1))
        {
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            if (!isWeekend && !CzechHolidays.IsPublicHoliday(day))
            {
                count++;
            }
        }

        return count;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services backend/test/Anela.Heblo.Tests/Application/Overtime/WorkingDaysCalculatorTests.cs
git commit -m "feat: add Czech holiday and working-days calculators"
```

---

### Task 4: Overtime options + contract-hours provider

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/OvertimeOptions.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime/IContractHoursProvider.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/ConfigurationContractHoursProvider.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (new `Overtime` section), `OvertimeModule.cs` (bind options, register provider)
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/ConfigurationContractHoursProviderTests.cs`

**Interfaces:**
- Produces:
  - `IContractHoursProvider { Task<decimal?> GetDailyHoursAsync(Guid personId, int year, int month, CancellationToken ct); }` (Domain) — returns null when unknown.
  - `OvertimeOptions { const string ConfigKey = "Overtime"; Dictionary<string, string> ActivityCategories; Dictionary<string, decimal> ContractHours; string ExportDriveId; string ExportFolderPath; string ExportFileName; }`
  - `enum OvertimeActivityCategory { Work, Break, Vacation, Sick, Doctor, Ocr, CompTime, Other }` (in `OvertimeOptions.cs` file, Application layer — used by calculation and DTOs).

The spec says úvazek is read from Logeto; that API investigation runs in a separate session. `IContractHoursProvider` isolates it: this task ships the configuration-backed implementation (person GUID → daily hours in appsettings), and the Logeto-backed implementation replaces the DI registration later without touching anything else.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Application.Overtime;

public class ConfigurationContractHoursProviderTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task ReturnsConfiguredHours_ForKnownPerson()
    {
        var options = new OvertimeOptions
        {
            ContractHours = new Dictionary<string, decimal> { [Person.ToString()] = 6.4m }
        };
        var provider = new ConfigurationContractHoursProvider(Options.Create(options));

        var hours = await provider.GetDailyHoursAsync(Person, 2026, 9, CancellationToken.None);

        hours.Should().Be(6.4m);
    }

    [Fact]
    public async Task ReturnsNull_ForUnknownPerson()
    {
        var provider = new ConfigurationContractHoursProvider(Options.Create(new OvertimeOptions()));

        var hours = await provider.GetDailyHoursAsync(Guid.NewGuid(), 2026, 9, CancellationToken.None);

        hours.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~ConfigurationContractHoursProviderTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

`IContractHoursProvider.cs` (Domain, `Anela.Heblo.Domain.Features.Attendance.Overtime`):

```csharp
namespace Anela.Heblo.Domain.Features.Attendance.Overtime;

/// <summary>Daily contract hours (úvazek, without break — e.g. 6.4) per person and month.
/// Currently configuration-backed; will be swapped for a Logeto-backed implementation
/// once the Logeto úvazek API investigation lands.</summary>
public interface IContractHoursProvider
{
    Task<decimal?> GetDailyHoursAsync(Guid personId, int year, int month, CancellationToken cancellationToken);
}
```

`OvertimeOptions.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Overtime;

public enum OvertimeActivityCategory
{
    Work,
    Break,
    Vacation,
    Sick,
    Doctor,
    Ocr,
    CompTime,
    Other
}

public class OvertimeOptions
{
    public const string ConfigKey = "Overtime";

    /// <summary>Logeto activity name → category name (Vacation/Sick/Doctor/Ocr/CompTime).
    /// Activities with Logeto Type=Work/Break need no mapping; unmapped non-Work activities
    /// fall into Other (not credited, surfaced as a warning).</summary>
    public Dictionary<string, string> ActivityCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Person GUID (string) → daily contract hours without break. Temporary source
    /// until the Logeto-backed IContractHoursProvider lands.</summary>
    public Dictionary<string, decimal> ContractHours { get; set; } = new();

    /// <summary>SharePoint drive for the generated report; empty = publishing disabled.</summary>
    public string ExportDriveId { get; set; } = string.Empty;

    /// <summary>Folder path inside the drive, e.g. "Provoz/Mzdy". Empty = drive root.</summary>
    public string ExportFolderPath { get; set; } = string.Empty;

    public string ExportFileName { get; set; } = "Evidence-prescasu.xlsx";
}
```

`ConfigurationContractHoursProvider.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public class ConfigurationContractHoursProvider : IContractHoursProvider
{
    private readonly IOptions<OvertimeOptions> _options;

    public ConfigurationContractHoursProvider(IOptions<OvertimeOptions> options)
    {
        _options = options;
    }

    public Task<decimal?> GetDailyHoursAsync(Guid personId, int year, int month, CancellationToken cancellationToken)
    {
        decimal? result = _options.Value.ContractHours.TryGetValue(personId.ToString(), out var hours)
            ? hours
            : null;
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 4: Bind options and register provider**

In `OvertimeModule.AddOvertimeModule`, before the repository registrations:

```csharp
        services.AddOptions<OvertimeOptions>()
            .Bind(configuration.GetSection(OvertimeOptions.ConfigKey));

        services.AddScoped<IContractHoursProvider, Services.ConfigurationContractHoursProvider>();
```

In `appsettings.json`, add a top-level `Overtime` section (after `Logeto`):

```json
  "Overtime": {
    "ActivityCategories": {
      "Dovolená": "Vacation",
      "Nemoc": "Sick",
      "Sick day": "Sick",
      "Lékař": "Doctor",
      "OČR": "Ocr",
      "Náhradní volno": "CompTime"
    },
    "ContractHours": {},
    "ExportDriveId": "",
    "ExportFolderPath": "",
    "ExportFileName": "Evidence-prescasu.xlsx"
  }
```

(Real activity names must be verified against the live Logeto account before enabling in staging — same caveat as `BreakActivityName`.)

- [ ] **Step 5: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Attendance/Overtime/IContractHoursProvider.cs backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/src/Anela.Heblo.API/appsettings.json backend/test/Anela.Heblo.Tests/Application/Overtime/ConfigurationContractHoursProviderTests.cs
git commit -m "feat: add overtime options and contract-hours provider"
```

---

### Task 5: Overtime calculation service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/Services/OvertimeCalculationService.cs`
- Modify: `OvertimeModule.cs` (register service)
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeCalculationServiceTests.cs`

**Interfaces:**
- Consumes: `ILogetoClient` (Task: existing), `IContractHoursProvider` (Task 4), `WorkingDaysCalculator` (Task 3), `OvertimeOptions.ActivityCategories` (Task 4), `OvertimeEmployee` (Task 2).
- Produces (used by Tasks 7 and 9):

```csharp
public class OvertimeCalculationService
{
    public OvertimeCalculationService(ILogetoClient client, IContractHoursProvider contractHours, IOptions<OvertimeOptions> options, ILogger<OvertimeCalculationService> logger);
    public Task<IReadOnlyList<PersonMonthComputation>> ComputeMonthAsync(int year, int month, IReadOnlyList<OvertimeEmployee> employees, CancellationToken cancellationToken);
}

public class PersonMonthComputation
{
    public Guid PersonId { get; set; }
    public decimal? DailyContractHours { get; set; }   // null = missing úvazek
    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }
    public List<string> Warnings { get; set; } = new();
}
```

**Rules implemented (from spec):**
- One `GetTimeTrackingAsync(monthStart, monthEnd)` call; entries grouped per tracked person; entries dated before the person's `BaselineDate` ignored.
- Entry hours: `(To − From).TotalHours` when both set; else `TimeSpan.Parse(Hours)`; else 0 + warning `"Záznam bez hodin: {date} {activityName}"`. In-progress entries (`From` set, `To` null) → 0 + warning.
- Category resolution: Logeto activity `Type == "Break"` → Break (never counted); `Type == "Work"` and name NOT in `ActivityCategories` → Work; name found in `ActivityCategories` → mapped category (regardless of Logeto Type); otherwise → Other + warning `"Nezařazená aktivita: {activityName}"`.
- `RequiredHours = CountWorkingDays(max(monthStart, BaselineDate), monthEnd) × dailyÚvazek`, rounded 2 dp.
- `DeltaHours = credited hours − RequiredHours`. **Exact bucketing:** `VacationHours`, `SickHours`, `DoctorHours` have their own columns and are credited; `Ocr` is credited and displayed inside `OtherAbsenceHours`; `Other` is NOT credited and also displayed inside `OtherAbsenceHours` (with a warning); `CompTime` is displayed but never credited. The computation tracks `creditedHours` as its own accumulator — `OtherAbsenceHours` is display-only and must not feed the delta directly.
- Employees whose `BaselineDate` is after the month's last day are skipped (no computation row).
- Missing úvazek → `DailyContractHours = null`, `RequiredHours = 0`, warning `"Chybí úvazek"` (close will refuse via error 3406).

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeCalculationServiceTests
{
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BreakActivity = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid VacationActivity = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid CompTimeActivity = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IContractHoursProvider> _contractHours = new();

    public OvertimeCalculationServiceTests()
    {
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = BreakActivity, Name = "Přestávka", Type = LogetoActivityTypes.Break },
                new() { Guid = VacationActivity, Name = "Dovolená", Type = LogetoActivityTypes.Work },
                new() { Guid = CompTimeActivity, Name = "Náhradní volno", Type = LogetoActivityTypes.Work }
            });
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6.4m);
    }

    private OvertimeCalculationService CreateSut()
    {
        var options = new OvertimeOptions
        {
            ActivityCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Dovolená"] = "Vacation",
                ["Náhradní volno"] = "CompTime"
            }
        };
        return new OvertimeCalculationService(
            _client.Object, _contractHours.Object, Options.Create(options),
            NullLogger<OvertimeCalculationService>.Instance);
    }

    private static OvertimeEmployee Employee(DateOnly? baseline = null) => new()
    {
        PersonId = Person, DisplayName = "Pepina",
        BaselineDate = baseline ?? new DateOnly(2026, 8, 1), IsActive = true
    };

    private void SetupEntries(params LogetoTimeEntry[] entries)
        => _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries.ToList());

    private static LogetoTimeEntry Entry(Guid activity, DateOnly date, int fromH, int toH) => new()
    {
        Guid = Guid.NewGuid(), Person = Person, Date = date, Activity = activity,
        From = new DateTimeOffset(date.Year, date.Month, date.Day, fromH, 0, 0, TimeSpan.Zero),
        To = new DateTimeOffset(date.Year, date.Month, date.Day, toH, 0, 0, TimeSpan.Zero)
    };

    private static LogetoTimeEntry HoursEntry(Guid activity, DateOnly date, string hours) => new()
    {
        Guid = Guid.NewGuid(), Person = Person, Date = date, Activity = activity, Hours = hours
    };

    [Fact]
    public async Task ComputesDelta_WorkAndVacationCredited_BreaksExcluded()
    {
        // August 2026 has 21 working days → required = 21 × 6.4 = 134.4
        SetupEntries(
            Entry(WorkActivity, new DateOnly(2026, 8, 3), 8, 14),          // 6h work
            Entry(BreakActivity, new DateOnly(2026, 8, 3), 11, 12),        // 1h break — ignored
            HoursEntry(VacationActivity, new DateOnly(2026, 8, 4), "06:24:00")); // 6.4h vacation

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.RequiredHours.Should().Be(134.40m);
        row.WorkedHours.Should().Be(6.00m);
        row.VacationHours.Should().Be(6.40m);
        row.DeltaHours.Should().Be(6.00m + 6.40m - 134.40m);
    }

    [Fact]
    public async Task CompTime_IsNotCredited()
    {
        SetupEntries(HoursEntry(CompTimeActivity, new DateOnly(2026, 8, 3), "06:24:00"));

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.CompTimeHours.Should().Be(6.40m);
        row.DeltaHours.Should().Be(-134.40m);   // comp time gives no credit
    }

    [Fact]
    public async Task EntriesBeforeBaseline_AreIgnored_AndRequiredCountsFromBaseline()
    {
        // Baseline 2026-08-17 (Mon): 11 working days remain → required = 11 × 6.4 = 70.4
        SetupEntries(
            Entry(WorkActivity, new DateOnly(2026, 8, 10), 8, 16),   // before baseline — ignored
            Entry(WorkActivity, new DateOnly(2026, 8, 18), 8, 16));  // 8h counted

        var result = await CreateSut().ComputeMonthAsync(
            2026, 8, new[] { Employee(baseline: new DateOnly(2026, 8, 17)) }, CancellationToken.None);

        var row = result.Single();
        row.RequiredHours.Should().Be(70.40m);
        row.WorkedHours.Should().Be(8.00m);
    }

    [Fact]
    public async Task BaselineAfterMonth_SkipsEmployee()
    {
        SetupEntries();

        var result = await CreateSut().ComputeMonthAsync(
            2026, 8, new[] { Employee(baseline: new DateOnly(2026, 9, 1)) }, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingContractHours_ProducesWarning_AndNullContract()
    {
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);
        SetupEntries(Entry(WorkActivity, new DateOnly(2026, 8, 3), 8, 16));

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.DailyContractHours.Should().BeNull();
        row.RequiredHours.Should().Be(0m);
        row.Warnings.Should().Contain(w => w.Contains("úvazek"));
    }

    [Fact]
    public async Task HourlessAndInProgressEntries_ProduceWarnings()
    {
        SetupEntries(
            new LogetoTimeEntry { Guid = Guid.NewGuid(), Person = Person, Date = new DateOnly(2026, 8, 3), Activity = VacationActivity }, // no hours at all
            new LogetoTimeEntry
            {
                Guid = Guid.NewGuid(), Person = Person, Date = new DateOnly(2026, 8, 4), Activity = WorkActivity,
                From = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero)   // in progress
            });

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        result.Single().Warnings.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task UnmappedNonWorkActivity_GoesToOther_NotCredited_WithWarning()
    {
        var unknown = Guid.NewGuid();
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity>
            {
                new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work },
                new() { Guid = unknown, Name = "Školení???", Type = "Absence" }
            });
        SetupEntries(HoursEntry(unknown, new DateOnly(2026, 8, 3), "04:00:00"));

        var result = await CreateSut().ComputeMonthAsync(2026, 8, new[] { Employee() }, CancellationToken.None);

        var row = result.Single();
        row.OtherAbsenceHours.Should().Be(4.00m);
        row.DeltaHours.Should().Be(-134.40m);   // not credited
        row.Warnings.Should().Contain(w => w.Contains("Školení???"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~OvertimeCalculationServiceTests"`
Expected: FAIL — `OvertimeCalculationService` doesn't exist.

- [ ] **Step 3: Implement the service**

`OvertimeCalculationService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public class PersonMonthComputation
{
    public Guid PersonId { get; set; }
    public decimal? DailyContractHours { get; set; }
    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class OvertimeCalculationService
{
    private readonly ILogetoClient _client;
    private readonly IContractHoursProvider _contractHours;
    private readonly IOptions<OvertimeOptions> _options;
    private readonly ILogger<OvertimeCalculationService> _logger;

    public OvertimeCalculationService(
        ILogetoClient client,
        IContractHoursProvider contractHours,
        IOptions<OvertimeOptions> options,
        ILogger<OvertimeCalculationService> logger)
    {
        _client = client;
        _contractHours = contractHours;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PersonMonthComputation>> ComputeMonthAsync(
        int year, int month, IReadOnlyList<OvertimeEmployee> employees, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var activities = await _client.GetActivitiesAsync(cancellationToken);
        var entries = await _client.GetTimeTrackingAsync(monthStart, monthEnd, cancellationToken);

        var categoryByActivity = BuildCategoryMap(activities);
        var nameByActivity = activities.ToDictionary(a => a.Guid, a => a.Name ?? a.Guid.ToString());
        var entriesByPerson = entries.GroupBy(e => e.Person).ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<PersonMonthComputation>();

        foreach (var employee in employees)
        {
            if (employee.BaselineDate > monthEnd)
            {
                continue;
            }

            var effectiveStart = employee.BaselineDate > monthStart ? employee.BaselineDate : monthStart;
            var row = new PersonMonthComputation { PersonId = employee.PersonId };
            var credited = 0m;

            var personEntries = entriesByPerson.TryGetValue(employee.PersonId, out var list)
                ? list.Where(e => e.Date >= effectiveStart)
                : Enumerable.Empty<LogetoTimeEntry>();

            foreach (var entry in personEntries)
            {
                var category = categoryByActivity.TryGetValue(entry.Activity, out var c)
                    ? c
                    : OvertimeActivityCategory.Other;

                if (category == OvertimeActivityCategory.Break)
                {
                    continue;
                }

                var activityName = nameByActivity.TryGetValue(entry.Activity, out var n) ? n : entry.Activity.ToString();
                var hours = GetEntryHours(entry, activityName, row.Warnings);

                if (category == OvertimeActivityCategory.Other)
                {
                    row.Warnings.Add($"Nezařazená aktivita: {activityName} ({entry.Date:yyyy-MM-dd})");
                }

                switch (category)
                {
                    case OvertimeActivityCategory.Work:
                        row.WorkedHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Vacation:
                        row.VacationHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Sick:
                        row.SickHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Doctor:
                        row.DoctorHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.Ocr:
                        row.OtherAbsenceHours += hours;
                        credited += hours;
                        break;
                    case OvertimeActivityCategory.CompTime:
                        row.CompTimeHours += hours;   // visible, never credited
                        break;
                    case OvertimeActivityCategory.Other:
                        row.OtherAbsenceHours += hours;   // visible, never credited
                        break;
                }
            }

            var daily = await _contractHours.GetDailyHoursAsync(employee.PersonId, year, month, cancellationToken);
            row.DailyContractHours = daily;
            if (daily is null)
            {
                row.Warnings.Add("Chybí úvazek");
            }
            else
            {
                var workingDays = WorkingDaysCalculator.CountWorkingDays(effectiveStart, monthEnd);
                row.RequiredHours = Round(workingDays * daily.Value);
            }

            row.WorkedHours = Round(row.WorkedHours);
            row.VacationHours = Round(row.VacationHours);
            row.SickHours = Round(row.SickHours);
            row.DoctorHours = Round(row.DoctorHours);
            row.CompTimeHours = Round(row.CompTimeHours);
            row.OtherAbsenceHours = Round(row.OtherAbsenceHours);
            row.DeltaHours = Round(credited) - row.RequiredHours;

            results.Add(row);
        }

        return results;
    }

    private Dictionary<Guid, OvertimeActivityCategory> BuildCategoryMap(IReadOnlyList<LogetoActivity> activities)
    {
        var map = new Dictionary<Guid, OvertimeActivityCategory>();
        var configured = _options.Value.ActivityCategories;

        foreach (var activity in activities)
        {
            if (activity.Name is not null
                && configured.TryGetValue(activity.Name, out var categoryName)
                && Enum.TryParse<OvertimeActivityCategory>(categoryName, ignoreCase: true, out var mapped))
            {
                map[activity.Guid] = mapped;
            }
            else if (activity.Type == LogetoActivityTypes.Break)
            {
                map[activity.Guid] = OvertimeActivityCategory.Break;
            }
            else if (activity.Type == LogetoActivityTypes.Work)
            {
                map[activity.Guid] = OvertimeActivityCategory.Work;
            }
            else
            {
                map[activity.Guid] = OvertimeActivityCategory.Other;
            }
        }

        return map;
    }

    private static decimal GetEntryHours(LogetoTimeEntry entry, string activityName, List<string> warnings)
    {
        if (entry.From.HasValue && entry.To.HasValue)
        {
            return (decimal)(entry.To.Value - entry.From.Value).TotalHours;
        }

        if (entry.From.HasValue && !entry.To.HasValue)
        {
            warnings.Add($"Neuzavřený záznam: {entry.Date:yyyy-MM-dd} {activityName}");
            return 0m;
        }

        if (!string.IsNullOrWhiteSpace(entry.Hours) && TimeSpan.TryParse(entry.Hours, out var span))
        {
            return (decimal)span.TotalHours;
        }

        warnings.Add($"Záznam bez hodin: {entry.Date:yyyy-MM-dd} {activityName}");
        return 0m;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
```

Register in `OvertimeModule.AddOvertimeModule`:

```csharp
        services.AddScoped<Services.OvertimeCalculationService>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeCalculationServiceTests.cs
git commit -m "feat: add overtime month calculation service"
```

---

### Task 6: Use cases — GetOvertimeEmployees + UpsertOvertimeEmployee

**Files:**
- Create: `.../Overtime/Contracts/OvertimeEmployeeDto.cs`
- Create: `.../Overtime/UseCases/GetOvertimeEmployees/GetOvertimeEmployeesRequest.cs`, `GetOvertimeEmployeesResponse.cs`, `GetOvertimeEmployeesHandler.cs`
- Create: `.../Overtime/UseCases/UpsertOvertimeEmployee/UpsertOvertimeEmployeeRequest.cs`, `UpsertOvertimeEmployeeResponse.cs`, `UpsertOvertimeEmployeeHandler.cs`, `UpsertOvertimeEmployeeValidator.cs`
- Modify: `OvertimeModule.cs` (validator + behavior registration)
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeEmployeeUseCaseTests.cs`

(`.../Overtime/` = `backend/src/Anela.Heblo.Application/Features/Attendance/Overtime/`.)

**Interfaces:**
- Consumes: repositories (Task 2), `ILogetoClient.GetPeopleAsync`.
- Produces (used by controller Task 12 and FE):
  - `OvertimeEmployeeDto { Guid PersonId; string DisplayName; decimal BaselineHours; DateOnly BaselineDate; bool IsActive; decimal CurrentBalance; }`
  - `GetOvertimeEmployeesResponse : BaseResponse { List<OvertimeEmployeeDto> Employees; List<AvailableLogetoPersonDto> AvailablePeople; }` with `AvailableLogetoPersonDto { Guid PersonId; string FullName; }` (active Logeto people not yet tracked — feeds the "add employee" picker).
  - `UpsertOvertimeEmployeeRequest : IRequest<UpsertOvertimeEmployeeResponse> { Guid PersonId; string DisplayName; decimal BaselineHours; DateOnly BaselineDate; bool IsActive; }`
  - `CurrentBalance` = latest closed statement's `BalanceAfter`, else `BaselineHours`.

- [ ] **Step 1: Write failing tests**

`OvertimeEmployeeUseCaseTests.cs` — Moq pattern (`CreateSut()`, field mocks):

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeEmployeeUseCaseTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<ILogetoClient> _client = new();

    [Fact]
    public async Task GetEmployees_ReturnsBalanceFromLatestClosedStatement_AndUntrackedPeople()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, IsActive = true }
            });
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeMonthlyStatement { PersonId = Person, Year = 2026, Month = 9, BalanceAfter = 7.5m, Status = OvertimeStatementStatus.Closed });
        var untracked = Guid.NewGuid();
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>
            {
                new() { Guid = Person, FirstName = "Pepina", LastName = "H." },
                new() { Guid = untracked, FirstName = "Bára", LastName = "Petrová" },
                new() { Guid = Guid.NewGuid(), FirstName = "Ex", LastName = "Worker", Inactive = true }
            });

        var handler = new GetOvertimeEmployeesHandler(_employees.Object, _statements.Object, _client.Object);
        var result = await handler.Handle(new GetOvertimeEmployeesRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Employees.Single().CurrentBalance.Should().Be(7.5m);
        result.AvailablePeople.Should().ContainSingle(p => p.PersonId == untracked && p.FullName == "Bára Petrová");
    }

    [Fact]
    public async Task GetEmployees_FallsBackToBaseline_WhenNoClosedStatement()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, IsActive = true }
            });
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeMonthlyStatement?)null);
        _client.Setup(c => c.GetPeopleAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoPerson>());

        var handler = new GetOvertimeEmployeesHandler(_employees.Object, _statements.Object, _client.Object);
        var result = await handler.Handle(new GetOvertimeEmployeesRequest(), CancellationToken.None);

        result.Employees.Single().CurrentBalance.Should().Be(2.5m);
    }

    [Fact]
    public async Task Upsert_RejectsBaselineChange_WhenClosedStatementExists()
    {
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeEmployee { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 9, 1) });
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeMonthlyStatement { PersonId = Person, Status = OvertimeStatementStatus.Closed });

        var handler = new UpsertOvertimeEmployeeHandler(_employees.Object, _statements.Object);
        var result = await handler.Handle(new UpsertOvertimeEmployeeRequest
        {
            PersonId = Person, DisplayName = "Pepina", BaselineHours = 99m, BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ValidationError);
        _employees.Verify(r => r.UpsertAsync(It.IsAny<OvertimeEmployee>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upsert_SavesEmployee_WhenNoClosedStatement()
    {
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeEmployee?)null);
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeMonthlyStatement?)null);

        var handler = new UpsertOvertimeEmployeeHandler(_employees.Object, _statements.Object);
        var result = await handler.Handle(new UpsertOvertimeEmployeeRequest
        {
            PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 9, 1), IsActive = true
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _employees.Verify(r => r.UpsertAsync(It.Is<OvertimeEmployee>(e => e.PersonId == Person && e.BaselineHours == 2.5m), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~OvertimeEmployeeUseCaseTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement DTOs, requests, handlers, validator**

`Contracts/OvertimeEmployeeDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;

public class OvertimeEmployeeDto
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal BaselineHours { get; set; }
    public DateOnly BaselineDate { get; set; }
    public bool IsActive { get; set; }
    public decimal CurrentBalance { get; set; }
}

public class AvailableLogetoPersonDto
{
    public Guid PersonId { get; set; }
    public string FullName { get; set; } = string.Empty;
}
```

`UseCases/GetOvertimeEmployees/GetOvertimeEmployeesRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;

public class GetOvertimeEmployeesRequest : IRequest<GetOvertimeEmployeesResponse>
{
}
```

`GetOvertimeEmployeesResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;

public class GetOvertimeEmployeesResponse : BaseResponse
{
    public List<OvertimeEmployeeDto> Employees { get; set; } = new();
    public List<AvailableLogetoPersonDto> AvailablePeople { get; set; } = new();
}
```

`GetOvertimeEmployeesHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;

public class GetOvertimeEmployeesHandler : IRequestHandler<GetOvertimeEmployeesRequest, GetOvertimeEmployeesResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly ILogetoClient _client;

    public GetOvertimeEmployeesHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        ILogetoClient client)
    {
        _employees = employees;
        _statements = statements;
        _client = client;
    }

    public async Task<GetOvertimeEmployeesResponse> Handle(GetOvertimeEmployeesRequest request, CancellationToken cancellationToken)
    {
        var tracked = await _employees.GetAllAsync(cancellationToken);
        var people = await _client.GetPeopleAsync(cancellationToken);

        var response = new GetOvertimeEmployeesResponse();

        foreach (var employee in tracked)
        {
            var latestClosed = await _statements.GetLatestClosedAsync(employee.PersonId, cancellationToken);
            response.Employees.Add(new OvertimeEmployeeDto
            {
                PersonId = employee.PersonId,
                DisplayName = employee.DisplayName,
                BaselineHours = employee.BaselineHours,
                BaselineDate = employee.BaselineDate,
                IsActive = employee.IsActive,
                CurrentBalance = latestClosed?.BalanceAfter ?? employee.BaselineHours
            });
        }

        var trackedIds = tracked.Select(t => t.PersonId).ToHashSet();
        response.AvailablePeople = people
            .Where(p => !p.Inactive && !trackedIds.Contains(p.Guid))
            .Select(p => new AvailableLogetoPersonDto
            {
                PersonId = p.Guid,
                FullName = $"{p.FirstName} {p.LastName}".Trim()
            })
            .OrderBy(p => p.FullName)
            .ToList();

        return response;
    }
}
```

`UseCases/UpsertOvertimeEmployee/UpsertOvertimeEmployeeRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeRequest : IRequest<UpsertOvertimeEmployeeResponse>
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public decimal BaselineHours { get; set; }
    public DateOnly BaselineDate { get; set; }
    public bool IsActive { get; set; }
}
```

`UpsertOvertimeEmployeeResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeResponse : BaseResponse
{
}
```

`UpsertOvertimeEmployeeHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeHandler : IRequestHandler<UpsertOvertimeEmployeeRequest, UpsertOvertimeEmployeeResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;

    public UpsertOvertimeEmployeeHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements)
    {
        _employees = employees;
        _statements = statements;
    }

    public async Task<UpsertOvertimeEmployeeResponse> Handle(UpsertOvertimeEmployeeRequest request, CancellationToken cancellationToken)
    {
        var existing = await _employees.GetByPersonIdAsync(request.PersonId, cancellationToken);
        var latestClosed = await _statements.GetLatestClosedAsync(request.PersonId, cancellationToken);

        var baselineChanged = existing is not null
            && (existing.BaselineHours != request.BaselineHours || existing.BaselineDate != request.BaselineDate);

        if (latestClosed is not null && baselineChanged)
        {
            return new UpsertOvertimeEmployeeResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.ValidationError,
                Params = new Dictionary<string, string>
                {
                    { "message", "Baseline nelze měnit — zaměstnanec už má uzavřený měsíc." }
                }
            };
        }

        await _employees.UpsertAsync(new OvertimeEmployee
        {
            PersonId = request.PersonId,
            DisplayName = request.DisplayName,
            BaselineHours = request.BaselineHours,
            BaselineDate = request.BaselineDate,
            IsActive = request.IsActive
        }, cancellationToken);

        return new UpsertOvertimeEmployeeResponse();
    }
}
```

`UpsertOvertimeEmployeeValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;

public class UpsertOvertimeEmployeeValidator : AbstractValidator<UpsertOvertimeEmployeeRequest>
{
    public UpsertOvertimeEmployeeValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaselineHours).InclusiveBetween(-1000m, 1000m);
        RuleFor(x => x.BaselineDate).NotEmpty();
    }
}
```

- [ ] **Step 4: Register validator + behavior**

In `OvertimeModule.AddOvertimeModule` (both lines — this codebase has no assembly scan for validators):

```csharp
        services.AddScoped<IValidator<UpsertOvertimeEmployeeRequest>, UpsertOvertimeEmployeeValidator>();
        services.AddScoped<IPipelineBehavior<UpsertOvertimeEmployeeRequest, UpsertOvertimeEmployeeResponse>,
            ValidationBehavior<UpsertOvertimeEmployeeRequest, UpsertOvertimeEmployeeResponse>>();
```

with usings `FluentValidation`, `MediatR`, `Anela.Heblo.Application.Common.Behaviors`, and the use-case namespaces.

- [ ] **Step 5: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeEmployeeUseCaseTests.cs
git commit -m "feat: add overtime employee use cases"
```

---

### Task 7: Use case — GetMonthlyStatements

**Files:**
- Create: `.../Overtime/Contracts/OvertimeStatementDto.cs`, `Contracts/OvertimeAdjustmentDto.cs`
- Create: `.../Overtime/UseCases/GetMonthlyStatements/GetMonthlyStatementsRequest.cs`, `GetMonthlyStatementsResponse.cs`, `GetMonthlyStatementsHandler.cs`
- Modify: `OvertimeModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs`

**Interfaces:**
- Consumes: `OvertimeCalculationService.ComputeMonthAsync` (Task 5), repositories (Task 2).
- Produces (used by Tasks 12, 14):
  - `OvertimeAdjustmentDto { int Id; Guid PersonId; OvertimeAdjustmentType Type; decimal Hours; string Note; DateTime CreatedAtUtc; string CreatedBy; }`
  - `OvertimeStatementDto { Guid PersonId; string DisplayName; bool IsReviewed; decimal? DailyContractHours; decimal RequiredHours; decimal WorkedHours; decimal VacationHours; decimal SickHours; decimal DoctorHours; decimal CompTimeHours; decimal OtherAbsenceHours; decimal DeltaHours; decimal PreviousBalance; decimal AdjustmentsTotal; decimal ProjectedBalance; List<string> Warnings; List<OvertimeAdjustmentDto> Adjustments; }`
  - `GetMonthlyStatementsRequest : IRequest<GetMonthlyStatementsResponse> { int Year; int Month; }`
  - `GetMonthlyStatementsResponse : BaseResponse { int Year; int Month; bool IsClosed; List<OvertimeStatementDto> Statements; }`

**Behavior:**
- Month is closed ⇔ any statement for (Year, Month) has `Status == Closed`. Closed → DTOs built purely from frozen statements + stored adjustments (`ProjectedBalance = BalanceAfter`, `Warnings` empty, `PreviousBalance = BalanceAfter − DeltaHours − AdjustmentsTotal`).
- Open → compute via `OvertimeCalculationService` for active employees; **materialize** a statement row per person (create Open row if missing, else refresh its hour fields); `PreviousBalance` = latest closed `BalanceAfter` ?? `BaselineHours`; `AdjustmentsTotal` = Σ `Hours` of that person-month's adjustments; `ProjectedBalance = PreviousBalance + DeltaHours + AdjustmentsTotal`.
- Logeto failure: wrap the compute call in try/catch of `Exception`, return `new GetMonthlyStatementsResponse(ex)` (BaseResponse exception ctor) so the FE shows a toast instead of a blank 500.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class GetMonthlyStatementsHandlerTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<IOvertimeAdjustmentRepository> _adjustments = new();
    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IContractHoursProvider> _contractHours = new();

    public GetMonthlyStatementsHandlerTests()
    {
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 8, 1), IsActive = true }
            });
        _statements.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeMonthlyStatement>());
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeMonthlyStatement?)null);
        _adjustments.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeAdjustment>());
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity> { new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work } });
        _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoTimeEntry>
            {
                new()
                {
                    Guid = Guid.NewGuid(), Person = Person, Date = new DateOnly(2026, 8, 3), Activity = WorkActivity,
                    From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
                    To = new DateTimeOffset(2026, 8, 3, 16, 0, 0, TimeSpan.Zero)
                }
            });
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, 2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(6.4m);
    }

    private GetMonthlyStatementsHandler CreateSut()
    {
        var calc = new OvertimeCalculationService(
            _client.Object, _contractHours.Object, Options.Create(new OvertimeOptions()),
            NullLogger<OvertimeCalculationService>.Instance);
        return new GetMonthlyStatementsHandler(_employees.Object, _statements.Object, _adjustments.Object, calc);
    }

    [Fact]
    public async Task OpenMonth_ComputesLive_MaterializesStatement_AndProjectsBalance()
    {
        var result = await CreateSut().Handle(new GetMonthlyStatementsRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.IsClosed.Should().BeFalse();
        var dto = result.Statements.Single();
        dto.WorkedHours.Should().Be(8.00m);
        dto.RequiredHours.Should().Be(134.40m);   // 21 working days × 6.4
        dto.PreviousBalance.Should().Be(2.5m);
        dto.ProjectedBalance.Should().Be(2.5m + dto.DeltaHours);
        _statements.Verify(r => r.AddAsync(It.Is<OvertimeMonthlyStatement>(
            s => s.PersonId == Person && s.Status == OvertimeStatementStatus.Open && s.WorkedHours == 8.00m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClosedMonth_ReturnsFrozenNumbers_WithoutTouchingLogeto()
    {
        _statements.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeMonthlyStatement>
            {
                new()
                {
                    PersonId = Person, Year = 2026, Month = 8, Status = OvertimeStatementStatus.Closed,
                    RequiredHours = 134.4m, WorkedHours = 130m, DeltaHours = -4.4m, BalanceAfter = -1.9m, IsReviewed = true
                }
            });

        var result = await CreateSut().Handle(new GetMonthlyStatementsRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.IsClosed.Should().BeTrue();
        result.Statements.Single().ProjectedBalance.Should().Be(-1.9m);
        _client.Verify(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogetoFailure_ReturnsErrorResponse_NotException()
    {
        _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Logeto down"));

        var result = await CreateSut().Handle(new GetMonthlyStatementsRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~GetMonthlyStatementsHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`Contracts/OvertimeAdjustmentDto.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;

public class OvertimeAdjustmentDto
{
    public int Id { get; set; }
    public Guid PersonId { get; set; }
    public OvertimeAdjustmentType Type { get; set; }
    public decimal Hours { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
```

`Contracts/OvertimeStatementDto.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;

public class OvertimeStatementDto
{
    public Guid PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsReviewed { get; set; }
    public decimal? DailyContractHours { get; set; }
    public decimal RequiredHours { get; set; }
    public decimal WorkedHours { get; set; }
    public decimal VacationHours { get; set; }
    public decimal SickHours { get; set; }
    public decimal DoctorHours { get; set; }
    public decimal CompTimeHours { get; set; }
    public decimal OtherAbsenceHours { get; set; }
    public decimal DeltaHours { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal AdjustmentsTotal { get; set; }
    public decimal ProjectedBalance { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<OvertimeAdjustmentDto> Adjustments { get; set; } = new();
}
```

`GetMonthlyStatementsRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;

public class GetMonthlyStatementsRequest : IRequest<GetMonthlyStatementsResponse>
{
    public int Year { get; set; }
    public int Month { get; set; }
}
```

`GetMonthlyStatementsResponse.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;

public class GetMonthlyStatementsResponse : BaseResponse
{
    public GetMonthlyStatementsResponse() { }
    public GetMonthlyStatementsResponse(Exception ex) : base(ex) { }

    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsClosed { get; set; }
    public List<OvertimeStatementDto> Statements { get; set; } = new();
}
```

`GetMonthlyStatementsHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Contracts;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;

public class GetMonthlyStatementsHandler : IRequestHandler<GetMonthlyStatementsRequest, GetMonthlyStatementsResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly OvertimeCalculationService _calculation;

    public GetMonthlyStatementsHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        OvertimeCalculationService calculation)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _calculation = calculation;
    }

    public async Task<GetMonthlyStatementsResponse> Handle(GetMonthlyStatementsRequest request, CancellationToken cancellationToken)
    {
        var allEmployees = await _employees.GetAllAsync(cancellationToken);
        var byPerson = allEmployees.ToDictionary(e => e.PersonId);
        var existing = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        var monthAdjustments = await _adjustments.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        var isClosed = existing.Any(s => s.Status == OvertimeStatementStatus.Closed);

        var response = new GetMonthlyStatementsResponse
        {
            Year = request.Year,
            Month = request.Month,
            IsClosed = isClosed
        };

        if (isClosed)
        {
            foreach (var statement in existing.OrderBy(s => byPerson.TryGetValue(s.PersonId, out var e) ? e.DisplayName : ""))
            {
                var adjustments = MapAdjustments(monthAdjustments, statement.PersonId);
                var adjustmentsTotal = adjustments.Sum(a => a.Hours);
                response.Statements.Add(new OvertimeStatementDto
                {
                    PersonId = statement.PersonId,
                    DisplayName = byPerson.TryGetValue(statement.PersonId, out var emp) ? emp.DisplayName : statement.PersonId.ToString(),
                    IsReviewed = statement.IsReviewed,
                    RequiredHours = statement.RequiredHours,
                    WorkedHours = statement.WorkedHours,
                    VacationHours = statement.VacationHours,
                    SickHours = statement.SickHours,
                    DoctorHours = statement.DoctorHours,
                    CompTimeHours = statement.CompTimeHours,
                    OtherAbsenceHours = statement.OtherAbsenceHours,
                    DeltaHours = statement.DeltaHours,
                    PreviousBalance = statement.BalanceAfter - statement.DeltaHours - adjustmentsTotal,
                    AdjustmentsTotal = adjustmentsTotal,
                    ProjectedBalance = statement.BalanceAfter,
                    Adjustments = adjustments
                });
            }

            return response;
        }

        IReadOnlyList<PersonMonthComputation> computations;
        try
        {
            var active = allEmployees.Where(e => e.IsActive).ToList();
            computations = await _calculation.ComputeMonthAsync(request.Year, request.Month, active, cancellationToken);
        }
        catch (Exception ex)
        {
            return new GetMonthlyStatementsResponse(ex);
        }

        var existingByPerson = existing.ToDictionary(s => s.PersonId);

        foreach (var computation in computations)
        {
            var employee = byPerson[computation.PersonId];

            if (!existingByPerson.TryGetValue(computation.PersonId, out var statement))
            {
                statement = new OvertimeMonthlyStatement
                {
                    PersonId = computation.PersonId,
                    Year = request.Year,
                    Month = request.Month,
                    Status = OvertimeStatementStatus.Open
                };
                CopyComputation(statement, computation);
                await _statements.AddAsync(statement, cancellationToken);
            }
            else
            {
                CopyComputation(statement, computation);
                await _statements.SaveChangesAsync(cancellationToken);
            }

            var latestClosed = await _statements.GetLatestClosedAsync(computation.PersonId, cancellationToken);
            var previousBalance = latestClosed?.BalanceAfter ?? employee.BaselineHours;
            var adjustments = MapAdjustments(monthAdjustments, computation.PersonId);
            var adjustmentsTotal = adjustments.Sum(a => a.Hours);

            response.Statements.Add(new OvertimeStatementDto
            {
                PersonId = computation.PersonId,
                DisplayName = employee.DisplayName,
                IsReviewed = statement.IsReviewed,
                DailyContractHours = computation.DailyContractHours,
                RequiredHours = computation.RequiredHours,
                WorkedHours = computation.WorkedHours,
                VacationHours = computation.VacationHours,
                SickHours = computation.SickHours,
                DoctorHours = computation.DoctorHours,
                CompTimeHours = computation.CompTimeHours,
                OtherAbsenceHours = computation.OtherAbsenceHours,
                DeltaHours = computation.DeltaHours,
                PreviousBalance = previousBalance,
                AdjustmentsTotal = adjustmentsTotal,
                ProjectedBalance = previousBalance + computation.DeltaHours + adjustmentsTotal,
                Warnings = computation.Warnings,
                Adjustments = adjustments
            });
        }

        response.Statements = response.Statements.OrderBy(s => s.DisplayName).ToList();
        return response;
    }

    private static void CopyComputation(OvertimeMonthlyStatement statement, PersonMonthComputation computation)
    {
        statement.RequiredHours = computation.RequiredHours;
        statement.WorkedHours = computation.WorkedHours;
        statement.VacationHours = computation.VacationHours;
        statement.SickHours = computation.SickHours;
        statement.DoctorHours = computation.DoctorHours;
        statement.CompTimeHours = computation.CompTimeHours;
        statement.OtherAbsenceHours = computation.OtherAbsenceHours;
        statement.DeltaHours = computation.DeltaHours;
    }

    private static List<OvertimeAdjustmentDto> MapAdjustments(IReadOnlyList<OvertimeAdjustment> monthAdjustments, Guid personId)
        => monthAdjustments
            .Where(a => a.PersonId == personId)
            .Select(a => new OvertimeAdjustmentDto
            {
                Id = a.Id,
                PersonId = a.PersonId,
                Type = a.Type,
                Hours = a.Hours,
                Note = a.Note,
                CreatedAtUtc = a.CreatedAtUtc,
                CreatedBy = a.CreatedBy
            })
            .ToList();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/test/Anela.Heblo.Tests/Application/Overtime/GetMonthlyStatementsHandlerTests.cs
git commit -m "feat: add monthly overtime statements query"
```

---

### Task 8: Use cases — SetStatementReviewed, CreateAdjustment, DeleteAdjustment

**Files:**
- Create: `.../Overtime/UseCases/SetStatementReviewed/SetStatementReviewedRequest.cs`, `SetStatementReviewedResponse.cs`, `SetStatementReviewedHandler.cs`
- Create: `.../Overtime/UseCases/CreateAdjustment/CreateAdjustmentRequest.cs`, `CreateAdjustmentResponse.cs`, `CreateAdjustmentHandler.cs`, `CreateAdjustmentValidator.cs`
- Create: `.../Overtime/UseCases/DeleteAdjustment/DeleteAdjustmentRequest.cs`, `DeleteAdjustmentResponse.cs`, `DeleteAdjustmentHandler.cs`
- Modify: `OvertimeModule.cs` (validator + behavior for CreateAdjustment)
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeAdjustmentUseCaseTests.cs`

**Interfaces:**
- Consumes: repositories (Task 2), `ICurrentUserService` (existing, `Anela.Heblo.Domain.Features.Users`), `TimeProvider`.
- Produces:
  - `SetStatementReviewedRequest : IRequest<SetStatementReviewedResponse> { Guid PersonId; int Year; int Month; bool IsReviewed; }`
  - `CreateAdjustmentRequest : IRequest<CreateAdjustmentResponse> { Guid PersonId; int Year; int Month; OvertimeAdjustmentType Type; decimal Hours; string Note; }`; `CreateAdjustmentResponse : BaseResponse { int Id; }`
  - `DeleteAdjustmentRequest : IRequest<DeleteAdjustmentResponse> { int Id; }`
- Guards: month already closed → `OvertimeMonthAlreadyClosed` (3402) for reviewed toggle, `OvertimeAdjustmentMonthClosed` (3404) for adjustments; unknown employee → `OvertimeEmployeeNotFound` (3401); unknown adjustment → `OvertimeAdjustmentNotFound` (3403). "Month closed" test: `GetByMonthAsync(year, month)` contains any `Status == Closed`.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeAdjustmentUseCaseTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<IOvertimeAdjustmentRepository> _adjustments = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    public OvertimeAdjustmentUseCaseTests()
    {
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-123", Name: "Andy", Email: null, IsAuthenticated: true));
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeEmployee { PersonId = Person, DisplayName = "Pepina" });
        SetupMonth(OvertimeStatementStatus.Open);
    }

    private void SetupMonth(OvertimeStatementStatus status)
        => _statements.Setup(r => r.GetByMonthAsync(2026, 9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeMonthlyStatement>
            {
                new() { PersonId = Person, Year = 2026, Month = 9, Status = status }
            });

    [Fact]
    public async Task SetReviewed_TogglesFlag_OnOpenStatement()
    {
        var handler = new SetStatementReviewedHandler(_statements.Object);
        var result = await handler.Handle(new SetStatementReviewedRequest { PersonId = Person, Year = 2026, Month = 9, IsReviewed = true }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _statements.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetReviewed_Fails_WhenMonthClosed()
    {
        SetupMonth(OvertimeStatementStatus.Closed);
        var handler = new SetStatementReviewedHandler(_statements.Object);
        var result = await handler.Handle(new SetStatementReviewedRequest { PersonId = Person, Year = 2026, Month = 9, IsReviewed = true }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.OvertimeMonthAlreadyClosed);
    }

    [Fact]
    public async Task CreateAdjustment_Saves_WithAuditFields()
    {
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));
        var handler = new CreateAdjustmentHandler(_employees.Object, _statements.Object, _adjustments.Object, _currentUser.Object, timeProvider.Object);

        var result = await handler.Handle(new CreateAdjustmentRequest
        {
            PersonId = Person, Year = 2026, Month = 9,
            Type = OvertimeAdjustmentType.Payout, Hours = -40m, Note = "Proplaceno v prémiích"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _adjustments.Verify(r => r.AddAsync(It.Is<OvertimeAdjustment>(a =>
            a.PersonId == Person && a.Hours == -40m && a.CreatedBy == "Andy"
            && a.CreatedAtUtc == new DateTime(2026, 9, 15, 10, 0, 0)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAdjustment_Fails_WhenMonthClosed()
    {
        SetupMonth(OvertimeStatementStatus.Closed);
        var handler = new CreateAdjustmentHandler(_employees.Object, _statements.Object, _adjustments.Object, _currentUser.Object, TimeProvider.System);

        var result = await handler.Handle(new CreateAdjustmentRequest
        {
            PersonId = Person, Year = 2026, Month = 9, Type = OvertimeAdjustmentType.Payout, Hours = -1m, Note = "x"
        }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeAdjustmentMonthClosed);
    }

    [Fact]
    public async Task CreateAdjustment_Fails_ForUnknownEmployee()
    {
        _employees.Setup(r => r.GetByPersonIdAsync(Person, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OvertimeEmployee?)null);
        var handler = new CreateAdjustmentHandler(_employees.Object, _statements.Object, _adjustments.Object, _currentUser.Object, TimeProvider.System);

        var result = await handler.Handle(new CreateAdjustmentRequest
        {
            PersonId = Person, Year = 2026, Month = 9, Type = OvertimeAdjustmentType.Other, Hours = 1m, Note = "x"
        }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeEmployeeNotFound);
    }

    [Fact]
    public async Task DeleteAdjustment_Fails_WhenMonthClosed()
    {
        SetupMonth(OvertimeStatementStatus.Closed);
        _adjustments.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeAdjustment { Id = 5, PersonId = Person, Year = 2026, Month = 9 });
        var handler = new DeleteAdjustmentHandler(_statements.Object, _adjustments.Object);

        var result = await handler.Handle(new DeleteAdjustmentRequest { Id = 5 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeAdjustmentMonthClosed);
        _adjustments.Verify(r => r.DeleteAsync(It.IsAny<OvertimeAdjustment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAdjustment_Deletes_OnOpenMonth()
    {
        _adjustments.Setup(r => r.GetByIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OvertimeAdjustment { Id = 5, PersonId = Person, Year = 2026, Month = 9 });
        var handler = new DeleteAdjustmentHandler(_statements.Object, _adjustments.Object);

        var result = await handler.Handle(new DeleteAdjustmentRequest { Id = 5 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        _adjustments.Verify(r => r.DeleteAsync(It.Is<OvertimeAdjustment>(a => a.Id == 5), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~OvertimeAdjustmentUseCaseTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`SetStatementReviewedRequest.cs` / `SetStatementReviewedResponse.cs` (response is an empty `BaseResponse` subclass, same shape as `UpsertOvertimeEmployeeResponse`):

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;

public class SetStatementReviewedRequest : IRequest<SetStatementReviewedResponse>
{
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool IsReviewed { get; set; }
}
```

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;

public class SetStatementReviewedResponse : BaseResponse
{
}
```

`SetStatementReviewedHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;

public class SetStatementReviewedHandler : IRequestHandler<SetStatementReviewedRequest, SetStatementReviewedResponse>
{
    private readonly IOvertimeStatementRepository _statements;

    public SetStatementReviewedHandler(IOvertimeStatementRepository statements)
    {
        _statements = statements;
    }

    public async Task<SetStatementReviewedResponse> Handle(SetStatementReviewedRequest request, CancellationToken cancellationToken)
    {
        var monthStatements = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);

        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new SetStatementReviewedResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeMonthAlreadyClosed,
                Params = new Dictionary<string, string>
                {
                    { "year", request.Year.ToString() },
                    { "month", request.Month.ToString() }
                }
            };
        }

        var statement = monthStatements.FirstOrDefault(s => s.PersonId == request.PersonId);
        if (statement is null)
        {
            return new SetStatementReviewedResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeEmployeeNotFound
            };
        }

        statement.IsReviewed = request.IsReviewed;
        await _statements.SaveChangesAsync(cancellationToken);

        return new SetStatementReviewedResponse();
    }
}
```

`CreateAdjustmentRequest.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentRequest : IRequest<CreateAdjustmentResponse>
{
    public Guid PersonId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public OvertimeAdjustmentType Type { get; set; }
    public decimal Hours { get; set; }
    public string Note { get; set; } = string.Empty;
}
```

`CreateAdjustmentResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentResponse : BaseResponse
{
    public int Id { get; set; }
}
```

`CreateAdjustmentHandler.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentHandler : IRequestHandler<CreateAdjustmentRequest, CreateAdjustmentResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;

    public CreateAdjustmentHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
    }

    public async Task<CreateAdjustmentResponse> Handle(CreateAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var employee = await _employees.GetByPersonIdAsync(request.PersonId, cancellationToken);
        if (employee is null)
        {
            return new CreateAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeEmployeeNotFound
            };
        }

        var monthStatements = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new CreateAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeAdjustmentMonthClosed
            };
        }

        var adjustment = new OvertimeAdjustment
        {
            PersonId = request.PersonId,
            Year = request.Year,
            Month = request.Month,
            Type = request.Type,
            Hours = request.Hours,
            Note = request.Note,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            CreatedBy = _currentUserService.GetCurrentUser().Name ?? "unknown"
        };

        await _adjustments.AddAsync(adjustment, cancellationToken);

        return new CreateAdjustmentResponse { Id = adjustment.Id };
    }
}
```

`CreateAdjustmentValidator.cs`:

```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;

public class CreateAdjustmentValidator : AbstractValidator<CreateAdjustmentRequest>
{
    public CreateAdjustmentValidator()
    {
        RuleFor(x => x.PersonId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2020, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Hours).InclusiveBetween(-1000m, 1000m);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}
```

`DeleteAdjustmentRequest.cs` / `DeleteAdjustmentResponse.cs` / `DeleteAdjustmentHandler.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;

public class DeleteAdjustmentRequest : IRequest<DeleteAdjustmentResponse>
{
    public int Id { get; set; }
}
```

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;

public class DeleteAdjustmentResponse : BaseResponse
{
}
```

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;

public class DeleteAdjustmentHandler : IRequestHandler<DeleteAdjustmentRequest, DeleteAdjustmentResponse>
{
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;

    public DeleteAdjustmentHandler(
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments)
    {
        _statements = statements;
        _adjustments = adjustments;
    }

    public async Task<DeleteAdjustmentResponse> Handle(DeleteAdjustmentRequest request, CancellationToken cancellationToken)
    {
        var adjustment = await _adjustments.GetByIdAsync(request.Id, cancellationToken);
        if (adjustment is null)
        {
            return new DeleteAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeAdjustmentNotFound
            };
        }

        var monthStatements = await _statements.GetByMonthAsync(adjustment.Year, adjustment.Month, cancellationToken);
        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new DeleteAdjustmentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeAdjustmentMonthClosed
            };
        }

        await _adjustments.DeleteAsync(adjustment, cancellationToken);
        return new DeleteAdjustmentResponse();
    }
}
```

- [ ] **Step 4: Register CreateAdjustment validator + behavior in `OvertimeModule`**

```csharp
        services.AddScoped<IValidator<CreateAdjustmentRequest>, CreateAdjustmentValidator>();
        services.AddScoped<IPipelineBehavior<CreateAdjustmentRequest, CreateAdjustmentResponse>,
            ValidationBehavior<CreateAdjustmentRequest, CreateAdjustmentResponse>>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeAdjustmentUseCaseTests.cs
git commit -m "feat: add overtime reviewed flag and adjustment use cases"
```

---

### Task 9: Excel builder + export use case

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — add `<PackageReference Include="ClosedXML" Version="0.104.2" />` (MIT license; already-referenced DocumentFormat.OpenXml is too low-level for this)
- Create: `.../Overtime/Services/OvertimeExcelBuilder.cs`
- Create: `.../Overtime/UseCases/ExportOvertimeReport/ExportOvertimeReportRequest.cs`, `ExportOvertimeReportResponse.cs`, `ExportOvertimeReportHandler.cs`
- Modify: `OvertimeModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeExcelBuilderTests.cs`

**Interfaces:**
- Consumes: repositories (Task 2).
- Produces (used by Tasks 10, 11, 12):
  - `OvertimeExcelBuilder { byte[] Build(IReadOnlyList<OvertimeEmployee> employees, IReadOnlyList<OvertimeMonthlyStatement> closedStatements, IReadOnlyList<OvertimeAdjustment> adjustments); }` — one worksheet per closed (Year, Month) named `"YYYY-MM"`, newest first; columns: Zaměstnanec | Převod z minula | Úvazek (h) | Odpracováno | Dovolená | Nemoc | Lékař | Náhradní volno | Ostatní | Rozdíl | Korekce (h) | Korekce – detail | Nový zůstatek. "Korekce – detail" = `"{Type}: {Hours}h – {Note}"` joined by `"; "` (SportBenefit entries listed even with 0 h).
  - `IOvertimeAdjustmentRepository` gains `Task<IReadOnlyList<OvertimeAdjustment>> GetAllAsync(CancellationToken ct);` (add to interface + implementation: `_context.OvertimeAdjustments.OrderBy(a => a.Year).ThenBy(a => a.Month).ThenBy(a => a.CreatedAtUtc).ToListAsync(...)`).
  - `ExportOvertimeReportRequest : IRequest<ExportOvertimeReportResponse> { }`; `ExportOvertimeReportResponse : BaseResponse { byte[] Content; string FileName; }` (FileName from `OvertimeOptions.ExportFileName`).

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using ClosedXML.Excel;
using FluentAssertions;

namespace Anela.Heblo.Tests.Application.Overtime;

public class OvertimeExcelBuilderTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Build_CreatesSheetPerClosedMonth_WithBalanceColumns()
    {
        var employees = new List<OvertimeEmployee>
        {
            new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m }
        };
        var statements = new List<OvertimeMonthlyStatement>
        {
            new()
            {
                PersonId = Person, Year = 2026, Month = 9, Status = OvertimeStatementStatus.Closed,
                RequiredHours = 134.4m, WorkedHours = 130m, VacationHours = 6.4m,
                DeltaHours = 2m, BalanceAfter = 3.5m
            }
        };
        var adjustments = new List<OvertimeAdjustment>
        {
            new() { PersonId = Person, Year = 2026, Month = 9, Type = OvertimeAdjustmentType.Payout, Hours = -1m, Note = "Prémie" }
        };

        var bytes = new OvertimeExcelBuilder().Build(employees, statements, adjustments);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheets.Should().ContainSingle(ws => ws.Name == "2026-09");
        var sheet = workbook.Worksheet("2026-09");
        sheet.Cell(1, 1).GetString().Should().Be("Zaměstnanec");
        sheet.Cell(2, 1).GetString().Should().Be("Pepina");
        sheet.Cell(2, 13).GetValue<decimal>().Should().Be(3.5m);   // Nový zůstatek
        sheet.Cell(2, 12).GetString().Should().Contain("Prémie");  // Korekce – detail
    }

    [Fact]
    public void Build_WithNoClosedMonths_ProducesInfoSheet()
    {
        var bytes = new OvertimeExcelBuilder().Build(
            new List<OvertimeEmployee>(), new List<OvertimeMonthlyStatement>(), new List<OvertimeAdjustment>());

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        workbook.Worksheets.Count.Should().Be(1);   // placeholder sheet, valid file
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~OvertimeExcelBuilderTests"`
Expected: FAIL (add the ClosedXML package reference to the **test** csproj too if the compiler can't find `ClosedXML.Excel` — it flows transitively via the Application project reference, so normally not needed).

- [ ] **Step 3: Implement the builder**

`OvertimeExcelBuilder.cs`:

```csharp
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using ClosedXML.Excel;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

/// <summary>Builds the shared "Evidence přesčasů" workbook: one sheet per closed month,
/// mirroring the legacy internal Excel's columns.</summary>
public class OvertimeExcelBuilder
{
    private static readonly string[] Headers =
    {
        "Zaměstnanec", "Převod z minula", "Úvazek (h)", "Odpracováno", "Dovolená",
        "Nemoc", "Lékař", "Náhradní volno", "Ostatní", "Rozdíl", "Korekce (h)",
        "Korekce – detail", "Nový zůstatek"
    };

    public byte[] Build(
        IReadOnlyList<OvertimeEmployee> employees,
        IReadOnlyList<OvertimeMonthlyStatement> closedStatements,
        IReadOnlyList<OvertimeAdjustment> adjustments)
    {
        using var workbook = new XLWorkbook();
        var nameByPerson = employees.ToDictionary(e => e.PersonId, e => e.DisplayName);

        var months = closedStatements
            .Where(s => s.Status == OvertimeStatementStatus.Closed)
            .GroupBy(s => (s.Year, s.Month))
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .ToList();

        if (months.Count == 0)
        {
            var info = workbook.AddWorksheet("Info");
            info.Cell(1, 1).Value = "Zatím není uzavřen žádný měsíc.";
        }

        foreach (var monthGroup in months)
        {
            var sheet = workbook.AddWorksheet($"{monthGroup.Key.Year}-{monthGroup.Key.Month:D2}");

            for (var i = 0; i < Headers.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = Headers[i];
                sheet.Cell(1, i + 1).Style.Font.Bold = true;
            }

            var row = 2;
            foreach (var statement in monthGroup.OrderBy(s => nameByPerson.TryGetValue(s.PersonId, out var n) ? n : ""))
            {
                var personAdjustments = adjustments
                    .Where(a => a.PersonId == statement.PersonId && a.Year == statement.Year && a.Month == statement.Month)
                    .ToList();
                var adjustmentsTotal = personAdjustments.Sum(a => a.Hours);
                var detail = string.Join("; ", personAdjustments.Select(a => $"{a.Type}: {a.Hours}h – {a.Note}"));

                sheet.Cell(row, 1).Value = nameByPerson.TryGetValue(statement.PersonId, out var name) ? name : statement.PersonId.ToString();
                sheet.Cell(row, 2).Value = statement.BalanceAfter - statement.DeltaHours - adjustmentsTotal;
                sheet.Cell(row, 3).Value = statement.RequiredHours;
                sheet.Cell(row, 4).Value = statement.WorkedHours;
                sheet.Cell(row, 5).Value = statement.VacationHours;
                sheet.Cell(row, 6).Value = statement.SickHours;
                sheet.Cell(row, 7).Value = statement.DoctorHours;
                sheet.Cell(row, 8).Value = statement.CompTimeHours;
                sheet.Cell(row, 9).Value = statement.OtherAbsenceHours;
                sheet.Cell(row, 10).Value = statement.DeltaHours;
                sheet.Cell(row, 11).Value = adjustmentsTotal;
                sheet.Cell(row, 12).Value = detail;
                sheet.Cell(row, 13).Value = statement.BalanceAfter;
                row++;
            }

            sheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
```

- [ ] **Step 4: Implement the export use case**

`ExportOvertimeReportRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;

public class ExportOvertimeReportRequest : IRequest<ExportOvertimeReportResponse>
{
}
```

`ExportOvertimeReportResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;

public class ExportOvertimeReportResponse : BaseResponse
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
}
```

`ExportOvertimeReportHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;

public class ExportOvertimeReportHandler : IRequestHandler<ExportOvertimeReportRequest, ExportOvertimeReportResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly OvertimeExcelBuilder _builder;
    private readonly IOptions<OvertimeOptions> _options;

    public ExportOvertimeReportHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        OvertimeExcelBuilder builder,
        IOptions<OvertimeOptions> options)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _builder = builder;
        _options = options;
    }

    public async Task<ExportOvertimeReportResponse> Handle(ExportOvertimeReportRequest request, CancellationToken cancellationToken)
    {
        var employees = await _employees.GetAllAsync(cancellationToken);
        var closed = await _statements.GetAllClosedAsync(cancellationToken);
        var adjustments = await _adjustments.GetAllAsync(cancellationToken);

        return new ExportOvertimeReportResponse
        {
            Content = _builder.Build(employees, closed, adjustments),
            FileName = _options.Value.ExportFileName
        };
    }
}
```

Register in `OvertimeModule`: `services.AddScoped<Services.OvertimeExcelBuilder>();` and add `GetAllAsync` to `IOvertimeAdjustmentRepository` + `OvertimeAdjustmentRepository` as specified in Interfaces.

- [ ] **Step 5: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests/Application/Overtime/OvertimeExcelBuilderTests.cs
git commit -m "feat: add overtime Excel report builder and export use case"
```

---

### Task 10: SharePoint report publisher

**Files:**
- Create: `.../Overtime/Services/IOvertimeReportPublisher.cs`, `Services/GraphOvertimeReportPublisher.cs`
- Modify: `OvertimeModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/GraphOvertimeReportPublisherTests.cs`

**Interfaces:**
- Consumes: `GraphApiHelpers` (existing, `Anela.Heblo.Application.Common.Graph` — `GraphBaseUrl`, `GraphScope`, `CreateRequest`, `EnsureSuccessAsync`), `ITokenAcquisition` (Microsoft.Identity.Web, app-only token — same pattern as `GraphCatalogDocumentsStorage`), `IHttpClientFactory` named client `"MicrosoftGraph"`, `OvertimeOptions` (Task 4).
- Produces (used by Tasks 11, 12):

```csharp
public interface IOvertimeReportPublisher
{
    bool IsConfigured { get; }
    /// <summary>Uploads (overwrites) the workbook to the configured SharePoint folder. Throws on failure.</summary>
    Task PublishAsync(byte[] content, string fileName, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing tests**

The Graph call itself is a thin PUT; unit-test the URL composition and the not-configured guard via a mocked `HttpMessageHandler`:

```csharp
using System.Net;
using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Moq;
using Moq.Protected;

namespace Anela.Heblo.Tests.Application.Overtime;

public class GraphOvertimeReportPublisherTests
{
    private readonly Mock<HttpMessageHandler> _handler = new();
    private readonly Mock<ITokenAcquisition> _tokens = new();
    private HttpRequestMessage? _captured;

    private GraphOvertimeReportPublisher CreateSut(OvertimeOptions options)
    {
        _tokens.Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TokenAcquisitionOptions?>()))
            .ReturnsAsync("token-123");
        _handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => _captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(new HttpClient(_handler.Object));

        return new GraphOvertimeReportPublisher(
            factory.Object, _tokens.Object, Options.Create(options),
            NullLogger<GraphOvertimeReportPublisher>.Instance);
    }

    [Fact]
    public void IsConfigured_False_WhenDriveIdEmpty()
        => CreateSut(new OvertimeOptions()).IsConfigured.Should().BeFalse();

    [Fact]
    public async Task Publish_PutsToDrivePath_WithReplaceBehavior()
    {
        var sut = CreateSut(new OvertimeOptions
        {
            ExportDriveId = "drive-1", ExportFolderPath = "Provoz/Mzdy", ExportFileName = "Evidence-prescasu.xlsx"
        });

        await sut.PublishAsync(new byte[] { 1, 2, 3 }, "Evidence-prescasu.xlsx", CancellationToken.None);

        _captured.Should().NotBeNull();
        _captured!.Method.Should().Be(HttpMethod.Put);
        _captured.RequestUri!.ToString().Should().Be(
            "https://graph.microsoft.com/v1.0/drives/drive-1/root:/Provoz/Mzdy/Evidence-prescasu.xlsx:/content?@microsoft.graph.conflictBehavior=replace");
        _captured.Headers.Authorization!.Parameter.Should().Be("token-123");
    }

    [Fact]
    public async Task Publish_Throws_WhenNotConfigured()
    {
        var act = () => CreateSut(new OvertimeOptions()).PublishAsync(Array.Empty<byte>(), "x.xlsx", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

(Add `Moq.Protected` using — package already present via Moq.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~GraphOvertimeReportPublisherTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`IOvertimeReportPublisher.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public interface IOvertimeReportPublisher
{
    bool IsConfigured { get; }

    /// <summary>Uploads (overwrites) the workbook to the configured SharePoint folder. Throws on failure.</summary>
    Task PublishAsync(byte[] content, string fileName, CancellationToken cancellationToken);
}
```

`GraphOvertimeReportPublisher.cs`:

```csharp
using System.Net.Http.Headers;
using Anela.Heblo.Application.Common.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public class GraphOvertimeReportPublisher : IOvertimeReportPublisher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IOptions<OvertimeOptions> _options;
    private readonly ILogger<GraphOvertimeReportPublisher> _logger;

    public GraphOvertimeReportPublisher(
        IHttpClientFactory httpClientFactory,
        ITokenAcquisition tokenAcquisition,
        IOptions<OvertimeOptions> options,
        ILogger<GraphOvertimeReportPublisher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenAcquisition = tokenAcquisition;
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Value.ExportDriveId);

    public async Task PublishAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Overtime report publishing is not configured (Overtime:ExportDriveId is empty).");
        }

        var options = _options.Value;
        var path = string.IsNullOrWhiteSpace(options.ExportFolderPath)
            ? fileName
            : $"{options.ExportFolderPath.Trim('/')}/{fileName}";

        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{options.ExportDriveId}/root:/{path}:/content" +
                  "?@microsoft.graph.conflictBehavior=replace";

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var response = await client.SendAsync(request, cancellationToken);
        await GraphApiHelpers.EnsureSuccessAsync(response, "upload overtime report", cancellationToken);

        _logger.LogInformation("Overtime report published to drive {DriveId} at {Path}", options.ExportDriveId, path);
    }
}
```

(If `GraphApiHelpers.EnsureSuccessAsync` has a different signature, adapt to the existing helper — check `backend/src/Anela.Heblo.Application/Common/Graph/GraphApiHelpers.cs` and mirror how `GraphCatalogDocumentsStorage` calls it.)

Register in `OvertimeModule`:

```csharp
        services.AddScoped<Services.IOvertimeReportPublisher, Services.GraphOvertimeReportPublisher>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/test/Anela.Heblo.Tests/Application/Overtime/GraphOvertimeReportPublisherTests.cs
git commit -m "feat: add SharePoint publisher for overtime report"
```

---

### Task 11: Use case — CloseMonth

**Files:**
- Create: `.../Overtime/UseCases/CloseMonth/CloseMonthRequest.cs`, `CloseMonthResponse.cs`, `CloseMonthHandler.cs`
- Modify: `OvertimeModule.cs`
- Test: `backend/test/Anela.Heblo.Tests/Application/Overtime/CloseMonthHandlerTests.cs`

**Interfaces:**
- Consumes: repositories (Task 2), `OvertimeCalculationService` (Task 5), `OvertimeExcelBuilder` (Task 9), `IOvertimeReportPublisher` (Task 10), `ICurrentUserService`, `TimeProvider`.
- Produces: `CloseMonthRequest : IRequest<CloseMonthResponse> { int Year; int Month; bool Force; }` (`Force` = close despite unreviewed people); `CloseMonthResponse : BaseResponse { int ClosedCount; bool PublishSkipped; bool PublishFailed; }`.

**Close algorithm (spec §Month lifecycle):**
1. Month already closed (any `Closed` statement in month) → error 3402 with `{year, month}` params.
2. `AnyOpenBeforeAsync(year, month)` → error 3407 with `{year, month}` params (months must close in order).
3. Compute month for **active** employees via `OvertimeCalculationService`.
4. Any row with `DailyContractHours == null` → error 3406, `Params["names"]` = comma-joined display names.
5. Unless `Force`: any person whose Open statement is missing or `IsReviewed == false` → error 3405, `Params["names"]`.
6. Per person: get-or-create statement, copy computed numbers, `previousBalance` = latest closed `BalanceAfter` ?? `BaselineHours`; `adjustmentsTotal` = Σ hours of person-month adjustments; `BalanceAfter = previousBalance + DeltaHours + adjustmentsTotal`; `Status = Closed`; `ClosedAtUtc = TimeProvider.GetUtcNow().UtcDateTime`; `ClosedBy` = current user name. Persist.
7. Best-effort publish: rebuild workbook from all closed data; if `publisher.IsConfigured` try `PublishAsync` — on exception log + `PublishFailed = true` (close still succeeds); if not configured `PublishSkipped = true`.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime;
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Application.Overtime;

public class CloseMonthHandlerTests
{
    private static readonly Guid Person = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid WorkActivity = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IOvertimeEmployeeRepository> _employees = new();
    private readonly Mock<IOvertimeStatementRepository> _statements = new();
    private readonly Mock<IOvertimeAdjustmentRepository> _adjustments = new();
    private readonly Mock<ILogetoClient> _client = new();
    private readonly Mock<IContractHoursProvider> _contractHours = new();
    private readonly Mock<IOvertimeReportPublisher> _publisher = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly List<OvertimeMonthlyStatement> _monthStatements = new();

    public CloseMonthHandlerTests()
    {
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: "user-123", Name: "Andy", Email: null, IsAuthenticated: true));
        _employees.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeEmployee>
            {
                new() { PersonId = Person, DisplayName = "Pepina", BaselineHours = 2.5m, BaselineDate = new DateOnly(2026, 8, 1), IsActive = true }
            });
        _monthStatements.Add(new OvertimeMonthlyStatement
        {
            PersonId = Person, Year = 2026, Month = 8, Status = OvertimeStatementStatus.Open, IsReviewed = true
        });
        _statements.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(_monthStatements);
        _statements.Setup(r => r.AnyOpenBeforeAsync(2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _statements.Setup(r => r.GetLatestClosedAsync(Person, It.IsAny<CancellationToken>())).ReturnsAsync((OvertimeMonthlyStatement?)null);
        _statements.Setup(r => r.GetAllClosedAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<OvertimeMonthlyStatement>());
        _adjustments.Setup(r => r.GetByMonthAsync(2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OvertimeAdjustment>
            {
                new() { PersonId = Person, Year = 2026, Month = 8, Type = OvertimeAdjustmentType.Payout, Hours = -1m, Note = "x" }
            });
        _adjustments.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<OvertimeAdjustment>());
        _client.Setup(c => c.GetActivitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoActivity> { new() { Guid = WorkActivity, Name = "Práce", Type = LogetoActivityTypes.Work } });
        _client.Setup(c => c.GetTimeTrackingAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LogetoTimeEntry>
            {
                new()
                {
                    Guid = Guid.NewGuid(), Person = Person, Date = new DateOnly(2026, 8, 3), Activity = WorkActivity,
                    From = new DateTimeOffset(2026, 8, 3, 8, 0, 0, TimeSpan.Zero),
                    To = new DateTimeOffset(2026, 8, 3, 16, 0, 0, TimeSpan.Zero)
                }
            });
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, 2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(6.4m);
        _publisher.SetupGet(p => p.IsConfigured).Returns(false);
    }

    private CloseMonthHandler CreateSut()
    {
        var calc = new OvertimeCalculationService(
            _client.Object, _contractHours.Object, Options.Create(new OvertimeOptions()),
            NullLogger<OvertimeCalculationService>.Instance);
        var timeProvider = new Mock<TimeProvider>();
        timeProvider.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        return new CloseMonthHandler(
            _employees.Object, _statements.Object, _adjustments.Object, calc,
            new OvertimeExcelBuilder(), _publisher.Object, _currentUser.Object, timeProvider.Object,
            NullLogger<CloseMonthHandler>.Instance);
    }

    [Fact]
    public async Task Close_FreezesStatement_ChainsBalance_AndSkipsPublishWhenUnconfigured()
    {
        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ClosedCount.Should().Be(1);
        result.PublishSkipped.Should().BeTrue();
        var statement = _monthStatements.Single();
        statement.Status.Should().Be(OvertimeStatementStatus.Closed);
        statement.ClosedBy.Should().Be("Andy");
        // worked 8, required 21×6.4=134.4 → delta −126.4; balance = 2.5 − 126.4 − 1 (adjustment)
        statement.BalanceAfter.Should().Be(2.5m + statement.DeltaHours - 1m);
    }

    [Fact]
    public async Task Close_Fails_WhenAlreadyClosed()
    {
        _monthStatements[0].Status = OvertimeStatementStatus.Closed;

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeMonthAlreadyClosed);
    }

    [Fact]
    public async Task Close_Fails_WhenOlderMonthOpen()
    {
        _statements.Setup(r => r.AnyOpenBeforeAsync(2026, 8, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimePreviousMonthOpen);
    }

    [Fact]
    public async Task Close_Fails_WhenUnreviewed_UnlessForced()
    {
        _monthStatements[0].IsReviewed = false;

        var blocked = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);
        blocked.ErrorCode.Should().Be(ErrorCodes.OvertimeMonthNotReviewed);
        blocked.Params!["names"].Should().Contain("Pepina");

        var forced = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8, Force = true }, CancellationToken.None);
        forced.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Close_Fails_WhenContractHoursMissing()
    {
        _contractHours.Setup(p => p.GetDailyHoursAsync(Person, 2026, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.OvertimeContractHoursMissing);
    }

    [Fact]
    public async Task Close_Succeeds_WithPublishFailedFlag_WhenPublisherThrows()
    {
        _publisher.SetupGet(p => p.IsConfigured).Returns(true);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("graph down"));

        var result = await CreateSut().Handle(new CloseMonthRequest { Year = 2026, Month = 8 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PublishFailed.Should().BeTrue();
        _monthStatements.Single().Status.Should().Be(OvertimeStatementStatus.Closed);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~CloseMonthHandlerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

`CloseMonthRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;

public class CloseMonthRequest : IRequest<CloseMonthResponse>
{
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Close even when some employees are not marked as reviewed.</summary>
    public bool Force { get; set; }
}
```

`CloseMonthResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;

public class CloseMonthResponse : BaseResponse
{
    public int ClosedCount { get; set; }
    public bool PublishSkipped { get; set; }
    public bool PublishFailed { get; set; }
}
```

`CloseMonthHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Attendance.Overtime;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;

public class CloseMonthHandler : IRequestHandler<CloseMonthRequest, CloseMonthResponse>
{
    private readonly IOvertimeEmployeeRepository _employees;
    private readonly IOvertimeStatementRepository _statements;
    private readonly IOvertimeAdjustmentRepository _adjustments;
    private readonly OvertimeCalculationService _calculation;
    private readonly OvertimeExcelBuilder _excelBuilder;
    private readonly IOvertimeReportPublisher _publisher;
    private readonly ICurrentUserService _currentUserService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloseMonthHandler> _logger;

    public CloseMonthHandler(
        IOvertimeEmployeeRepository employees,
        IOvertimeStatementRepository statements,
        IOvertimeAdjustmentRepository adjustments,
        OvertimeCalculationService calculation,
        OvertimeExcelBuilder excelBuilder,
        IOvertimeReportPublisher publisher,
        ICurrentUserService currentUserService,
        TimeProvider timeProvider,
        ILogger<CloseMonthHandler> logger)
    {
        _employees = employees;
        _statements = statements;
        _adjustments = adjustments;
        _calculation = calculation;
        _excelBuilder = excelBuilder;
        _publisher = publisher;
        _currentUserService = currentUserService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<CloseMonthResponse> Handle(CloseMonthRequest request, CancellationToken cancellationToken)
    {
        var monthParams = new Dictionary<string, string>
        {
            { "year", request.Year.ToString() },
            { "month", request.Month.ToString() }
        };

        var monthStatements = await _statements.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        if (monthStatements.Any(s => s.Status == OvertimeStatementStatus.Closed))
        {
            return new CloseMonthResponse { Success = false, ErrorCode = ErrorCodes.OvertimeMonthAlreadyClosed, Params = monthParams };
        }

        if (await _statements.AnyOpenBeforeAsync(request.Year, request.Month, cancellationToken))
        {
            return new CloseMonthResponse { Success = false, ErrorCode = ErrorCodes.OvertimePreviousMonthOpen, Params = monthParams };
        }

        var allEmployees = await _employees.GetAllAsync(cancellationToken);
        var active = allEmployees.Where(e => e.IsActive).ToList();
        var nameByPerson = allEmployees.ToDictionary(e => e.PersonId, e => e.DisplayName);
        var computations = await _calculation.ComputeMonthAsync(request.Year, request.Month, active, cancellationToken);

        var missingContract = computations.Where(c => c.DailyContractHours is null)
            .Select(c => nameByPerson[c.PersonId]).ToList();
        if (missingContract.Count > 0)
        {
            return new CloseMonthResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeContractHoursMissing,
                Params = new Dictionary<string, string> { { "names", string.Join(", ", missingContract) } }
            };
        }

        var statementByPerson = monthStatements.ToDictionary(s => s.PersonId);
        if (!request.Force)
        {
            var unreviewed = computations
                .Where(c => !statementByPerson.TryGetValue(c.PersonId, out var s) || !s.IsReviewed)
                .Select(c => nameByPerson[c.PersonId]).ToList();
            if (unreviewed.Count > 0)
            {
                return new CloseMonthResponse
                {
                    Success = false,
                    ErrorCode = ErrorCodes.OvertimeMonthNotReviewed,
                    Params = new Dictionary<string, string> { { "names", string.Join(", ", unreviewed) } }
                };
            }
        }

        var monthAdjustments = await _adjustments.GetByMonthAsync(request.Year, request.Month, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var closedBy = _currentUserService.GetCurrentUser().Name ?? "unknown";

        foreach (var computation in computations)
        {
            if (!statementByPerson.TryGetValue(computation.PersonId, out var statement))
            {
                statement = new OvertimeMonthlyStatement
                {
                    PersonId = computation.PersonId,
                    Year = request.Year,
                    Month = request.Month
                };
                await _statements.AddAsync(statement, cancellationToken);
            }

            statement.RequiredHours = computation.RequiredHours;
            statement.WorkedHours = computation.WorkedHours;
            statement.VacationHours = computation.VacationHours;
            statement.SickHours = computation.SickHours;
            statement.DoctorHours = computation.DoctorHours;
            statement.CompTimeHours = computation.CompTimeHours;
            statement.OtherAbsenceHours = computation.OtherAbsenceHours;
            statement.DeltaHours = computation.DeltaHours;

            var latestClosed = await _statements.GetLatestClosedAsync(computation.PersonId, cancellationToken);
            var employee = active.First(e => e.PersonId == computation.PersonId);
            var previousBalance = latestClosed?.BalanceAfter ?? employee.BaselineHours;
            var adjustmentsTotal = monthAdjustments.Where(a => a.PersonId == computation.PersonId).Sum(a => a.Hours);

            statement.BalanceAfter = previousBalance + computation.DeltaHours + adjustmentsTotal;
            statement.Status = OvertimeStatementStatus.Closed;
            statement.ClosedAtUtc = now;
            statement.ClosedBy = closedBy;
        }

        await _statements.SaveChangesAsync(cancellationToken);

        var response = new CloseMonthResponse { ClosedCount = computations.Count };

        if (!_publisher.IsConfigured)
        {
            response.PublishSkipped = true;
            return response;
        }

        try
        {
            var closed = await _statements.GetAllClosedAsync(cancellationToken);
            var allAdjustments = await _adjustments.GetAllAsync(cancellationToken);
            var workbook = _excelBuilder.Build(allEmployees, closed, allAdjustments);
            await _publisher.PublishAsync(workbook, "Evidence-prescasu.xlsx", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overtime report publish failed after closing {Year}/{Month}", request.Year, request.Month);
            response.PublishFailed = true;
        }

        return response;
    }
}
```

Note: the publish filename should come from `OvertimeOptions.ExportFileName` — inject `IOptions<OvertimeOptions>` and use it instead of the literal (test doesn't assert the name; keep it consistent with Task 9's handler).

- [ ] **Step 4: Run tests to verify they pass**

Run: same filter as Step 2. Expected: PASS (6 tests).

- [ ] **Step 5: Run the whole overtime test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.Overtime"`
Expected: PASS — all tasks so far.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Attendance/Overtime backend/test/Anela.Heblo.Tests/Application/Overtime/CloseMonthHandlerTests.cs
git commit -m "feat: add overtime month close workflow"
```

---

### Task 12: Controller + generated artifacts

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/OvertimeController.cs`
- Regenerate: TS client + access matrix
- Test: build + existing contract tests (no new unit tests — controller is thin MediatR pass-through, same as `CarrierCoolingController`)

**Interfaces:**
- Consumes: all use cases (Tasks 6–11).
- Produces: generated client methods `overtime_GetEmployees`, `overtime_UpsertEmployee`, `overtime_GetMonthlyStatements`, `overtime_SetReviewed`, `overtime_CreateAdjustment`, `overtime_DeleteAdjustment`, `overtime_CloseMonth`, `overtime_DownloadReport`, `overtime_PublishReport` (used by Task 13).

- [ ] **Step 1: Write the controller**

```csharp
using Anela.Heblo.Application.Features.Attendance.Overtime.Services;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CloseMonth;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.CreateAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.DeleteAdjustment;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.ExportOvertimeReport;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetMonthlyStatements;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.GetOvertimeEmployees;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.SetStatementReviewed;
using Anela.Heblo.Application.Features.Attendance.Overtime.UseCases.UpsertOvertimeEmployee;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Attendance_Overtime)]
[ApiController]
[Route("api/overtime")]
public class OvertimeController : BaseApiController
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IMediator _mediator;
    private readonly IOvertimeReportPublisher _publisher;

    public OvertimeController(IMediator mediator, IOvertimeReportPublisher publisher)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _publisher = publisher;
    }

    [HttpGet("employees")]
    public async Task<ActionResult<GetOvertimeEmployeesResponse>> GetEmployees(CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new GetOvertimeEmployeesRequest(), cancellationToken));

    [HttpPut("employees")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<UpsertOvertimeEmployeeResponse>> UpsertEmployee(
        [FromBody] UpsertOvertimeEmployeeRequest request, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(request, cancellationToken));

    [HttpGet("statements/{year:int}/{month:int}")]
    public async Task<ActionResult<GetMonthlyStatementsResponse>> GetMonthlyStatements(
        int year, int month, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new GetMonthlyStatementsRequest { Year = year, Month = month }, cancellationToken));

    [HttpPost("statements/{year:int}/{month:int}/reviewed")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<SetStatementReviewedResponse>> SetReviewed(
        int year, int month, [FromBody] SetStatementReviewedRequest request, CancellationToken cancellationToken = default)
    {
        request.Year = year;
        request.Month = month;
        return HandleResponse(await _mediator.Send(request, cancellationToken));
    }

    [HttpPost("adjustments")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<CreateAdjustmentResponse>> CreateAdjustment(
        [FromBody] CreateAdjustmentRequest request, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(request, cancellationToken));

    [HttpDelete("adjustments/{id:int}")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<DeleteAdjustmentResponse>> DeleteAdjustment(
        int id, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new DeleteAdjustmentRequest { Id = id }, cancellationToken));

    [HttpPost("close/{year:int}/{month:int}")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<CloseMonthResponse>> CloseMonth(
        int year, int month, [FromQuery] bool force = false, CancellationToken cancellationToken = default)
        => HandleResponse(await _mediator.Send(new CloseMonthRequest { Year = year, Month = month, Force = force }, cancellationToken));

    [HttpGet("export")]
    public async Task<IActionResult> DownloadReport(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ExportOvertimeReportRequest(), cancellationToken);
        if (!response.Success)
        {
            return BadRequest(response);
        }

        return File(response.Content, XlsxContentType, response.FileName);
    }

    [HttpPost("export/publish")]
    [FeatureAuthorize(Feature.Attendance_Overtime, AccessLevel.Write)]
    public async Task<ActionResult<ExportOvertimeReportResponse>> PublishReport(CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ExportOvertimeReportRequest(), cancellationToken);
        if (!response.Success)
        {
            return HandleResponse(response);
        }

        try
        {
            await _publisher.PublishAsync(response.Content, response.FileName, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Manual overtime report publish failed");
            return HandleResponse(new ExportOvertimeReportResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.OvertimeExportPublishFailed
            });
        }

        // Content is not re-sent on publish — return a lean success envelope.
        return Ok(new ExportOvertimeReportResponse { FileName = response.FileName });
    }
}
```

- [ ] **Step 2: Build backend + run contract gate tests**

Run: `dotnet build` (repo root sln), then
`dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~ErrorHandlingTests"`
Expected: build OK; response-inheritance + error-range assertions PASS.

- [ ] **Step 3: Regenerate the TypeScript client**

Run: `./scripts/regenerate-api-client.sh`
Expected: `frontend/src/api/generated/api-client.ts` gains `overtime_*` methods and DTO types. (Access-matrix artifacts were already regenerated in Task 1; a Debug build re-runs the generator harmlessly.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/OvertimeController.cs frontend/src/api/generated/api-client.ts
git commit -m "feat: add overtime API controller and regenerate TS client"
```

---

### Task 13: Frontend API hooks

**Files:**
- Modify: `frontend/src/api/client.ts` — add `overtime: ["overtime"] as const` to `QUERY_KEYS`
- Create: `frontend/src/api/hooks/useOvertime.ts`
- Test: `frontend/src/api/hooks/__tests__/useOvertime.test.ts`

**Interfaces:**
- Consumes: generated client `overtime_*` methods (Task 12), `getAuthenticatedApiClient`, `QUERY_KEYS`.
- Produces (used by Task 14): `useOvertimeEmployeesQuery()`, `useMonthlyStatementsQuery(year, month)`, `useUpsertEmployeeMutation()`, `useSetReviewedMutation()`, `useCreateAdjustmentMutation()`, `useDeleteAdjustmentMutation()`, `useCloseMonthMutation()`, `usePublishReportMutation()`, `downloadReportUrl()` helper; re-exported generated types.

- [ ] **Step 1: Write failing hook test**

`useOvertime.test.ts` (pattern from `useRecurringJobs.test.ts`):

```ts
import React from 'react';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

jest.mock('../../client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { overtime: ['overtime'] },
}));

import { getAuthenticatedApiClient } from '../../client';
import { useMonthlyStatementsQuery, useCloseMonthMutation } from '../useOvertime';

const createWrapper = ({ children }: { children: React.ReactNode }) => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return React.createElement(QueryClientProvider, { client: queryClient }, children);
};

describe('useOvertime', () => {
  afterEach(() => jest.resetAllMocks());

  test('useMonthlyStatementsQuery fetches statements for the month', async () => {
    const mockClient = {
      overtime_GetMonthlyStatements: jest.fn().mockResolvedValue({
        success: true, year: 2026, month: 8, isClosed: false, statements: [],
      }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useMonthlyStatementsQuery(2026, 8), { wrapper: createWrapper });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockClient.overtime_GetMonthlyStatements).toHaveBeenCalledWith(2026, 8);
  });

  test('useCloseMonthMutation passes force flag', async () => {
    const mockClient = {
      overtime_CloseMonth: jest.fn().mockResolvedValue({ success: true, closedCount: 3 }),
    };
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue(mockClient);

    const { result } = renderHook(() => useCloseMonthMutation(), { wrapper: createWrapper });
    await act(async () => {
      await result.current.mutateAsync({ year: 2026, month: 8, force: true });
    });

    expect(mockClient.overtime_CloseMonth).toHaveBeenCalledWith(2026, 8, true);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useOvertime.test.ts`
Expected: FAIL — `useOvertime` doesn't exist.

- [ ] **Step 3: Implement the hooks**

`frontend/src/api/hooks/useOvertime.ts`:

```ts
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getAuthenticatedApiClient, QUERY_KEYS } from '../client';
import {
  UpsertOvertimeEmployeeRequest,
  CreateAdjustmentRequest,
  SetStatementReviewedRequest,
  type OvertimeEmployeeDto,
  type AvailableLogetoPersonDto,
  type OvertimeStatementDto,
  type OvertimeAdjustmentDto,
  OvertimeAdjustmentType,
} from '../generated/api-client';

const overtimeKeys = {
  all: [...QUERY_KEYS.overtime] as const,
  employees: () => [...overtimeKeys.all, 'employees'] as const,
  month: (year: number, month: number) => [...overtimeKeys.all, 'month', year, month] as const,
};

export const useOvertimeEmployeesQuery = () =>
  useQuery({
    queryKey: overtimeKeys.employees(),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      return await client.overtime_GetEmployees();
    },
  });

export const useMonthlyStatementsQuery = (year: number, month: number) =>
  useQuery({
    queryKey: overtimeKeys.month(year, month),
    queryFn: async () => {
      const client = getAuthenticatedApiClient();
      return await client.overtime_GetMonthlyStatements(year, month);
    },
  });

const useInvalidatingMutation = <TVariables,>(
  mutationFn: (variables: TVariables) => Promise<unknown>,
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: overtimeKeys.all });
    },
  });
};

export const useUpsertEmployeeMutation = () =>
  useInvalidatingMutation(async (employee: {
    personId: string; displayName: string; baselineHours: number; baselineDate: string; isActive: boolean;
  }) => {
    const client = getAuthenticatedApiClient();
    const request = new UpsertOvertimeEmployeeRequest(employee as any);
    return await client.overtime_UpsertEmployee(request);
  });

export const useSetReviewedMutation = () =>
  useInvalidatingMutation(async (variables: { personId: string; year: number; month: number; isReviewed: boolean }) => {
    const client = getAuthenticatedApiClient();
    const request = new SetStatementReviewedRequest({
      personId: variables.personId,
      year: variables.year,
      month: variables.month,
      isReviewed: variables.isReviewed,
    });
    return await client.overtime_SetReviewed(variables.year, variables.month, request);
  });

export const useCreateAdjustmentMutation = () =>
  useInvalidatingMutation(async (variables: {
    personId: string; year: number; month: number; type: OvertimeAdjustmentType; hours: number; note: string;
  }) => {
    const client = getAuthenticatedApiClient();
    const request = new CreateAdjustmentRequest(variables as any);
    return await client.overtime_CreateAdjustment(request);
  });

export const useDeleteAdjustmentMutation = () =>
  useInvalidatingMutation(async (id: number) => {
    const client = getAuthenticatedApiClient();
    return await client.overtime_DeleteAdjustment(id);
  });

export const useCloseMonthMutation = () =>
  useInvalidatingMutation(async (variables: { year: number; month: number; force?: boolean }) => {
    const client = getAuthenticatedApiClient();
    return await client.overtime_CloseMonth(variables.year, variables.month, variables.force ?? false);
  });

export const usePublishReportMutation = () =>
  useInvalidatingMutation(async () => {
    const client = getAuthenticatedApiClient();
    return await client.overtime_PublishReport();
  });

/** Absolute URL for the XLSX download (opened via window.open — the generated
 * client can't stream files; absolute per CLAUDE.md, relative would hit port 3001). */
export const downloadReportUrl = (): string => {
  const client = getAuthenticatedApiClient() as any;
  return `${client.baseUrl}/api/overtime/export`;
};

export type {
  OvertimeEmployeeDto,
  AvailableLogetoPersonDto,
  OvertimeStatementDto,
  OvertimeAdjustmentDto,
};
export { OvertimeAdjustmentType };
```

In `frontend/src/api/client.ts`, add to `QUERY_KEYS`:

```ts
  overtime: ["overtime"] as const,
```

(Exact generated names/ctor shapes may differ slightly after NSwag runs — align the hook to the actual `api-client.ts`, keeping the exported hook signatures unchanged.)

- [ ] **Step 4: Run tests to verify they pass**

Run: same command as Step 2. Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/client.ts frontend/src/api/hooks/useOvertime.ts frontend/src/api/hooks/__tests__/useOvertime.test.ts
git commit -m "feat: add overtime frontend API hooks"
```

---

### Task 14: Frontend page — Evidence přesčasů

**Files:**
- Create: `frontend/src/pages/OvertimePage.tsx`
- Create: `frontend/src/components/dialogs/CloseOvertimeMonthDialog.tsx`
- Modify: `frontend/src/App.tsx` (import + route), `frontend/src/components/Layout/Sidebar.tsx` (nav item in the `"automatizace"` / "Administrace" section)
- Test: `frontend/src/pages/__tests__/OvertimePage.test.tsx`

**Interfaces:**
- Consumes: all hooks from Task 13, `usePermissionsContext` (`attendance.overtime.write` gates mutations), `useToast`, `useScreenView('Admin', 'Overtime')` (`'Admin'` already exists in `frontend/src/telemetry/screenModules.ts`), `LoadingIndicator`.

**Page layout** (per `docs/design/layout_definition.md` conventions used by `RecurringJobsPage`):
- Header `h1` "Evidence přesčasů" + month navigation (`ChevronLeft` / `ChevronRight` buttons around `"srpen 2026"` label, state `{year, month}` initialized to previous calendar month — that's the month Andy processes).
- Action bar: `Uzavřít měsíc` (primary, hidden when `isClosed`), `Stáhnout Excel` (secondary, `window.open(downloadReportUrl(), '_blank')`), `Nahrát na SharePoint` (secondary), closed badge `Uzavřeno` when `isClosed`.
- Main table, one row per statement: Zkontrolováno (checkbox → `useSetReviewedMutation`, disabled when closed or no write permission) | Zaměstnanec | Převod | Úvazek | Odpracováno | Dovolená | Nemoc | Lékař | NV | Rozdíl | Korekce | **Zůstatek** (bold) | warnings icon (`AlertTriangle` with `title={warnings.join('\n')}`) | `+` button opens inline adjustment form.
- Expandable adjustment rows per person (list existing adjustments with delete buttons + add form: type `<select>` with Czech labels `{Payout: 'Proplacení', PurchaseDeduction: 'Nákup', Correction: 'Korekce', SportBenefit: 'Benefit sport', Other: 'Jiné'}`, hours `<input type="number" step="0.01">`, note `<input>`).
- Collapsible section "Nastavení zaměstnanců" below the table: tracked-employee rows (baseline hours/date inputs, active toggle → `useUpsertEmployeeMutation`) + "Přidat" `<select>` fed by `availablePeople`.
- Numbers formatted via `formatNumber` from `frontend/src/utils/formatters.ts`; hours deltas colored `text-green-600` (≥ 0) / `text-red-600` (< 0).

`CloseOvertimeMonthDialog.tsx` — copy the `ConfirmTriggerJobDialog` modal skeleton; props `{ isOpen, monthLabel, unreviewedNames, onConfirm: (force: boolean) => void, onCancel }`; when `unreviewedNames.length > 0` show the names and make the confirm button read `Uzavřít i tak` (calls `onConfirm(true)`), else `Uzavřít` (calls `onConfirm(false)`). Message: `Opravdu uzavřít měsíc {monthLabel}? Uzavřený měsíc už nelze měnit.`

- [ ] **Step 1: Write failing page test**

`OvertimePage.test.tsx` (pattern from `ExpeditionListArchivePage.test.tsx`):

```tsx
import React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

jest.mock('../../auth/PermissionsContext', () => ({ usePermissionsContext: jest.fn() }));
jest.mock('../../api/client', () => ({
  getAuthenticatedApiClient: jest.fn(),
  QUERY_KEYS: { overtime: ['overtime'] },
}));
jest.mock('../../telemetry/useScreenView', () => ({ useScreenView: jest.fn() }));
jest.mock('../../contexts/ToastContext', () => ({
  useToast: () => ({ showSuccess: jest.fn(), showError: jest.fn() }),
}));

import { getAuthenticatedApiClient } from '../../api/client';
import { usePermissionsContext } from '../../auth/PermissionsContext';
import OvertimePage from '../OvertimePage';

const renderPage = () => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <OvertimePage />
    </QueryClientProvider>,
  );
};

describe('OvertimePage', () => {
  beforeEach(() => {
    (usePermissionsContext as jest.Mock).mockReturnValue({
      hasPermission: (p: string) => ['attendance.overtime.read', 'attendance.overtime.write'].includes(p),
    });
    (getAuthenticatedApiClient as jest.Mock).mockReturnValue({
      overtime_GetMonthlyStatements: jest.fn().mockResolvedValue({
        success: true, year: 2026, month: 7, isClosed: false,
        statements: [{
          personId: 'cccccccc-cccc-cccc-cccc-cccccccccccc', displayName: 'Pepina', isReviewed: false,
          requiredHours: 134.4, workedHours: 130, vacationHours: 6.4, sickHours: 0, doctorHours: 0,
          compTimeHours: 0, otherAbsenceHours: 0, deltaHours: 2, previousBalance: 2.5,
          adjustmentsTotal: 0, projectedBalance: 4.5, warnings: [], adjustments: [],
        }],
      }),
      overtime_GetEmployees: jest.fn().mockResolvedValue({ success: true, employees: [], availablePeople: [] }),
    });
  });

  test('renders statement row with projected balance', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Pepina')).toBeInTheDocument());
    expect(screen.getByText('Evidence přesčasů')).toBeInTheDocument();
    expect(screen.getByText(/4,5|4.5/)).toBeInTheDocument();
  });

  test('shows close button for writer on open month', async () => {
    renderPage();
    await waitFor(() => expect(screen.getByText('Pepina')).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /Uzavřít měsíc/ })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__/OvertimePage.test.tsx`
Expected: FAIL — page doesn't exist.

- [ ] **Step 3: Implement dialog + page**

Implement `CloseOvertimeMonthDialog.tsx` and `OvertimePage.tsx` per the layout spec above. Structural skeleton for the page (fill table cells/forms per the column list):

```tsx
import React, { useState } from 'react';
import { ChevronLeft, ChevronRight, Download, Upload, Lock, AlertTriangle, Plus, Trash2, Check } from 'lucide-react';
import {
  useOvertimeEmployeesQuery, useMonthlyStatementsQuery, useUpsertEmployeeMutation,
  useSetReviewedMutation, useCreateAdjustmentMutation, useDeleteAdjustmentMutation,
  useCloseMonthMutation, usePublishReportMutation, downloadReportUrl,
  OvertimeAdjustmentType, type OvertimeStatementDto,
} from '../api/hooks/useOvertime';
import { usePermissionsContext } from '../auth/PermissionsContext';
import { useToast } from '../contexts/ToastContext';
import { LoadingIndicator } from '../components/ui/LoadingIndicator';
import CloseOvertimeMonthDialog from '../components/dialogs/CloseOvertimeMonthDialog';
import { useScreenView } from '../telemetry/useScreenView';
import { formatNumber } from '../utils/formatters';

const WRITE_PERMISSION = 'attendance.overtime.write';
const MONTH_LABELS = ['leden', 'únor', 'březen', 'duben', 'květen', 'červen',
  'červenec', 'srpen', 'září', 'říjen', 'listopad', 'prosinec'];

const previousMonth = (): { year: number; month: number } => {
  const now = new Date();
  const month = now.getMonth() === 0 ? 12 : now.getMonth();      // getMonth() is 0-based → this is already the previous month 1-based
  const year = now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();
  return { year, month };
};

const OvertimePage: React.FC = () => {
  useScreenView('Admin', 'Overtime');
  const [{ year, month }, setPeriod] = useState(previousMonth);
  const [closeDialogOpen, setCloseDialogOpen] = useState(false);
  const [expandedPerson, setExpandedPerson] = useState<string | null>(null);
  const { hasPermission } = usePermissionsContext();
  const canWrite = hasPermission(WRITE_PERMISSION);
  const { showSuccess, showError } = useToast();

  const statementsQuery = useMonthlyStatementsQuery(year, month);
  const employeesQuery = useOvertimeEmployeesQuery();
  const setReviewed = useSetReviewedMutation();
  const closeMonth = useCloseMonthMutation();
  // ... remaining mutations

  const shiftMonth = (delta: number) => setPeriod(({ year, month }) => {
    const next = month + delta;
    if (next < 1) return { year: year - 1, month: 12 };
    if (next > 12) return { year: year + 1, month: 1 };
    return { year, month: next };
  });

  const handleClose = async (force: boolean) => {
    setCloseDialogOpen(false);
    try {
      const result: any = await closeMonth.mutateAsync({ year, month, force });
      if (result?.publishFailed) {
        showError('Měsíc uzavřen', 'Nahrání reportu na SharePoint ale selhalo.');
      } else {
        showSuccess('Měsíc uzavřen', `Uzavřeno ${result?.closedCount ?? 0} zaměstnanců.`);
      }
    } catch (err) {
      showError('Uzavření selhalo', err instanceof Error ? err.message : 'Neznámá chyba');
    }
  };

  // render: header + month nav, action bar, statements table,
  // per-person expandable adjustments, employee settings section, dialog
  // (structure identical to RecurringJobsPage layout shell)
  ...
};

export default OvertimePage;
```

Wire into `App.tsx`:

```tsx
import OvertimePage from "./pages/OvertimePage";
...
<Route path="/overtime" element={guard("/overtime", <OvertimePage />)} />
```

and into `Sidebar.tsx`'s "Administrace" section items:

```tsx
{
  id: "overtime",
  name: "Evidence přesčasů",
  href: "/overtime",
  key: "/overtime",
},
```

(The menu item shows only for users holding `attendance.overtime.read` via `ACCESS_ROUTES` — that mapping came from Task 1's `menuPaths` entry.)

- [ ] **Step 4: Run tests to verify they pass**

Run: same command as Step 2. Expected: PASS (2 tests).

- [ ] **Step 5: Build + lint**

Run: `cd frontend && CI=false npm run build && npm run lint`
Expected: build succeeds (this catches type errors `tsc` misses — known project gotcha), lint clean.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/OvertimePage.tsx frontend/src/components/dialogs/CloseOvertimeMonthDialog.tsx frontend/src/App.tsx frontend/src/components/Layout/Sidebar.tsx frontend/src/pages/__tests__/OvertimePage.test.tsx
git commit -m "feat: add overtime ledger admin page"
```

---

### Task 15: Final validation

**Files:** none new — verification only.

- [ ] **Step 1: Backend gates**

```bash
dotnet build
dotnet format --verify-no-changes || dotnet format
dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~Anela.Heblo.Tests.Application.Overtime"
dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false --filter "FullyQualifiedName~ErrorHandlingTests"
```

Expected: all pass. If `dotnet format` changed files, re-run build and commit the formatting.

- [ ] **Step 2: Frontend gates**

```bash
cd frontend && CI=false npm run build && npm run lint && CI=true npx react-scripts test --watchAll=false src/api/hooks/__tests__/useOvertime.test.ts src/pages/__tests__/OvertimePage.test.tsx
```

Expected: all pass.

- [ ] **Step 3: Full backend test suite (regression)**

Run: `dotnet test backend/test/Anela.Heblo.Tests -p:UseSharedCompilation=false`
Expected: no regressions (AccessMatrixGen crash noise is non-fatal, known).

- [ ] **Step 4: Commit any stragglers**

```bash
git add -A && git commit -m "chore: overtime ledger validation fixes" || echo "clean"
```

**Deployment notes (manual, post-merge):**
- Apply the `AddOvertimeLedger` migration manually to `Heblo_TST` / production (project convention).
- Fill `Overtime:ContractHours` (person GUID → daily hours) and verify `Overtime:ActivityCategories` names against the live Logeto account before first use.
- SharePoint publishing needs `Overtime:ExportDriveId` (+ folder path); the Graph app registration already used by CatalogDocuments covers `Files.ReadWrite.All` app-only.
- Seed the new roles: `scripts/seed-authorization.sh` / `scripts/sync-entra-access.sh`.
- E2E (staging, nightly) is intentionally not part of this plan — the page needs staging data + closed months first; add a smoke test in a follow-up once staging has the migration.





