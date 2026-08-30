# Code Review: add-dqt-invoice-snapshot-contracts-and-mapper

## Summary

The implementation adds exactly the three DataQuality-owned contract classes and the
provider-owned `InvoiceDqtSnapshotMapper` specified in the task context, matching the
required file contents verbatim. The task-required test file was created with the exact
specified content, all three tests pass, and the solution builds cleanly end-to-end. The
task's "self-contained, nothing wired in yet" constraint is honored — no other file
references the new types.

## Review Result: PASS

### task: add-dqt-invoice-snapshot-contracts-and-mapper
**Status:** PASS

Verification performed:

1. **File placement matches the spec exactly:**
   - `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/DqtInvoiceSnapshot.cs`
   - `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapper.cs`
   - `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceDqtSnapshotMapperTests.cs`

   The mapper is placed beside the two existing adapters
   (`InvoiceShoptetSourceAdapter.cs`, `InvoiceErpClientAdapter.cs`) rather than in
   `Contracts/` or DataQuality's `Services/` folder, satisfying the Decision 3 placement
   rule (provider-owned code that spans both namespaces belongs with the adapters).

2. **DTO rule compliance:** `DqtInvoiceSourceQuery`, `DqtInvoiceSnapshot`, and
   `DqtInvoiceItem` are plain classes with `{ get; set; }` properties — not records —
   satisfying the repo-wide "DTOs are classes, never records" rule.

3. **Contract purity:** `DqtInvoiceSnapshot.cs` has zero `using` directives and contains
   no reference to `Anela.Heblo.Domain.Features.Invoices` — it is provider-agnostic, as
   required.

4. **Field mapping correctness**, checked against the task's mapping table and against
   `InvoiceDqtComparer`'s actual field reads:
   - `IssuedInvoiceDetail.Code` → `DqtInvoiceSnapshot.Code` ✓
   - `IssuedInvoiceDetail.Price.TotalWithVat` → `.TotalWithVat` ✓
   - `IssuedInvoiceDetail.Price.TotalWithoutVat` → `.TotalWithoutVat` ✓
   - `IssuedInvoiceDetail.Items[]` → `.Items[]` via `ToDqtItem` ✓
   - `IssuedInvoiceDetailItem.Code` → `DqtInvoiceItem.Code` ✓
   - `IssuedInvoiceDetailItem.Amount` → `.Amount` ✓
   - `IssuedInvoiceDetailItem.ItemPrice.WithVat` → `.WithVat` ✓ (not swapped with
     `WithoutVat`, not sourced from `BuyPrice`)
   - `IssuedInvoiceDetailItem.ItemPrice.WithoutVat` → `.WithoutVat` ✓

   No extra fields not listed in the mapping table (e.g. `Name`, `VariantName`,
   `ProductGuid`, `BuyPrice`, `Vat`, `CurrencyCode`) were mapped, matching the task's
   explicit "deliberately NOT mapped" instructions.

5. **Testability pattern:** `InvoiceDqtSnapshotMapper` is `internal static`, matching the
   existing `InvoiceConsumptionSourceAdapter` pattern, reachable from
   `Anela.Heblo.Tests` via the assembly's existing `InternalsVisibleTo` attribute — no
   accessibility changes were needed or made.

6. **Test file content** matches the task context's exact specified content
   character-for-character (3 `[Fact]` tests, `FluentAssertions`/`Xunit` usings, no
   `Moq` needed and none imported).

7. **Self-containment verified:** grepped the full `backend/src` and `backend/test`
   trees for `DqtInvoiceSnapshot`, `DqtInvoiceItem`, `DqtInvoiceSourceQuery`, and
   `InvoiceDqtSnapshotMapper` — the only hits are the three new files themselves. Task 1
   correctly does not touch `IInvoiceShoptetSource`, `IInvoiceErpClient`,
   `InvoiceShoptetSourceAdapter`/`InvoiceErpClientAdapter`'s `GetAllAsync` bodies, or
   `InvoiceDqtComparer` — all confirmed unmodified (not present in `git status --short`).

8. **Build/test verification:**
   - `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — succeeded,
     0 errors (244 pre-existing warnings, none introduced by the new files).
   - `dotnet test ... --filter "FullyQualifiedName~InvoiceDqtSnapshotMapperTests"` —
     `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`, and this run also rebuilt
     the entire solution (all adapter/API projects) with zero errors, confirming no
     regressions elsewhere.

No blocking issues found. No functional requirement from the task spec is unmet, no
architecture placement rule is violated, and the implementation introduces no logic
beyond the specified straight-line field mapping.

## Docs to Update

None. This task adds internal, currently-unused types and a mapper with no change to
public behavior, CLI commands, environment variables, or agent/pipeline configuration.
`docs/integrations/shoptet-api.md` is not implicated since no live Shoptet call
behavior changed.

## Overall Notes

The mapper's use of `Items.Select(ToDqtItem).ToList()` reads cleanly and correctly
resolves the instance-extension-method-as-delegate call (`ToDqtItem` bound to each
`IssuedInvoiceDetailItem` in the sequence). No further changes needed for this task; the
follow-up task (wiring these types into `IInvoiceShoptetSource`/`IInvoiceErpClient` and
the two adapters) is out of scope here and correctly deferred.

**Status:** PASS
