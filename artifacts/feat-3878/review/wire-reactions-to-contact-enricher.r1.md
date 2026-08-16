# Code Review: wire-reactions-to-contact-enricher

## Summary
All 12 files listed in the task context were verified: the 6 already touched by the earlier
interrupted run match the required "Change to" blocks exactly, and the remaining 6
(`ConversationReplyReactionBase` + its 3 subclasses, `RefreshOrphanContactsHandler`,
`ConversationReactionsTests`) were completed and match the spec's implementation steps. The solution
builds with 0 errors and the reaction test suite passes in full.

## Review Result: PASS

### task: wire-reactions-to-contact-enricher
**Status:** PASS

## Docs to Update
(None — this is an internal refactor with no public API, CLI, or operational behaviour change.)

## Overall Notes
- Confirmed `ISmartsuppWebhookReaction` implementations are DI-resolved via `services.AddScoped<...>()`
  in `SmartsuppModule.cs` (constructor-injected by type, not `new`'d explicitly), so no other call sites
  needed updates beyond the 12 files and the test file.
- `ConversationReplyReactionBase.HandleAsync`'s message-only branch (no `convEl`) correctly does not
  call the enricher, matching the task context's explicit constraint.
- `RefreshOrphanContactsHandler` correctly keeps its existing `ISmartsuppApiClient` dependency (used for
  `GetConversationAsync` re-discovery) alongside the new `ISmartsuppContactEnricher`, per the task
  context's explicit instruction not to strip it.
- Build: `dotnet build Anela.Heblo.sln` — 0 errors, 253 pre-existing/unrelated warnings.
- Tests: `ConversationReactionsTests` filter — 19/19 passed (task context estimated 20; the file has and
  always had 19 `[Fact]` tests, not a defect). Full `Smartsupp` filter — 207 passed, 12 failed, all 12
  being pre-existing Testcontainers/Docker-unavailable integration test failures unrelated to this change
  (same count documented by the prior task's impl artifact).
- `SmartsuppRepository`'s own REST-fetch path is now unreachable from any of these call sites, as
  intended — cleanup is explicitly deferred to the next task (`remove-rest-dependency-from-smartsupp-repository`).

**Status:** PASS
