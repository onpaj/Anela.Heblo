# Architecture review r1

- Change is test-only; fits existing layout: adapter tests live in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/...` (sibling `Departments/FlexiDepartmentQueryServiceTests.cs`, `Analytics/AccountingTemplateSyncServiceTests.cs` already mock Flexi SDK clients).
- Stack available in test project: xunit, Moq 4.20, FluentAssertions 6.12, AutoFixture. Use Moq for `IAccountingTemplateClient` and `IReceivedInvoiceClient`; `Options.Create(new DataSourceOptions{...})` for options; `NullLogger<T>`.
- Defaults: trigger label "KLASIFIKACE", manual review "MANUAL-KLASIF" (`DataSourceOptions`). Use custom distinct values in tests to prove config is read.
- Risk: SDK result types (Rem.FlexiBeeSDK 0.1.144, `OperationResult`-like with `IsSuccess`) may have non-public constructors; fall back to Moq mock of result type or a helper that builds via reflection/JSON. Verify first.
- Risk: `RemoveTagAsync` takes a collection in the update path and a single string in the mark path (different overloads); verify setups match each overload.
- No docs/process change: no behaviour change, so no `docs/processes` update required (verify the invoice classification process doc is not owned by a touched source file - only test files touched).
- Production bug candidate (out of scope): partial failure in MarkInvoiceForManualReview leaves the invoice with both labels. File as follow-up.
