# Design r1 - test design

No UI. Single test class `FlexiInvoiceClassificationsClientTests` (namespace `Anela.Heblo.Adapters.Flexi.Tests.Accounting.InvoiceClassification`).

Fixture: Moq mocks `IAccountingTemplateClient`, `IReceivedInvoiceClient`; `IOptions<DataSourceOptions>` with TriggerLabel="TRIGGER-X", ManualReviewLabel="MANUAL-X"; `NullLogger`. Private helper `Sut()` and `Ok()/Fail()` result factories.

Tests (naming `Method_Condition_Expected`):
1. UpdateInvoiceClassificationAsync_WhenUpdateSucceeds_RemovesBothTagsAndReturnsTrue
2. UpdateInvoiceClassificationAsync_WhenUpdateFails_DoesNotRemoveTagsAndReturnsFalse (Verify Times.Never)
3. MarkInvoiceForManualReviewAsync_WhenBothSucceed_ReturnsTrue (verify Add manual label, Remove trigger label)
4. MarkInvoiceForManualReviewAsync_WhenRemoveFails_ReturnsFalse
5. MarkInvoiceForManualReviewAsync_WhenAddFails_ReturnsFalse (optional)
6. GetValidAccountingTemplatesAsync_FiltersAndOrders (optional)

Use FluentAssertions for results and Moq `Verify` for side effects.
