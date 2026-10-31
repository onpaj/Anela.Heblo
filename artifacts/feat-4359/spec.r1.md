# Spec: Remove unused Bank domain using from InvoicesModule

## Problem
`InvoicesModule.cs` line 14 imports `Anela.Heblo.Domain.Features.Bank`, but no Bank type is referenced in the file (verified by grep: the only match for "Bank" is the using itself). This is a phantom cross-module coupling.

## Requirements
- R1: Remove `using Anela.Heblo.Domain.Features.Bank;` from `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`.
- R2: No other lines change; behaviour is unchanged.

## Acceptance criteria
- AC1: The directive is absent from the file.
- AC2: `dotnet build` succeeds; `dotnet format --verify-no-changes` is clean for the file.
- AC3: Existing tests (incl. ModuleBoundariesTests) pass.

## Out of scope
Reordering other usings, other modules. No process-doc change (no behaviour change).
