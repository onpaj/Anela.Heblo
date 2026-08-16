# Design: Move Smartsupp contact-enrichment REST call out of SmartsuppRepository

## Component Design

### `ISmartsuppContactEnricher` / `SmartsuppContactEnricher`
**Location:** `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/SmartsuppContactEnricher.cs`

**Responsibility:** Given a `SmartsuppConversation` that references a `ContactId`, guarantee that by
the time it returns, the conversation either carries a resolvable, locally-persisted contact link,
or has had that link explicitly cleared. This is the sole owner of the "should we hit Smartsupp
REST for this contact, and what do we do if that fails" business decision — previously implicit
inside `SmartsuppRepository.UpsertConversationAsync`.

**Dependencies:** `ISmartsuppApiClient` (Adapters), `ISmartsuppRepository` (Persistence, used only
for its existing `UpsertContactAsync` method), `ILogger<SmartsuppContactEnricher>`.

**Contract:**
```csharp
public interface ISmartsuppContactEnricher
{
    Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);
}
```

**Behavior (state machine, mirrors current `SmartsuppRepository.TryFetchAndStageContactAsync` +
`UpsertConversationAsync` local-lookup exactly):**

```
EnrichContactAsync(conversation, ct):
  if conversation.ContactId is null:
      return conversation                                  // nothing to enrich

  local = repository.TryGetContact(conversation.ContactId)  // via a lookup the repo already exposes
                                                              // internally — see "Repository lookup"
                                                              // note below; NOT a new interface method,
                                                              // reuses existing hydration path in
                                                              // UpsertConversationAsync itself.
  if local is not null:
      conversation.ContactName  ??= local.Name
      conversation.ContactEmail ??= local.Email
      return conversation                                   // no REST call — matches
                                                              // DoesNotCallRest_WhenContactAlreadyInDb

  try:
      data = apiClient.GetContactAsync(conversation.ContactId, ct)
  catch (Exception ex):
      log.Warning(ex, "smartsupp: failed to fetch contact {ContactId} while upserting
                        conversation; continuing without link", conversation.ContactId)
      conversation.ContactId = null
      return conversation                                   // fail-open, matches
                                                              // WipesContactIdAndLogsWarning_WhenRestThrows

  if data is null:
      log.Warning("smartsupp: contact {ContactId} not found via REST while upserting
                    conversation; continuing without link", conversation.ContactId)
      conversation.ContactId = null
      return conversation                                   // matches WipesContactId_WhenRestReturnsNull

  contact = MapContactDataToEntity(data, conversation.SyncedAt)   // moved verbatim from
                                                                    // SmartsuppRepository, DateTimeKind.Utc
                                                                    // handling preserved
  repository.UpsertContactAsync(contact, ct)
  conversation.ContactName  ??= contact.Name
  conversation.ContactEmail ??= contact.Email
  return conversation
```

**Note on the "local lookup" step:** today, `SmartsuppRepository.UpsertConversationAsync` does its
own `_db.SmartsuppContacts.AsNoTracking().FirstOrDefaultAsync(...)` lookup and *also* is where the
enrichment decision was made. After this change, `UpsertConversationAsync` keeps that exact lookup
for hydration purposes (FR-2 — it's a read, not an external call, so it stays in Persistence and is
not a layering violation). `SmartsuppContactEnricher` needs its own way to know "is this contact
already known locally" so it doesn't need a *second* full lookup, but the simplest correct design
that changes nothing about `ISmartsuppRepository`'s surface is: the enricher does not need to
duplicate the "is it known locally" check at all — it only needs to run when the *conversation DTO*
doesn't already carry a name/email (checked from the incoming payload, not the DB) OR always run
and let `UpsertConversationAsync`'s own COALESCE-based hydration remain the single source of truth
for "did we already have this contact." Concretely: the enricher's local-presence check reuses
`ISmartsuppRepository`'s already-public `UpsertContactAsync` idempotently — but to avoid a redundant
REST call when the contact is already staged, the enricher performs its own local existence check
via a new, minimal read added to `ISmartsuppRepository`: `Task<bool> ContactExistsAsync(string
contactId, CancellationToken ct)`. This is a plain `AnyAsync` — no business logic, appropriate for
a persistence-layer read (same category as `ListOrphanContactConversationIdsAsync`), and keeps
`SmartsuppContactEnricher` free of direct `ApplicationDbContext` access.

### `SmartsuppRepository` (modified)
**Location:** `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

**Responsibility (narrowed):** Persist `SmartsuppConversation`/`SmartsuppContact`/`SmartsuppMessage`
rows. No outbound HTTP. `UpsertConversationAsync` keeps its local-contact hydration read (an EF
query, not an external call) for denormalized `ContactName`/`ContactEmail` fields, and keeps the
unchanged raw-SQL upsert. It gains one small new read method, `ContactExistsAsync`, consumed only
by `SmartsuppContactEnricher`.

**Removed:** `ISmartsuppApiClient` field/ctor param, `TryFetchAndStageContactAsync`,
`MapContactDataToEntity`, the `conversation.ContactId = null` wipe-on-miss branch.

### Reaction classes (7) + `RefreshOrphanContactsHandler` (modified)
Each gains an `ISmartsuppContactEnricher` constructor dependency and one `await` call:
```csharp
conversation = await _enricher.EnrichContactAsync(conversation, cancellationToken);
await _repository.UpsertConversationAsync(conversation, cancellationToken);
```
placed immediately before the existing `UpsertConversationAsync` call in each of:
`ConversationOpenedReaction`, `ConversationRatedReaction`, `ConversationClosedReaction`,
`ConversationClosedByContactReaction`, `ConversationAgentAssignedReaction`,
`ConversationAgentUnassignedReaction`, `ConversationReplyReactionBase` (only the
conversation-upsert branch — the sibling message-only branch is untouched), and
`RefreshOrphanContactsHandler` (after its `local.ContactId = remote.ContactId` assignment).

## Data Schemas
No database schema changes — no new tables, columns, or migrations. `SmartsuppContact` and
`SmartsuppConversation` entity shapes are unchanged.

**Interface additions (C# contracts only, no wire format changes):**

```csharp
// Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs — one new method
Task<bool> ContactExistsAsync(string contactId, CancellationToken cancellationToken);
```

```csharp
// Anela.Heblo.Application/Features/Smartsupp/Infrastructure/ISmartsuppContactEnricher.cs — new file
public interface ISmartsuppContactEnricher
{
    Task<SmartsuppConversation> EnrichContactAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);
}
```

No HTTP request/response shapes change — `ISmartsuppApiClient.GetContactAsync` and its
`SmartsuppContactData` DTO are consumed identically, just from a different assembly. No webhook
payload shape changes. No public REST API of this application changes.
