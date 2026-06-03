## Module
Bank

## Finding
`backend/src/Anela.Heblo.Application/Features/Bank/Contracts/BankStatementImportDto.cs`, line 13:

```csharp
public string? ErrorType => ImportResult != "OK" ? ImportResult : null;
```

The string `"OK"` is the same value as `ImportStatus.Success`, which is already declared as a named constant in `backend/src/Anela.Heblo.Domain/Features/Bank/ImportStatus.cs`:

```csharp
public static class ImportStatus
{
    public const string Success = "OK";
    ...
}
```

Every other consumer in the module uses `ImportStatus.Success` (e.g. `BankStatementImportRepository.cs` line 49, `ImportBankStatementHandler.cs` line 89). `BankStatementImportDto` is the one outlier that bypasses the constant.

## Why it matters
If `ImportStatus.Success` is ever changed (e.g. normalised to lowercase or a richer value), `ErrorType` will silently start misclassifying successful imports as errors. The constant exists precisely to prevent this kind of scattered literal.

## Suggested fix
Reference the constant from the DTO. The Application layer already depends on the Domain layer, so no new dependency is introduced:

```csharp
using Anela.Heblo.Domain.Features.Bank;

public string? ErrorType => ImportResult != ImportStatus.Success ? ImportResult : null;
```

---
_Filed by daily arch-review routine on 2026-06-03._