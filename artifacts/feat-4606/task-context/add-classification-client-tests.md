### task: add-classification-client-tests
**Goal:** Add `FlexiInvoiceClassificationsClientTests` covering the four required scenarios plus AddTag-fails and template-filter tests.

**Files:**
- Create: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClientTests.cs`

**Steps:**
1. Inspect SDK types (`IAccountingTemplateClient.UpdateInvoiceAsync`, `IReceivedInvoiceClient.AddTagAsync/RemoveTagAsync`) in `Rem.FlexiBeeSDK.Client` 0.1.144 and their result type (`IsSuccess`); determine how to construct success/failure results (constructor, object initializer, or Moq). Note the RemoveTagAsync overloads (collection vs string).
2. Write the test class per `design.r1.md`: Moq mocks, `Options.Create(new DataSourceOptions{InvoiceClassificationTriggerLabel="TRIGGER-X", InvoiceClassificationManualReviewLabel="MANUAL-X"})`, `NullLogger<...>.Instance`.
3. Implement tests 1-6; assert return values with FluentAssertions and side effects with `Verify` (Times.Once / Times.Never).
4. Run `cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests --filter FlexiInvoiceClassificationsClientTests` - all pass.
5. Mutation sanity check: temporarily remove the `if (result.IsSuccess)` guard and confirm test 2 fails; revert.
6. Run `dotnet build` and `dotnet format` (BE validation); ensure no production files changed.

**Done when:** tests green, format clean, only the new test file added.

