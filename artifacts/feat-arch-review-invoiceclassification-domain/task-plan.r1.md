# InvoiceClassification Domain DTO Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the Clean Architecture violation in the `InvoiceClassification` module by introducing dedicated Application contract DTOs for two response objects and renaming the Domain-layer types so they no longer carry the `Dto` suffix — while preserving the public API JSON shape byte-for-byte.

**Architecture:** Two-step refactor. **Step A** (additive): create three new Application contract classes in `Application/Features/InvoiceClassification/Contracts/`, extend `InvoiceClassificationMappingProfile` with the three Domain→Contract maps, and update `GetAccountingTemplatesHandler` + `GetInvoiceDetailsHandler` to map via `IMapper` before returning. **Step B** (atomic rename): rename `AccountingTemplateDto`/`ReceivedInvoiceDto`/`ReceivedInvoiceItemDto` in `Domain/Features/InvoiceClassification/` to `AccountingTemplate`/`ReceivedInvoice`/`ReceivedInvoiceItem` and fan the rename out across all consumers (rule engine, services, handlers, Flexi adapter, tests). The Domain rule engine continues to evaluate against the (now correctly named) Domain value objects; only the API boundary is reshaped.

**Tech Stack:** .NET 8, C#, AutoMapper, MediatR, xUnit + FluentAssertions + Moq, Clean Architecture layout (Domain / Application / Adapters / API).

---

## Authoritative decisions (from `arch-review.r1.md`)

1. **Option A only** for FR-2 — Domain types are renamed *in place* (not moved to Adapters). `IClassificationRule.Evaluate` and 5 concrete rules consume these types; moving to Adapters would invert the dependency. See arch-review § "Decision 1".
2. **Application contract names keep the `Dto` suffix** (`AccountingTemplateDto`, `ReceivedInvoiceDto`, `ReceivedInvoiceItemDto`) so the generated TypeScript client class names stay identical and 24 frontend references compile unchanged. See arch-review § "Decision 2".
3. **AutoMapper profile is the only mapping seam** — no inline construction in handlers, no hand-written mapper class. See arch-review § "Decision 3".
4. **Contracts are plain classes**, not records (`CLAUDE.md` mandate: "DTOs are classes, never C# records"). See arch-review § "Decision 4".
5. **Domain value objects remain mutable classes** with settable properties — required by AutoMapper destination semantics and existing test arrangements. See arch-review § "Decision 5".
6. **`GetInvoiceDetailsHandler` must keep null safety explicit** — never pass a `null` Domain `ReceivedInvoice` into `_mapper.Map<ReceivedInvoiceDto>(…)`. See arch-review § Risks (row 1) and § "Specification Amendments" item 2.
7. **OpenAPI parity is gated by a committed swagger baseline + diff**, not by manual review. See arch-review § "Specification Amendments" item 3.

## File-by-file inventory

**New files (created):**
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/AccountingTemplateDto.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ReceivedInvoiceDto.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ReceivedInvoiceItemDto.cs`
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs`
- `artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json` (baseline snapshot)
- `artifacts/feat-arch-review-invoiceclassification-domain/swagger-after.json` (post-refactor snapshot for diffing)

**Files renamed in place** (file name change + type name change; same Domain folder, same `namespace Anela.Heblo.Domain.Features.InvoiceClassification`):
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/AccountingTemplateDto.cs` → `AccountingTemplate.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceDto.cs` → `ReceivedInvoice.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceItemDto.cs` → `ReceivedInvoiceItem.cs`

**Files modified (type-reference updates and handler reshape):**
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRule.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IInvoiceClassificationsClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IReceivedInvoicesClient.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/VatClassificationRule.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/DescriptionClassificationRule.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/CompanyNameClassificationRule.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs`
- `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/ItemDescriptionClassificationRule.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IInvoiceClassificationService.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IRuleEvaluationEngine.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsResponse.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoicesClient.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs`
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`
- `frontend/src/api/generated/api-client.ts` (auto-regenerated by PostBuild — should be byte-identical except possibly XML doc comments)

---

## Task 1: Capture pre-refactor OpenAPI snapshot

**Files:**
- Create: `artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json`

This baseline is the FR-4 diff target. If `swagger-after.json` (Task 13) differs from this file in any field name, type, or nullability for the two affected endpoints, the refactor is broken.

- [ ] **Step 1: Start the backend dev server**

Run (in a separate terminal, leave running):

```bash
cd backend/src/Anela.Heblo.API && dotnet run
```

Expected: listens on `http://localhost:5001` and `https://localhost:5002`. Wait until you see `Now listening on:` in the output.

- [ ] **Step 2: Fetch the swagger doc and extract the two affected endpoints + their schemas**

```bash
mkdir -p artifacts/feat-arch-review-invoiceclassification-domain
curl -sk https://localhost:5002/swagger/v1/swagger.json | \
  jq '{
    paths: {
      "/api/invoice-classification/accounting-templates": .paths["/api/invoice-classification/accounting-templates"],
      "/api/invoice-classification/invoices/{invoiceId}": .paths["/api/invoice-classification/invoices/{invoiceId}"]
    },
    schemas: {
      AccountingTemplateDto: .components.schemas.AccountingTemplateDto,
      ReceivedInvoiceDto: .components.schemas.ReceivedInvoiceDto,
      ReceivedInvoiceItemDto: .components.schemas.ReceivedInvoiceItemDto,
      GetAccountingTemplatesResponse: .components.schemas.GetAccountingTemplatesResponse,
      GetInvoiceDetailsResponse: .components.schemas.GetInvoiceDetailsResponse
    }
  }' > artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json
```

Expected: the file exists, is non-empty, and contains JSON keys `paths` and `schemas`. Each `schemas.*` value must be a non-null object with a `properties` field.

If any schema key is `null`, the route name has changed since this plan was written — find the correct route in `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs` and re-run with the corrected path.

Verify with:

```bash
jq '.schemas | to_entries | map({key, hasProperties: (.value.properties != null)})' \
  artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json
```

Expected: all five entries have `hasProperties: true`.

- [ ] **Step 3: Stop the backend dev server**

Ctrl+C the terminal where `dotnet run` is executing.

- [ ] **Step 4: Commit the baseline**

```bash
git add artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json
git commit -m "chore: snapshot pre-refactor swagger for InvoiceClassification endpoints"
```

---

## Task 2: Create the three Application contract DTOs

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/AccountingTemplateDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ReceivedInvoiceDto.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ReceivedInvoiceItemDto.cs`

These are pure additions — nothing references them yet, so the build still passes and the API surface is unchanged. Each property mirrors the current Domain class verbatim.

- [ ] **Step 1: Create `Contracts/AccountingTemplateDto.cs`**

```csharp
namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class AccountingTemplateDto
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string AccountCode { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create `Contracts/ReceivedInvoiceItemDto.cs`**

```csharp
namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class ReceivedInvoiceItemDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
```

Note: the current Domain `ReceivedInvoiceItemDto` declares `Code` / `Name` as non-nullable `string` without an initializer (which produces a CS8618 warning under nullable reference types). The new contract initializes them to `string.Empty` to remove that warning while keeping the JSON shape identical (a `null` would never have been serialized in practice — the Flexi mapping always provides a value).

- [ ] **Step 3: Create `Contracts/ReceivedInvoiceDto.cs`**

```csharp
namespace Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

public class ReceivedInvoiceDto
{
    public string InvoiceNumber { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string CompanyVat { get; set; } = string.Empty;

    public DateTime? InvoiceDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string Description { get; set; } = string.Empty;

    public List<ReceivedInvoiceItemDto> Items { get; set; } = new();

    public DateTime? DueDate { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? DepartmentCode { get; set; }

    public string[] Labels { get; set; } = Array.Empty<string>();
}
```

Note: `Labels` is declared `string[]` (non-nullable) to match the current Domain shape, and initialized to `Array.Empty<string>()` to match how `ClassifyInvoicesHandlerTests.cs` constructs the Domain type today.

- [ ] **Step 4: Build to confirm no warnings or errors**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with zero errors and no new warnings.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/
git commit -m "feat(invoice-classification): add Application contract DTOs"
```

---

## Task 3: Write failing mapping profile validation test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs`

Mirrors `ManufactureOrderMappingProfileTests` pattern: constructor calls `AssertConfigurationIsValid()`, then per-mapping tests assert field-by-field preservation. This task writes the test BEFORE the new `CreateMap` entries exist — it is expected to compile but fail at runtime.

- [ ] **Step 1: Create the test file**

```csharp
using Anela.Heblo.Application.Features.InvoiceClassification;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DomainTypes = Anela.Heblo.Domain.Features.InvoiceClassification;
using ContractTypes = Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class InvoiceClassificationMappingProfileTests
{
    private readonly IMapper _mapper;

    public InvoiceClassificationMappingProfileTests()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<InvoiceClassificationMappingProfile>(),
            NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        _mapper = config.CreateMapper();
    }

    [Fact]
    public void Map_AccountingTemplate_To_Dto_PreservesAllFields()
    {
        var source = new DomainTypes.AccountingTemplateDto
        {
            Code = "ACC-001",
            Name = "Office supplies",
            Description = "Pens, paper, etc.",
            AccountCode = "501100",
        };

        var dto = _mapper.Map<ContractTypes.AccountingTemplateDto>(source);

        dto.Code.Should().Be("ACC-001");
        dto.Name.Should().Be("Office supplies");
        dto.Description.Should().Be("Pens, paper, etc.");
        dto.AccountCode.Should().Be("501100");
    }

    [Fact]
    public void Map_ReceivedInvoice_To_Dto_PreservesAllFields()
    {
        var source = new DomainTypes.ReceivedInvoiceDto
        {
            InvoiceNumber = "FV-2026-001",
            CompanyName = "Acme s.r.o.",
            CompanyVat = "CZ12345678",
            InvoiceDate = new DateTime(2026, 5, 26),
            DueDate = new DateTime(2026, 6, 9),
            TotalAmount = 12345.67m,
            Description = "Materials for May",
            AccountingTemplateCode = "ACC-001",
            DepartmentCode = "OPS",
            Labels = new[] { "auto", "review" },
            Items = new List<DomainTypes.ReceivedInvoiceItemDto>
            {
                new() { Code = "ITEM-1", Name = "Paper A4", Amount = 100m },
                new() { Code = "ITEM-2", Name = "Pens",     Amount = 50m  },
            },
        };

        var dto = _mapper.Map<ContractTypes.ReceivedInvoiceDto>(source);

        dto.InvoiceNumber.Should().Be("FV-2026-001");
        dto.CompanyName.Should().Be("Acme s.r.o.");
        dto.CompanyVat.Should().Be("CZ12345678");
        dto.InvoiceDate.Should().Be(new DateTime(2026, 5, 26));
        dto.DueDate.Should().Be(new DateTime(2026, 6, 9));
        dto.TotalAmount.Should().Be(12345.67m);
        dto.Description.Should().Be("Materials for May");
        dto.AccountingTemplateCode.Should().Be("ACC-001");
        dto.DepartmentCode.Should().Be("OPS");
        dto.Labels.Should().Equal("auto", "review");
        dto.Items.Should().HaveCount(2);
        dto.Items[0].Code.Should().Be("ITEM-1");
        dto.Items[0].Name.Should().Be("Paper A4");
        dto.Items[0].Amount.Should().Be(100m);
        dto.Items[1].Code.Should().Be("ITEM-2");
        dto.Items[1].Name.Should().Be("Pens");
        dto.Items[1].Amount.Should().Be(50m);
    }

    [Fact]
    public void Map_ReceivedInvoiceItem_To_Dto_PreservesAllFields()
    {
        var source = new DomainTypes.ReceivedInvoiceItemDto
        {
            Code = "ITEM-X",
            Name = "Widget",
            Amount = 42.5m,
        };

        var dto = _mapper.Map<ContractTypes.ReceivedInvoiceItemDto>(source);

        dto.Code.Should().Be("ITEM-X");
        dto.Name.Should().Be("Widget");
        dto.Amount.Should().Be(42.5m);
    }
}
```

- [ ] **Step 2: Run the new test class and verify it FAILS at runtime**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceClassificationMappingProfileTests" \
  --no-restore
```

Expected: tests fail. The most likely failure is `AutoMapperConfigurationException` from `AssertConfigurationIsValid()` — "Missing type map configuration or unsupported mapping" — because no `AccountingTemplateDto → Contracts.AccountingTemplateDto` map exists yet. Some `Map_*_To_Dto_PreservesAllFields` tests may instead fail with a constructor exception cascading from the same root cause.

This is the desired RED state. Do not commit yet — the next task makes them green.

---

## Task 4: Extend `InvoiceClassificationMappingProfile` with the three Domain→Contract maps

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using AutoMapper;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification;

public class InvoiceClassificationMappingProfile : Profile
{
    public InvoiceClassificationMappingProfile()
    {
        CreateMap<ClassificationRule, ClassificationRuleDto>()
            .ForMember(dest => dest.RuleTypeIdentifier, opt => opt.MapFrom(src => src.RuleTypeIdentifier));
        CreateMap<ClassificationHistory, ClassificationHistoryDto>();
        CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();
        CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();

        // Domain → Application contracts (Step A of the InvoiceClassification DTO separation).
        // Source types use their current Domain names; they will be renamed in a later commit
        // and the source side of these maps will be updated then.
        CreateMap<Domain.Features.InvoiceClassification.AccountingTemplateDto,
                  Contracts.AccountingTemplateDto>();
        CreateMap<Domain.Features.InvoiceClassification.ReceivedInvoiceItemDto,
                  Contracts.ReceivedInvoiceItemDto>();
        CreateMap<Domain.Features.InvoiceClassification.ReceivedInvoiceDto,
                  Contracts.ReceivedInvoiceDto>();
    }
}
```

The fully-qualified source names disambiguate from the Application contract types of the same simple name. The temporary comment is removed in Task 11 when the source side is renamed.

- [ ] **Step 2: Re-run the mapping profile tests and verify they PASS**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceClassificationMappingProfileTests" \
  --no-restore
```

Expected: 3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs \
        backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs
git commit -m "feat(invoice-classification): map Domain types to Application contract DTOs"
```

---

## Task 5: Update `GetAccountingTemplatesResponse` + Handler to use the Application contract

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/GetAccountingTemplatesHandler.cs`

These two changes must land together — the property type swap on the Response forces the Handler to map before assigning. Skipping either side breaks the build.

- [ ] **Step 1: Replace `GetAccountingTemplatesResponse.cs`**

```csharp
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;

public class GetAccountingTemplatesResponse : BaseResponse
{
    public List<AccountingTemplateDto> Templates { get; set; } = new();

    public GetAccountingTemplatesResponse() : base() { }

    public GetAccountingTemplatesResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

Only the `using` changes; the property type name (`AccountingTemplateDto`) is identical in JSON.

- [ ] **Step 2: Replace `GetAccountingTemplatesHandler.cs`**

```csharp
using AutoMapper;
using MediatR;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetAccountingTemplates;

public class GetAccountingTemplatesHandler : IRequestHandler<GetAccountingTemplatesRequest, GetAccountingTemplatesResponse>
{
    private readonly IInvoiceClassificationsClient _invoiceClassificationsClient;
    private readonly IMapper _mapper;

    public GetAccountingTemplatesHandler(
        IInvoiceClassificationsClient invoiceClassificationsClient,
        IMapper mapper)
    {
        _invoiceClassificationsClient = invoiceClassificationsClient;
        _mapper = mapper;
    }

    public async Task<GetAccountingTemplatesResponse> Handle(GetAccountingTemplatesRequest request, CancellationToken cancellationToken)
    {
        var templates = await _invoiceClassificationsClient.GetValidAccountingTemplatesAsync(cancellationToken);

        return new GetAccountingTemplatesResponse
        {
            Templates = _mapper.Map<List<AccountingTemplateDto>>(templates)
        };
    }
}
```

`AccountingTemplateDto` in the return-type position resolves to the Application contract (via the `Contracts` `using`). `Domain.Features.InvoiceClassification` is imported only for `IInvoiceClassificationsClient`.

- [ ] **Step 3: Build and run the touched tests**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetAccountingTemplates/
git commit -m "refactor(invoice-classification): map accounting templates to Application contract in handler"
```

---

## Task 6: Update `GetInvoiceDetailsResponse` + Handler with null-safe mapping

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs`

The handler currently returns `Invoice = null, Found = false` when no invoice is found. AutoMapper's default behavior maps `null` to a non-null destination (allocating an empty `ReceivedInvoiceDto`), which would change the JSON contract from `null` to an empty object. Explicit null check before mapping prevents that.

- [ ] **Step 1: Write a failing not-found unit test for `GetInvoiceDetailsHandler`**

Append the following test class (or extend the existing test file if one exists for this handler) to a new file: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs`.

```csharp
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class GetInvoiceDetailsHandlerTests
{
    private static IMapper BuildMapper()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<Application.Features.InvoiceClassification.InvoiceClassificationMappingProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }

    [Fact]
    public async Task Handle_WhenInvoiceNotFound_ReturnsNullInvoiceAndFoundFalse()
    {
        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("missing"))
                  .ReturnsAsync((ReceivedInvoiceDto?)null);

        var handler = new GetInvoiceDetailsHandler(
            clientMock.Object,
            new NullLogger<GetInvoiceDetailsHandler>(),
            BuildMapper());

        var response = await handler.Handle(
            new GetInvoiceDetailsRequest { InvoiceId = "missing" },
            CancellationToken.None);

        response.Invoice.Should().BeNull();
        response.Found.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenInvoiceFound_MapsToApplicationContract()
    {
        var domainInvoice = new ReceivedInvoiceDto
        {
            InvoiceNumber = "FV-001",
            CompanyName = "Acme",
            Labels = Array.Empty<string>(),
        };

        var clientMock = new Mock<IReceivedInvoicesClient>();
        clientMock.Setup(c => c.GetInvoiceByIdAsync("FV-001"))
                  .ReturnsAsync(domainInvoice);

        var handler = new GetInvoiceDetailsHandler(
            clientMock.Object,
            new NullLogger<GetInvoiceDetailsHandler>(),
            BuildMapper());

        var response = await handler.Handle(
            new GetInvoiceDetailsRequest { InvoiceId = "FV-001" },
            CancellationToken.None);

        response.Found.Should().BeTrue();
        response.Invoice.Should().NotBeNull();
        response.Invoice!.InvoiceNumber.Should().Be("FV-001");
        response.Invoice.CompanyName.Should().Be("Acme");
        // Confirm the runtime type is the Application contract, not the Domain class.
        response.Invoice.Should().BeOfType<Application.Features.InvoiceClassification.Contracts.ReceivedInvoiceDto>();
    }
}
```

This will fail to compile right now because the handler doesn't yet accept `IMapper` in its constructor and the response still types `Invoice` as the Domain `ReceivedInvoiceDto`. That's the RED.

- [ ] **Step 2: Replace `GetInvoiceDetailsResponse.cs`**

```csharp
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsResponse : BaseResponse
{
    public ReceivedInvoiceDto? Invoice { get; set; }

    public bool Found { get; set; }

    public GetInvoiceDetailsResponse() : base() { }

    public GetInvoiceDetailsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

- [ ] **Step 3: Replace `GetInvoiceDetailsHandler.cs`**

```csharp
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.UseCases.GetInvoiceDetails;

public class GetInvoiceDetailsHandler : IRequestHandler<GetInvoiceDetailsRequest, GetInvoiceDetailsResponse>
{
    private readonly IReceivedInvoicesClient _invoicesClient;
    private readonly ILogger<GetInvoiceDetailsHandler> _logger;
    private readonly IMapper _mapper;

    public GetInvoiceDetailsHandler(
        IReceivedInvoicesClient invoicesClient,
        ILogger<GetInvoiceDetailsHandler> logger,
        IMapper mapper)
    {
        _invoicesClient = invoicesClient;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<GetInvoiceDetailsResponse> Handle(GetInvoiceDetailsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Getting details for invoice {InvoiceId}", request.InvoiceId);

            var invoice = await _invoicesClient.GetInvoiceByIdAsync(request.InvoiceId);

            // Explicit null check: AutoMapper would otherwise allocate an empty destination,
            // breaking the API contract that returns `Invoice = null` when not found.
            var mapped = invoice is null ? null : _mapper.Map<ReceivedInvoiceDto>(invoice);

            return new GetInvoiceDetailsResponse
            {
                Invoice = mapped,
                Found = invoice != null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoice details for {InvoiceId}", request.InvoiceId);
            return new GetInvoiceDetailsResponse
            {
                Invoice = null,
                Found = false
            };
        }
    }
}
```

- [ ] **Step 4: Build and run the new tests; verify they PASS**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetInvoiceDetailsHandlerTests" \
  --no-restore
```

Expected: build succeeds; both tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/ \
        backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs
git commit -m "refactor(invoice-classification): map invoice details to Application contract with null safety"
```

---

## Task 7: Verify API JSON contract is unchanged after Step A

**Files:**
- Create: `artifacts/feat-arch-review-invoiceclassification-domain/swagger-after-stepA.json`

At this point, the public API surface no longer leaks Domain types (the responses use `Application.Features.InvoiceClassification.Contracts.*`). The JSON shape must be byte-identical to the baseline from Task 1.

- [ ] **Step 1: Start the backend dev server (fresh build to pick up handler changes)**

```bash
cd backend/src/Anela.Heblo.API && dotnet run
```

Wait for `Now listening on:` output.

- [ ] **Step 2: Capture the post-Step-A swagger snapshot using the same jq filter as Task 1**

In another terminal:

```bash
curl -sk https://localhost:5002/swagger/v1/swagger.json | \
  jq '{
    paths: {
      "/api/invoice-classification/accounting-templates": .paths["/api/invoice-classification/accounting-templates"],
      "/api/invoice-classification/invoices/{invoiceId}": .paths["/api/invoice-classification/invoices/{invoiceId}"]
    },
    schemas: {
      AccountingTemplateDto: .components.schemas.AccountingTemplateDto,
      ReceivedInvoiceDto: .components.schemas.ReceivedInvoiceDto,
      ReceivedInvoiceItemDto: .components.schemas.ReceivedInvoiceItemDto,
      GetAccountingTemplatesResponse: .components.schemas.GetAccountingTemplatesResponse,
      GetInvoiceDetailsResponse: .components.schemas.GetInvoiceDetailsResponse
    }
  }' > artifacts/feat-arch-review-invoiceclassification-domain/swagger-after-stepA.json
```

- [ ] **Step 3: Diff against the baseline**

```bash
diff -u artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json \
        artifacts/feat-arch-review-invoiceclassification-domain/swagger-after-stepA.json
```

Expected: **no output** (the two snapshots are byte-identical).

If there is any field-level difference — name, type, format, nullability, required array, or `enum` list — STOP. The contract has drifted. Inspect the diff, fix the handler/contract DTO/profile so the post-snapshot matches, and re-snapshot.

Acceptable diffs (rare): description text changes inside `description` properties of OpenAPI schemas. If you see only `"description": "…"` deltas, accept them. Any other delta is a hard fail.

- [ ] **Step 4: Stop the backend dev server**

Ctrl+C the dotnet run terminal.

- [ ] **Step 5: Delete the temporary snapshot — it's been verified, no need to commit it**

```bash
rm artifacts/feat-arch-review-invoiceclassification-domain/swagger-after-stepA.json
```

(A final `swagger-after.json` is produced in Task 13.)

---

## Task 8: Rename the three Domain types (file + class names)

**Files:**
- Rename: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/AccountingTemplateDto.cs` → `AccountingTemplate.cs`
- Rename: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceDto.cs` → `ReceivedInvoice.cs`
- Rename: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceItemDto.cs` → `ReceivedInvoiceItem.cs`

After this task, the build WILL be broken because every consumer still references the old names. Tasks 9–11 are mandatory companion changes that get the build back to green. **Do not commit until Task 11 is done.**

- [ ] **Step 1: Rename `AccountingTemplateDto.cs` and update the class name**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/AccountingTemplateDto.cs \
       backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/AccountingTemplate.cs
```

Then replace the file contents with:

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class AccountingTemplate
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string AccountCode { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Rename `ReceivedInvoiceItemDto.cs` and update the class name**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceItemDto.cs \
       backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceItem.cs
```

Replace contents with:

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoiceItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
```

(Initializers added to remove the existing CS8618 warning, matching the Application contract — no behavior change since Flexi mapping always populates these.)

- [ ] **Step 3: Rename `ReceivedInvoiceDto.cs` and update the class + nested type reference**

```bash
git mv backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoiceDto.cs \
       backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs
```

Replace contents with:

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoice
{
    public string InvoiceNumber { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public string CompanyVat { get; set; } = string.Empty;

    public DateTime? InvoiceDate { get; set; }

    public decimal TotalAmount { get; set; }

    public string Description { get; set; } = string.Empty;

    public List<ReceivedInvoiceItem> Items { get; set; } = new();

    public DateTime? DueDate { get; set; }

    public string? AccountingTemplateCode { get; set; }

    public string? DepartmentCode { get; set; }

    public string[] Labels { get; set; } = Array.Empty<string>();
}
```

`Labels` is initialized to `Array.Empty<string>()` to remove the existing CS8618 warning on this field and mirror how every existing call site constructs the type today.

- [ ] **Step 4: Confirm the build is broken (sanity check)**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: many errors of the form `The type or namespace name 'ReceivedInvoiceDto' could not be found …`, `'AccountingTemplateDto' could not be found …` originating from Domain rule classes, the Flexi adapter, and Application handlers. This confirms we identified all consumers; we fix them in the next tasks.

Do NOT commit yet.

---

## Task 9: Fan the Domain rename through Domain interfaces and rule implementations

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationRule.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IInvoiceClassificationsClient.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IReceivedInvoicesClient.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/VatClassificationRule.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/DescriptionClassificationRule.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/CompanyNameClassificationRule.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/AmountClassificationRule.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Rules/ItemDescriptionClassificationRule.cs`

Each change is a simple type-name substitution: `ReceivedInvoiceDto` → `ReceivedInvoice`, `AccountingTemplateDto` → `AccountingTemplate`. The bodies of the rules and the interface contracts (other than the parameter types) do not change.

- [ ] **Step 1: Replace `IClassificationRule.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IClassificationRule
{
    string Identifier { get; }
    string DisplayName { get; }
    string Description { get; }
    bool Evaluate(ReceivedInvoice invoice, string pattern);
}
```

- [ ] **Step 2: Replace `IInvoiceClassificationsClient.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IInvoiceClassificationsClient
{
    Task<List<AccountingTemplate>> GetValidAccountingTemplatesAsync(CancellationToken? cancellationToken = default);

    Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingTemplateCode,
        string? matchedRuleDepartment, CancellationToken? cancellationToken = default);

    Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason, CancellationToken? cancellationToken = default);
}
```

- [ ] **Step 3: Replace `IReceivedInvoicesClient.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public interface IReceivedInvoicesClient
{
    Task<List<ReceivedInvoice>> GetUnclassifiedInvoicesAsync();

    Task<ReceivedInvoice?> GetInvoiceByIdAsync(string invoiceId);
}
```

- [ ] **Step 4: Replace `Rules/VatClassificationRule.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class VatClassificationRule : IClassificationRule
{
    public string Identifier => "ICO";
    public string DisplayName => "IČO";
    public string Description => "Porovnání IČO firmy";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        return string.Equals(invoice.CompanyVat?.Trim(), pattern?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: Replace `Rules/DescriptionClassificationRule.cs`**

```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class DescriptionClassificationRule : IClassificationRule
{
    public string Identifier => "DESCRIPTION";
    public string DisplayName => "Popis faktury";
    public string Description => "Regex nebo text v popisu faktury";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(invoice.Description) || string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            return Regex.IsMatch(invoice.Description, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return invoice.Description.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 6: Replace `Rules/CompanyNameClassificationRule.cs`**

```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class CompanyNameClassificationRule : IClassificationRule
{
    public string Identifier => "COMPANY_NAME";
    public string DisplayName => "Název firmy";
    public string Description => "Regex nebo text v názvu firmy";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(invoice.CompanyName) || string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            return Regex.IsMatch(invoice.CompanyName, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return invoice.CompanyName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 7: Replace `Rules/AmountClassificationRule.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class AmountClassificationRule : IClassificationRule
{
    public string Identifier => "AMOUNT";
    public string DisplayName => "Částka";
    public string Description => "Porovnání celkové částky faktury (>=, <=, >, <, =)";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            if (pattern.StartsWith(">="))
            {
                return decimal.TryParse(pattern.Substring(2), out var minValue) && invoice.TotalAmount >= minValue;
            }
            if (pattern.StartsWith("<="))
            {
                return decimal.TryParse(pattern.Substring(2), out var maxValue) && invoice.TotalAmount <= maxValue;
            }
            if (pattern.StartsWith(">"))
            {
                return decimal.TryParse(pattern.Substring(1), out var minValue) && invoice.TotalAmount > minValue;
            }
            if (pattern.StartsWith("<"))
            {
                return decimal.TryParse(pattern.Substring(1), out var maxValue) && invoice.TotalAmount < maxValue;
            }
            if (pattern.StartsWith("="))
            {
                return decimal.TryParse(pattern.Substring(1), out var exactValue) && invoice.TotalAmount == exactValue;
            }

            return decimal.TryParse(pattern, out var value) && invoice.TotalAmount == value;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 8: Replace `Rules/ItemDescriptionClassificationRule.cs`**

```csharp
using System.Text.RegularExpressions;

namespace Anela.Heblo.Domain.Features.InvoiceClassification.Rules;

public class ItemDescriptionClassificationRule : IClassificationRule
{
    public string Identifier => "ITEM_DESCRIPTION";
    public string DisplayName => "Popis položky";
    public string Description => "Regex nebo text v popisu některé položky faktury";

    public bool Evaluate(ReceivedInvoice invoice, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        return invoice.Items.Any(item => EvaluateItemDescription(item.Name, pattern));
    }

    private bool EvaluateItemDescription(string itemDescription, string pattern)
    {
        if (string.IsNullOrWhiteSpace(itemDescription))
            return false;

        try
        {
            return Regex.IsMatch(itemDescription, pattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return itemDescription.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

Do NOT build/commit yet — Application + Adapter layers still reference the old names.

---

## Task 10: Fan the Domain rename through Application services and handlers

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IInvoiceClassificationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/IRuleEvaluationEngine.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/RuleEvaluationEngine.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs`

Every signature that took `ReceivedInvoiceDto` now takes `ReceivedInvoice`. The handler also drops the `Dto` suffix in its local `List<…>` variable.

- [ ] **Step 1: Replace `Services/IInvoiceClassificationService.cs`**

```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public interface IInvoiceClassificationService
{
    Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice);
}
```

- [ ] **Step 2: Replace the two `ReceivedInvoiceDto` mentions in `Services/InvoiceClassificationService.cs`**

Edit the file by replacing only:
- The signature `public async Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoiceDto invoice)` → `public async Task<InvoiceClassificationResult> ClassifyInvoiceAsync(ReceivedInvoice invoice)`
- The signature `private async Task RecordClassificationHistory(ReceivedInvoiceDto invoice, …)` → `private async Task RecordClassificationHistory(ReceivedInvoice invoice, …)`

No other changes to this file. The body uses `invoice.InvoiceNumber`, `invoice.CompanyName` etc. which are identical on the renamed type.

- [ ] **Step 3: Replace `Services/IRuleEvaluationEngine.cs`**

```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public interface IRuleEvaluationEngine
{
    ClassificationRule? FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules);
}
```

- [ ] **Step 4: Replace `Services/RuleEvaluationEngine.cs`**

```csharp
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Application.Features.InvoiceClassification.Services;

public class RuleEvaluationEngine : IRuleEvaluationEngine
{
    private readonly IEnumerable<IClassificationRule> _classificationRules;

    public RuleEvaluationEngine(IEnumerable<IClassificationRule> classificationRules)
    {
        _classificationRules = classificationRules;
    }

    public ClassificationRule? FindMatchingRule(ReceivedInvoice invoice, List<ClassificationRule> rules)
    {
        foreach (var rule in rules.Where(r => r.IsActive).OrderBy(r => r.Order))
        {
            if (EvaluateRule(invoice, rule))
            {
                return rule;
            }
        }

        return null;
    }

    private bool EvaluateRule(ReceivedInvoice invoice, ClassificationRule rule)
    {
        var classificationRule = _classificationRules.FirstOrDefault(r => r.Identifier == rule.RuleTypeIdentifier);
        return classificationRule?.Evaluate(invoice, rule.Pattern) ?? false;
    }
}
```

- [ ] **Step 5: Update `UseCases/ClassifyInvoices/ClassifyInvoicesHandler.cs`**

Three changes inside the method body (no other edits):
- `List<ReceivedInvoiceDto> invoicesToClassify;` → `List<ReceivedInvoice> invoicesToClassify;`
- `invoicesToClassify = new List<ReceivedInvoiceDto>();` → `invoicesToClassify = new List<ReceivedInvoice>();`

The `using Anela.Heblo.Domain.Features.InvoiceClassification;` import already brings in the renamed `ReceivedInvoice` type.

- [ ] **Step 6: Update `InvoiceClassificationMappingProfile.cs` source-side type names**

Replace the last three `CreateMap` calls so their source types use the new Domain names, and remove the temporary comment:

```csharp
using AutoMapper;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;

namespace Anela.Heblo.Application.Features.InvoiceClassification;

public class InvoiceClassificationMappingProfile : Profile
{
    public InvoiceClassificationMappingProfile()
    {
        CreateMap<ClassificationRule, ClassificationRuleDto>()
            .ForMember(dest => dest.RuleTypeIdentifier, opt => opt.MapFrom(src => src.RuleTypeIdentifier));
        CreateMap<ClassificationHistory, ClassificationHistoryDto>();
        CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();
        CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();

        CreateMap<AccountingTemplate, AccountingTemplateDto>();
        CreateMap<ReceivedInvoiceItem, ReceivedInvoiceItemDto>();
        CreateMap<ReceivedInvoice, ReceivedInvoiceDto>();
    }
}
```

The destination `*Dto` simple names resolve to the Application contract namespace via `using Anela.Heblo.Application.Features.InvoiceClassification.Contracts;`. The source simple names resolve to the renamed Domain types via the Domain `using`.

---

## Task 11: Fan the Domain rename through the Flexi adapter and unblock the build

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoicesClient.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs`

After this task, the build is green again.

- [ ] **Step 1: Replace `FlexiInvoiceClassificationsClient.cs`**

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.Accounting;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;

namespace Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;

public class FlexiInvoiceClassificationsClient : IInvoiceClassificationsClient
{
    private readonly IAccountingTemplateClient _accountingTemplateClient;
    private readonly IReceivedInvoiceClient _receivedInvoiceClient;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly ILogger<FlexiInvoiceClassificationsClient> _logger;

    public FlexiInvoiceClassificationsClient(
        IAccountingTemplateClient accountingTemplateClient,
        IReceivedInvoiceClient receivedInvoiceClient,
        IOptions<DataSourceOptions> options,
        ILogger<FlexiInvoiceClassificationsClient> logger)
    {
        _accountingTemplateClient = accountingTemplateClient;
        _receivedInvoiceClient = receivedInvoiceClient;
        _options = options;
        _logger = logger;
    }

    public async Task<List<AccountingTemplate>> GetValidAccountingTemplatesAsync(CancellationToken? cancellationToken = default)
    {
        var templates = await _accountingTemplateClient.GetAsync();
        return templates
            .Where(w => !w.Code.StartsWith("N-") && w.ModuleReceivedInvoicedAvailable)
            .Select(s => new AccountingTemplate
            {
                AccountCode = s.AccountCode,
                Code = s.Code,
                Description = s.Description,
                Name = s.Name,
            })
            .OrderBy(o => o.Code)
            .ToList();
    }

    public async Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingTemplateCode,
        string? departmentCode, CancellationToken? cancellationToken = default)
    {
        var result = await _accountingTemplateClient.UpdateInvoiceAsync(invoiceId, accountingTemplateCode, departmentCode);
        if (result.IsSuccess)
        {
            await _receivedInvoiceClient.RemoveTagAsync(invoiceId, [_options.Value.InvoiceClassificationTriggerLabel, _options.Value.InvoiceClassificationManualReviewLabel], cancellationToken ?? CancellationToken.None);
        }

        return result.IsSuccess;
    }

    public async Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason, CancellationToken? cancellationToken = default)
    {
        var resultAdd =
            await _receivedInvoiceClient.AddTagAsync(invoiceId, _options.Value.InvoiceClassificationManualReviewLabel, cancellationToken ?? CancellationToken.None);
        var resultRemove =
            await _receivedInvoiceClient.RemoveTagAsync(invoiceId, _options.Value.InvoiceClassificationTriggerLabel, cancellationToken ?? CancellationToken.None);

        return resultAdd.IsSuccess && resultRemove.IsSuccess;
    }
}
```

(Only the return type of `GetValidAccountingTemplatesAsync` and the `new AccountingTemplate {…}` projection inside the `Select` differ from the original.)

- [ ] **Step 2: Replace `FlexiReceivedInvoicesClient.cs`**

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rem.FlexiBeeSDK.Client.Clients.ReceivedInvoices;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;

public class FlexiReceivedInvoicesClient : IReceivedInvoicesClient
{
    private readonly IReceivedInvoiceClient _client;
    private readonly IOptions<DataSourceOptions> _dataSourceOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FlexiReceivedInvoicesClient> _logger;
    private readonly IMapper _mapper;

    public FlexiReceivedInvoicesClient(
        IReceivedInvoiceClient client,
        IOptions<DataSourceOptions> dataSourceOptions,
        TimeProvider timeProvider,
        ILogger<FlexiReceivedInvoicesClient> logger,
        IMapper mapper)
    {
        _client = client;
        _dataSourceOptions = dataSourceOptions;
        _timeProvider = timeProvider;
        _logger = logger;
        _mapper = mapper;
    }

    public async Task<List<ReceivedInvoice>> GetUnclassifiedInvoicesAsync()
    {
        var dateTo = _timeProvider.GetLocalNow().DateTime;
        var dateFrom = dateTo.AddDays(-1 * _dataSourceOptions.Value.InvoiceClassificationDaysBack);
        var invoices = await _client.SearchAsync(new ReceivedInvoiceRequest(dateFrom, dateTo,
            label: _dataSourceOptions.Value.InvoiceClassificationTriggerLabel));

        return _mapper.Map<List<ReceivedInvoice>>(invoices);
    }

    public async Task<ReceivedInvoice?> GetInvoiceByIdAsync(string invoiceId)
    {
        var found = await _client.GetAsync(invoiceId);
        return _mapper.Map<ReceivedInvoice>(found);
    }
}
```

(Return types and the generic mapping arguments now refer to the renamed Domain types. Trailing `; ;` in the original `return _mapper.Map<ReceivedInvoiceDto>(found); ;` is also cleaned up.)

- [ ] **Step 3: Replace `FlexiReceivedInvoiceMappingProfile.cs`**

```csharp
using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;

public class FlexiReceivedInvoiceMappingProfile : BaseFlexiProfile
{
    public FlexiReceivedInvoiceMappingProfile()
    {
        CreateMap<ReceivedInvoiceFlexiDto, ReceivedInvoice>()
            .ForMember(dest => dest.InvoiceNumber, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.CompanyName, opt => opt.MapFrom(src => src.CompanyName))
            .ForMember(dest => dest.CompanyVat, opt => opt.MapFrom(src => src.CompanyId))
            .ForMember(dest => dest.InvoiceDate, opt => opt.MapFrom(src => src.IssueDate))
            .ForMember(dest => dest.DueDate, opt => opt.MapFrom(src => src.DueDate))
            .ForMember(dest => dest.Description, opt => opt.MapFrom(src => src.Description))
            .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => (decimal)src.TotalAmount))
            .ForMember(dest => dest.DepartmentCode, opt => opt.MapFrom(src => src.Department != null ? src.Department.Code : null))
            .ForMember(dest => dest.AccountingTemplateCode, opt => opt.MapFrom(src => src.AccountingTemplate != null ? src.AccountingTemplate.Code : null))
            .ForMember(dest => dest.Labels, opt => opt.MapFrom(src => src.Labels.Split(",", StringSplitOptions.RemoveEmptyEntries)))
            .ForMember(dest => dest.Items, opt => opt.MapFrom(src => src.Items));


        CreateMap<ReceivedInvoiceItemFlexiDto, ReceivedInvoiceItem>()
            .ForMember(dest => dest.Code, opt => opt.MapFrom(src => src.Code))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount));
    }
}
```

(Only the destination type names change.)

- [ ] **Step 4: Build the solution; verify it succeeds**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: `Build succeeded.` with zero errors. There should be no new warnings. If `error CS0246` for `ReceivedInvoiceDto` / `AccountingTemplateDto` / `ReceivedInvoiceItemDto` still appears, identify the missed file with:

```bash
```
<!-- Use the Grep tool, not bash grep -->

…and edit it to use the new name. Repeat until the build is clean.

Do NOT commit yet — tests still reference the old names.

---

## Task 12: Update existing tests and the mapping profile tests to use the renamed Domain types

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassifyInvoicesHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs`

`ClassifyInvoicesHandlerTests.cs` has 8 references to `ReceivedInvoiceDto` (in `Mock<…>.Setup` lambdas, `ReturnsAsync(new ReceivedInvoiceDto …)`, `It.IsAny<ReceivedInvoiceDto>()`, and one `List<ReceivedInvoiceDto>` for `unclassifiedInvoices`). The mapping profile test uses `DomainTypes.AccountingTemplateDto` etc. via an alias — update the alias targets. The handler test for not-found uses `(ReceivedInvoiceDto?)null` in a `ReturnsAsync` and `new ReceivedInvoiceDto { … }` for the found case.

- [ ] **Step 1: Replace `ClassifyInvoicesHandlerTests.cs` (full file)**

```csharp
using Anela.Heblo.Application.Features.InvoiceClassification.Services;
using Anela.Heblo.Application.Features.InvoiceClassification.UseCases.ClassifyInvoices;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.InvoiceClassification;

public class ClassifyInvoicesHandlerTests
{
    private readonly Mock<IReceivedInvoicesClient> _invoicesClientMock;
    private readonly Mock<IInvoiceClassificationService> _classificationServiceMock;
    private readonly Mock<IClassificationRuleRepository> _ruleRepositoryMock;
    private readonly Mock<ILogger<ClassifyInvoicesHandler>> _loggerMock;
    private readonly ClassifyInvoicesHandler _handler;

    public ClassifyInvoicesHandlerTests()
    {
        _invoicesClientMock = new Mock<IReceivedInvoicesClient>();
        _classificationServiceMock = new Mock<IInvoiceClassificationService>();
        _ruleRepositoryMock = new Mock<IClassificationRuleRepository>();
        _loggerMock = new Mock<ILogger<ClassifyInvoicesHandler>>();

        _handler = new ClassifyInvoicesHandler(
            _invoicesClientMock.Object,
            _classificationServiceMock.Object,
            _ruleRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WithMultipleInvoiceIds_FetchesAllInvoicesInParallel()
    {
        // Reproduces bug from issue #969: sequential foreach caused N sequential Flexi API calls.
        // Fix: use Task.WhenAll to fetch all invoices concurrently.

        var invoiceId1 = "INV-001";
        var invoiceId2 = "INV-002";
        var invoiceId3 = "INV-003";

        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId1))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId1, Labels = Array.Empty<string>() });
        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId2))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId2, Labels = Array.Empty<string>() });
        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(invoiceId3))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = invoiceId3, Labels = Array.Empty<string>() });

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { invoiceId1, invoiceId2, invoiceId3 }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(3);
        response.SuccessfulClassifications.Should().Be(3);
        response.Errors.Should().Be(0);

        _invoicesClientMock.Verify(x => x.GetInvoiceByIdAsync(invoiceId1), Times.Once);
        _invoicesClientMock.Verify(x => x.GetInvoiceByIdAsync(invoiceId2), Times.Once);
        _invoicesClientMock.Verify(x => x.GetInvoiceByIdAsync(invoiceId3), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSomeInvoicesNotFound_CountsThemAsErrors()
    {
        var foundId = "INV-001";
        var missingId = "INV-999";

        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(foundId))
            .ReturnsAsync(new ReceivedInvoice { InvoiceNumber = foundId, Labels = Array.Empty<string>() });
        _invoicesClientMock
            .Setup(x => x.GetInvoiceByIdAsync(missingId))
            .ReturnsAsync((ReceivedInvoice?)null);

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest
        {
            InvoiceIds = new List<string> { foundId, missingId }
        };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(1);
        response.SuccessfulClassifications.Should().Be(1);
        response.Errors.Should().Be(1);
        response.ErrorMessages.Should().ContainSingle(m => m.Contains(missingId));
    }

    [Fact]
    public async Task Handle_WithNoInvoiceIds_FetchesAllUnclassifiedInvoices()
    {
        var unclassifiedInvoices = new List<ReceivedInvoice>
        {
            new() { InvoiceNumber = "UNCLASSIFIED-001", Labels = Array.Empty<string>() }
        };

        _invoicesClientMock
            .Setup(x => x.GetUnclassifiedInvoicesAsync())
            .ReturnsAsync(unclassifiedInvoices);

        _classificationServiceMock
            .Setup(x => x.ClassifyInvoiceAsync(It.IsAny<ReceivedInvoice>()))
            .ReturnsAsync(new InvoiceClassificationResult { Result = ClassificationResult.Success });

        var request = new ClassifyInvoicesRequest { InvoiceIds = null };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.TotalInvoicesProcessed.Should().Be(1);
        response.SuccessfulClassifications.Should().Be(1);
        _invoicesClientMock.Verify(x => x.GetUnclassifiedInvoicesAsync(), Times.Once);
    }
}
```

- [ ] **Step 2: Update `InvoiceClassificationMappingProfileTests.cs` source-type usages**

Edit the file so that every occurrence of `DomainTypes.AccountingTemplateDto`, `DomainTypes.ReceivedInvoiceDto`, and `DomainTypes.ReceivedInvoiceItemDto` is replaced with `DomainTypes.AccountingTemplate`, `DomainTypes.ReceivedInvoice`, and `DomainTypes.ReceivedInvoiceItem` respectively. The `ContractTypes.*` destination names remain unchanged. Concretely:

- `new DomainTypes.AccountingTemplateDto { … }` → `new DomainTypes.AccountingTemplate { … }`
- `new DomainTypes.ReceivedInvoiceDto { … }` → `new DomainTypes.ReceivedInvoice { … }`
- `new List<DomainTypes.ReceivedInvoiceItemDto>` → `new List<DomainTypes.ReceivedInvoiceItem>`
- `new DomainTypes.ReceivedInvoiceItemDto { … }` → `new DomainTypes.ReceivedInvoiceItem { … }`

- [ ] **Step 3: Update `GetInvoiceDetailsHandlerTests.cs` source-type usages**

In the file created in Task 6, replace every occurrence of `ReceivedInvoiceDto` (used for the Domain mock setup and the not-found `null` cast) with `ReceivedInvoice`. The destination assertion `Application.Features.InvoiceClassification.Contracts.ReceivedInvoiceDto` stays unchanged — that's the contract type the response is expected to carry.

After edit, the relevant lines look like:

```csharp
clientMock.Setup(c => c.GetInvoiceByIdAsync("missing"))
          .ReturnsAsync((ReceivedInvoice?)null);

var domainInvoice = new ReceivedInvoice
{
    InvoiceNumber = "FV-001",
    CompanyName = "Acme",
    Labels = Array.Empty<string>(),
};
```

- [ ] **Step 4: Build and run the full InvoiceClassification test slice; verify everything passes**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceClassification" \
  --no-restore
```

Expected: build succeeds; all tests under `…Features.InvoiceClassification.*` pass.

- [ ] **Step 5: Format**

```bash
dotnet format backend/Anela.Heblo.sln
```

- [ ] **Step 6: Commit the rename**

```bash
git add backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ \
        backend/src/Anela.Heblo.Application/Features/InvoiceClassification/ \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/ \
        backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/
git commit -m "refactor(invoice-classification): rename Domain *Dto types to value-object names"
```

---

## Task 13: Verify API JSON contract is still byte-identical after Step B

**Files:**
- Create: `artifacts/feat-arch-review-invoiceclassification-domain/swagger-after.json`

Final FR-4 gate. Same procedure as Task 7, with the final snapshot persisted to the artifacts directory.

- [ ] **Step 1: Start the backend dev server**

```bash
cd backend/src/Anela.Heblo.API && dotnet run
```

- [ ] **Step 2: Snapshot the post-refactor swagger and diff against the baseline**

```bash
curl -sk https://localhost:5002/swagger/v1/swagger.json | \
  jq '{
    paths: {
      "/api/invoice-classification/accounting-templates": .paths["/api/invoice-classification/accounting-templates"],
      "/api/invoice-classification/invoices/{invoiceId}": .paths["/api/invoice-classification/invoices/{invoiceId}"]
    },
    schemas: {
      AccountingTemplateDto: .components.schemas.AccountingTemplateDto,
      ReceivedInvoiceDto: .components.schemas.ReceivedInvoiceDto,
      ReceivedInvoiceItemDto: .components.schemas.ReceivedInvoiceItemDto,
      GetAccountingTemplatesResponse: .components.schemas.GetAccountingTemplatesResponse,
      GetInvoiceDetailsResponse: .components.schemas.GetInvoiceDetailsResponse
    }
  }' > artifacts/feat-arch-review-invoiceclassification-domain/swagger-after.json

diff -u artifacts/feat-arch-review-invoiceclassification-domain/swagger-before.json \
        artifacts/feat-arch-review-invoiceclassification-domain/swagger-after.json
```

Expected: **no output**. Field names, types, nullability, required arrays, and enums are identical for all five schemas and the two paths.

If a diff appears: pause, inspect the delta, and fix the offending DTO or AutoMapper map until the snapshots match. The OpenAPI contract is the hard gate. Do not proceed.

- [ ] **Step 3: Stop the dev server (Ctrl+C)**

- [ ] **Step 4: Commit the final snapshot**

```bash
git add artifacts/feat-arch-review-invoiceclassification-domain/swagger-after.json
git commit -m "chore: snapshot post-refactor swagger; matches baseline byte-for-byte"
```

---

## Task 14: Regenerate the frontend TypeScript client and verify it builds

**Files:**
- Regenerate: `frontend/src/api/generated/api-client.ts` (via backend PostBuild event)
- Verify: `frontend/src/api/hooks/useInvoiceClassification.ts` and 22 other frontend files still compile

The arch review (§ Risks row 5) notes the regenerated file may show namespace/comment deltas. Field-level changes are a hard fail.

- [ ] **Step 1: Trigger a Debug backend build to regenerate the TS client**

```bash
dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj --configuration Debug
```

Expected: succeeds. The PostBuild event writes `frontend/src/api/generated/api-client.ts`.

- [ ] **Step 2: Inspect the generated client diff**

```bash
git diff --stat frontend/src/api/generated/api-client.ts
```

If the file is unchanged: nothing to do; skip Step 4.

If it has changes:

```bash
git diff frontend/src/api/generated/api-client.ts
```

Verify that every change is in one of these acceptable categories:
- C# XML doc comment text (transcribed into TS JSDoc) for `AccountingTemplateDto` / `ReceivedInvoiceDto` / `ReceivedInvoiceItemDto`
- Reordering of namespace import comments
- Whitespace

A renamed exported class, a changed property name, a changed property type (`string` → `string | null`, etc.), or an added/removed field is a hard fail — return to Task 11 and find the source mismatch.

- [ ] **Step 3: Run the frontend build to confirm nothing downstream broke**

```bash
cd frontend && npm run build
```

Expected: build succeeds. `useInvoiceClassification.ts` and other consumers continue to import `AccountingTemplateDto`, `ReceivedInvoiceDto`, `ReceivedInvoiceItemDto` without changes.

If the build fails citing one of those types: a manual frontend edit is being demanded by the regeneration — that is out of scope for this refactor and indicates the contract DID change. Stop and investigate.

- [ ] **Step 4: Commit the regenerated client (if it changed)**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI TypeScript client after Domain rename"
```

---

## Task 15: Domain layer cleanup verification

**Files:** (verification only, no edits unless violations are found)

NFR-3 acceptance: zero types with the `Dto` suffix should remain under `Anela.Heblo.Domain.Features.InvoiceClassification`. The spec scopes this to InvoiceClassification only; pre-existing violations in other modules are out of scope.

- [ ] **Step 1: Grep for residual `*Dto` declarations in InvoiceClassification Domain**

Use the Grep tool, pattern `class\s+\w+Dto\b`, path `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification`, output_mode `content`.

Expected: no results.

If any match appears (e.g. a sibling type still ends in `Dto`), document it in the PR description under "Out of scope" but do not delete it — the spec explicitly limits scope to the three named types.

- [ ] **Step 2: Grep for stale `*Dto` references in InvoiceClassification production code**

Use the Grep tool, pattern `\bReceivedInvoiceDto\b|\bReceivedInvoiceItemDto\b|\bAccountingTemplateDto\b`, paths `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification`, `backend/src/Anela.Heblo.Application/Features/InvoiceClassification`, `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification`.

Expected hits:
- `Anela.Heblo.Application.Features.InvoiceClassification.Contracts/ReceivedInvoiceDto.cs` — the new contract (allowed)
- `Anela.Heblo.Application.Features.InvoiceClassification.Contracts/ReceivedInvoiceItemDto.cs` — allowed
- `Anela.Heblo.Application.Features.InvoiceClassification.Contracts/AccountingTemplateDto.cs` — allowed
- `InvoiceClassificationMappingProfile.cs` — references the contracts as `CreateMap<…, AccountingTemplateDto>` (allowed)
- `GetAccountingTemplatesResponse.cs` / `GetAccountingTemplatesHandler.cs` — reference the contract (allowed)
- `GetInvoiceDetailsResponse.cs` / `GetInvoiceDetailsHandler.cs` — reference the contract (allowed)

Unexpected hits (Domain rule code, FlexiClient code referencing `*Dto` as a Domain type) indicate a missed substitution — fix and re-commit.

- [ ] **Step 3: Grep for orphaned `using Anela.Heblo.Domain.Features.InvoiceClassification;` imports**

Use the Grep tool, pattern `using Anela\.Heblo\.Domain\.Features\.InvoiceClassification;`, path `backend/src/Anela.Heblo.Application/Features/InvoiceClassification`.

For each result, open the file and verify the Domain import is still needed (i.e. the file references `IInvoiceClassificationsClient`, `IReceivedInvoicesClient`, `ClassificationRule`, `ClassificationResult`, `ClassificationHistory`, `ReceivedInvoice`, `AccountingTemplate`, etc.). If a file imports the Domain namespace solely to access one of the three renamed types AND now uses only the Application contract, remove the import. The acceptance criterion in FR-5 is: no `using Anela.Heblo.Domain.Features.InvoiceClassification;` remains where its only purpose was to access one of the relocated/renamed types.

If you make any edits in this step, run `dotnet build backend/Anela.Heblo.sln` then commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification
git commit -m "chore(invoice-classification): drop unused Domain imports after rename"
```

- [ ] **Step 4: Final full-solution build + test sweep**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes
dotnet test  backend/Anela.Heblo.sln --no-build
```

Expected: build clean (zero warnings about obsolete or missing types per FR-5), formatting clean, all tests green.

If `dotnet format --verify-no-changes` reports issues, run `dotnet format backend/Anela.Heblo.sln` and commit the formatting fix:

```bash
git add -u backend
git commit -m "chore: dotnet format after InvoiceClassification rename"
```

---

## Self-review notes (author check)

**Spec coverage:**
- FR-1 (Application contracts) → Task 2.
- FR-2 (Domain rename, Option A) → Tasks 8–11, locked to Option A per arch-review Decision 1.
- FR-3 (mapping in profile + AssertConfigurationIsValid) → Tasks 3, 4, 10, 12 step 2.
- FR-3 amendment (null-safe `GetInvoiceDetailsHandler`) → Task 6.
- FR-4 (no contract drift, committed snapshot diff) → Tasks 1, 7, 13.
- FR-5 (all references updated, FE compiles, no stale Domain imports) → Tasks 9–12, 14, 15.
- NFR-3 (no `*Dto` types left in Domain InvoiceClassification) → Task 15.
- NFR-4 (mapping profile test exists) → Tasks 3, 4.

**Spec amendments from arch review applied:**
- Amendment 1 (Option A mandate) → Decisions table + Tasks 8–11.
- Amendment 2 (null-safe handler) → Task 6.
- Amendment 3 (committed swagger diff) → Tasks 1, 7, 13.
- Amendment 4 (test file updates explicit) → Task 12 step 1.
- Amendment 5 (Domain grep scoped) → Task 15 steps 1–2.

**Placeholder scan:** no `TBD`, `TODO`, "fill in", or "similar to Task N" placeholders. Every step shows the exact code.

**Type consistency:** new Domain names used consistently — `AccountingTemplate`, `ReceivedInvoice`, `ReceivedInvoiceItem`. Application contract names consistently `AccountingTemplateDto`, `ReceivedInvoiceDto`, `ReceivedInvoiceItemDto` in `Application.Features.InvoiceClassification.Contracts`. AutoMapper map signatures match the property names defined in the contract files in Task 2 and the renamed Domain files in Task 8.
