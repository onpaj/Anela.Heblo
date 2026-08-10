# Specification: Collapse duplicate Smartsupp webhook reaction implementations into shared base classes

## Summary
Eight of the eighteen `ISmartsuppWebhookReaction` implementations under `Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/` share three character-for-character identical `HandleAsync` bodies, differing only in the `EventName` string they expose. This spec introduces three internal abstract base classes — one per behaviour group — and reduces each of the eight concrete reaction classes to a one-line `EventName` override, so each behaviour is written exactly once. No functional, DI-registration, or public-contract change is intended: this is a pure structural deduplication.

## Background
`ProcessWebhookEventHandler` resolves one `ISmartsuppWebhookReaction` per incoming Smartsupp webhook event by matching `EventName` (see `ProcessWebhookEventHandler.cs:22,42`), and all eighteen implementations are registered individually in `SmartsuppModule.cs:51-70`. Verified against the actual source in this worktree, three groups of reactions are exact duplicates of each other except for the `EventName` string:

- **Group A — "reply" reactions** (`ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`, `ConversationContactRepliedReaction`, each `.cs:6-28`): upsert the conversation (if present) via `SmartsuppPayloadMapper.MapConversation`, then upsert the message (if present) via `SmartsuppPayloadMapper.MapMessage` + `_repository.UpsertMessagesAsync`. Bodies are byte-identical apart from `EventName`.
- **Group B — "contact write + backfill" reactions** (`ContactCreatedReaction`, `ContactUpdatedReaction`, `ContactAcquiredReaction`, each `.cs:6-22`): map + `UpsertContactAsync`, then `BackfillConversationDenormFieldsAsync`. Bodies are byte-identical apart from `EventName`.
- **Group C — "contact write only" reactions** (`ContactBannedReaction`, `ContactUnbannedReaction`, each `.cs:6-20`): map + `UpsertContactAsync` only. Bodies are byte-identical apart from `EventName`.

This matches the finding shape already accepted in #3612 (identical import jobs differing only by currency) and #3853 (copy-pasted shipment logic that silently drifted). The repository's `docs/architecture/development_guidelines.md` "Feature cohesion" rule is the basis for collapsing these. `ConversationClosedReaction` and `ConversationClosedByContactReaction` were also inspected (per the brief's callout) and confirmed to **genuinely differ** — `ConversationClosedReaction` reads `close_type` and `agent_id` from the payload and falls back to `ctx.Data` when no `conversation` key is present, while `ConversationClosedByContactReaction` hardcodes `CloseType = "contact"` and does not read `agent_id`. These two, and the remaining eight reactions not named in the brief (`ConversationOpenedReaction`, `ConversationRatedReaction`, `ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`, `ConversationAgentJoinedReaction`, `ConversationAgentLeftReaction`, `ConversationMessageDeliveredReaction`, `ConversationMessageDeliveryFailedReaction`), each have distinct bodies and are unaffected by this change.

A load-bearing constraint discovered while reading the code: **existing unit tests instantiate the concrete reaction classes directly by name and constructor signature** — `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Reactions/ContactReactionsTests.cs:45-50,72-77` and `.../ConversationReactionsTests.cs:134,149,164` all do `new ConversationAgentRepliedReaction(_repo.Object)`, `new ContactCreatedReaction(_repo.Object)`, etc. Also, `ProcessWebhookEventHandler.cs:63` logs `reaction.GetType().Name` on failure, which today distinguishes which of the three "reply" (or "contact write") reactions threw. Both of these argue for an **inheritance-based** design (thin per-event subclasses of a shared abstract base) rather than a single class parameterised by an `EventName` constructor argument: inheritance preserves the concrete class names, constructor signatures (`(ISmartsuppRepository repository)`), and per-type `GetType().Name` distinctness that the tests and error logs currently depend on, with zero changes required to `SmartsuppModule.cs` registration call sites or to the existing test files.

## Functional Requirements

### FR-1: Shared base class for Group A ("reply" reactions)
Introduce an internal abstract class (suggested name `ConversationReplyReactionBase`, colocated in the `Reactions/` folder, e.g. `ConversationReplyReactionBase.cs`) implementing `ISmartsuppWebhookReaction` with:
- A constructor taking `ISmartsuppRepository repository`, stored in a `protected` field.
- `public abstract string EventName { get; }`.
- `public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)` containing exactly the body currently duplicated in `ConversationAgentRepliedReaction.cs:14-27` (conditional conversation upsert, then conditional message upsert).

`ConversationAgentRepliedReaction`, `ConversationBotRepliedReaction`, and `ConversationContactRepliedReaction` become `sealed` subclasses of this base, each retaining their existing public constructor signature `(ISmartsuppRepository repository) : base(repository)` and overriding only `EventName` with their current string literal (`"conversation.agent_replied"`, `"conversation.bot_replied"`, `"conversation.contact_replied"` respectively).

**Acceptance criteria:**
- The three concrete classes contain no logic beyond the constructor and the `EventName` override.
- `ConversationReplyReactionBase.HandleAsync` is byte-for-byte behaviourally equivalent to the current duplicated body (same null-checks, same mapper calls, same repository calls, same argument order).
- All three classes keep their existing class name, namespace, and public constructor signature unchanged.

### FR-2: Shared base class for Group B ("contact write + backfill" reactions)
Introduce an internal abstract class (suggested name `ContactUpsertWithBackfillReactionBase`) implementing `ISmartsuppWebhookReaction` with the constructor/`EventName` shape described in FR-1, and `HandleAsync` containing exactly the body currently duplicated in `ContactCreatedReaction.cs:14-21` (null-guard on `ctx.GetContact()`, map contact, `UpsertContactAsync`, then `BackfillConversationDenormFieldsAsync`).

`ContactCreatedReaction`, `ContactUpdatedReaction`, and `ContactAcquiredReaction` become `sealed` subclasses, each overriding only `EventName` (`"contact.created"`, `"contact.updated"`, `"contact.acquired"`).

**Acceptance criteria:**
- Same criteria as FR-1, applied to this group and base class.

### FR-3: Shared base class for Group C ("contact write only" reactions)
Introduce an internal abstract class (suggested name `ContactUpsertOnlyReactionBase`) implementing `ISmartsuppWebhookReaction` with `HandleAsync` containing exactly the body currently duplicated in `ContactBannedReaction.cs:14-19` (null-guard on `ctx.GetContact()`, map contact, `UpsertContactAsync`, no backfill call).

`ContactBannedReaction` and `ContactUnbannedReaction` become `sealed` subclasses, each overriding only `EventName` (`"contact.banned"`, `"contact.unbanned"`).

**Acceptance criteria:**
- Same criteria as FR-1, applied to this group and base class.
- `ContactUpsertOnlyReactionBase` is a distinct type from `ContactUpsertWithBackfillReactionBase` (Group B and Group C are not merged into one class with a flag) — this preserves the arch-review's own grouping and keeps each base class's `HandleAsync` free of conditional branching for a "should I backfill" flag.

### FR-4: No change to unrelated reactions
`ConversationClosedReaction`, `ConversationClosedByContactReaction`, and the ten other reactions not named in the brief are left untouched — no base class, no shared code extraction. This is a deliberate contrast case per the brief's own guidance.

**Acceptance criteria:**
- `git diff` for this change touches only: the three new base-class files, the eight Group A/B/C concrete reaction files, and no other reaction file.
- `SmartsuppModule.cs` DI registrations (`AddScoped<ISmartsuppWebhookReaction, T>()` for all eighteen types) require **no changes**, since concrete class names and constructor signatures are preserved.

### FR-5: Test compatibility
All existing tests in `ContactReactionsTests.cs` and `ConversationReactionsTests.cs` must continue to pass **unmodified** — they construct concrete reaction types directly (`new ConversationAgentRepliedReaction(_repo.Object)`, `new ContactCreatedReaction(_repo.Object)`, etc.) and assert on `EventName` and on repository calls made by `HandleAsync`.

**Acceptance criteria:**
- `dotnet test` for `Anela.Heblo.Tests` passes with zero changes to `ContactReactionsTests.cs` / `ConversationReactionsTests.cs`.
- No new tests are strictly required by this change (existing tests already exercise every affected class's `HandleAsync` and `EventName`), but if the implementer adds a test directly against a base class's shared logic, it must not replace or remove existing per-class tests, since those pin the DI-visible contract.

### FR-6: Behavioural equivalence (regression safety)
The refactor must not alter externally observable behaviour: the set of registered `EventName` strings handled by `ProcessWebhookEventHandler`, the repository calls made per event, the order of those calls, and the data passed to them must be identical before and after.

**Acceptance criteria:**
- For each of the 8 affected event names, replaying the same webhook payload before and after the change results in identical `ISmartsuppRepository` method calls (method, arguments, call count, call order).
- `reaction.GetType().Name` used in `ProcessWebhookEventHandler.cs:63` error logging still returns the specific concrete class name (e.g. `ConversationAgentRepliedReaction`), not the shared base class name, for each of the 8 affected reactions — confirming the inheritance design (not a single parameterised class) was used.

## Non-Functional Requirements

### NFR-1: Maintainability
A future change to the Group A message-upsert path (e.g., an added field, a null guard, an idempotency check, or a different `ConversationId` fallback when `SmartsuppPayloadMapper.MapMessage` returns an empty one — see `Mappers/SmartsuppPayloadMapper.cs:72-74`) must require editing exactly one file (`ConversationReplyReactionBase.cs`), not three. Same for Group B and Group C via their respective base classes.

### NFR-2: No behavioural or performance change
This is a structural-only refactor. No new business logic (null guards, idempotency checks, different fallbacks) is introduced by this change — those are hypothetical future changes cited in the brief as motivation, not part of this scope. No measurable performance impact is expected (same number of repository/database calls per event, just routed through one extra virtual dispatch).

### NFR-3: Security
No change — no new data exposure, no change to authentication/authorization on the webhook endpoint, no change to what data is persisted.

## Data Model
No data model changes. `SmartsuppConversation`, `SmartsuppMessage`, `SmartsuppContact` and their persistence remain untouched.

## API / Interface Design

New types (all in `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ProcessWebhookEvent/Reactions/`):

```csharp
internal abstract class ConversationReplyReactionBase : ISmartsuppWebhookReaction
{
    protected readonly ISmartsuppRepository Repository;
    protected ConversationReplyReactionBase(ISmartsuppRepository repository) => Repository = repository;
    public abstract string EventName { get; }
    public async Task HandleAsync(WebhookEventContext ctx, CancellationToken cancellationToken)
    {
        // body moved verbatim from ConversationAgentRepliedReaction.HandleAsync
    }
}

public sealed class ConversationAgentRepliedReaction : ConversationReplyReactionBase
{
    public ConversationAgentRepliedReaction(ISmartsuppRepository repository) : base(repository) { }
    public override string EventName => "conversation.agent_replied";
}
// ...ConversationBotRepliedReaction, ConversationContactRepliedReaction analogous
```

```csharp
internal abstract class ContactUpsertWithBackfillReactionBase : ISmartsuppWebhookReaction { /* Group B body */ }
public sealed class ContactCreatedReaction : ContactUpsertWithBackfillReactionBase { EventName => "contact.created"; }
// ...ContactUpdatedReaction, ContactAcquiredReaction analogous
```

```csharp
internal abstract class ContactUpsertOnlyReactionBase : ISmartsuppWebhookReaction { /* Group C body */ }
public sealed class ContactBannedReaction : ContactUpsertOnlyReactionBase { EventName => "contact.banned"; }
public sealed class ContactUnbannedReaction : ContactUpsertOnlyReactionBase { EventName => "contact.unbanned"; }
```

Concrete classes keep their existing accessibility (`public sealed class`, as today) since they are referenced directly by tests and by `SmartsuppModule.cs`. Base classes can be `internal` since nothing outside the `Reactions/` namespace needs to construct them directly (assumption — see FR-4's "no DI change" criterion, which only requires the concrete types to remain resolvable).

No changes to `ISmartsuppWebhookReaction`, `WebhookEventContext`, `SmartsuppPayloadMapper`, `ProcessWebhookEventHandler`, or `SmartsuppModule.cs`.

## Dependencies
- `ISmartsuppRepository` (`backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`) — unchanged interface, used as-is by the new base classes.
- `SmartsuppPayloadMapper` (`Mappers/SmartsuppPayloadMapper.cs`) — unchanged, called from the base classes exactly as it is today from the concrete classes.
- No new NuGet packages, no new external services.

## Out of Scope
- Any change to `ConversationClosedReaction` / `ConversationClosedByContactReaction` (confirmed genuinely divergent — left as-is).
- Any change to the 8 other reaction classes not named in the brief (`ConversationOpenedReaction`, `ConversationRatedReaction`, `ConversationAgentAssignedReaction`, `ConversationAgentUnassignedReaction`, `ConversationAgentJoinedReaction`, `ConversationAgentLeftReaction`, `ConversationMessageDeliveredReaction`, `ConversationMessageDeliveryFailedReaction`).
- Any change to `SmartsuppModule.cs` DI registration (should not be needed under the inheritance design in FR-1–FR-3).
- Any new business logic (idempotency checks, null-guard changes, different `ConversationId` fallback behaviour) — those are cited in the brief purely as *future* changes this refactor makes cheaper, not requirements of this change.
- Merging Group B and Group C into a single base class with a "should backfill" flag — rejected in FR-3 to keep each base class's body branch-free and to match the brief's own three-group taxonomy.
- Refactoring the test files' duplicated `switch`-based test-case dispatch (`ContactReactionsTests.cs:43-51,70-78`) — optional cleanup, not required for this fix.

## Open Questions
None.

## Status: COMPLETE
