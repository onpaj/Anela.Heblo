# Implementation Plan: Fix AbraInvoiceId population in ClassificationHistory

## Goal
`InvoiceClassificationService.RecordClassificationHistory` currently writes `invoice.InvoiceNumber` into **both** the `abraInvoiceId` and `invoiceNumber` constructor arguments of `ClassificationHistory`, so every audit row has `AbraInvoiceId == InvoiceNumber`. FlexiBee genuinely exposes two distinct identifiers on `ReceivedInvoiceFlexiDto` — internal numeric `Id` and human-readable `Code` — but the domain entity `ReceivedInvoice` only carries `Code` (as `InvoiceNumber`). This plan adds an `AbraInvoiceId` property to `ReceivedInvoice`, populates it from FlexiBee's internal `Id`, and fixes the `RecordClassificationHistory` call site so the two `ClassificationHistory` fields carry genuinely distinct values going forward. No DB migration, no DTO shape change — the `AbraInvoiceId` column already exists.

## Architecture
Three layers, one field, already-established Vertical Slice pattern for `InvoiceClassification`:
`ReceivedInvoiceFlexiDto.Id` (FlexiBee SDK) → `FlexiReceivedInvoiceMappingProfile` (AutoMapper, Adapter layer) → `ReceivedInvoice.AbraInvoiceId` (Domain entity) → `InvoiceClassificationService.RecordClassificationHistory` (Application layer) → `ClassificationHistory` constructor (unchanged) → EF persistence (unchanged) → `ClassificationHistoryDto.InvoiceId` (unchanged mapping). Every downstream consumer is already correctly wired for two distinct values; only the upstream source and the one call site that conflates them need fixing.

## Tech Stack
.NET 8, MediatR + MVC controllers, AutoMapper for FlexiBee DTO → domain mapping, EF Core (no changes here), xUnit + Moq + FluentAssertions for tests. Solution file: `Anela.Heblo.sln` at repo root. Test project for this change: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`.

## Validation commands used throughout this plan
Run all commands from the repository root (`/home/user/worktrees/feature-4056-Arch-Review-Invoiceclassification-Classificationhi`).
- Fast, scoped test loop during TDD steps:
  `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"`
- Full solution build (required before declaring the task done, per `CLAUDE.md`):
  `dotnet build`
- Full test suite (required before declaring the task done):
  `dotnet test`
- Formatting check (required before declaring the task done):
  `dotnet format`

---

### task: add-abra-invoice-id-domain-property

**Why:** `ReceivedInvoice` is the domain entity representing an in-memory (non-persisted) FlexiBee received invoice. It currently only exposes `InvoiceNumber` (FlexiBee's `Code`). It needs a second property, `AbraInvoiceId`, to carry FlexiBee's internal `Id` so downstream code (the mapping profile in the next task, and the service call site two tasks from now) has somewhere to put/read that value. This is a pure additive change to a plain POCO — there is no existing test file for `ReceivedInvoice` itself (confirmed: no `ReceivedInvoiceTests.cs` exists in `backend/test/`), so this step is verified by compilation, not a new unit test.

1. Open `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs`. Its current full content is:

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

2. Add the new `AbraInvoiceId` property directly above `InvoiceNumber`, matching the existing plain-settable-property style (this class is a class, not a record, per the project's DTO/entity convention):

```csharp
namespace Anela.Heblo.Domain.Features.InvoiceClassification;

public class ReceivedInvoice
{
    public string AbraInvoiceId { get; set; } = string.Empty;

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

3. Build just the Domain project to confirm it compiles:

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded.` with 0 errors.

4. Commit:

```bash
git add backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ReceivedInvoice.cs
git commit -m "Add AbraInvoiceId property to ReceivedInvoice domain entity

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9hXSww9WLhMo5YTaZwmv2"
```

---

### task: map-abra-invoice-id-from-flexibee-id

**Why:** `FlexiReceivedInvoiceMappingProfile` is the AutoMapper profile that translates `ReceivedInvoiceFlexiDto` (FlexiBee SDK, `Rem.FlexiBeeSDK.Model` v0.1.139) into the domain's `ReceivedInvoice`. `ReceivedInvoiceFlexiDto` exposes both `Int32 Id` (FlexiBee's internal record identifier, currently never mapped anywhere) and `String Code` (the human-readable document number, already mapped to `InvoiceNumber`). Both call sites that produce a `ReceivedInvoice` — `FlexiReceivedInvoicesClient.GetUnclassifiedInvoicesAsync` and `GetInvoiceByIdAsync` — go through `_mapper.Map<ReceivedInvoice>(...)` using this exact profile (confirmed: `grep` shows only these two call sites plus test fixtures construct `ReceivedInvoice`), so adding one `.ForMember(...)` line here populates `AbraInvoiceId` everywhere a `ReceivedInvoice` originates from FlexiBee. There is no existing unit test file for this mapping profile (`backend/test/Anela.Heblo.Adapters.Flexi.Tests/` has no `FlexiReceivedInvoiceMappingProfile` test), so this step is verified by a full solution build (which validates the mapping profile's expression trees compile) rather than a new test.

1. Open `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs`. Its current full content is:

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

2. Add a `using System.Globalization;` directive and a new `.ForMember(...)` line mapping `src.Id` (stringified with `CultureInfo.InvariantCulture`, per the architecture review's conversion guidance — a plain positive sequential `Int32` needs no locale-sensitive formatting, but invariant culture removes any doubt) to `dest.AbraInvoiceId`. Place it right before the existing `InvoiceNumber` mapping so the two identifier mappings sit next to each other:

```csharp
using System.Globalization;
using Anela.Heblo.Adapters.Flexi.Common;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Rem.FlexiBeeSDK.Model.Invoices;

namespace Anela.Heblo.Adapters.Flexi.Accounting.InvoiceClassification;

public class FlexiReceivedInvoiceMappingProfile : BaseFlexiProfile
{
    public FlexiReceivedInvoiceMappingProfile()
    {
        CreateMap<ReceivedInvoiceFlexiDto, ReceivedInvoice>()
            .ForMember(dest => dest.AbraInvoiceId, opt => opt.MapFrom(src => src.Id.ToString(CultureInfo.InvariantCulture)))
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

3. Build the Adapters.Flexi project to confirm the profile compiles and `ReceivedInvoiceFlexiDto.Id` resolves against the referenced `Rem.FlexiBeeSDK.Model` v0.1.139 package:

```bash
dotnet build backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

Expected: `Build succeeded.` with 0 errors.

4. Run the existing Adapters.Flexi test suite to confirm no other AutoMapper profile validation in that project breaks:

```bash
dotnet test backend/test/Anela.Heblo.Adapters.Flexi.Tests/Anela.Heblo.Adapters.Flexi.Tests.csproj
```

Expected: all tests pass (no new tests added in this task; this is a regression check).

5. Commit:

```bash
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiReceivedInvoiceMappingProfile.cs
git commit -m "Map FlexiBee internal Id to ReceivedInvoice.AbraInvoiceId

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9hXSww9WLhMo5YTaZwmv2"
```

---

### task: update-invoice-classification-service-tests-for-abra-invoice-id

**Why:** `InvoiceClassificationServiceTests.cs` has four test methods, each asserting `capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber)` — i.e. asserting the *current buggy* behavior where both fields are conflated. Per TDD, this task updates those tests (and their `ReceivedInvoice` fixtures) to assert the *correct* behavior first — using genuinely distinct `AbraInvoiceId`/`InvoiceNumber` fixture values — which will make all four tests **fail** against the current (not-yet-fixed) `InvoiceClassificationService.RecordClassificationHistory`, because that method still passes `invoice.InvoiceNumber` for both constructor arguments. The next task then fixes the call site to make these tests pass. This task depends on `add-abra-invoice-id-domain-property` already being done, since the fixtures below set `ReceivedInvoice.AbraInvoiceId`, which must already exist as a settable property for the file to compile.

The four occurrences are at (pre-edit) lines 76, 156, 237, 299 of `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`, one per test method. Each is preceded (30-45 lines above) by that test's `ReceivedInvoice` object initializer. Apply all four edits below.

1. In `ClassifyInvoiceAsync_NoMatchingRule_MarksForManualReviewAndRecordsHistory` (starts at line 30), update the fixture at lines 33-39 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-001",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-001",
            InvoiceNumber = "INV-001",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice"
        };
```

and update the assertion at line 76 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

2. In `ClassifyInvoiceAsync_RuleMatchedAndAbraSucceeds_RecordsSuccessAndReturnsRuleResult` (starts at line 100), update the fixture at lines 104-110 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-002",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Rule"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-002",
            InvoiceNumber = "INV-002",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Rule"
        };
```

and update the assertion at line 156 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

3. In `ClassifyInvoiceAsync_RuleMatchedAndAbraFails_RecordsErrorAndReturnsRuleIdForDisplay` (starts at line 181), update the fixture at lines 185-191 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-003",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with ABRA Failure"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-003",
            InvoiceNumber = "INV-003",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with ABRA Failure"
        };
```

and update the assertion at line 237 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

4. In `ClassifyInvoiceAsync_ExceptionThrown_RecordsErrorWithMessageAndReturnsErrorResult` (starts at line 262), update the fixture at lines 266-272 from:

```csharp
        var invoice = new ReceivedInvoice
        {
            InvoiceNumber = "INV-004",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Exception"
        };
```

to:

```csharp
        var invoice = new ReceivedInvoice
        {
            AbraInvoiceId = "ABRA-004",
            InvoiceNumber = "INV-004",
            InvoiceDate = DateTime.UtcNow,
            CompanyName = "Test Company",
            Description = "Test Invoice with Exception"
        };
```

and update the assertion at line 299 from:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.InvoiceNumber);
```

to:

```csharp
        capturedHistory.AbraInvoiceId.Should().Be(invoice.AbraInvoiceId);
```

5. Do **not** touch any other assertion in this file — `capturedHistory.InvoiceNumber.Should().Be(invoice.InvoiceNumber)` and every other line stay exactly as they are.

6. Run the scoped test suite and confirm all four tests now **fail** (the service under test hasn't been fixed yet):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
```

Expected: `Failed: 4, Passed: 0` (or similar — all 4 tests in this class fail), each failure showing an assertion mismatch like `Expected capturedHistory.AbraInvoiceId to be "ABRA-001" but found "INV-001"`. This confirms the test now correctly exercises the bug.

7. Commit:

```bash
git add backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs
git commit -m "Update InvoiceClassificationServiceTests to expect distinct AbraInvoiceId

Tests now use a distinct AbraInvoiceId fixture value per case and assert
against it, so they correctly fail against the current call-site bug
that conflates AbraInvoiceId with InvoiceNumber.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9hXSww9WLhMo5YTaZwmv2"
```

---

### task: fix-classification-history-call-site

**Why:** `InvoiceClassificationService.RecordClassificationHistory` (private method, lines 98-116) builds the `ClassificationHistory` audit row for every classification attempt. It currently passes `invoice.InvoiceNumber` for **both** the `abraInvoiceId` and `invoiceNumber` constructor parameters (lines 101-103), which is the root cause of the bug this whole plan fixes. With `ReceivedInvoice.AbraInvoiceId` now populated correctly (previous two tasks), this task changes the first constructor argument to `invoice.AbraInvoiceId`. This is the change that makes the four tests updated in `update-invoice-classification-service-tests-for-abra-invoice-id` pass.

1. Open `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs`. The `RecordClassificationHistory` method (lines 98-116) currently reads:

```csharp
    private async Task RecordClassificationHistory(ReceivedInvoice invoice, Guid? ruleId,
        ClassificationResult result, string? accountingTemplateCode, string? department, string? errorMessage, string processedBy)
    {
        var history = new ClassificationHistory(
            invoice.InvoiceNumber, // AbraInvoiceId
            invoice.InvoiceNumber, // InvoiceNumber
            invoice.InvoiceDate,   // InvoiceDate
            invoice.CompanyName,   // CompanyName
            invoice.Description,   // Description
            result,
            processedBy,
            ruleId,
            accountingTemplateCode,
            department,
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
```

2. Change the first constructor argument from `invoice.InvoiceNumber` to `invoice.AbraInvoiceId`:

```csharp
    private async Task RecordClassificationHistory(ReceivedInvoice invoice, Guid? ruleId,
        ClassificationResult result, string? accountingTemplateCode, string? department, string? errorMessage, string processedBy)
    {
        var history = new ClassificationHistory(
            invoice.AbraInvoiceId, // AbraInvoiceId
            invoice.InvoiceNumber, // InvoiceNumber
            invoice.InvoiceDate,   // InvoiceDate
            invoice.CompanyName,   // CompanyName
            invoice.Description,   // Description
            result,
            processedBy,
            ruleId,
            accountingTemplateCode,
            department,
            errorMessage
        );

        await _historyRepository.AddAsync(history);
    }
```

No other line in this file changes. In particular, `ClassifyInvoiceAsync`'s calls to `_classificationsClient.MarkInvoiceForManualReviewAsync(invoice.InvoiceNumber, ...)` and `_classificationsClient.UpdateInvoiceClassificationAsync(invoice.InvoiceNumber, ...)` (lines 41 and 49-50) correctly continue to use `invoice.InvoiceNumber` (FlexiBee's `Code`), since those are outbound FlexiBee API calls that address invoices by code, not by internal ID — this is explicitly out of scope per the spec.

3. Run the scoped test suite and confirm all four previously-failing tests now pass:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassificationServiceTests"
```

Expected: `Passed: 4, Failed: 0`.

4. Run the two other InvoiceClassification test files that construct `ClassificationHistory` directly (`InvoiceClassificationMappingProfileTests.cs`, `ClassificationHistoryRepositoryTests.cs`) to confirm they are unaffected, as predicted by the spec/arch-review (they already pass explicit, distinct `abraInvoiceId:` arguments and don't go through `ReceivedInvoice`):

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"
```

Expected: all tests in the `InvoiceClassification` namespace pass, with no regressions.

5. Run the full backend build and full test suite (final validation for the whole plan, per `CLAUDE.md`):

```bash
dotnet build
dotnet test
```

Expected: `Build succeeded.` with 0 errors/warnings introduced by this change, and all tests pass.

6. Run formatting check and apply formatting if needed:

```bash
dotnet format
```

Expected: no unexpected diffs outside the four files touched by this plan (`ReceivedInvoice.cs`, `FlexiReceivedInvoiceMappingProfile.cs`, `InvoiceClassificationService.cs`, `InvoiceClassificationServiceTests.cs`). If `dotnet format` modifies any of these four files, re-run step 5 to confirm tests still pass, then include the formatting diff in this commit.

7. Commit:

```bash
git add backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Services/InvoiceClassificationService.cs
git commit -m "Pass AbraInvoiceId instead of InvoiceNumber into ClassificationHistory

RecordClassificationHistory previously passed invoice.InvoiceNumber for
both the AbraInvoiceId and InvoiceNumber constructor arguments of
ClassificationHistory, so every audit row had AbraInvoiceId ==
InvoiceNumber. ReceivedInvoice.AbraInvoiceId (sourced from FlexiBee's
internal Id via FlexiReceivedInvoiceMappingProfile) now carries the
genuinely distinct value this field was meant to hold.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9hXSww9WLhMo5YTaZwmv2"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (add `AbraInvoiceId` to `ReceivedInvoice`, default `string.Empty`, plain settable property) → `add-abra-invoice-id-domain-property`.
- FR-2 (map FlexiBee's internal `Id` to `AbraInvoiceId` via AutoMapper, `.ToString()`-based conversion) → `map-abra-invoice-id-from-flexibee-id`, using `CultureInfo.InvariantCulture` as the arch-review's clarification (Specification Amendments section) directed.
- FR-3 (fix `RecordClassificationHistory` call site, no change to `ClassificationHistory` constructor / EF config / DTO, no migration) → `fix-classification-history-call-site`; confirmed no other file in this plan touches `ClassificationHistoryConfiguration.cs`, `InvoiceClassificationMappingProfile.cs`, or any migration.
- FR-4 (update exactly the 4 test occurrences at lines 76/156/237/299 with distinct fixture values, leave `InvoiceClassificationMappingProfileTests.cs`/`ClassificationHistoryRepositoryTests.cs` untouched) → `update-invoice-classification-service-tests-for-abra-invoice-id`; step 4 of the final task explicitly runs those two other files to confirm they remain green without modification.
- NFR-1 (no DTO/contract shape change) → satisfied by construction; no DTO file appears in any task.
- NFR-2 (no truncation, invariant culture, fits `HasMaxLength(100)`) → satisfied by `CultureInfo.InvariantCulture` in the mapping profile task; no schema/column change made.
- Out-of-scope items (backfill, changing `Code`-based FlexiBee call parameters, exposing `AbraInvoiceId` on `ReceivedInvoiceDto`, documenting in `flexibee-api.md`) → none of these are touched by any task, matching the spec.

**Placeholder scan:** no "TBD"/"TODO"/"add appropriate error handling"/"similar to Task N" phrasing anywhere above; every code block is the complete before/after file content or method body, not a diff fragment requiring inference.

**Type consistency:** `AbraInvoiceId` is `string` (non-nullable, defaults to `string.Empty`) consistently across `ReceivedInvoice` (task 1), the AutoMapper `MapFrom` target (task 2), the test fixtures (task 3, string literals `"ABRA-00N"`), and the `ClassificationHistory` constructor's existing `string abraInvoiceId` parameter (task 4, unchanged) — no mismatch.
