# Code Review: Remove unused methods from IClassificationHistoryRepository

## Summary
The implementation removes exactly `GetHistoryAsync` and `GetHistoryByInvoiceIdAsync` from `IClassificationHistoryRepository` and `ClassificationHistoryRepository`, matching the task spec's target content byte-for-byte. `AddAsync` and `GetPagedHistoryAsync` are untouched, whitespace is normalized to a single blank line between remaining members, and no other file in the repo references the removed methods.

## Review Result: PASS

### task: remove-unused-classification-history-methods
**Status:** PASS

## Overall Notes
- Verified `git show --stat HEAD~1` and the full diff: exactly the two expected method blocks were deleted (interface: lines 7-9 removed; implementation: `GetHistoryAsync` and `GetHistoryByInvoiceIdAsync` bodies removed), with no unrelated changes and no stray blank lines.
- Read the current contents of both files directly — they match the spec's Step 1/Step 2 target content exactly, including the retained `using Microsoft.EntityFrameworkCore;` directive (still required by `GetPagedHistoryAsync`).
- Ran `grep -rn "GetHistoryAsync\|GetHistoryByInvoiceIdAsync" backend/ --include=*.cs` independently: zero matches for `GetHistoryByInvoiceIdAsync` anywhere, and all remaining `GetHistoryAsync` matches belong to unrelated interfaces/classes in the Manufacture and Purchase/Catalog modules (`IManufactureHistoryClient`, `IPurchaseHistoryClient`, `IPurchaseOrderRepository`, `FlexiManufactureHistoryClient`, `FlexiPurchaseHistoryQueryClient`, `PurchaseOrderRepository`), none of which reference `IClassificationHistoryRepository`. This confirms both the spec's and the impl summary's claims.
- No test files (`InvoiceClassificationServiceTests.cs`, `ClassificationHistoryRepositoryTests.cs`) required changes, consistent with the spec's prediction that no mock or fake referenced the removed methods.
- This is a pure subtraction with no behavioral change to any existing consumer, aligned with the arch-review's guidance (delete immediately, no deprecation window needed, no scope creep into the unrelated naming-ambiguity issue).
