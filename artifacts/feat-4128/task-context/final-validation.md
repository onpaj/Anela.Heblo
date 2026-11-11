### task: final-validation

- [ ] `cd backend && dotnet build && dotnet format --verify-no-changes` (run `dotnet format` and commit if needed).
- [ ] `dotnet test` for the whole backend solution; all pass.
- [ ] Confirm OpenAPI unchanged: build the API project and verify the generated TypeScript client shows no diff (`git status frontend/src/api`).
- [ ] `grep -rn "Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/Manufacture` lists only the documented residual (ERP stock-taking types, `CatalogManufactureRecord`, `StockTakingType` in the history DTO).
- [ ] File a follow-up GitHub issue (or note in PR description) for the residual allowlist entries.
