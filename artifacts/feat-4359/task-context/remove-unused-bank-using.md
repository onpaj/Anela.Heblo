### task: remove-unused-bank-using
Delete line `using Anela.Heblo.Domain.Features.Bank;` from `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`.

Verification:
- `grep -n "Domain.Features.Bank" backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` returns nothing.
- `dotnet build` and `dotnet format` pass; relevant tests (ModuleBoundariesTests, Invoices tests) pass.
