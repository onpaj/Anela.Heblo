# Design: Rename VatClassificationRule / CompanyVat to IČO naming

## Component Design
- `IcoClassificationRule : IClassificationRule` (Domain/Features/InvoiceClassification/Rules). Identifier `"ICO"`, DisplayName `"IČO"`, Description `"Porovnání IČO firmy"`. `Evaluate` compares `invoice.CompanyIco?.Trim()` with `pattern?.Trim()` using `OrdinalIgnoreCase`, identical to current logic.
- `ReceivedInvoice.CompanyIco` (string, default empty) with XML doc "Supplier IČO (Flexi `ic`)". `SupplierVatId` doc: "Supplier DIČ (Flexi `dic`). Distinct from CompanyIco, which holds the IČO."
- `FlexiReceivedInvoiceMappingProfile`: `.ForMember(dest => dest.CompanyIco, opt => opt.MapFrom(src => src.CompanyId))`.
- `InvoiceClassificationModule`: `services.AddScoped<IClassificationRule, IcoClassificationRule>();`
- `ReceivedInvoiceDto.CompanyIco`.

## Data Schemas
- Database: no change (`ClassificationRules.RuleTypeIdentifier` keeps `"ICO"`).
- API: `GetInvoiceDetailsResponse.invoice.companyVat` becomes `companyIco` (string).
