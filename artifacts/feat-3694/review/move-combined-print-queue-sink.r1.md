# Code Review: move-combined-print-queue-sink

## Summary
This is a pure relocation of `CombinedPrintQueueSink` from the API host project into `Anela.Heblo.Adapters.Azure`, matching the placement of sibling `IPrintQueueSink` implementations. The commit diff (1911cc1) matches the spec byte-for-byte: the file was git-renamed with only the namespace and access modifier (`internal sealed` → `public sealed`) changed, the call site and both test files were updated exactly as prescribed, and no unrelated lines were touched.

## Review Result: PASS

### task: move-combined-print-queue-sink
**Status:** PASS

## Docs to Update
None — this is an internal relocation with no external-facing or documented behavior change.

## Overall Notes
Verification performed independently against the actual commit and working tree (not just the developer's report):
- `git show 1911cc1` confirms a clean rename (similarity 82%) of the source file into `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/ExpeditionList/CombinedPrintQueueSink.cs`, with only the namespace and `internal` → `public` modifier changed; constructor, fields, and `SendAsync` body are byte-for-byte identical to the original, as required.
- `ServiceCollectionExtensions.cs` diff is exactly the two lines specified: the dead `using Anela.Heblo.API.Features.ExpeditionList;` removed, and the `"Combined"` case now constructs `CombinedPrintQueueSink` via the pre-existing `Adapters.Azure` using directive. No other line changed.
- Both test files changed only their `using` directive, no test logic touched.
- No `.csproj` changes were made or needed.
- Confirmed `backend/src/Anela.Heblo.API/Features/ExpeditionList/` no longer exists (old directory removed).
- Confirmed `grep -rn "Anela.Heblo.API.Features.ExpeditionList" backend/` returns zero hits.
- Confirmed `grep -rln "CombinedPrintQueueSink" backend/` only matches the new adapter source file, the updated call site, the two updated test files, and build-output artifacts (bin/obj) — no stray source references.
- Ran `dotnet build Anela.Heblo.sln` myself: succeeds with 0 errors (251 pre-existing nullable-reference warnings across unrelated test files, none referencing the moved class or its call site).
- Ran `dotnet test --filter "FullyQualifiedName~CombinedPrintQueueSink"` myself: 9/9 passed, matching the developer's reported count.
- All Definition of Done items are satisfied. This meets the bar for a pure, surgical relocation with no behavior change.
