# Implementation: group-b-contact-upsert-with-backfill-reaction-base

Created `ContactUpsertWithBackfillReactionBase` (abstract, implements `ISmartsuppWebhookReaction`) holding the shared `HandleAsync` body (upsert contact + backfill conversation denorm fields) extracted verbatim from `ContactCreatedReaction`. `ContactCreatedReaction`, `ContactUpdatedReaction`, and `ContactAcquiredReaction` now derive from it as `sealed` classes with only a constructor forwarding to `base(repository)` and an `EventName` override. `SmartsuppModule.cs` required no changes.

## Verification
- Baseline + post-change: `dotnet test --filter "FullyQualifiedName~ContactReactionsTests"` and the combined `--filter "FullyQualifiedName~Reactions"` run (30/30 passed, covering all 3 groups + untouched conversation reactions).
- Full solution `dotnet build Anela.Heblo.sln` — 0 errors.

## Commit
`9b045ce39 refactor(smartsupp): extract ContactUpsertWithBackfillReactionBase for contact-write+backfill reactions`
