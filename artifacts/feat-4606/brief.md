# Coverage gap — `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/InvoiceClassification/FlexiInvoiceClassificationsClient.cs`

**Line coverage:** 27.5% | **CI run:** 41210660028

## What is untested

No test file exists for this adapter.

Two branching paths with observable side-effects on the live Flexi account:

1. **`UpdateInvoiceClassificationAsync` — conditional tag removal:**
   ```csharp
   if (result.IsSuccess)
       await _receivedInvoiceClient.RemoveTagAsync(invoiceId, [...]);
   return result.IsSuccess;
   ```
   When the template update fails, the trigger/manual-review tags must **not** be removed.  Untested means a failed update could still strip both labels, making the invoice invisible to the next classification run.

2. **`MarkInvoiceForManualReviewAsync` — partial failure:**
   ```csharp
   return resultAdd.IsSuccess && resultRemove.IsSuccess;
   ```
   If `AddTagAsync` succeeds but `RemoveTagAsync` fails, the method returns `false` but the invoice already carries the manual-review label and still has the trigger label — it will be re-processed automatically on the next hourly run, potentially double-classifying.

## Suggested tests

1. `UpdateInvoiceClassification` — success path: verify `RemoveTagAsync` is called.
2. `UpdateInvoiceClassification` — failure path (`IsSuccess = false`): verify `RemoveTagAsync` is **not** called.
3. `MarkInvoiceForManualReview` — both calls succeed: verify `true` is returned.
4. `MarkInvoiceForManualReview` — `AddTag` succeeds, `RemoveTag` fails: verify `false` is returned.
