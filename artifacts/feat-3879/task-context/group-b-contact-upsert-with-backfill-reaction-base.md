### task: group-b-contact-upsert-with-backfill-reaction-base

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpsertWithBackfillReactionBase.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactCreatedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpdatedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactAcquiredReaction.cs`
- Test (existing, unmodified, used for regression verification): `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ContactReactionsTests.cs`

**Interfaces:**
- Consumes: `ISmartsuppRepository.UpsertContactAsync(SmartsuppContact, CancellationToken)` and `ISmartsuppRepository.BackfillConversationDenormFieldsAsync(SmartsuppContact, CancellationToken)` (unchanged); `SmartsuppPayloadMapper.MapContact(JsonElement, DateTime)` (unchanged); `WebhookEventContext.GetContact()` (unchanged). Independent of Task A's `ConversationReplyReactionBase` — no shared code between the two base classes.
- Produces: `ContactUpsertWithBackfillReactionBase` — `public abstract class ContactUpsertWithBackfillReactionBase : ISmartsuppWebhookReaction` with the same shape as `ConversationReplyReactionBase` (`protected readonly ISmartsuppRepository Repository`, `protected` ctor, `public abstract string EventName { get; }`), and `HandleAsync` containing the contact-write-with-backfill body. Not consumed by Task C (Task C's base class is a separate, distinct type per FR-3 — no shared "should backfill" flag).

- [ ] **Step 1: Baseline — run the existing Group B tests to confirm they pass before any change**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ContactReactionsTests"
  ```
  Expected output: all tests in `ContactReactionsTests` pass — the two `[Theory]` methods (`AllContactReactions_UpsertContact`, `AllContactReactions_HaveCorrectEventName`) each run 5 `[InlineData]` cases covering `contact.created`, `contact.updated`, `contact.acquired`, `contact.banned`, `contact.unbanned` — 10 total. Summary line reads e.g. `Passed! - Failed: 0, Passed: 10, Skipped: 0`. Record this as the baseline before editing.

- [ ] **Step 2: Create the shared base class**

  Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpsertWithBackfillReactionBase.cs` with exactly this content (body extracted verbatim from `ContactCreatedReaction.HandleAsync`, `.cs:14-21`):

  ```csharp
  using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public abstract class ContactUpsertWithBackfillReactionBase : ISmartsuppWebhookReaction
  {
      protected readonly ISmartsuppRepository Repository;

      protected ContactUpsertWithBackfillReactionBase(ISmartsuppRepository repository) => Repository = repository;

      public abstract string EventName { get; }

      public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
      {
          var contactEl = ctx.GetContact();
          if (contactEl is null) return;
          var contact = SmartsuppPayloadMapper.MapContact(contactEl.Value, ctx.Timestamp);
          await Repository.UpsertContactAsync(contact, cancellationToken);
          await Repository.BackfillConversationDenormFieldsAsync(contact, cancellationToken);
      }
  }
  ```

  Then replace the full contents of the three concrete files:

  `ContactCreatedReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ContactCreatedReaction : ContactUpsertWithBackfillReactionBase
  {
      public ContactCreatedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "contact.created";
  }
  ```

  `ContactUpdatedReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ContactUpdatedReaction : ContactUpsertWithBackfillReactionBase
  {
      public ContactUpdatedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "contact.updated";
  }
  ```

  `ContactAcquiredReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ContactAcquiredReaction : ContactUpsertWithBackfillReactionBase
  {
      public ContactAcquiredReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "contact.acquired";
  }
  ```

  Do not touch `SmartsuppModule.cs` (`.cs:64-66` already register these three concrete types by name).

- [ ] **Step 3: Build**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```
  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Re-run the existing Group B tests to confirm they still pass unmodified**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ContactReactionsTests"
  ```
  Expected output: identical pass count to Step 1 (`Passed! - Failed: 0, Passed: 10, Skipped: 0`), zero changes to `ContactReactionsTests.cs`. The `contact.created`/`contact.updated`/`contact.acquired` `InlineData` cases of both theories must be green, confirming `UpsertContactAsync` is still called with the mapped contact and `EventName` still returns the correct literal for each concrete subclass.

- [ ] **Step 5: Commit**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpsertWithBackfillReactionBase.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactCreatedReaction.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactUpdatedReaction.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ContactAcquiredReaction.cs
  git commit -m "refactor(smartsupp): extract ContactUpsertWithBackfillReactionBase for contact-write+backfill reactions"
  ```

---

