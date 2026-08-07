### task: group-a-conversation-reply-reaction-base

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationReplyReactionBase.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationBotRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationContactRepliedReaction.cs`
- Test (existing, unmodified, used for regression verification): `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`

**Interfaces:**
- Consumes: `ISmartsuppRepository` (`backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`, unchanged) — specifically `UpsertConversationAsync(SmartsuppConversation, CancellationToken)` and `UpsertMessagesAsync(string conversationId, List<SmartsuppMessage>, CancellationToken)`; `SmartsuppPayloadMapper.MapConversation(JsonElement, DateTime)` and `SmartsuppPayloadMapper.MapMessage(JsonElement)` (`Mappers/SmartsuppPayloadMapper.cs`, unchanged); `WebhookEventContext.GetConversation()` / `GetMessage()` (unchanged).
- Produces: `ConversationReplyReactionBase` — `public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction` with `protected readonly ISmartsuppRepository Repository`, `protected ConversationReplyReactionBase(ISmartsuppRepository repository)`, `public abstract string EventName { get; }`, and `public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)`. Task B and C do **not** depend on this type (they get their own base classes), but this establishes the pattern they both follow.

- [ ] **Step 1: Baseline — run the existing Group A tests to confirm they pass before any change**

  Run:
  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConversationReactionsTests"
  ```
  Expected output: all tests in `ConversationReactionsTests` pass, including (relevant to this task) `ConversationContactRepliedReaction_UpsertsConversationAndMessage`, `ConversationAgentRepliedReaction_UpsertsConversationAndMessage`, `ConversationBotRepliedReaction_UpsertsConversationAndMessage`. Summary line reads something like `Passed! - Failed: 0, Passed: 19, Skipped: 0`. This is the baseline — record that it's green before editing any file.

- [ ] **Step 2: Create the shared base class**

  Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationReplyReactionBase.cs` with exactly this content (body extracted verbatim from `ConversationAgentRepliedReaction.HandleAsync`, `.cs:14-27`):

  ```csharp
  using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
  {
      protected readonly ISmartsuppRepository Repository;

      protected ConversationReplyReactionBase(ISmartsuppRepository repository) => Repository = repository;

      public abstract string EventName { get; }

      public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
      {
          var convEl = ctx.GetConversation();
          if (convEl.HasValue)
              await Repository.UpsertConversationAsync(
                  SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp), cancellationToken);

          var msgEl = ctx.GetMessage();
          if (msgEl.HasValue)
          {
              var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
              await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
          }
      }
  }
  ```

  Then replace the full contents of the three concrete files as follows.

  `ConversationAgentRepliedReaction.cs`:
  ```csharp
  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
  {
      public ConversationAgentRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "conversation.agent_replied";
  }
  ```
  Note: `ISmartsuppRepository` is in `Anela.Heblo.Domain.Features.Smartsupp`, so this file needs `using Anela.Heblo.Domain.Features.Smartsupp;` for the constructor parameter type. Full file:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
  {
      public ConversationAgentRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "conversation.agent_replied";
  }
  ```

  `ConversationBotRepliedReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ConversationBotRepliedReaction : ConversationReplyReactionBase
  {
      public ConversationBotRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "conversation.bot_replied";
  }
  ```

  `ConversationContactRepliedReaction.cs`:
  ```csharp
  using Anela.Heblo.Domain.Features.Smartsupp;

  namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

  public sealed class ConversationContactRepliedReaction : ConversationReplyReactionBase
  {
      public ConversationContactRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

      public override string EventName => "conversation.contact_replied";
  }
  ```

  Do not touch `SmartsuppModule.cs` — it registers these three concrete types by name already (`SmartsuppModule.cs:54-56`) and needs no change since the class names and constructor signatures are unchanged.

- [ ] **Step 3: Build**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
  ```
  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Re-run the existing Group A tests to confirm they still pass unmodified**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConversationReactionsTests"
  ```
  Expected output: identical pass count to Step 1 (e.g. `Passed! - Failed: 0, Passed: 19, Skipped: 0`), with zero changes to `ConversationReactionsTests.cs`. In particular `ConversationAgentRepliedReaction_UpsertsConversationAndMessage`, `ConversationBotRepliedReaction_UpsertsConversationAndMessage`, and `ConversationContactRepliedReaction_UpsertsConversationAndMessage` must be green — these exercise `new ConversationAgentRepliedReaction(_repo.Object)` etc. directly and assert `UpsertConversationAsync`/`UpsertMessagesAsync` calls, proving the base class's `HandleAsync` is byte-for-byte equivalent to the old duplicated bodies.

- [ ] **Step 5: Commit**

  ```bash
  cd /Users/rem/orca/workspaces/Anela.Heblo/worktrees/feature-3879-Arch-Review-Smartsupp-8-Of-18-Webhook-Reaction-Cla
  git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationReplyReactionBase.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentRepliedReaction.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationBotRepliedReaction.cs \
          backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationContactRepliedReaction.cs
  git commit -m "refactor(smartsupp): extract ConversationReplyReactionBase for reply reactions"
  ```

---

