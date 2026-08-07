# Design: Collapse duplicate Smartsupp webhook reaction implementations into shared base classes

## Component Design

All files live in the existing folder:
`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/`

Namespace for everything below: `Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions`.

No changes to `ISmartsuppWebhookReaction` (`EventName { get; }`, `Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)`), `WebhookEventContext`, `SmartsuppPayloadMapper`, `ProcessWebhookEventHandler`, or `SmartsuppModule.cs`. Per the arch-review's amendment (Decision 1), all three base classes are `public abstract class`, matching the `DailyInvoiceImportJobBase` / `BankImportJobBase` precedent — not `internal` as originally drafted in the spec.

### 1. `ConversationReplyReactionBase` — new file `ConversationReplyReactionBase.cs`

Extracted verbatim from `ConversationAgentRepliedReaction.HandleAsync` (current body, unchanged).

```csharp
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

**Subclasses** (existing files, bodies replaced with constructor + `EventName` override only; class name, namespace, accessibility (`public sealed class`), and constructor signature `(ISmartsuppRepository repository)` unchanged):

| File | Class | `EventName` override |
|---|---|---|
| `ConversationAgentRepliedReaction.cs` | `ConversationAgentRepliedReaction` | `"conversation.agent_replied"` |
| `ConversationBotRepliedReaction.cs` | `ConversationBotRepliedReaction` | `"conversation.bot_replied"` |
| `ConversationContactRepliedReaction.cs` | `ConversationContactRepliedReaction` | `"conversation.contact_replied"` |

Example (identical shape for the other two):

```csharp
public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
{
    public ConversationAgentRepliedReaction(ISmartsuppRepository repository) : base(repository) { }
    public override string EventName => "conversation.agent_replied";
}
```

### 2. `ContactUpsertWithBackfillReactionBase` — new file `ContactUpsertWithBackfillReactionBase.cs`

Extracted verbatim from `ContactCreatedReaction.HandleAsync` (current body, unchanged).

```csharp
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

**Subclasses** (existing files, same rules as above):

| File | Class | `EventName` override |
|---|---|---|
| `ContactCreatedReaction.cs` | `ContactCreatedReaction` | `"contact.created"` |
| `ContactUpdatedReaction.cs` | `ContactUpdatedReaction` | `"contact.updated"` |
| `ContactAcquiredReaction.cs` | `ContactAcquiredReaction` | `"contact.acquired"` |

### 3. `ContactUpsertOnlyReactionBase` — new file `ContactUpsertOnlyReactionBase.cs`

Extracted verbatim from `ContactBannedReaction.HandleAsync` (current body, unchanged). Deliberately a distinct type from `ContactUpsertWithBackfillReactionBase` (no shared "should backfill" flag) — keeps each base class's `HandleAsync` branch-free, per FR-3.

```csharp
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

**Subclasses** (existing files, same rules as above):

| File | Class | `EventName` override |
|---|---|---|
| `ContactBannedReaction.cs` | `ContactBannedReaction` | `"contact.banned"` |
| `ContactUnbannedReaction.cs` | `ContactUnbannedReaction` | `"contact.unbanned"` |

### Unaffected components

`ProcessWebhookEventHandler` (resolves `IEnumerable<ISmartsuppWebhookReaction>`, dictionary-keys by `EventName`, logs `reaction.GetType().Name` on failure — still returns the concrete subclass name, e.g. `ConversationAgentRepliedReaction`, not the base class name, satisfying FR-6), `SmartsuppModule.cs` (all eighteen `AddScoped<ISmartsuppWebhookReaction, T>()` registrations unchanged — each still names a concrete `sealed` type), `ConversationClosedReaction`, `ConversationClosedByContactReaction`, and the other ten reaction classes are all left untouched.

### File change summary (11 files touched, 3 new)

- New: `ConversationReplyReactionBase.cs`, `ContactUpsertWithBackfillReactionBase.cs`, `ContactUpsertOnlyReactionBase.cs`
- Modified (body reduced to constructor + `EventName` override): `ConversationAgentRepliedReaction.cs`, `ConversationBotRepliedReaction.cs`, `ConversationContactRepliedReaction.cs`, `ContactCreatedReaction.cs`, `ContactUpdatedReaction.cs`, `ContactAcquiredReaction.cs`, `ContactBannedReaction.cs`, `ContactUnbannedReaction.cs`
- Untouched: `SmartsuppModule.cs`, `ProcessWebhookEventHandler.cs`, `ISmartsuppWebhookReaction.cs`, `WebhookEventContext.cs`, `SmartsuppPayloadMapper.cs`, `ContactReactionsTests.cs`, `ConversationReactionsTests.cs`, and all ten non-duplicated reaction classes.

## Data Schemas

No data schema changes. This is a pure code-structure refactor: no database schema, API request/response shape, or event payload is added, removed, or altered. `SmartsuppConversation`, `SmartsuppMessage`, `SmartsuppContact` and their persistence via `ISmartsuppRepository` remain exactly as they are today — the new base classes call the same repository methods, with the same arguments, in the same order, as the concrete classes they replace.
