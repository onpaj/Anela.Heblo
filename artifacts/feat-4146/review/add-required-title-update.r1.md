# Code Review: add-required-title-update

## Summary
The implementation adds `[Required]` to `Title` on `UpdateJournalEntryRequest` exactly as specified — matching the ordering and pattern already used on `Content`, and consistent with the sibling `add-required-title-create` change on `CreateJournalEntryRequest`. No other property, constructor, or class was touched. The existing `UpdateJournalEntryHandlerTests` (8 tests) pass unchanged before and after.

## Review Result: PASS

### task: add-required-title-update
**Status:** PASS

## Docs to Update
(none — this is a data-annotation-only fix with no change to public behaviour, CLI, or docs)

## Overall Notes
Same sandbox-specific `dotnet test` Debug-configuration hang noted on `add-required-title-create` applies here too; verified with `-c Release`, unrelated to this change. The `verify-build-and-contract-regen` task should be aware it may need the same workaround.
