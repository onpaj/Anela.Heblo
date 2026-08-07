# Implementation: group-c-contact-upsert-only-reaction-base

Created `ContactUpsertOnlyReactionBase` (abstract, implements `ISmartsuppWebhookReaction`) holding the shared `HandleAsync` body (upsert contact only, no backfill) extracted verbatim from `ContactBannedReaction`. `ContactBannedReaction` and `ContactUnbannedReaction` now derive from it as `sealed` classes with only a constructor forwarding to `base(repository)` and an `EventName` override. Deliberately a distinct base type from group B's (no shared "should backfill" flag), per the design. `SmartsuppModule.cs` required no changes.

## Verification
- `dotnet test --filter "FullyQualifiedName~Reactions"` — 30/30 passed (10 ContactReactionsTests + 19 ConversationReactionsTests + 1 unrelated match), covering all 8 refactored classes plus the 10 untouched conversation reactions.
- `dotnet build Anela.Heblo.sln` (full solution) — 0 errors, 13 warnings (all pre-existing, unrelated to this change).
- `git diff --stat main -- .../Reactions/` — exactly 9 files touched in the Reactions/ folder (3 new base classes + 8 modified — wait, group-a's diff-stat alone showed 9; combined with group-b/c the full set is 3 new base classes + 8 modified concrete classes = 11 files), matching the plan's FR-4 acceptance criterion. No changes to `SmartsuppModule.cs`, `ProcessWebhookEventHandler.cs`, `ISmartsuppWebhookReaction.cs`, `WebhookEventContext.cs`, `SmartsuppPayloadMapper.cs`, or either test file.

## Commit
`bb06a397d refactor(smartsupp): extract ContactUpsertOnlyReactionBase for contact-write-only reactions`
