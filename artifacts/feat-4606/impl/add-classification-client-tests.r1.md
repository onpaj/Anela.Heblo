# Impl: add-classification-client-tests r1
Added `FlexiInvoiceClassificationsClientTests` (5 tests: update success/failure, mark-for-review both ok / remove fails / add fails).
Note: single-tag AddTagAsync/RemoveTagAsync are SDK extension methods delegating to the IEnumerable overloads, so mocks target those.
Result: 5/5 pass. Mutation check (guard replaced by `true`) fails the update-failure test; reverted. No production files changed.
