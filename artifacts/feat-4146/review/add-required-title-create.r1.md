# Code Review: add-required-title-create

## Summary
The implementation adds `[Required]` to `Title` on `CreateJournalEntryRequest` exactly as specified — matching the ordering and pattern already used on `Content`. No other property, constructor, or class was touched. The existing `CreateJournalEntryHandlerTests` (7 tests) pass unchanged before and after.

## Review Result: PASS

### task: add-required-title-create
**Status:** PASS

## Docs to Update
(none — this is a data-annotation-only fix with no change to public behaviour, CLI, or docs)

## Overall Notes
The developer flagged an environment-specific hang in the sandbox when running `dotnet test` in the default Debug configuration, caused by a pre-existing Debug-only MSBuild target (`GenerateAccessMatrix` on `Anela.Heblo.API.csproj`) that shells out via `Exec` to a nested `dotnet run`. This is unrelated to the code change (confirmed by running the generator tool standalone, which completes cleanly in ~8s) and was worked around for local verification with `-c Release`. Noting this for the `verify-build-and-contract-regen` task, which should be aware it may hit the same hang when running `dotnet build`/`dotnet test` without a configuration override, and may need the same workaround or a longer timeout.
