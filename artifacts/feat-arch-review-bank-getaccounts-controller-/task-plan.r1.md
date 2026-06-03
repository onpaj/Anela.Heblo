Plan saved to `docs/superpowers/plans/2026-06-03-bank-getaccounts-mediatr-refactor.md`.

**Summary of the plan:**
- **7 tasks**, TDD-ordered, each with bite-sized steps and full code:
  1. Create `GetBankAccountsRequest` (empty MediatR request).
  2. Create `GetBankAccountsResponse : BaseResponse` (and commit pair 1+2 together so the repo stays green).
  3. Write 5 failing xUnit tests for `GetBankAccountsHandler` (null Accounts → empty, empty → empty, mapping with `.ToString()`, null-options throws `ArgumentNullException`, null-logger throws).
  4. Implement the handler synchronously with `Task.FromResult` and the tightened `bankSettings?.Value ?? throw …` null-check; log `"Retrieved {Count} bank accounts"`.
  5. Refactor the controller: `async` + `CancellationToken`, dispatch via `_mediator.Send`, `Ok(response.Accounts)`, drop `IOptions<BankAccountSettings>`, `_bankSettings` field, and the two now-unused `using` directives. Add the new namespace `using`.
  6. Add `BankStatementsControllerTests` that asserts the controller returns a bare `IEnumerable<BankAccountDto>` (not the `GetBankAccountsResponse` envelope) — pinning the high-severity wire-shape risk.
  7. Final solution-wide `dotnet build` + `dotnet test` + `dotnet format`, plus optional manual `curl` and TS-client regen sanity checks.
- All five arch-review amendments are explicitly addressed and traced in the self-review.