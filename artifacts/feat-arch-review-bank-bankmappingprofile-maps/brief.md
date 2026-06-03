## Module
Bank

## Finding
`BankStatementImportDto` defines `ErrorType` as a get-only computed property:

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs, line 13
public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
```

`BankMappingProfile` then configures a mapping for the same property:

```csharp
// backend/src/Anela.Heblo.Application/Features/Bank/BankMappingProfile.cs, line 12
CreateMap<BankStatementImport, BankStatementImportDto>()
    .ForMember(dest => dest.ErrorType, opt => opt.MapFrom(src => src.ImportResult != "OK" ? src.ImportResult : null));
```

AutoMapper silently ignores `ForMember` calls targeting read-only (get-only) destination properties, so this configuration line has no effect at runtime. The identical `"not OK → return result"` expression is written twice, and the mapper config is dead code.

## Why it matters
- The `ForMember` call is misleading: it looks like it does something but doesn't. A future change to the DTO property (e.g. adding a setter) would suddenly activate the mapper rule, potentially with unexpected behaviour.
- The business rule for deriving `ErrorType` from `ImportResult` is duplicated across two files with no single source of truth.
- KISS: one place for the derivation is enough.

## Suggested fix
Remove the `.ForMember(dest => dest.ErrorType, ...)` line from `BankMappingProfile.cs` entirely. The get-only computed property on the DTO is the correct and sufficient implementation. Optionally, rename `ImportStatus.Success` (`"OK"`) to make the DTO comparison more readable.

---
_Filed by daily arch-review routine on 2026-05-29._