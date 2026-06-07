## Module
Bank

## Finding
`BankStatementsController.GetBankStatement(int id)` (lines 142–169 of `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`) does not route through a purpose-built MediatR handler. Instead the controller constructs a `GetBankStatementListRequest` with `Id = id, Take = 1`, sends it to the list handler, calls `FirstOrDefault()` on the result, and decides whether to return `Ok` or `NotFound(...)`. The "not found → 404" decision is business logic sitting in the controller.

Meanwhile `IBankStatementImportRepository.GetByIdAsync(int id)` (declared in `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` and implemented in `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`) goes uncalled through any handler — it is only reachable via the workaround above.

## Why it matters
The project guidelines state: *"Business logic must be in MediatR handlers, NOT in controllers."* The null-check + `NotFound` branch is a business decision (what a missing record means for callers). Placing it in the controller also misuses the list handler for a point-lookup, leaking pagination semantics (`Take=1`) into a path that has nothing to do with pagination.

## Suggested fix
Add a `GetBankStatementByIdHandler` in `backend/src/Anela.Heblo.Application/Features/Bank/UseCases/GetBankStatementById/` that calls `_repository.GetByIdAsync(id)` and returns a typed result (e.g. `null` / a populated response). The controller action becomes a thin dispatcher:

```csharp
var response = await _mediator.Send(new GetBankStatementByIdRequest(id), cancellationToken);
return response is null ? NotFound() : Ok(response);
```

No changes to the repository interface or implementation are required — `GetByIdAsync` is already there.

---
_Filed by daily arch-review routine on 2026-06-03._