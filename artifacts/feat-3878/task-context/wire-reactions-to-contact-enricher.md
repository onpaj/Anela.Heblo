### task: wire-reactions-to-contact-enricher

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationOpenedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationRatedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationClosedByContactReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentAssignedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentUnassignedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationReplyReactionBase.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationContactRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationAgentRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationBotRepliedReaction.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`

#### Goal

Satisfy FR-4 from `spec.r1.md`: every call site that relies on `UpsertConversationAsync`'s implicit
contact enrichment now calls `ISmartsuppContactEnricher.EnrichContactAsync` explicitly first. After
this task, `SmartsuppRepository`'s own REST-fetch path is dead code (still present, but no caller
reaches the "contact not found locally" branch through a path that needed it) — Task 3 deletes it.

#### Context you need before touching code

- **`ConversationReplyReactionBase` is the base class for 3 sealed subclasses** — `ConversationContactRepliedReaction`,
  `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction` — each with a single
  pass-through constructor (`: base(repository) { }`). Adding a constructor parameter to the base
  requires updating all 3 subclass constructors too, or the solution won't compile.
- **Only the conversation-upsert branch of `ConversationReplyReactionBase.HandleAsync` needs
  enrichment** — the message-only branch (when only `msgEl` is present, no `convEl`) must not call
  the enricher; that branch never touches `ContactId`.
- **`RefreshOrphanContactsHandler` already injects `ISmartsuppApiClient` and `ISmartsuppRepository`
  directly** — do not remove those; it still needs `ISmartsuppApiClient` for its own
  `GetConversationAsync` re-discovery call (spec is explicit: only `SmartsuppRepository` loses the
  dependency, not every Smartsupp Application-layer class). Add `ISmartsuppContactEnricher` as a
  fourth constructor dependency.
- **`ConversationReactionsTests.cs` has a single shared `Mock<ISmartsuppRepository> _repo` field**
  used by all 20 tests. Add a sibling `Mock<ISmartsuppContactEnricher> _enricher` field and update
  every affected reaction constructor call. Default the mock's `EnrichContactAsync` to return the
  input conversation unchanged (pass-through), so existing assertions on `UpsertConversationAsync`'s
  argument continue to hold without every test needing its own enricher setup.

#### Implementation steps

- [ ] **Step 1: `ConversationOpenedReaction`**

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ConversationOpenedReaction.cs`
currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationOpenedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationOpenedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.opened";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationOpenedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationOpenedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.opened";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 2: `ConversationRatedReaction`**

`.../ConversationRatedReaction.cs` currently reads:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationRatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationRatedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.rated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);

        if (ctx.Data.TryGetProperty("rating_value", out var rv) && rv.ValueKind == JsonValueKind.Number)
            conversation.Rating = rv.GetInt32();

        conversation.RatingText = SmartsuppPayloadMapper.TryGetString(ctx.Data, "rating_text");
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationRatedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationRatedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.rated";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);

        if (ctx.Data.TryGetProperty("rating_value", out var rv) && rv.ValueKind == JsonValueKind.Number)
            conversation.Rating = rv.GetInt32();

        conversation.RatingText = SmartsuppPayloadMapper.TryGetString(ctx.Data, "rating_text");
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 3: `ConversationClosedReaction`**

`.../ConversationClosedReaction.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationClosedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.closed";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = SmartsuppPayloadMapper.TryGetString(ctx.Data, "close_type");
        conversation.ClosedByAgentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationClosedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.closed";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = SmartsuppPayloadMapper.TryGetString(ctx.Data, "close_type");
        conversation.ClosedByAgentId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "agent_id");
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 4: `ConversationClosedByContactReaction`**

`.../ConversationClosedByContactReaction.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedByContactReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationClosedByContactReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.closed_by_contact";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = "contact";
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationClosedByContactReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationClosedByContactReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.closed_by_contact";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation() ?? ctx.Data;
        var conversation = SmartsuppPayloadMapper.MapConversation(convEl, ctx.Timestamp);
        conversation.CloseType = "contact";
        conversation.LastClosedAt = SmartsuppPayloadMapper.AsUtc(ctx.Timestamp);
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 5: `ConversationAgentAssignedReaction`**

`.../ConversationAgentAssignedReaction.cs` currently reads:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationAgentAssignedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentAssignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationAgentAssignedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.agent_assigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        var assignedId = SmartsuppPayloadMapper.TryGetString(ctx.Data, "assigned");
        if (assignedId is not null)
            conversation.AssignedAgentIdsJson = JsonSerializer.Serialize(new[] { assignedId });

        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 6: `ConversationAgentUnassignedReaction`**

`.../ConversationAgentUnassignedReaction.cs` currently reads:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentUnassignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;

    public ConversationAgentUnassignedReaction(ISmartsuppRepository repository) => _repository = repository;

    public string EventName => "conversation.agent_unassigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        conversation.AssignedAgentIdsJson = null;
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentUnassignedReaction : ISmartsuppWebhookReaction
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    public ConversationAgentUnassignedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        _repository = repository;
        _contactEnricher = contactEnricher;
    }

    public string EventName => "conversation.agent_unassigned";

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl is null) return;

        var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
        conversation.AssignedAgentIdsJson = null;
        conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
        await _repository.UpsertConversationAsync(conversation, cancellationToken);
    }
}
```

- [ ] **Step 7: `ConversationReplyReactionBase`**

`.../ConversationReplyReactionBase.cs` currently reads:

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

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Mappers;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;
    private readonly ISmartsuppContactEnricher _contactEnricher;

    protected ConversationReplyReactionBase(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
    {
        Repository = repository;
        _contactEnricher = contactEnricher;
    }

    public abstract string EventName { get; }

    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        var convEl = ctx.GetConversation();
        if (convEl.HasValue)
        {
            var conversation = SmartsuppPayloadMapper.MapConversation(convEl.Value, ctx.Timestamp);
            conversation = await _contactEnricher.EnrichContactAsync(conversation, cancellationToken);
            await Repository.UpsertConversationAsync(conversation, cancellationToken);
        }

        var msgEl = ctx.GetMessage();
        if (msgEl.HasValue)
        {
            var msg = SmartsuppPayloadMapper.MapMessage(msgEl.Value);
            await Repository.UpsertMessagesAsync(msg.ConversationId, new List<SmartsuppMessage> { msg }, cancellationToken);
        }
    }
}
```

- [ ] **Step 8: Update the 3 `ConversationReplyReactionBase` subclasses**

`.../ConversationContactRepliedReaction.cs` currently reads:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationContactRepliedReaction : ConversationReplyReactionBase
{
    public ConversationContactRepliedReaction(ISmartsuppRepository repository) : base(repository) { }

    public override string EventName => "conversation.contact_replied";
}
```

Change to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationContactRepliedReaction : ConversationReplyReactionBase
{
    public ConversationContactRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.contact_replied";
}
```

Apply the identical pattern to `.../ConversationAgentRepliedReaction.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
{
    public ConversationAgentRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.agent_replied";
}
```

And to `.../ConversationBotRepliedReaction.cs`:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public sealed class ConversationBotRepliedReaction : ConversationReplyReactionBase
{
    public ConversationBotRepliedReaction(ISmartsuppRepository repository, ISmartsuppContactEnricher contactEnricher)
        : base(repository, contactEnricher) { }

    public override string EventName => "conversation.bot_replied";
}
```

- [ ] **Step 9: `RefreshOrphanContactsHandler`**

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
currently reads:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

public class RefreshOrphanContactsHandler
    : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RefreshOrphanContactsHandler> _logger;

    public RefreshOrphanContactsHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ApplicationDbContext db,
        ILogger<RefreshOrphanContactsHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _db = db;
        _logger = logger;
    }
```

and the body of the try block reads:

```csharp
                // Re-attach the contact_id Smartsupp still knows about and let UpsertConversationAsync
                // pull the contact via REST (same path as the runtime fix).
                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
```

Change the field/constructor block to:

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

public class RefreshOrphanContactsHandler
    : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
{
    private readonly ISmartsuppRepository _repository;
    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppContactEnricher _contactEnricher;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<RefreshOrphanContactsHandler> _logger;

    public RefreshOrphanContactsHandler(
        ISmartsuppRepository repository,
        ISmartsuppApiClient apiClient,
        ISmartsuppContactEnricher contactEnricher,
        ApplicationDbContext db,
        ILogger<RefreshOrphanContactsHandler> logger)
    {
        _repository = repository;
        _apiClient = apiClient;
        _contactEnricher = contactEnricher;
        _db = db;
        _logger = logger;
    }
```

Change the try-block body to:

```csharp
                // Re-attach the contact_id Smartsupp still knows about and let the contact
                // enricher pull the contact via REST (same path as the runtime fix, #3878).
                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                local = await _contactEnricher.EnrichContactAsync(local, cancellationToken);
                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
```

- [ ] **Step 10: Build**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
```

Expected: build **fails** at this point with constructor-argument-count errors in
`ConversationReactionsTests.cs` (the only place still constructing these reactions with one
argument) — that is expected and fixed in the next step. Confirm the failures are limited to that
one file:

```bash
dotnet build 2>&1 | grep -E "^.*error CS" | sed -E 's/^([^ (]+).*/\1/' | sort -u
```

Expected: only paths under `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs`.

- [ ] **Step 11: Fix `ConversationReactionsTests.cs`**

Add a shared enricher mock and update every affected constructor call. The field declarations at
the top of the class currently read:

```csharp
public class ConversationReactionsTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
```

Change to:

```csharp
public class ConversationReactionsTests
{
    private readonly Mock<ISmartsuppRepository> _repo = new();
    private readonly Mock<ISmartsuppContactEnricher> _enricher = new();

    public ConversationReactionsTests()
    {
        _enricher
            .Setup(e => e.EnrichContactAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation c, CancellationToken _) => c);
    }
```

Add the using at the top of the file (alphabetically with the other `Anela.Heblo.Application.Features.Smartsupp.*` usings):

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure;
```

Then update every constructor call for the 9 affected reaction types. Every occurrence of:

```csharp
new ConversationOpenedReaction(_repo.Object)
```

becomes:

```csharp
new ConversationOpenedReaction(_repo.Object, _enricher.Object)
```

Apply the same `, _enricher.Object)` insertion to every other constructed reaction in this file that
takes `ISmartsuppRepository` alone: `ConversationClosedReaction`, `ConversationClosedByContactReaction`,
`ConversationContactRepliedReaction`, `ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`,
`ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`, `ConversationRatedReaction`.
Leave `ConversationAgentJoinedReaction`, `ConversationAgentLeftReaction`, `ConversationMessageDeliveredReaction`,
and `ConversationMessageDeliveryFailedReaction` untouched — they use `ISmartsuppPresenceRepository`/
`ISmartsuppAgentCache` or don't call `UpsertConversationAsync`, and were not modified in Steps 1-9.

- [ ] **Step 12: Build and run the reaction tests**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet build
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConversationReactionsTests"
```

Expected: `Build succeeded.` with 0 errors; **Passed! - Failed: 0, Passed: 20**.

- [ ] **Step 13: Run the full Smartsupp test suite**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Smartsupp"
```

Expected: all tests pass. (`SmartsuppRepositoryUnknownContactFetchTests` and the Postgres integration
tests are untouched by this task and should still pass exactly as before — `SmartsuppRepository`
still has its old constructor and old REST path at this point, just unreachable from any reaction.)

- [ ] **Step 14: Commit**

```bash
cd /home/user/worktrees/feature-3878-Arch-Review-Smartsupp-Smartsupprepository-Performs
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/ \
        backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ConversationReactionsTests.cs
git commit -m "feat(smartsupp): route contact enrichment through ISmartsuppContactEnricher (#3878)"
```

---

