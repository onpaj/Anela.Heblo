### task: register-import-bank-statement-validator-in-di

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs:29-32`

- [ ] **Step 1: Confirm current DI registration block**

The relevant existing lines in `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` read:

```csharp
        services.AddScoped<IValidator<GetBankStatementListRequest>, GetBankStatementListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetBankStatementListRequest, GetBankStatementListResponse>,
            ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>>();
```

- [ ] **Step 2: Add the new registrations directly below that block**

Edit `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` so the block becomes:

```csharp
        services.AddScoped<IValidator<GetBankStatementListRequest>, GetBankStatementListRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<GetBankStatementListRequest, GetBankStatementListResponse>,
            ValidationBehavior<GetBankStatementListRequest, GetBankStatementListResponse>>();

        services.AddScoped<IValidator<ImportBankStatementRequest>, ImportBankStatementRequestValidator>();
        services.AddScoped<
            IPipelineBehavior<ImportBankStatementRequest, BankStatementImportResultDto>,
            ValidationBehavior<ImportBankStatementRequest, BankStatementImportResultDto>>();
```

Add the two missing `using` statements at the top of the file (both namespaces already exist in the codebase, and `BankModule.cs` already has a `using Anela.Heblo.Application.Features.Bank.Validators;` line — verify it's present, it already is since `GetBankStatementListRequestValidator` is in that namespace):

```csharp
using Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement;
using Anela.Heblo.Application.Features.Bank.Contracts;
```

(`ImportBankStatementRequest` lives in `Anela.Heblo.Application.Features.Bank.UseCases.ImportBankStatement`; `BankStatementImportResultDto` lives in `Anela.Heblo.Application.Features.Bank.Contracts` — add whichever of these two `using` lines is not already present in the file. `Anela.Heblo.Application.Features.Bank.Validators` is already imported since `GetBankStatementListRequestValidator` is used two lines above.)

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs
git commit -m "feat(bank): register ImportBankStatementRequestValidator in DI pipeline"
```

---
