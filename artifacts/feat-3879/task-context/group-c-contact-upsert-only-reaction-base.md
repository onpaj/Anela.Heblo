### task: group-c-contact-upsert-only-reaction-base

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpsertOnlyReactionBase.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactBannedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUnbannedReaction.cs`
- Test (existing, unmodified, used for regression verification): `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ContactReactionsTests.cs`

**Interfaces:**
- Consumes: `ISmartsuppRepository.UpsertContactAsync(SmartsuppContact, CancellationToken)` (unchanged); `SmartsuppPayloadMapper.MapContact(JsonElement, DateTime)` (unchanged); `WebhookEventContext.GetContact()` (unchanged). Independent of Task B's `ContactUpsertWithBackfillReactionBase` — deliberately a distinct type per FR-3 (no shared "should backfill" flag), so this base class's `HandleAsync` has no `BackfillConversationDenormFieldsAsync` call.
- Produces: `ContactUpsertOnlyReactionBase` — `public abstract class ContactUpsertOnlyReactionBase : ISmartsuppWebhookReaction`, same shape as the other two base classes, `HandleAsync` containing only the contact-write body (no backfill).

- [ ] **Step 1: Baseline — run the existing Group C tests to confirm they pass before any change**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ContactReactionsTests"
  ```
  Expected output: all 10 cases pass (same suite as Task B's Step 1/4 — Task B's changes are already committed at this point and should still be green). Summary line e.g. `Passed! - Failed: 0, Passed: 10, Skipped: 0`. This confirms the starting point before touching the Group C files, specifically the `contact.banned` and `contact.unbanned` `InlineData` cases of both theories.

- [ ] **Step 2: Create the shared base class**

  Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpsertOnlyReactionBase.cs` with exactly this content (body extracted verbatim from `ContactBannedReaction.HandleAsync`, `.cs:14-19`):

  ```csharp
  using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public abstract class ContactUpsertOnlyReactionBase : ISmartsuppWebhookReaction
  {
      protected readonly ISmartsuppRepository Repository;

      protected ContactUpsertOnlyReactionBase(ISmartsuppRepository repository) => Repository = repository;

      public abstract string EventName { get; }

      public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
      {
          var contactEl = ctx.GetContact();
          if (contactEl is null) return;
          await Repository.UpsertContactAsync(SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp), cancellationToken);
      }
  }
  ```

  Then replace the full contents of the two concrete files:

  `ContactBannedReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ContactBannedReaction : ContactUpsertOnlyReactionBase
  {
      public ContactBannedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "contact.banned";
  }
  ```

  `ContactUnbannedReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ContactUnbannedReaction : ContactUpsertOnlyReactionBase
  {
      public ContactUnbannedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "contact.unbanned";
  }
  ```

  Do not touch `SmartsuppModule.cs` (`.cs:67-68` already register these two concrete types by name).

- [ ] **Step 3: Build**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```
  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Re-run the existing Group C tests to confirm they still pass unmodified**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ContactReactionsTests"
  ```
  Expected output: identical pass count to Step 1 (`Passed! - Failed: 0, Passed: 10, Skipped: 0`), zero changes to `ContactReactionsTests.cs`. The `contact.banned`/`contact.unbanned` cases of both theories must be green, confirming `UpsertContactAsync` is still called and `BackfillConversationDenormFieldsAsync` is still **not** called for these two events (the mock is never set up to expect it, so this is implicitly verified by the test passing without additional stubbing errors).

- [ ] **Step 5: Full regression pass across both affected test files, then commit**

  This is the last of the three tasks, so also run the full Smartsupp reaction suite together as a final end-to-end confirmation of FR-5/FR-6 (all 8 affected classes across all 3 groups, plus the 10 untouched reactions in `ConversationReactionsTests.cs` that must remain unaffected):

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Reactions"
  dotnet build backend/Anela.Heblo.sln 2>/dev/null || dotnet build Anela.Heblo.sln
  git diff --stat main -- backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/
  ```
  Expected:
  - `dotnet test --filter "FullyQualifiedName~Reactions"` reports 0 failures across both `ContactReactionsTests` and `ConversationReactionsTests` (29 tests: 10 + 19).
  - `dotnet build` on the full solution succeeds with 0 errors (confirms `SmartsuppModule.cs` and every other consumer still compile against the now-`sealed`-subclassing concrete types).
  - `git diff --stat` against `main` shows exactly 11 changed files under the `Reactions/` folder: 3 new base classes + 8 modified concrete classes, and no changes to `SmartsuppModule.cs`, `ProcessWebhookEventHandler.cs`, `ISmartsuppWebhookReaction.cs`, `WebhookEventContext.cs`, `SmartsuppPayloadMapper.cs`, `ContactReactionsTests.cs`, or `ConversationReactionsTests.cs` — this directly checks FR-4's acceptance criterion.

  Then commit:
  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpsertOnlyReactionBase.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactBannedReaction.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUnbannedReaction.cs
  git commit -m "refactor(smartsupp): extract ContactUpsertOnlyReactionBase for contact-write-only reactions"
  ```

---

## Self-review notes

- **Spec coverage:** FR-1 → task `group-a-conversation-reply-reaction-base`. FR-2 → task `group-b-contact-upsert-with-backfill-reaction-base`. FR-3 → task `group-c-contact-upsert-only-reaction-base` (distinct type from Group B's base, verified by them being separate files/classes with no shared parent beyond `ISmartsuppWebhookReaction`). FR-4 (no unrelated file changes) → checked mechanically in task C's Step 5 via `git diff --stat`. FR-5 (existing tests pass unmodified) → verified in every task's Step 1 (baseline) and Step 4 (post-change), and again in task C's Step 5 full-suite run; no task edits `ContactReactionsTests.cs` or `ConversationReactionsTests.cs`. FR-6 (behavioural equivalence incl. `GetType().Name` still returning the concrete subclass name) → satisfied structurally because the 8 concrete classes stay `sealed` with their original names and no `HandleAsync` override — `ProcessWebhookEventHandler.cs:63`'s `reaction.GetType().Name` call is unaffected by inheritance since `GetType()` always returns the runtime (most-derived) type. NFR-1/NFR-2/NFR-3 are satisfied by construction (single-file edit points for future changes, zero new logic, zero security-relevant surface). "Base class accessibility: `public`" (arch-review Decision 1) is reflected in every base class's code block above (`public abstract class`, not `internal`).
- **Placeholder scan:** every step in every task has an actual, complete command or code block — no "TBD", no "similar to Task N" cross-references without inlined code (each task repeats its own base-class body and concrete-class bodies in full rather than pointing at Task A).
- **Type/signature consistency:** all three base classes share the identical shape (`protected readonly ISmartsuppRepository Repository`, `protected` single-arg constructor, `public abstract string EventName { get; }`, `public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)`); all 8 concrete classes share the identical shape (`public sealed class X : YBase { public X(ISmartsuppRepository repository) : base(repository) { } public override string EventName => "..."; }`) with only the base-class name and `EventName` literal varying, matching the design doc's snippets verbatim.
