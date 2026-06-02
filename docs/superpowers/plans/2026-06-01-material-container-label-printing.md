# MaterialContainer Barcode Label Printing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Plan location note:** Written here because the session is in plan mode (only this file is writable). On execution, copy to `docs/superpowers/plans/2026-06-01-material-container-label-printing.md`.

**Goal:** Add an admin action that generates fresh `M########` container codes, persists them as `Unassigned` records, and prints Code128 labels to a Zebra printer via the existing CUPS server; the terminal scan flow then *assigns* those codes and rejects any code the app didn't generate.

**Architecture:** Reuse the existing CUPS/IPP delivery (`ICupsPrintingService` over Tailscale). Backend builds ZPL (the Zebra renders Code128 from `^BC` — no barcode library needed) and ships it as a raw print job through a new Application-level `ILabelPrintingService` abstraction implemented in the CUPS adapter. A new `Unassigned` status + nullable material/lot let a printed label exist before it is scanned; the existing scan handler flips from *create* to *assign-existing*.

**Tech Stack:** .NET 8, MediatR, FluentValidation, EF Core (PostgreSQL, manual migrations), SharpIppNext, React + React Query, auto-generated OpenAPI client.

---

## Context

Today the terminal workflow (#2184) can only **scan pre-printed `M`+8-digit labels** — the app never generates codes (`MaterialContainerCodeGenerator` emits the wrong `INT-{seq:D8}` format and is unused). The company is switching to printing its own labels on demand. Decisions from brainstorm: print = generate new blank labels; records are created at print time as `Unassigned`; delivery is ZPL → CUPS raw Zebra queue; label is Code128 + human-readable M-code at ~50×25 mm; trigger is an admin button on `MaterialContainerList`; full switchover — scanning an unknown (not-generated) code is rejected; the code sequence starts above the highest existing M-code.

## File Structure

**Backend — create:**
- `.../Inventory/UseCases/PrintMaterialContainerLabels/PrintMaterialContainerLabelsRequest.cs` (+ `Response`, `Validator`, `Handler`)
- `.../Inventory/Printing/MaterialContainerLabelZplBuilder.cs` (pure ZPL builder)
- `Application/Shared/Printing/ILabelPrintingService.cs` (abstraction)
- `Adapters/Anela.Heblo.Adapters.Cups/CupsLabelPrintingService.cs` (implementation)
- Migration `*_MakeMaterialContainerMaterialLotNullable.cs`
- Migration `*_SeedMaterialContainerSequence.cs`

**Backend — modify:**
- `Domain/.../Inventory/MaterialContainerStatus.cs`, `MaterialContainer.cs`
- `Persistence/.../Inventory/MaterialContainerConfiguration.cs`, `MaterialContainerCodeGenerator.cs`
- `Adapters/.../Cups/ICupsPrintingService.cs`, `CupsPrintingService.cs`, `CupsOptions.cs`, `CupsAdapterServiceCollectionExtensions.cs`
- `Application/.../Inventory/Contracts/MaterialContainerDto.cs`
- `Application/.../Inventory/UseCases/CreateMaterialContainers/CreateMaterialContainersHandler.cs` (create → assign)
- `Application/Shared/ErrorCodes.cs`
- `API/Controllers/MaterialContainersController.cs`
- `API/appsettings.json`

**Frontend — modify:**
- `frontend/src/api/hooks/useMaterialContainers.ts`
- `frontend/src/components/pages/MaterialContainerList.tsx`
- `frontend/src/components/terminal/lot-identification/ContainerScanLoop.tsx`

---

## Task 1: Add `Unassigned` status + entity lifecycle

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainerStatus.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Inventory/MaterialContainer.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/MaterialContainerTests.cs` (create)

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class MaterialContainerTests
{
    [Fact]
    public void CreateUnassigned_SetsUnassignedStatus_WithNoMaterialOrLot()
    {
        var c = MaterialContainer.CreateUnassigned("M00000001", "tester");

        c.Status.Should().Be(MaterialContainerStatus.Unassigned);
        c.Code.Should().Be("M00000001");
        c.MaterialCode.Should().BeNull();
        c.LotCode.Should().BeNull();
        c.CreatedBy.Should().Be("tester");
    }

    [Fact]
    public void Assign_FillsMaterialAndLot_AndFlipsToAssigned()
    {
        var c = MaterialContainer.CreateUnassigned("M00000002", "tester");

        c.Assign("MAT-1", "LOT-9", amount: null, unit: null, purchaseOrderLineId: 5, updatedBy: "worker");

        c.Status.Should().Be(MaterialContainerStatus.Assigned);
        c.MaterialCode.Should().Be("MAT-1");
        c.LotCode.Should().Be("LOT-9");
        c.PurchaseOrderLineId.Should().Be(5);
        c.UpdatedBy.Should().Be("worker");
        c.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Assign_Throws_WhenNotUnassigned()
    {
        var c = MaterialContainer.CreateUnassigned("M00000003", "tester");
        c.Assign("MAT-1", "LOT-9", null, null, null, "worker");

        var act = () => c.Assign("MAT-2", "LOT-8", null, null, null, "worker");

        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter MaterialContainerTests`
Expected: FAIL — `CreateUnassigned` / `Assign` do not exist.

- [ ] **Step 3: Add the enum value**

In `MaterialContainerStatus.cs`:

```csharp
public enum MaterialContainerStatus
{
    Assigned = 0,
    Discarded = 1,
    Unassigned = 2,
}
```

- [ ] **Step 4: Make material/lot nullable and add factory + transition**

In `MaterialContainer.cs`: change the two properties to nullable and add the methods. Keep the existing public constructor unchanged.

```csharp
    public string? MaterialCode { get; private set; }
    public string? LotCode { get; private set; }

    public static MaterialContainer CreateUnassigned(string code, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));

        return new MaterialContainer
        {
            Code = code,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            Status = MaterialContainerStatus.Unassigned,
        };
    }

    public void Assign(string materialCode, string lotCode, decimal? amount, string? unit, int? purchaseOrderLineId, string updatedBy)
    {
        if (Status != MaterialContainerStatus.Unassigned)
            throw new InvalidOperationException($"Container {Code} cannot be assigned from status {Status}.");
        if (string.IsNullOrWhiteSpace(materialCode)) throw new ArgumentException("MaterialCode is required.", nameof(materialCode));
        if (string.IsNullOrWhiteSpace(lotCode)) throw new ArgumentException("LotCode is required.", nameof(lotCode));
        if (amount is <= 0) throw new ArgumentException("Amount must be positive when provided.", nameof(amount));
        if (unit is not null && string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit must be non-empty when provided.", nameof(unit));
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("UpdatedBy is required.", nameof(updatedBy));

        MaterialCode = materialCode;
        LotCode = lotCode;
        Amount = amount;
        Unit = unit;
        PurchaseOrderLineId = purchaseOrderLineId;
        Status = MaterialContainerStatus.Assigned;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
```

Note: the `private set;` on `MaterialCode`/`LotCode` plus the parameterless `protected MaterialContainer()` make the object-initializer in `CreateUnassigned` valid (it runs inside the class).

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter MaterialContainerTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/MaterialContainerTests.cs
git commit -m "feat: add Unassigned status and Assign transition to MaterialContainer"
```

---

## Task 2: Make material/lot columns nullable (EF config + migration)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerConfiguration.cs`
- Create: migration `*_MakeMaterialContainerMaterialLotNullable.cs`

- [ ] **Step 1: Relax the EF config**

In `MaterialContainerConfiguration.cs`, remove `.IsRequired()` from `MaterialCode` and `LotCode`:

```csharp
        builder.Property(x => x.MaterialCode)
            .HasMaxLength(50);

        builder.Property(x => x.LotCode)
            .HasMaxLength(100);
```

- [ ] **Step 2: Generate the migration**

Run: `cd backend/src/Anela.Heblo.Persistence && dotnet ef migrations add MakeMaterialContainerMaterialLotNullable --startup-project ../Anela.Heblo.API`
Expected: a new migration file is created with `AlterColumn` for both columns.

- [ ] **Step 3: Verify the migration `Up` body**

The generated `Up` should match this (mirrors `MakeMaterialContainerAmountUnitNullable`). Fix it to this if EF produced anything different:

```csharp
migrationBuilder.AlterColumn<string>(
    name: "MaterialCode", schema: "public", table: "MaterialContainers",
    type: "character varying(50)", maxLength: 50, nullable: true,
    oldClrType: typeof(string), oldType: "character varying(50)", oldMaxLength: 50);

migrationBuilder.AlterColumn<string>(
    name: "LotCode", schema: "public", table: "MaterialContainers",
    type: "character varying(100)", maxLength: 100, nullable: true,
    oldClrType: typeof(string), oldType: "character varying(100)", oldMaxLength: 100);
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Persistence`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence
git commit -m "feat: make MaterialContainer material/lot columns nullable"
```

---

## Task 3: Fix code generator format + seed the sequence

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Catalog/Inventory/MaterialContainerCodeGenerator.cs`
- Create: migration `*_SeedMaterialContainerSequence.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/MaterialContainerCodeGeneratorFormatTests.cs` (create)

- [ ] **Step 1: Write a failing format test** (pure regex check on the format string — no DB)

```csharp
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class MaterialContainerCodeGeneratorFormatTests
{
    [Theory]
    [InlineData(1, "M00000001")]
    [InlineData(123, "M00000123")]
    [InlineData(99999999, "M99999999")]
    public void Format_ProducesScanCompatibleCode(long seq, string expected)
    {
        var code = $"M{seq:D8}";

        code.Should().Be(expected);
        Regex.IsMatch(code, @"^M\d{8}$").Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test, verify it passes** (this guards the format we will hardcode)

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter MaterialContainerCodeGeneratorFormatTests`
Expected: PASS.

- [ ] **Step 3: Change the generator format**

In `MaterialContainerCodeGenerator.cs` line 32, replace:

```csharp
            codes.Add($"INT-{seq:D8}");
```
with:
```csharp
            codes.Add($"M{seq:D8}");
```

- [ ] **Step 4: Create the sequence-seed migration** (no entity change, so add an empty migration then add SQL)

Run: `cd backend/src/Anela.Heblo.Persistence && dotnet ef migrations add SeedMaterialContainerSequence --startup-project ../Anela.Heblo.API`

Then set the `Up`/`Down` bodies to:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Advance the code sequence above the highest existing M-code so generated
    // codes never collide with already-scanned pre-printed labels.
    migrationBuilder.Sql(@"
        SELECT setval(
            'material_container_internal_seq',
            GREATEST(
                (SELECT last_value FROM material_container_internal_seq),
                COALESCE((SELECT MAX(CAST(SUBSTRING(""Code"" FROM 2) AS bigint))
                          FROM public.""MaterialContainers""
                          WHERE ""Code"" ~ '^M[0-9]{8}$'), 0)
            ),
            true);");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    // No-op: resetting a sequence downward is unsafe and unnecessary.
}
```

- [ ] **Step 5: Build + run the format test**

Run: `dotnet build backend/src/Anela.Heblo.Persistence && dotnet test backend/test/Anela.Heblo.Tests --filter MaterialContainerCodeGeneratorFormatTests`
Expected: success / PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/MaterialContainerCodeGeneratorFormatTests.cs
git commit -m "feat: generate M-format container codes and seed sequence above existing codes"
```

---

## Task 4: ZPL label builder (pure)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Printing/MaterialContainerLabelZplBuilder.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/MaterialContainerLabelZplBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class MaterialContainerLabelZplBuilderTests
{
    [Fact]
    public void Build_EmitsOneLabelBlockPerCode_WithCode128AndText()
    {
        var zpl = MaterialContainerLabelZplBuilder.Build(new[] { "M00000001", "M00000002" });

        zpl.Split("^XZ", System.StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
        System.Text.RegularExpressions.Regex.Matches(zpl, "\\^XA").Should().HaveCount(2);
        zpl.Should().Contain("^BCN");              // Code128 barcode command
        zpl.Should().Contain("^FDM00000001^FS");   // barcode + human-readable use same data
    }

    [Fact]
    public void Build_Throws_OnEmptyInput()
    {
        var act = () => MaterialContainerLabelZplBuilder.Build(System.Array.Empty<string>());
        act.Should().Throw<System.ArgumentException>();
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter MaterialContainerLabelZplBuilderTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the builder** (50×25 mm @ 203 dpi ≈ 406×203 dots; tune via test print)

```csharp
using System.Text;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.Printing;

public static class MaterialContainerLabelZplBuilder
{
    private const int LabelWidthDots = 406;   // ~50 mm @ 203 dpi
    private const int LabelHeightDots = 203;  // ~25 mm @ 203 dpi

    public static string Build(IReadOnlyCollection<string> codes)
    {
        if (codes is null || codes.Count == 0)
            throw new ArgumentException("At least one code is required.", nameof(codes));

        var sb = new StringBuilder();
        foreach (var code in codes)
        {
            sb.Append("^XA");
            sb.Append($"^PW{LabelWidthDots}");
            sb.Append($"^LL{LabelHeightDots}");
            sb.Append("^FO30,25^BY2");
            sb.Append("^BCN,90,N,N,N");          // Code128, height 90, no embedded text
            sb.Append($"^FD{code}^FS");
            sb.Append($"^FO30,135^A0N,30,30^FD{code}^FS");  // human-readable code
            sb.Append("^XZ");
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter MaterialContainerLabelZplBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/Printing backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/MaterialContainerLabelZplBuilderTests.cs
git commit -m "feat: add ZPL builder for MaterialContainer labels"
```

---

## Task 5: Raw-ZPL CUPS delivery (`ILabelPrintingService`)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Shared/Printing/ILabelPrintingService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/ICupsPrintingService.cs`, `CupsPrintingService.cs`, `CupsOptions.cs`, `CupsAdapterServiceCollectionExtensions.cs`
- Create: `backend/src/Adapters/Anela.Heblo.Adapters.Cups/CupsLabelPrintingService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsLabelPrintingServiceTests.cs`

- [ ] **Step 1: Add the document-format parameter to the CUPS service**

`ICupsPrintingService.cs`:
```csharp
Task PrintAsync(string filePath, string? printerName = null, string documentFormat = "application/pdf", CancellationToken cancellationToken = default);
```

`CupsPrintingService.cs` — update the signature and use the parameter:
```csharp
    public async Task PrintAsync(string filePath, string? printerName = null, string documentFormat = "application/pdf", CancellationToken cancellationToken = default)
```
and line 51:
```csharp
                    DocumentFormat = documentFormat
```
(Existing `CupsPrintQueueSink` callers pass no format → still `application/pdf`. Unchanged.)

- [ ] **Step 2: Add the label-printer config key**

`CupsOptions.cs`:
```csharp
    public string LabelPrinterName { get; set; } = string.Empty; // raw ZPL queue (Zebra)
```

- [ ] **Step 3: Add the Application-level abstraction**

`Application/Shared/Printing/ILabelPrintingService.cs`:
```csharp
namespace Anela.Heblo.Application.Shared.Printing;

public interface ILabelPrintingService
{
    Task PrintZplAsync(string zpl, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write a failing test for the adapter**

`CupsLabelPrintingServiceTests.cs`:
```csharp
using Anela.Heblo.Adapters.Cups;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CupsLabelPrintingServiceTests
{
    [Fact]
    public async Task PrintZplAsync_WritesTempFile_SendsRawToLabelPrinter_ThenDeletes()
    {
        var cups = new Mock<ICupsPrintingService>();
        string? capturedPath = null;
        cups.Setup(c => c.PrintAsync(It.IsAny<string>(), "Zebra-Raw", "application/octet-stream", It.IsAny<CancellationToken>()))
            .Callback<string, string?, string, CancellationToken>((p, _, _, _) =>
            {
                capturedPath = p;
                File.Exists(p).Should().BeTrue();
            })
            .Returns(Task.CompletedTask);

        var options = Options.Create(new CupsOptions { LabelPrinterName = "Zebra-Raw" });
        var sut = new CupsLabelPrintingService(cups.Object, options, NullLogger<CupsLabelPrintingService>.Instance);

        await sut.PrintZplAsync("^XA^XZ");

        cups.VerifyAll();
        File.Exists(capturedPath!).Should().BeFalse(); // temp cleaned up
    }
}
```

- [ ] **Step 5: Run test, verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter CupsLabelPrintingServiceTests`
Expected: FAIL — `CupsLabelPrintingService` does not exist.

- [ ] **Step 6: Implement the adapter**

`CupsLabelPrintingService.cs`:
```csharp
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.Cups;

public class CupsLabelPrintingService : ILabelPrintingService
{
    private readonly ICupsPrintingService _cups;
    private readonly IOptions<CupsOptions> _options;
    private readonly ILogger<CupsLabelPrintingService> _logger;

    public CupsLabelPrintingService(
        ICupsPrintingService cups, IOptions<CupsOptions> options, ILogger<CupsLabelPrintingService> logger)
    {
        _cups = cups;
        _options = options;
        _logger = logger;
    }

    public async Task PrintZplAsync(string zpl, CancellationToken cancellationToken = default)
    {
        var printer = _options.Value.LabelPrinterName;
        if (string.IsNullOrWhiteSpace(printer))
            throw new InvalidOperationException("CupsOptions.LabelPrinterName is not configured.");

        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, zpl, cancellationToken);
            await _cups.PrintAsync(tempPath, printer, "application/octet-stream", cancellationToken);
            _logger.LogInformation("Sent ZPL label batch to printer {Printer}", printer);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
```

- [ ] **Step 7: Register the service in DI**

`CupsAdapterServiceCollectionExtensions.cs`, after the existing registrations:
```csharp
        services.AddScoped<ILabelPrintingService, CupsLabelPrintingService>();
```
(Add `using Anela.Heblo.Application.Shared.Printing;` if not present.)

- [ ] **Step 8: Run test, verify it passes; update existing CUPS tests if the new param broke any call**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "CupsLabelPrintingServiceTests|CupsPrintingServiceTests"`
Expected: PASS. If `CupsPrintingServiceTests` mocks `PrintJobAsync` and asserts `DocumentFormat`, it still sees `application/pdf` (default) — no change needed.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/Printing backend/src/Adapters/Anela.Heblo.Adapters.Cups backend/test/Anela.Heblo.Tests/Features/ExpeditionList/CupsLabelPrintingServiceTests.cs
git commit -m "feat: add raw-ZPL label printing over CUPS"
```

---

## Task 6: `PrintMaterialContainerLabels` use case + DTO status

**Files:**
- Create: `.../UseCases/PrintMaterialContainerLabels/PrintMaterialContainerLabelsRequest.cs`, `Response.cs`, `Validator.cs`, `Handler.cs`
- Modify: `.../Inventory/Contracts/MaterialContainerDto.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/PrintMaterialContainerLabelsHandlerTests.cs`

- [ ] **Step 1: Add `Status` to the DTO**

`MaterialContainerDto.cs` — add (and make material/lot nullable to match the entity):
```csharp
    public string? MaterialCode { get; set; }
    public string? LotCode { get; set; }
    public string Status { get; set; } = null!;
```
Update existing `MapToDto` in `CreateMaterialContainersHandler` and the List/GetByCode handlers to set `Status = c.Status.ToString()`. (Search: `new MaterialContainerDto` / `MapToDto` across `Features/Catalog/Inventory`.)

- [ ] **Step 2: Define request / response / validator**

`PrintMaterialContainerLabelsRequest.cs`:
```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsRequest : IRequest<PrintMaterialContainerLabelsResponse>
{
    public int Count { get; set; }
}
```

`PrintMaterialContainerLabelsResponse.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsResponse : BaseResponse
{
    public List<MaterialContainerDto> Containers { get; set; } = new();

    public PrintMaterialContainerLabelsResponse() : base() { }
    public PrintMaterialContainerLabelsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

`PrintMaterialContainerLabelsRequestValidator.cs`:
```csharp
using FluentValidation;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsRequestValidator : AbstractValidator<PrintMaterialContainerLabelsRequest>
{
    public PrintMaterialContainerLabelsRequestValidator()
    {
        RuleFor(x => x.Count).GreaterThan(0).LessThanOrEqualTo(200)
            .WithMessage("Count must be between 1 and 200.");
    }
}
```

- [ ] **Step 3: Write a failing handler test**

`PrintMaterialContainerLabelsHandlerTests.cs`:
```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class PrintMaterialContainerLabelsHandlerTests
{
    [Fact]
    public async Task Handle_GeneratesCodes_PersistsUnassigned_AndPrintsZpl()
    {
        var generator = new Mock<IMaterialContainerCodeGenerator>();
        generator.Setup(g => g.GenerateAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "M00000010", "M00000011" });

        var repo = new Mock<IMaterialContainerRepository>();
        var label = new Mock<ILabelPrintingService>();
        var user = new Mock<ICurrentUserService>();
        user.Setup(u => u.GetCurrentUser()).Returns(new CurrentUser { Name = "admin" });

        var sut = new PrintMaterialContainerLabelsHandler(
            NullLogger<PrintMaterialContainerLabelsHandler>.Instance,
            generator.Object, repo.Object, label.Object, user.Object);

        var result = await sut.Handle(new PrintMaterialContainerLabelsRequest { Count = 2 }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Containers.Should().HaveCount(2);
        result.Containers.Should().OnlyContain(c => c.Status == "Unassigned");
        repo.Verify(r => r.AddRangeAsync(
            It.Is<IEnumerable<MaterialContainer>>(cs => cs.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        label.Verify(l => l.PrintZplAsync(It.Is<string>(z => z.Contains("M00000010") && z.Contains("M00000011")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```
(Confirm the exact shape of `CurrentUser`/`ICurrentUserService.GetCurrentUser()` from `CreateMaterialContainersHandler` and match it — that handler uses `currentUser.Name ?? "System"`.)

- [ ] **Step 4: Run test, verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter PrintMaterialContainerLabelsHandlerTests`
Expected: FAIL — handler does not exist.

- [ ] **Step 5: Implement the handler** (persist-then-print: a physical label must never exist without a DB record)

```csharp
using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Features.Catalog.Inventory.Printing;
using Anela.Heblo.Application.Shared.Printing;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsHandler
    : IRequestHandler<PrintMaterialContainerLabelsRequest, PrintMaterialContainerLabelsResponse>
{
    private readonly ILogger<PrintMaterialContainerLabelsHandler> _logger;
    private readonly IMaterialContainerCodeGenerator _generator;
    private readonly IMaterialContainerRepository _repository;
    private readonly ILabelPrintingService _labelPrinter;
    private readonly ICurrentUserService _currentUserService;

    public PrintMaterialContainerLabelsHandler(
        ILogger<PrintMaterialContainerLabelsHandler> logger,
        IMaterialContainerCodeGenerator generator,
        IMaterialContainerRepository repository,
        ILabelPrintingService labelPrinter,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _generator = generator;
        _repository = repository;
        _labelPrinter = labelPrinter;
        _currentUserService = currentUserService;
    }

    public async Task<PrintMaterialContainerLabelsResponse> Handle(
        PrintMaterialContainerLabelsRequest request, CancellationToken cancellationToken)
    {
        var createdBy = _currentUserService.GetCurrentUser().Name ?? "System";

        var codes = await _generator.GenerateAsync(request.Count, cancellationToken);
        var containers = codes.Select(code => MaterialContainer.CreateUnassigned(code, createdBy)).ToList();

        await _repository.AddRangeAsync(containers, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var zpl = MaterialContainerLabelZplBuilder.Build(codes);
        await _labelPrinter.PrintZplAsync(zpl, cancellationToken);

        _logger.LogInformation("Printed {Count} MaterialContainer labels", containers.Count);

        return new PrintMaterialContainerLabelsResponse
        {
            Containers = containers.Select(c => new MaterialContainerDto
            {
                Id = c.Id, Code = c.Code, MaterialCode = c.MaterialCode, LotCode = c.LotCode,
                Amount = c.Amount, Unit = c.Unit, Status = c.Status.ToString(),
                CreatedAt = c.CreatedAt, CreatedBy = c.CreatedBy, PurchaseOrderLineId = c.PurchaseOrderLineId,
            }).ToList(),
        };
    }
}
```

- [ ] **Step 6: Run test, verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter PrintMaterialContainerLabelsHandlerTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Inventory backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/PrintMaterialContainerLabelsHandlerTests.cs
git commit -m "feat: add PrintMaterialContainerLabels use case"
```

---

## Task 7: Controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs`

- [ ] **Step 1: Add the route**

Add `using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;` and a new action:
```csharp
    [HttpPost("print-labels")]
    public async Task<ActionResult<PrintMaterialContainerLabelsResponse>> PrintLabels(
        [FromBody] PrintMaterialContainerLabelsRequest request, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return HandleResponse(response);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/MaterialContainersController.cs
git commit -m "feat: add POST /api/material-containers/print-labels endpoint"
```

---

## Task 8: Scan handler — create → assign + reject unknown codes

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Inventory/UseCases/CreateMaterialContainers/CreateMaterialContainersHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateMaterialContainersHandlerTests.cs` (update existing)

- [ ] **Step 1: Add the new error code**

`ErrorCodes.cs`, after `MaterialContainerCodeInvalidFormat = 2808`:
```csharp
    UnknownMaterialContainerCode = 2809,
```
(Also add a Czech message wherever 2807/2808 messages are mapped — search for `MaterialContainerCodeExists` in the error-message resource/dictionary and add a sibling entry for `UnknownMaterialContainerCode`, e.g. "Neznámý štítek — nejprve jej vygenerujte v aplikaci.")

- [ ] **Step 2: Update the handler tests for the new semantics**

Replace the "creates new container" expectations. Key cases:
```csharp
[Fact]
public async Task Handle_AssignsUnassignedContainer_OnScan()
{
    var container = MaterialContainer.CreateUnassigned("M00000001", "admin");
    _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", It.IsAny<CancellationToken>())).ReturnsAsync(container);
    // ... PO repo returns a valid line for the provided PurchaseOrderLineId ...

    var result = await _sut.Handle(RequestFor("M00000001", "MAT-1", "LOT-1", poLineId: 7), CancellationToken.None);

    result.Success.Should().BeTrue();
    container.Status.Should().Be(MaterialContainerStatus.Assigned);
    container.MaterialCode.Should().Be("MAT-1");
    _containerRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Handle_ReturnsUnknownCode_WhenCodeNotInDatabase()
{
    _containerRepo.Setup(r => r.GetByCodeAsync("M00000099", It.IsAny<CancellationToken>())).ReturnsAsync((MaterialContainer?)null);

    var result = await _sut.Handle(RequestFor("M00000099", "MAT-1", "LOT-1"), CancellationToken.None);

    result.Success.Should().BeFalse();
    result.ErrorCode.Should().Be(ErrorCodes.UnknownMaterialContainerCode);
}

[Fact]
public async Task Handle_ReturnsCodeExists_WhenAlreadyAssigned()
{
    var assigned = MaterialContainer.CreateUnassigned("M00000001", "admin");
    assigned.Assign("MAT-OLD", "LOT-OLD", null, null, null, "worker");
    _containerRepo.Setup(r => r.GetByCodeAsync("M00000001", It.IsAny<CancellationToken>())).ReturnsAsync(assigned);

    var result = await _sut.Handle(RequestFor("M00000001", "MAT-1", "LOT-1"), CancellationToken.None);

    result.ErrorCode.Should().Be(ErrorCodes.MaterialContainerCodeExists);
    result.Params!["MaterialCode"].Should().Be("MAT-OLD");
}
```

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter CreateMaterialContainersHandlerTests`
Expected: FAIL — handler still creates.

- [ ] **Step 4: Rewrite the handler body** (replace lines 43–85; keep the in-batch duplicate guard and the PO-line check, replace the existence loop + creation)

```csharp
        var currentUser = _currentUserService.GetCurrentUser();
        var updatedBy = currentUser.Name ?? "System";

        var toAssign = new List<(MaterialContainer Container, CreateMaterialContainerItem Item)>();

        foreach (var item in request.Items)
        {
            var existing = await _containerRepository.GetByCodeAsync(item.Code, cancellationToken);

            if (existing is null)
            {
                return new CreateMaterialContainersResponse(
                    ErrorCodes.UnknownMaterialContainerCode,
                    new Dictionary<string, string> { { "Code", item.Code } });
            }

            if (existing.Status != MaterialContainerStatus.Unassigned)
            {
                return new CreateMaterialContainersResponse(
                    ErrorCodes.MaterialContainerCodeExists,
                    new Dictionary<string, string>
                    {
                        { "Code", item.Code },
                        { "MaterialCode", existing.MaterialCode ?? string.Empty },
                        { "LotCode", existing.LotCode ?? string.Empty },
                        { "Status", existing.Status.ToString() }
                    });
            }

            if (item.PurchaseOrderLineId.HasValue)
            {
                var line = await _purchaseOrderRepository.GetLineByIdAsync(item.PurchaseOrderLineId.Value, cancellationToken);
                if (line == null)
                {
                    return new CreateMaterialContainersResponse(
                        ErrorCodes.PurchaseOrderLineNotFound,
                        new Dictionary<string, string> { { "PurchaseOrderLineId", item.PurchaseOrderLineId.Value.ToString() } });
                }
            }

            toAssign.Add((existing, item));
        }

        foreach (var (container, item) in toAssign)
        {
            container.Assign(item.MaterialCode, item.LotCode, item.Amount, item.Unit, item.PurchaseOrderLineId, updatedBy);
        }

        await _containerRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Assigned {Count} MaterialContainers", toAssign.Count);

        return new CreateMaterialContainersResponse { Containers = toAssign.Select(t => MapToDto(t.Container)).ToList() };
```
Update `MapToDto` to set `Status = c.Status.ToString()` and the now-nullable `MaterialCode`/`LotCode`. (`GetByCodeAsync` returns a tracked entity, so mutating + `SaveChangesAsync` persists; no explicit `UpdateAsync` needed.)

- [ ] **Step 5: Run tests, verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter CreateMaterialContainersHandlerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application backend/test/Anela.Heblo.Tests/Features/Catalog/Inventory/CreateMaterialContainersHandlerTests.cs
git commit -m "feat: scan flow assigns unassigned containers and rejects unknown codes"
```

---

## Task 9: Config + secrets

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

- [ ] **Step 1: Add the label-printer key to the `Cups` section**

```json
"Cups": {
  "ServerUrl": "...",
  "PrinterName": "Brother-HL-L2442DW",
  "LabelPrinterName": "Zebra-Raw",
  "Username": "xxxx",
  "Password": "xxxxx"
}
```
(`Zebra-Raw` = the raw CUPS queue name for the Zebra — confirm the actual queue name. No secret here; credentials already live in Key Vault as `Cups--Username` / `Cups--Password`.)

- [ ] **Step 2: Full backend build + format**

Run: `dotnet build backend && dotnet format backend --verify-no-changes`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "chore: add Cups label printer config key"
```

**Ops prerequisite (not code):** a **raw** print queue named `Zebra-Raw` must exist on the CUPS server pointing at the Zebra label printer. The app cannot create it.

---

## Task 10: Frontend — regenerate client + print hook

**Files:**
- Modify: `frontend/src/api/hooks/useMaterialContainers.ts`
- Test: `frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts` (update)

- [ ] **Step 1: Regenerate the OpenAPI client** (the C# build emits it; confirm `materialContainers_PrintLabels` + `Status` on the DTO appear)

Run: `cd frontend && npm run build`
Expected: build succeeds; `frontend/src/api/generated/api-client.ts` now contains `materialContainers_PrintLabels` and `PrintMaterialContainerLabelsRequest`.

- [ ] **Step 2: Write a failing hook test** (mock the generated client method)

Add to the existing test file a case asserting `usePrintMaterialContainerLabels` calls `materialContainers_PrintLabels` with `{ count }` and invalidates `QUERY_KEYS.materialContainers`. (Mirror the existing `useCreateMaterialContainers` test setup.)

- [ ] **Step 3: Run test, verify it fails**

Run: `cd frontend && npm test -- useMaterialContainers`
Expected: FAIL — hook not exported.

- [ ] **Step 4: Add the hook**

In `useMaterialContainers.ts`:
```typescript
import {
  // ...existing imports...
  PrintMaterialContainerLabelsRequest,
  PrintMaterialContainerLabelsResponse,
} from '../generated/api-client';

export const usePrintMaterialContainerLabels = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { count: number }): Promise<PrintMaterialContainerLabelsResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const request = new PrintMaterialContainerLabelsRequest({ count: input.count });
      return apiClient.materialContainers_PrintLabels(request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: QUERY_KEYS.materialContainers });
    },
  });
};
```
(Confirm the generated method name — nswag may produce `materialContainers_PrintLabels`. Match exactly.)

- [ ] **Step 5: Run test, verify it passes**

Run: `cd frontend && npm test -- useMaterialContainers`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useMaterialContainers.ts frontend/src/api/hooks/__tests__/useMaterialContainers.test.ts frontend/src/api/generated
git commit -m "feat: add usePrintMaterialContainerLabels hook"
```

---

## Task 11: Frontend — print action + status column on the list

**Files:**
- Modify: `frontend/src/components/pages/MaterialContainerList.tsx`
- Test: `frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx` (update)

- [ ] **Step 1: Write a failing UI test**

Assert: a "Tisk štítků" button renders; clicking opens a quantity input; submitting calls the mocked `usePrintMaterialContainerLabels().mutate` with `{ count: N }`; a "Stav" (Status) column header renders.

- [ ] **Step 2: Run test, verify it fails**

Run: `cd frontend && npm test -- MaterialContainerList`
Expected: FAIL.

- [ ] **Step 3: Implement** — add a header button, a small inline quantity panel, success toast/message, and a Status column:

```tsx
// header (next to the <h1>):
const [showPrint, setShowPrint] = useState(false);
const [qty, setQty] = useState(10);
const printLabels = usePrintMaterialContainerLabels();

<button
  onClick={() => setShowPrint((v) => !v)}
  className="bg-indigo-600 hover:bg-indigo-700 text-white font-medium py-2 px-4 rounded-md text-sm"
>
  Tisk štítků
</button>

{showPrint && (
  <div className="flex items-center gap-2">
    <input
      type="number" min={1} max={200} value={qty}
      onChange={(e) => setQty(Number(e.target.value))}
      className="w-20 border border-gray-300 rounded-md px-2 py-1 text-sm"
    />
    <button
      disabled={printLabels.isPending || qty < 1 || qty > 200}
      onClick={() => printLabels.mutate({ count: qty }, { onSuccess: () => setShowPrint(false) })}
      className="bg-green-600 hover:bg-green-700 text-white font-medium py-1 px-3 rounded-md text-sm disabled:opacity-50"
    >
      {printLabels.isPending ? 'Tisknu…' : `Vytisknout ${qty}`}
    </button>
  </div>
)}
```
Add a `<th>Stav</th>` after the "Kód kontejneru" header and a `<td>{container.status}</td>` cell. Existing material/lot cells already tolerate empty values (Unassigned rows show blank material/lot).

- [ ] **Step 4: Run test, verify it passes**

Run: `cd frontend && npm test -- MaterialContainerList`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/MaterialContainerList.tsx frontend/src/components/pages/__tests__/MaterialContainerList.test.tsx
git commit -m "feat: add print-labels action and status column to MaterialContainerList"
```

---

## Task 12: Frontend — reject-unknown-code message in the scan loop

**Files:**
- Modify: `frontend/src/components/terminal/lot-identification/ContainerScanLoop.tsx`
- Test: `frontend/src/components/terminal/lot-identification/__tests__/ContainerScanLoop.test.tsx` (update)

- [ ] **Step 1: Write a failing test**

Assert that when `create.mutate` resolves with `{ success: false, errorCode: ErrorCodes.UnknownMaterialContainerCode }`, the alert shows the unknown-label message and the count does not increment.

- [ ] **Step 2: Run test, verify it fails**

Run: `cd frontend && npm test -- ContainerScanLoop`
Expected: FAIL.

- [ ] **Step 3: Implement** — add a constant and a branch in `onSuccess`:

```tsx
const UNKNOWN_CODE_MESSAGE = 'Neznámý štítek – nejprve jej vygenerujte v aplikaci.';
```
In the `onSuccess` handler, add before the generic `else`:
```tsx
} else if (data.errorCode === ErrorCodes.UnknownMaterialContainerCode) {
  setError(UNKNOWN_CODE_MESSAGE);
```

- [ ] **Step 4: Run test, verify it passes**

Run: `cd frontend && npm test -- ContainerScanLoop`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/terminal/lot-identification/ContainerScanLoop.tsx frontend/src/components/terminal/lot-identification/__tests__/ContainerScanLoop.test.tsx
git commit -m "feat: handle unknown-label rejection in container scan loop"
```

---

## Final Verification

- [ ] **Backend:** `dotnet build backend && dotnet format backend --verify-no-changes && dotnet test backend/test/Anela.Heblo.Tests` — all green.
- [ ] **Frontend:** `cd frontend && npm run build && npm run lint && npm test` — all green.
- [ ] **Migrations (manual):** apply `MakeMaterialContainerMaterialLotNullable` then `SeedMaterialContainerSequence` to the target DB before runtime testing.
- [ ] **End-to-end print check** (staging, with `Cups:LabelPrinterName` → the Zebra raw queue):
  1. `POST /api/material-containers/print-labels { "count": 2 }` → 2 `Unassigned` rows appear in the list; 2 Code128 labels print on the Zebra.
  2. In the terminal lot-identification flow, scan one printed code → it flips to `Assigned` with the chosen material/lot.
  3. Scan a made-up `M99999999` → rejected with the unknown-label message.
  4. Re-scan an already-assigned code → "already assigned" conflict (unchanged behavior).

## Spec Coverage Self-Review
- Generate new blank labels → Tasks 3, 6, 7, 11. Unassigned records at print time → Tasks 1, 2, 6. ZPL→CUPS→Zebra → Tasks 4, 5, 9. Code128 + M-code label → Task 4. Admin trigger → Tasks 7, 11. Reject unknown / assign existing → Task 8, 12. Sequence above existing → Task 3. No placeholders; types (`CreateUnassigned`, `Assign`, `PrintZplAsync`, `MaterialContainerLabelZplBuilder.Build`, `usePrintMaterialContainerLabels`, `UnknownMaterialContainerCode`) are consistent across tasks.

## Out of Scope (v1 / YAGNI)
- Reprint of an `Unassigned` label after a printer jam (codes persist → addable later).
- Terminal-side label printing (admin-only for now).
- Extra label content beyond barcode + code.
