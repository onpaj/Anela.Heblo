# Implementation: group-a-conversation-reply-reaction-base

Created `ConversationReplyReactionBase` (abstract, implements `ISmartsuppWebhookReaction`) holding the shared `HandleAsync` body extracted verbatim from `ConversationAgentRepliedReaction`. `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`, and `ConversationContactRepliedReaction` now derive from it as `sealed` classes with only a constructor forwarding to `base(repository)` and an `EventName` override. `SmartsuppModule.cs` required no changes (concrete types keep their names/signatures).

## Verification
- Baseline: `dotnet test --filter "FullyQualifiedName~ConversationReactionsTests"` — 19/19 passed before change.
- `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — 0 errors.
- Post-change: same 19/19 passed, zero changes to the test file.

## Commit
`6d5e5d5d6 refactor(smartsupp): extract ConversationReplyReactionBase for reply reactions`
