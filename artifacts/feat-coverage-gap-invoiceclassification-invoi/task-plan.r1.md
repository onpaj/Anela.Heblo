Plan saved to `artifacts/feat-coverage-gap-invoiceclassification-invoi/plan.r1.md`.

Six tasks, each ending in a commit:

1. **Task 1** — Scaffold the test class with fixture, mocks, and `CreateInvoice`/`CreateRule` helpers.
2. **Task 2** — FR-1: no matching rule → manual review + history.
3. **Task 3** — FR-2: rule matched, ABRA success → success + history.
4. **Task 4** — FR-3: rule matched, ABRA failure → error + history (with the documented `AccountingTemplateCode` asymmetry between DTO and history).
5. **Task 5** — FR-4: exception thrown → caught, error returned, history recorded.
6. **Task 6** — Validation: run the class, run the feature folder, `dotnet format --verify-no-changes`, full `dotnet build`.

The plan implements the architecture review's amendments (flat path under `Features/InvoiceClassification/`, sync `FindMatchingRule`, `ClassificationResult` enum, no `GetByIdAsync` on the ABRA-failure path, non-null `CurrentUser.Name`) and uses `Callback`-captured `ClassificationHistory` for diff-friendly failure messages on FR-5.