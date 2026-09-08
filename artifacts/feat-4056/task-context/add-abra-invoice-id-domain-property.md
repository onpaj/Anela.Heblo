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

