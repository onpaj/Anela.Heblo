# Spec r1 - Unit tests for FlexiInvoiceClassificationsClient (issue 4606)

## Goal
Close the coverage gap on `FlexiInvoiceClassificationsClient` (27.5% line coverage) by adding unit tests. No production behaviour change.

## Scope
Target: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClient.cs`.
New test file in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/InvoiceClassification/`.

## Requirements
- R1 `UpdateInvoiceClassificationAsync`, update succeeds: returns true; `RemoveTagAsync` called once with invoice id and both labels (trigger, manual review) taken from `DataSourceOptions`.
- R2 Update fails (`IsSuccess=false`): returns false; `RemoveTagAsync` never called.
- R3 `MarkInvoiceForManualReviewAsync`, add and remove succeed: returns true; AddTag called with manual-review label, RemoveTag with trigger label.
- R4 AddTag succeeds, RemoveTag fails: returns false (documents partial-failure behaviour).
- Optional extras (same file): AddTag fails -> false; `GetValidAccountingTemplatesAsync` filters `N-` prefix and `ModuleReceivedInvoicedAvailable=false`, orders by Code, maps fields; passed CancellationToken forwarded.

## Non-goals
Fixing the partial-failure semantics (re-processing risk) is out of scope; tests only characterise current behaviour. Mention as follow-up.

## Acceptance
Tests pass via `dotnet test` on the Flexi test project; `dotnet build` and `dotnet format` clean.

## Open question
Exact SDK result type returned by `UpdateInvoiceAsync/AddTagAsync/RemoveTagAsync` (appears to be `OperationResult`-like with `IsSuccess`); implementer must confirm constructability/mockability.
