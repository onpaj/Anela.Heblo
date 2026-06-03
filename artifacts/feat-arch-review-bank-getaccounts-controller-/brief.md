## Module
Bank

## Finding
`BankStatementsController.GetAccounts()` (`backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`, lines 33–44) maps domain objects to DTOs directly inside the controller action, with no MediatR handler involved:

```csharp
public ActionResult<IEnumerable<BankAccountDto>> GetAccounts()
{
    var accounts = (_bankSettings.Accounts ?? [])
        .Select(a => new BankAccountDto
        {
            Name = a.Name,
            AccountNumber = a.AccountNumber,
            Provider = a.Provider.ToString(),
            Currency = a.Currency.ToString(),
        });
    return Ok(accounts);
}
```

The controller also holds a direct dependency on `BankAccountSettings` (injected via `IOptions<BankAccountSettings>` at lines 22–27), which is an infrastructure/configuration concern.

## Why it matters
`development_guidelines.md` explicitly lists "Business logic in Controller class" as a forbidden practice. The two other actions in the same controller (`ImportStatements`, `GetBankStatements`) correctly delegate to MediatR handlers — `GetAccounts` is inconsistent and couples domain knowledge (iterating settings, constructing DTOs) to the HTTP layer. It also makes the operation untestable in isolation via MediatR.

## Suggested fix
Create a `GetBankAccounts` use case (request + handler) in `Application/Features/Bank/UseCases/GetBankAccounts/`. The handler reads `IOptions<BankAccountSettings>` and returns a `GetBankAccountsResponse`. The controller action becomes a one-liner: `return Ok(await _mediator.Send(new GetBankAccountsRequest()))`. Remove the `IOptions<BankAccountSettings>` injection from the controller.

---
_Filed by daily arch-review routine on 2026-05-29._