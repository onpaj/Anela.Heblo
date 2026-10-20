# Design: Move BankStatementImportDto.ErrorType derivation out of the DTO and into BankMappingProfile

## Component Design

### `BankStatementImportDto` (`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`)
Responsibility: pure data container for a bank statement import row crossing the API boundary. No behavior, no domain references.

Final shape:
```csharp
namespace Anela.Heblo.Application.Features.Bank.Contracts;

public class BankStatementImportDto
{
    public int Id { get; set; }
    public string TransferId { get; set; } = null!;
    public DateTime StatementDate { get; set; }
    public DateTime ImportDate { get; set; }
    public string Account { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public int ItemCount { get; set; }
    public string ImportResult { get; set; } = null!;
    public string? ErrorType { get; set; }
}
```
Change from current state: `ErrorType` goes from a computed get-only expression-bodied property (`=> ImportResult != ImportStatus.Success ? ImportResult : null`) to a plain nullable auto-property with a public setter. The `using Anela.Heblo.Domain.Features.Bank;` directive at the top of the file is removed, since it was needed only for `ImportStatus`.

### `BankMappingProfile` (`backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs`)
Responsibility: owns Domain → Contract mapping rules for the Bank module, including any field whose value is derived rather than a direct property copy. This is where the `ErrorType` derivation rule now lives, consistent with the module's DTO/contract rules.

Final shape:
```csharp
using Anela.Heblo.Application.Features.Bank.Contracts;
using Anela.Heblo.Domain.Features.Bank;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Bank;

public class BankMappingProfile : Profile
{
    public BankMappingProfile()
    {
        CreateMap<BankStatementImport, BankStatementImportDto>()
            .ForMember(dest => dest.ErrorType,
                opt => opt.MapFrom(src =>
                    src.ImportResult != ImportStatus.Success ? src.ImportResult : null));
    }
}
```
This is the single `CreateMap` for this type pair, gaining one `.ForMember(...)` chained call. No second `CreateMap` and no separate mapping profile are introduced.

### Consumers (unchanged — no code edits required)
- `GetBankStatementListHandler.Handle` — continues to call `_mapper.Map<List<BankStatementImportDto>>(items)`. The mapper now populates `ErrorType` via the new `ForMember` rule instead of the DTO computing it itself; the handler's own code is untouched.
- `GetBankStatementByIdHandler.Handle` — continues to call `_mapper.Map<BankStatementImportDto>(entity)`; untouched.
- `BankStatementsController` and any downstream response wrapper (`GetBankStatementListResponse`) — untouched; they only reference the DTO's field set, which is unchanged.
- `ImportTab.tsx` (frontend) — untouched; it only reads `statement.errorType` for display, never sets it.

## Data Schemas

### `BankStatementImportDto` JSON shape (API response)
Unchanged field set and unchanged nullability of `errorType` in the JSON payload itself:
```json
{
  "id": 42,
  "transferId": "T12345",
  "statementDate": "2026-01-15T00:00:00Z",
  "importDate": "2026-01-15T08:03:00Z",
  "account": "123456789",
  "currency": "CZK",
  "itemCount": 7,
  "importResult": "OK",
  "errorType": null
}
```
`errorType` is `null` when `importResult == "OK"` and equals `importResult`'s value otherwise — identical runtime values before and after this change (spec FR-3).

### OpenAPI schema / generated TypeScript client — intentional contract change
Before this change, NSwag (or the project's configured OpenAPI generator) emits `errorType` as a computed, read-only schema property (the generator sees a get-only CLR property and marks it `readOnly: true` in the OpenAPI schema, which typically surfaces as an optional, non-assignable field in the generated TypeScript interface). After this change, `ErrorType` is a normal settable CLR property, so the generator will emit it as an ordinary writable schema property — the same treatment every other field of this DTO (`Id`, `TransferId`, `Account`, etc.) already gets. Per spec NFR-4, this is the intended outcome, not a regression: it is the concrete fix for "any client that tries to round-trip the DTO will silently lose the field," per the issue.

No change to the schema's field name, type (`string`, nullable), or its presence/absence in the payload — only its read-only/writable classification in the schema and generated client type.

No other DTO, database table, or event payload is affected by this change.
