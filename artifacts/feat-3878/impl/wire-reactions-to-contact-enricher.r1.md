# Implementation: wire-reactions-to-contact-enricher

## What was implemented

Satisfied FR-4 from `spec.r1.md`: every call site that relied on `SmartsuppRepository.UpsertConversationAsync`'s
implicit contact enrichment now calls `ISmartsuppContactEnricher.EnrichContactAsync` explicitly before
upserting. This picked up mid-task: 6 of the 12 target files (`ConversationOpenedReaction`,
`ConversationRatedReaction`, `ConversationClosedReaction`, `ConversationClosedByContactReaction`,
`ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`) already had the correct
change applied by an earlier, interrupted run of this task — each was verified line-for-line against
`task-context/wire-reactions-to-contact-enricher.md` Steps 1-6 and found to match exactly, so they were
left untouched. The remaining 6 files (`ConversationReplyReactionBase` + its 3 sealed subclasses,
`RefreshOrphanContactsHandler`, `ConversationReactionsTests`) were completed in this pass per Steps 7-11.

After this task, `SmartsuppRepository`'s own REST-fetch path is dead code — no caller reaches the
"contact not found locally" branch through a path that needed it. Removing that dead code is Task 3
(`remove-rest-dependency-from-smartsupp-repository`), out of scope here.

## Files created/modified

- `.../Reactions/ConversationOpenedReaction.cs` — pre-existing (verified correct, unchanged this pass)
- `.../Reactions/ConversationRatedReaction.cs` — pre-existing (verified correct, unchanged this pass)
- `.../Reactions/ConversationClosedReaction.cs` — pre-existing (verified correct, unchanged this pass)
- `.../Reactions/ConversationClosedByContactReaction.cs` — pre-existing (verified correct, unchanged this pass)
- `.../Reactions/ConversationAgentAssignedReaction.cs` — pre-existing (verified correct, unchanged this pass)
- `.../Reactions/ConversationAgentUnassignedReaction.cs` — pre-existing (verified correct, unchanged this pass)
- `.../Reactions/ConversationReplyReactionBase.cs` — added `ISmartsuppContactEnricher` constructor
  dependency; the conversation-upsert branch of `HandleAsync` now maps the conversation, enriches it,
  then upserts. The message-only branch is untouched (it never touches `ContactId`).
- `.../Reactions/ConversationContactRepliedReaction.cs` — pass-through constructor updated to take and
  forward `ISmartsuppContactEnricher`.
- `.../Reactions/ConversationAgentRepliedReaction.cs` — same pass-through update.
- `.../Reactions/ConversationBotRepliedReaction.cs` — same pass-through update.
- `.../UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs` — added `ISmartsuppContactEnricher`
  as a fourth constructor dependency (kept `ISmartsuppApiClient` and `ISmartsuppRepository` as-is, per
  the task context — only `SmartsuppRepository` itself loses the REST dependency, not this handler). The
  try-block now calls `EnrichContactAsync` on the re-attached `local` conversation before upserting.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs` — added a
  shared `Mock<ISmartsuppContactEnricher> _enricher` field defaulted (via constructor) to a pass-through
  `EnrichContactAsync`, and updated the 9 affected reaction constructor calls
  (`ConversationOpenedReaction`, `ConversationClosedReaction`, `ConversationClosedByContactReaction`,
  `ConversationContactRepliedReaction`, `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`,
  `ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`, `ConversationRatedReaction`)
  to pass `_enricher.Object`. `ConversationAgentJoinedReaction`, `ConversationAgentLeftReaction`,
  `ConversationMessageDeliveredReaction`, `ConversationMessageDeliveryFailedReaction` were left untouched
  as instructed (different dependencies / don't call `UpsertConversationAsync`).

## Tests

No new test files. `ConversationReactionsTests.cs` (19 `[Fact]` tests — the task context's step 12
estimated 20, but the file has and always had 19; all pass) now exercises every reaction through the
enricher mock instead of the real repository's implicit REST fetch.

## How to verify

```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~ConversationReactionsTests"
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --no-build --filter "FullyQualifiedName~Smartsupp"
```

Results:
- `dotnet build Anela.Heblo.sln`: **Build succeeded, 0 Error(s)** (253 pre-existing, unrelated nullable/
  obsolete-API warnings across the test project; none touch Smartsupp files changed by this task).
- `ConversationReactionsTests` filter: **Passed! - Failed: 0, Passed: 19, Total: 19**.
- Full `Smartsupp` filter: **207 Passed, 12 Failed, Total 219**. All 12 failures are the pre-existing
  `SmartsuppRepositoryUpsertIntegrationTests` / `SmartsuppPresenceRepositoryIntegrationTests` cases that
  need a Postgres Testcontainers daemon (`System.ArgumentException: Docker is either not running or
  misconfigured`) — same count/cause as recorded in `impl/add-smartsupp-contact-enricher.r1.md` for the
  prior task; not a regression from this change, and none of the 12 touch any file this task modified.

Used `dotnet build Anela.Heblo.sln` (not the `backend/` cwd from the task context — no `.sln` exists
directly under `backend/`, only at the repo root) followed by `dotnet test --no-build`, matching the
Docker-daemon/AccessMatrixGen workaround already documented by the prior task's impl artifact.

## Notes

- Verified the 6 already-modified files from the interrupted prior run byte-for-byte against the task
  context's Step 1-6 "Change to" blocks via `git diff` before treating them as done — no corrections were
  needed.
- No other call sites reference the 9 reaction constructors or `RefreshOrphanContactsHandler`'s
  constructor outside `SmartsuppModule`'s DI registration (which resolves by interface/type, not by
  explicit `new`, so it needed no changes) and the updated test file.

## PR Summary
Wires the `ISmartsuppContactEnricher` added in the previous task into every Smartsupp webhook reaction
and into `RefreshOrphanContactsHandler`, so contact enrichment now happens explicitly at each call site
instead of implicitly inside `SmartsuppRepository.UpsertConversationAsync`. This is step 2 of 3 for
issue #3878: after this change, `SmartsuppRepository`'s own REST-fetch path is unreachable dead code,
which the next task deletes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/*.cs` — 9 reaction classes + the shared `ConversationReplyReactionBase` now take `ISmartsuppContactEnricher` and call `EnrichContactAsync` before `UpsertConversationAsync`
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs` — added `ISmartsuppContactEnricher` dependency, calls it before re-upserting the backfilled conversation
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs` — added shared enricher mock, updated 9 reaction constructor call sites

## Status
DONE
