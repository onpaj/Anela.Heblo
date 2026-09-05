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

