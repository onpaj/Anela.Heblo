Plan saved to `artifacts/feat-arch-review-marketing-marketingaction-do/plan.r1.md`.

**Summary:** Five tasks, each TDD-scoped with a single commit:
- Task 1: `AssociateWithProduct(productCode, utcNow)` — entity + 2 handlers + existing-tests rewrite + new exact-equality test
- Task 2: `LinkToFolder(folderKey, folderType, utcNow)` — entity + 2 handlers + new test file
- Task 3: `SoftDelete(userId, username, utcNow)` + `IMarketingActionRepository.DeleteSoftAsync(..., utcNow, ct)` + `MarketingActionRepository` impl + `DeleteMarketingActionHandler` captures `now` + 2 existing test updates + new test file
- Task 4: Verification — grep gates on `MarketingAction.cs` and `MarketingActionRepository.cs` for any remaining clock reads, and a `JournalEntry`-untouched guard
- Task 5: `dotnet build` + `dotnet format` + full test suite

The plan honors every architectural amendment from the review (corrected FR-4 — handler does not currently capture `now`; FR-5 extended to the repository file; FR-3 identity-equality regression test).