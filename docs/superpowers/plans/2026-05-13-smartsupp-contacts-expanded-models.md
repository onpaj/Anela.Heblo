# Smartsupp Contact Fetching & Expanded Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /contacts/{id}` fetching to the Smartsupp sync job, fix the broken `sub_type`/`contact_id` author mapping, and expand all response models (conversations, messages, contacts) to capture the full set of operationally useful fields.

**Architecture:** The Smartsupp adapter layer holds private API response DTOs (snake_case JSON); the domain layer holds public data classes passed between adapter and application; the persistence layer maps domain entities to PostgreSQL. All three layers must be updated together. A new `SmartsuppContact` entity is persisted via a new EF configuration + migration. The sync job caches contacts by ID within a run to avoid duplicate HTTP calls.

**Tech Stack:** .NET 8, C#, Entity Framework Core (PostgreSQL/Npgsql), Polly, xUnit, FluentAssertions, Moq

---

## File Map

**Modified:**
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs` — domain data contracts
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppConversation.cs` — entity
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppMessage.cs` — entity
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs` — interface
- `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs` — HTTP client + DTOs
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs` — sync orchestration
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs` — EF config
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppMessageConfiguration.cs` — EF config
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs` — repository
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — DbContext
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs` — tests
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs` — tests

**Created:**
- `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppContact.cs` — new entity
- `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppContactConfiguration.cs` — new EF config
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddSmartsuppContactsAndExtendedFields.cs` — auto-generated

---

### Task 1: Update domain data contracts in ISmartsuppApiClient.cs

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs`

- [ ] **Step 1: Replace ISmartsuppApiClient.cs with the updated contracts**

Replace the entire file content:

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppApiClient
{
    Task<SmartsuppSearchResult> SearchConversationsAsync(
        string? cursor,
        int size,
        CancellationToken cancellationToken);

    Task<List<SmartsuppMessageData>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken);

    Task<SmartsuppContactData?> GetContactAsync(
        string contactId,
        CancellationToken cancellationToken);
}

public class SmartsuppSearchResult
{
    public int Total { get; set; }
    public string? After { get; set; }
    public List<SmartsuppConversationData> Items { get; set; } = new();
}

public class SmartsuppConversationData
{
    public string Id { get; set; } = null!;
    public string? ExtId { get; set; }
    public string? Status { get; set; }
    public bool Unread { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ContactId { get; set; }
    public string? VisitorId { get; set; }
    public List<string> AgentIds { get; set; } = new();
    public List<string> AssignedIds { get; set; } = new();
    public string? GroupId { get; set; }
    public int? RatingValue { get; set; }
    public string? RatingText { get; set; }
    public string? Domain { get; set; }
    public string? Referer { get; set; }
    public bool IsOffline { get; set; }
    public bool IsServed { get; set; }
    public string? ChannelType { get; set; }
    public string? ChannelId { get; set; }
    public string? LocationCountry { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationIp { get; set; }
    public string? LocationCode { get; set; }
    public string? VariablesJson { get; set; }
    public string? TagsJson { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageAt { get; set; }
}

public class SmartsuppMessageData
{
    public string Id { get; set; } = null!;
    public string? ExtId { get; set; }
    public string? Type { get; set; }
    public string? SubType { get; set; }
    public string? Content { get; set; }
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ConversationId { get; set; }
    public string? VisitorId { get; set; }
    public string? AgentId { get; set; }
    public string? TriggerId { get; set; }
    public string? TriggerName { get; set; }
    public string? DeliveryTo { get; set; }
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public bool IsReply { get; set; }
    public bool IsFirstReply { get; set; }
    public bool IsOffline { get; set; }
    public bool IsOfflineReply { get; set; }
    public int? ResponseTime { get; set; }
    public string? PageUrl { get; set; }
    public string? AttachmentsJson { get; set; }
    public string? ChannelType { get; set; }
    public string? ChannelId { get; set; }
}

public class SmartsuppContactData
{
    public string Id { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BannedBy { get; set; }
    public bool GdprApproved { get; set; }
    public string? TagsJson { get; set; }
    public string? PropertiesJson { get; set; }
}
```

- [ ] **Step 2: Verify no build errors from contract changes**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -20
```

Expected: The domain project itself builds clean. Other projects will have errors until we update them in later tasks — that is expected at this stage.

- [ ] **Step 3: Commit domain contracts**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppApiClient.cs
git commit -m "feat: expand Smartsupp domain data contracts — add contact, sub_type, extended fields"
```

---

### Task 2: Create SmartsuppContact entity

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppContact.cs`

- [ ] **Step 1: Create the entity file**

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppContact
{
    public string Id { get; set; } = null!;
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTime? BannedAt { get; set; }
    public string? BannedBy { get; set; }
    public bool GdprApproved { get; set; }
    public string? TagsJson { get; set; }
    public string? PropertiesJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppContact.cs
git commit -m "feat: add SmartsuppContact domain entity"
```

---

### Task 3: Expand SmartsuppConversation and SmartsuppMessage entities

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppConversation.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppMessage.cs`

- [ ] **Step 1: Update SmartsuppConversation.cs**

Replace the entire file:

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppConversation
{
    public string Id { get; set; } = null!;
    public string? ExtId { get; set; }
    public string? Subject { get; set; }
    public string? ContactId { get; set; }
    public SmartsuppContact? Contact { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAvatarUrl { get; set; }
    public string? VisitorId { get; set; }
    public SmartsuppConversationStatus Status { get; set; }
    public bool IsUnread { get; set; }
    public bool IsOffline { get; set; }
    public bool IsServed { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Domain { get; set; }
    public string? Referer { get; set; }
    public string? LocationCountry { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationIp { get; set; }
    public string? LocationCode { get; set; }
    public string? VariablesJson { get; set; }
    public string? TagsJson { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
    public List<SmartsuppMessage> Messages { get; set; } = new();
}
```

- [ ] **Step 2: Update SmartsuppMessage.cs**

Replace the entire file:

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppMessage
{
    public string Id { get; set; } = null!;
    public string ConversationId { get; set; } = null!;
    public SmartsuppConversation Conversation { get; set; } = null!;
    public SmartsuppMessageAuthorType AuthorType { get; set; }
    public string? SubType { get; set; }
    public string? AuthorName { get; set; }
    public string? Content { get; set; }
    public string? TriggerName { get; set; }
    public string? TriggerId { get; set; }
    public string? PageUrl { get; set; }
    public string? AgentId { get; set; }
    public string? VisitorId { get; set; }
    public string? DeliveryStatus { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public bool IsOffline { get; set; }
    public bool IsReply { get; set; }
    public bool IsFirstReply { get; set; }
    public int? ResponseTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? AttachmentsJson { get; set; }
}
```

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppConversation.cs \
        backend/src/Anela.Heblo.Domain/Features/Smartsupp/SmartsuppMessage.cs
git commit -m "feat: expand SmartsuppConversation and SmartsuppMessage entities with new fields"
```

---

### Task 4: Update ISmartsuppRepository and SmartsuppRepository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`

- [ ] **Step 1: Add UpsertContactAsync to ISmartsuppRepository**

Replace the entire file:

```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public interface ISmartsuppRepository
{
    Task<(List<SmartsuppConversation> Items, int Total)> ListConversationsAsync(
        SmartsuppConversationStatus status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SmartsuppConversation?> GetConversationAsync(
        string id,
        CancellationToken cancellationToken);

    Task UpsertContactAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken);

    Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken);

    Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken);

    Task<SmartsuppSyncState> GetOrCreateSyncStateAsync(CancellationToken cancellationToken);

    Task SetSyncWatermarkAsync(DateTime lastUpdatedAtSeen, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Update SmartsuppRepository.cs**

Replace the entire file with the expanded implementation. Key changes: add `UpsertContactAsync`, extend `UpsertConversationAsync` to copy all new fields, extend `UpsertMessagesAsync` to copy new message fields.

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppRepository : ISmartsuppRepository
{
    private readonly ApplicationDbContext _db;

    public SmartsuppRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<(List<SmartsuppConversation> Items, int Total)> ListConversationsAsync(
        SmartsuppConversationStatus status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _db.SmartsuppConversations
            .AsNoTracking()
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.LastMessageAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<SmartsuppConversation?> GetConversationAsync(
        string id,
        CancellationToken cancellationToken) =>
        await _db.SmartsuppConversations
            .AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task UpsertContactAsync(
        SmartsuppContact contact,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppContacts
            .FirstOrDefaultAsync(c => c.Id == contact.Id, cancellationToken);

        if (existing is null)
        {
            _db.SmartsuppContacts.Add(contact);
        }
        else
        {
            existing.Email = contact.Email;
            existing.Name = contact.Name;
            existing.Phone = contact.Phone;
            existing.Note = contact.Note;
            existing.BannedAt = contact.BannedAt;
            existing.BannedBy = contact.BannedBy;
            existing.GdprApproved = contact.GdprApproved;
            existing.TagsJson = contact.TagsJson;
            existing.PropertiesJson = contact.PropertiesJson;
            existing.UpdatedAt = contact.UpdatedAt;
            existing.SyncedAt = contact.SyncedAt;
        }
    }

    public async Task UpsertConversationAsync(
        SmartsuppConversation conversation,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppConversations
            .FirstOrDefaultAsync(c => c.Id == conversation.Id, cancellationToken);

        if (existing is null)
        {
            _db.SmartsuppConversations.Add(conversation);
        }
        else
        {
            existing.Status = conversation.Status;
            existing.IsUnread = conversation.IsUnread;
            existing.IsOffline = conversation.IsOffline;
            existing.IsServed = conversation.IsServed;
            existing.ContactId = conversation.ContactId;
            existing.ContactName = conversation.ContactName;
            existing.ContactEmail = conversation.ContactEmail;
            existing.ContactAvatarUrl = conversation.ContactAvatarUrl;
            existing.VisitorId = conversation.VisitorId;
            existing.ExtId = conversation.ExtId;
            existing.FinishedAt = conversation.FinishedAt;
            existing.Domain = conversation.Domain;
            existing.Referer = conversation.Referer;
            existing.LocationCountry = conversation.LocationCountry;
            existing.LocationCity = conversation.LocationCity;
            existing.LocationIp = conversation.LocationIp;
            existing.LocationCode = conversation.LocationCode;
            existing.VariablesJson = conversation.VariablesJson;
            existing.TagsJson = conversation.TagsJson;
            existing.LastMessageAt = conversation.LastMessageAt;
            existing.LastMessagePreview = conversation.LastMessagePreview;
            existing.UpdatedAt = conversation.UpdatedAt;
            existing.SyncedAt = conversation.SyncedAt;
        }
    }

    public async Task UpsertMessagesAsync(
        string conversationId,
        List<SmartsuppMessage> messages,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SmartsuppMessages
            .Where(m => m.ConversationId == conversationId)
            .ToDictionaryAsync(m => m.Id, cancellationToken);

        foreach (var message in messages)
        {
            if (existing.TryGetValue(message.Id, out var tracked))
            {
                tracked.Content = message.Content;
                tracked.AuthorName = message.AuthorName;
                tracked.SubType = message.SubType;
                tracked.TriggerName = message.TriggerName;
                tracked.TriggerId = message.TriggerId;
                tracked.PageUrl = message.PageUrl;
                tracked.AgentId = message.AgentId;
                tracked.VisitorId = message.VisitorId;
                tracked.DeliveryStatus = message.DeliveryStatus;
                tracked.DeliveredAt = message.DeliveredAt;
                tracked.IsOffline = message.IsOffline;
                tracked.IsReply = message.IsReply;
                tracked.IsFirstReply = message.IsFirstReply;
                tracked.ResponseTime = message.ResponseTime;
                tracked.UpdatedAt = message.UpdatedAt;
                tracked.AttachmentsJson = message.AttachmentsJson;
            }
            else
            {
                _db.SmartsuppMessages.Add(message);
            }
        }
    }

    public async Task<SmartsuppSyncState> GetOrCreateSyncStateAsync(CancellationToken cancellationToken)
    {
        var state = await _db.SmartsuppSyncState.FirstOrDefaultAsync(cancellationToken);
        if (state is not null) return state;

        state = new SmartsuppSyncState { LastSyncStartedAt = Unspecified(DateTime.UtcNow) };
        _db.SmartsuppSyncState.Add(state);
        await _db.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task SetSyncWatermarkAsync(DateTime lastUpdatedAtSeen, CancellationToken cancellationToken)
    {
        var state = await GetOrCreateSyncStateAsync(cancellationToken);
        state.LastUpdatedAtSeen = Unspecified(lastUpdatedAtSeen);
        state.LastSyncStartedAt = Unspecified(DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await _db.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs \
        backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs
git commit -m "feat: add UpsertContactAsync and expand upsert methods to copy new fields"
```

---

### Task 5: Create SmartsuppContactConfiguration and update EF configs

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppContactConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppMessageConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create SmartsuppContactConfiguration.cs**

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppContactConfiguration : IEntityTypeConfiguration<SmartsuppContact>
{
    public void Configure(EntityTypeBuilder<SmartsuppContact> builder)
    {
        builder.ToTable("SmartsuppContacts", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Name).HasMaxLength(200);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.BannedBy).HasMaxLength(200);
        builder.Property(e => e.Note).HasColumnType("text");
        builder.Property(e => e.TagsJson).HasColumnType("text");
        builder.Property(e => e.PropertiesJson).HasColumnType("text");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.SyncedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.BannedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => e.Email);
    }
}
```

- [ ] **Step 2: Update SmartsuppConversationConfiguration.cs**

Replace the entire file:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppConversationConfiguration : IEntityTypeConfiguration<SmartsuppConversation>
{
    public void Configure(EntityTypeBuilder<SmartsuppConversation> builder)
    {
        builder.ToTable("SmartsuppConversations", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.ExtId).HasMaxLength(100);
        builder.Property(e => e.Subject).HasMaxLength(500);
        builder.Property(e => e.ContactId).HasMaxLength(100);
        builder.Property(e => e.ContactName).HasMaxLength(200);
        builder.Property(e => e.ContactEmail).HasMaxLength(200);
        builder.Property(e => e.ContactAvatarUrl).HasMaxLength(500);
        builder.Property(e => e.VisitorId).HasMaxLength(100);
        builder.Property(e => e.Domain).HasMaxLength(200);
        builder.Property(e => e.Referer).HasMaxLength(500);
        builder.Property(e => e.LocationCountry).HasMaxLength(100);
        builder.Property(e => e.LocationCity).HasMaxLength(100);
        builder.Property(e => e.LocationIp).HasMaxLength(50);
        builder.Property(e => e.LocationCode).HasMaxLength(10);
        builder.Property(e => e.LastMessagePreview).HasMaxLength(500);
        builder.Property(e => e.VariablesJson).HasColumnType("text");
        builder.Property(e => e.TagsJson).HasColumnType("text");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.FinishedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.LastMessageAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.SyncedAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => new { e.Status, e.LastMessageAt });
        builder.HasIndex(e => e.ContactId);
        builder.HasOne(e => e.Contact)
            .WithMany()
            .HasForeignKey(e => e.ContactId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
        builder.HasMany(e => e.Messages)
            .WithOne(e => e.Conversation)
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Update SmartsuppMessageConfiguration.cs**

Replace the entire file:

```csharp
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Smartsupp;

public sealed class SmartsuppMessageConfiguration : IEntityTypeConfiguration<SmartsuppMessage>
{
    public void Configure(EntityTypeBuilder<SmartsuppMessage> builder)
    {
        builder.ToTable("SmartsuppMessages", "public");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasMaxLength(100);
        builder.Property(e => e.ConversationId).HasMaxLength(100);
        builder.Property(e => e.AuthorType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SubType).HasMaxLength(20);
        builder.Property(e => e.AuthorName).HasMaxLength(200);
        builder.Property(e => e.TriggerName).HasMaxLength(200);
        builder.Property(e => e.TriggerId).HasMaxLength(100);
        builder.Property(e => e.AgentId).HasMaxLength(100);
        builder.Property(e => e.VisitorId).HasMaxLength(100);
        builder.Property(e => e.DeliveryStatus).HasMaxLength(50);
        builder.Property(e => e.Content).HasColumnType("text");
        builder.Property(e => e.PageUrl).HasColumnType("text");
        builder.Property(e => e.AttachmentsJson).HasColumnType("text");
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamp without time zone");
        builder.Property(e => e.DeliveredAt).HasColumnType("timestamp without time zone");
        builder.HasIndex(e => new { e.ConversationId, e.CreatedAt });
        builder.HasIndex(e => new { e.ConversationId, e.SubType });
    }
}
```

- [ ] **Step 4: Add SmartsuppContacts DbSet to ApplicationDbContext**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, find the Smartsupp section (around line 121-124) and add the new DbSet:

```csharp
    // Smartsupp module
    public DbSet<SmartsuppConversation> SmartsuppConversations { get; set; } = null!;
    public DbSet<SmartsuppMessage> SmartsuppMessages { get; set; } = null!;
    public DbSet<SmartsuppSyncState> SmartsuppSyncState { get; set; } = null!;
    public DbSet<SmartsuppContact> SmartsuppContacts { get; set; } = null!;
```

- [ ] **Step 5: Verify persistence project builds**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj 2>&1 | tail -20
```

Expected: Build succeeds (0 errors).

- [ ] **Step 6: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppContactConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppConversationConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppMessageConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat: add SmartsuppContact EF config and extend conversation/message configs"
```

---

### Task 6: Generate EF migration

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddSmartsuppContactsAndExtendedFields.cs` (auto-generated)

- [ ] **Step 1: Add the migration**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet ef migrations add AddSmartsuppContactsAndExtendedFields \
  --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

Expected: Three files created — `<timestamp>_AddSmartsuppContactsAndExtendedFields.cs`, `<timestamp>_AddSmartsuppContactsAndExtendedFields.Designer.cs`, and updated `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 2: Verify migration is not empty**

```bash
grep -c "migrationBuilder\." backend/src/Anela.Heblo.Persistence/Migrations/*AddSmartsuppContactsAndExtendedFields.cs
```

Expected: A number > 0 (there should be many operations — CreateTable for contacts, AddColumn for conversations and messages, etc.).

- [ ] **Step 3: Commit the migration**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add migration AddSmartsuppContactsAndExtendedFields"
```

---

### Task 7: Update SmartsuppApiClient — DTOs, new method, updated mappers

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs`

- [ ] **Step 1: Write the new SmartsuppApiClient.cs**

Replace the entire file:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Anela.Heblo.Adapters.Smartsupp;

public class SmartsuppApiClient : ISmartsuppApiClient
{
    private static readonly ResiliencePipeline DefaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests),
            DelayGenerator = static args =>
            {
                if (args.Outcome.Exception is HttpRequestException { Data: var data } &&
                    data["RetryAfter"] is TimeSpan retryAfter)
                    return new ValueTask<TimeSpan?>(retryAfter);
                return new ValueTask<TimeSpan?>((TimeSpan?)null);
            }
        })
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SmartsuppOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SmartsuppApiClient> _logger;
    private readonly ResiliencePipeline _pipeline;

    public SmartsuppApiClient(
        IOptions<SmartsuppOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SmartsuppApiClient> logger,
        ResiliencePipeline? pipeline = null)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pipeline = pipeline ?? DefaultPipeline;
    }

    public async Task<SmartsuppSearchResult> SearchConversationsAsync(
        string? cursor,
        int size,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        var body = new ConversationSearchRequest
        {
            Size = size,
            Query = [new ConversationQueryItem { Field = "status", Value = ["open", "served"] }],
            Sort = [new ConversationSortItem()],
            After = cursor is not null ? JsonSerializer.Deserialize<JsonElement[]>(cursor) : null,
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}conversations/search");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp search failed {Status}: {Body}", response.StatusCode, errorBody);
                var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppSearchApiResponse>(raw, JsonOptions)
                         ?? new SmartsuppSearchApiResponse();

            return MapSearchResult(result);
        }, cancellationToken);
    }

    public async Task<List<SmartsuppMessageData>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}conversations/{conversationId}/messages?size=200");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp messages failed {Status}: {Body}", response.StatusCode, errorBody);
                throw new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppMessagesApiResponse>(raw, JsonOptions);

            return result?.Items?.Select(MapMessage).ToList() ?? new List<SmartsuppMessageData>();
        }, cancellationToken);
    }

    public async Task<SmartsuppContactData?> GetContactAsync(
        string contactId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.ApiToken))
            throw new InvalidOperationException("Smartsupp:ApiToken is not configured.");

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var client = _httpClientFactory.CreateClient("Smartsupp");
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_options.BaseUrl}contacts/{contactId}");
            request.Headers.Add("Authorization", $"Bearer {_options.ApiToken}");

            var response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Smartsupp contact failed {Status}: {Body}", response.StatusCode, errorBody);
                var ex = new HttpRequestException($"Smartsupp API {(int)response.StatusCode}", null, response.StatusCode);
                if (response.Headers.RetryAfter?.Delta is { } delta)
                    ex.Data["RetryAfter"] = delta;
                throw ex;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<SmartsuppContactApiResponse>(raw, JsonOptions);

            return result is null ? null : MapContact(result);
        }, cancellationToken);
    }

    private static SmartsuppSearchResult MapSearchResult(SmartsuppSearchApiResponse r) =>
        new()
        {
            Total = r.Total,
            After = r.After is not null ? JsonSerializer.Serialize(r.After) : null,
            Items = r.Items?.Select(MapConversation).ToList() ?? new List<SmartsuppConversationData>()
        };

    private static SmartsuppConversationData MapConversation(SmartsuppConversationApiItem item) =>
        new()
        {
            Id = item.Id ?? "",
            ExtId = item.ExtId,
            Status = item.Status ?? "open",
            Unread = item.Unread,
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = Unspecified(item.UpdatedAt),
            FinishedAt = item.FinishedAt is { } fa ? Unspecified(fa) : null,
            ContactId = item.ContactId,
            VisitorId = item.VisitorId,
            AgentIds = item.AgentIds ?? new List<string>(),
            AssignedIds = item.AssignedIds ?? new List<string>(),
            GroupId = item.GroupId,
            RatingValue = item.RatingValue,
            RatingText = item.RatingText,
            Domain = item.Domain,
            Referer = item.Referer,
            IsOffline = item.IsOffline,
            IsServed = item.IsServed,
            ChannelType = item.Channel?.Type,
            ChannelId = item.Channel?.Id,
            LocationCountry = item.Location?.Country,
            LocationCity = item.Location?.City,
            LocationIp = item.Location?.Ip,
            LocationCode = item.Location?.Code,
            VariablesJson = item.Variables is { } v ? JsonSerializer.Serialize(v) : null,
            TagsJson = item.Tags is { } t ? JsonSerializer.Serialize(t) : null,
            LastMessageText = item.LastMessage?.Text,
            LastMessageAt = item.LastMessage?.CreatedAt is { } lm ? Unspecified(lm) : null,
        };

    private static SmartsuppMessageData MapMessage(SmartsuppMessageApiItem item) =>
        new()
        {
            Id = item.Id ?? "",
            ExtId = item.ExtId,
            Type = item.Type,
            SubType = item.SubType,
            Content = item.Content?.Text,
            ContentType = item.Content?.Type,
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = item.UpdatedAt is { } ua ? Unspecified(ua) : default,
            ConversationId = item.ConversationId,
            VisitorId = item.VisitorId,
            AgentId = item.AgentId,
            TriggerId = item.TriggerId,
            TriggerName = item.TriggerName,
            DeliveryTo = item.DeliveryTo,
            DeliveryStatus = item.DeliveryStatus,
            DeliveredAt = item.DeliveredAt is { } da ? Unspecified(da) : null,
            IsReply = item.IsReply,
            IsFirstReply = item.IsFirstReply,
            IsOffline = item.IsOffline,
            IsOfflineReply = item.IsOfflineReply,
            ResponseTime = item.ResponseTime,
            PageUrl = item.PageUrl,
            AttachmentsJson = item.Attachments is { } a ? JsonSerializer.Serialize(a) : null,
            ChannelType = item.Channel?.Type,
            ChannelId = item.Channel?.Id,
        };

    private static SmartsuppContactData MapContact(SmartsuppContactApiResponse item) =>
        new()
        {
            Id = item.Id ?? "",
            CreatedAt = Unspecified(item.CreatedAt),
            UpdatedAt = Unspecified(item.UpdatedAt),
            Email = item.Email,
            Name = item.Name,
            Phone = item.Phone,
            Note = item.Note,
            BannedAt = item.BannedAt is { } ba ? Unspecified(ba) : null,
            BannedBy = item.BannedBy,
            GdprApproved = item.GdprApproved,
            TagsJson = item.Tags is { } t ? JsonSerializer.Serialize(t) : null,
            PropertiesJson = item.Properties is { } p ? JsonSerializer.Serialize(p) : null,
        };

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);

    // ---- API request shapes (private, internal to adapter) ----

    private sealed class ConversationSearchRequest
    {
        public int Size { get; init; }
        public List<ConversationQueryItem> Query { get; init; } = [];
        public List<ConversationSortItem> Sort { get; init; } = [];
        public JsonElement[]? After { get; init; }
    }

    private sealed class ConversationQueryItem
    {
        public string Field { get; init; } = "";
        public string[] Value { get; init; } = [];
    }

    private sealed class ConversationSortItem
    {
        public string CreatedAt { get; init; } = "desc";
    }

    // ---- API response shapes (private, internal to adapter) ----

    private sealed class SmartsuppSearchApiResponse
    {
        public int Total { get; set; }
        public JsonElement[]? After { get; set; }
        public List<SmartsuppConversationApiItem>? Items { get; set; }
    }

    private sealed class SmartsuppConversationApiItem
    {
        public string? Id { get; set; }
        public string? ExtId { get; set; }
        public string? Status { get; set; }
        public bool Unread { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public SmartsuppChannelApiItem? Channel { get; set; }
        public string? ContactId { get; set; }
        public string? VisitorId { get; set; }
        public List<string>? AgentIds { get; set; }
        public List<string>? AssignedIds { get; set; }
        public string? GroupId { get; set; }
        public int? RatingValue { get; set; }
        public string? RatingText { get; set; }
        public string? Domain { get; set; }
        public string? Referer { get; set; }
        public bool IsOffline { get; set; }
        public bool IsServed { get; set; }
        public JsonElement? Variables { get; set; }
        public JsonElement? Tags { get; set; }
        public SmartsuppLocationApiItem? Location { get; set; }
        public SmartsuppLastMessageApiItem? LastMessage { get; set; }
    }

    private sealed class SmartsuppChannelApiItem
    {
        public string? Type { get; set; }
        public string? Id { get; set; }
    }

    private sealed class SmartsuppLocationApiItem
    {
        public string? Ip { get; set; }
        public string? Code { get; set; }
        public string? Country { get; set; }
        public string? City { get; set; }
    }

    private sealed class SmartsuppLastMessageApiItem
    {
        public string? Text { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    private sealed class SmartsuppMessagesApiResponse
    {
        public int Total { get; set; }
        public string? After { get; set; }
        public List<SmartsuppMessageApiItem>? Items { get; set; }
    }

    private sealed class SmartsuppMessageApiItem
    {
        public string? Id { get; set; }
        public string? ExtId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? Type { get; set; }
        public string? SubType { get; set; }
        public SmartsuppChannelApiItem? Channel { get; set; }
        public string? ConversationId { get; set; }
        public string? VisitorId { get; set; }
        public string? AgentId { get; set; }
        public string? TriggerId { get; set; }
        public string? TriggerName { get; set; }
        public string? DeliveryTo { get; set; }
        public string? DeliveryStatus { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public bool IsReply { get; set; }
        public bool IsFirstReply { get; set; }
        public bool IsOffline { get; set; }
        public bool IsOfflineReply { get; set; }
        public int? ResponseTime { get; set; }
        public JsonElement? Attachments { get; set; }
        public string? PageUrl { get; set; }
        public SmartsuppMessageContentApiItem? Content { get; set; }
    }

    private sealed class SmartsuppMessageContentApiItem
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
        public JsonElement? Data { get; set; }
    }

    private sealed class SmartsuppContactApiResponse
    {
        public string? Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public JsonElement? Properties { get; set; }
        public string? Note { get; set; }
        public DateTime? BannedAt { get; set; }
        public string? BannedBy { get; set; }
        public JsonElement? Tags { get; set; }
        public bool GdprApproved { get; set; }
    }
}
```

- [ ] **Step 2: Verify adapter project builds**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Adapters/Anela.Heblo.Adapters.Smartsupp/Anela.Heblo.Adapters.Smartsupp.csproj 2>&1 | tail -20
```

Expected: Build succeeds (0 errors).

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Adapters/Anela.Heblo.Adapters.Smartsupp/SmartsuppApiClient.cs
git commit -m "feat: add GetContactAsync and expand API DTOs and mappers in SmartsuppApiClient"
```

---

### Task 8: Update SmartsuppSyncJob

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs`

- [ ] **Step 1: Replace SmartsuppSyncJob.cs**

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;

public class SmartsuppSyncJob : IRecurringJob
{
    private const int PageSize = 50;
    private const int LastMessagePreviewMaxLength = 200;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "smartsupp-sync",
        DisplayName = "Smartsupp Sync",
        Description = "Polls Smartsupp API for updated conversations and syncs them to the local database",
        CronExpression = "*/2 * * * *",
        DefaultIsEnabled = false,
    };

    private readonly ISmartsuppApiClient _apiClient;
    private readonly ISmartsuppRepository _repository;
    private readonly ILogger<SmartsuppSyncJob> _logger;

    public SmartsuppSyncJob(
        ISmartsuppApiClient apiClient,
        ISmartsuppRepository repository,
        ILogger<SmartsuppSyncJob> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var syncedAt = DateTime.UtcNow;

        _logger.LogInformation("smartsupp-sync starting");

        var totalUpserted = 0;
        string? cursor = null;
        DateTime? latestUpdatedAt = null;
        var contactCache = new Dictionary<string, SmartsuppContactData?>(StringComparer.Ordinal);

        do
        {
            SmartsuppSearchResult page;

            try
            {
                page = await _apiClient.SearchConversationsAsync(cursor, PageSize, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "smartsupp-sync failed to fetch page (cursor={Cursor}), aborting run", cursor);
                return;
            }

            foreach (var item in page.Items)
            {
                await ProcessConversationAsync(item, syncedAt, contactCache, cancellationToken);
                totalUpserted++;

                if (latestUpdatedAt is null || item.UpdatedAt > latestUpdatedAt)
                    latestUpdatedAt = item.UpdatedAt;
            }

            await _repository.SaveChangesAsync(cancellationToken);
            cursor = page.After;

        } while (cursor is not null);

        if (latestUpdatedAt.HasValue)
            await _repository.SetSyncWatermarkAsync(latestUpdatedAt.Value, cancellationToken);

        _logger.LogInformation("smartsupp-sync completed — {Count} conversations upserted", totalUpserted);
    }

    private async Task ProcessConversationAsync(
        SmartsuppConversationData data,
        DateTime syncedAt,
        Dictionary<string, SmartsuppContactData?> contactCache,
        CancellationToken cancellationToken)
    {
        List<SmartsuppMessageData> messageData;
        try
        {
            messageData = await _apiClient.GetConversationMessagesAsync(data.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch messages for conversation {ConversationId} — conversation upserted without messages",
                data.Id);
            messageData = [];
        }

        SmartsuppContactData? contact = null;
        if (!string.IsNullOrEmpty(data.ContactId))
        {
            contact = await FetchContactCachedAsync(data.ContactId, contactCache, cancellationToken);

            if (contact is not null)
            {
                var contactEntity = new SmartsuppContact
                {
                    Id = contact.Id,
                    Email = contact.Email,
                    Name = contact.Name,
                    Phone = contact.Phone,
                    Note = contact.Note,
                    BannedAt = contact.BannedAt,
                    BannedBy = contact.BannedBy,
                    GdprApproved = contact.GdprApproved,
                    TagsJson = contact.TagsJson,
                    PropertiesJson = contact.PropertiesJson,
                    CreatedAt = Unspecified(contact.CreatedAt),
                    UpdatedAt = Unspecified(contact.UpdatedAt),
                    SyncedAt = Unspecified(syncedAt),
                };
                await _repository.UpsertContactAsync(contactEntity, cancellationToken);
            }
        }

        var status = data.Status?.ToLowerInvariant() == "resolved"
            ? SmartsuppConversationStatus.Resolved
            : SmartsuppConversationStatus.Open;

        var conversation = new SmartsuppConversation
        {
            Id = data.Id,
            ExtId = data.ExtId,
            Status = status,
            IsUnread = data.Unread,
            IsOffline = data.IsOffline,
            IsServed = data.IsServed,
            ContactId = data.ContactId,
            ContactName = contact?.Name,
            ContactEmail = contact?.Email,
            ContactAvatarUrl = null,
            VisitorId = data.VisitorId,
            FinishedAt = data.FinishedAt is { } fa ? Unspecified(fa) : null,
            Domain = data.Domain,
            Referer = data.Referer,
            LocationCountry = data.LocationCountry,
            LocationCity = data.LocationCity,
            LocationIp = data.LocationIp,
            LocationCode = data.LocationCode,
            VariablesJson = data.VariablesJson,
            TagsJson = data.TagsJson,
            LastMessagePreview = data.LastMessageText?.Length > LastMessagePreviewMaxLength
                ? data.LastMessageText[..LastMessagePreviewMaxLength]
                : data.LastMessageText,
            LastMessageAt = data.LastMessageAt,
            CreatedAt = data.CreatedAt,
            UpdatedAt = data.UpdatedAt,
            SyncedAt = syncedAt,
        };

        await _repository.UpsertConversationAsync(conversation, cancellationToken);

        if (messageData.Count == 0)
            return;

        var messages = messageData.Select(m => new SmartsuppMessage
        {
            Id = m.Id,
            ConversationId = data.Id,
            AuthorType = ParseAuthorType(m.SubType),
            SubType = m.SubType,
            AuthorName = ComposeAuthorName(m, contact),
            Content = m.Content,
            TriggerName = m.TriggerName,
            TriggerId = m.TriggerId,
            PageUrl = m.PageUrl,
            AgentId = m.AgentId,
            VisitorId = m.VisitorId,
            DeliveryStatus = m.DeliveryStatus,
            DeliveredAt = m.DeliveredAt is { } da ? Unspecified(da) : null,
            IsOffline = m.IsOffline,
            IsReply = m.IsReply,
            IsFirstReply = m.IsFirstReply,
            ResponseTime = m.ResponseTime,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt,
            AttachmentsJson = m.AttachmentsJson,
        }).ToList();

        await _repository.UpsertMessagesAsync(data.Id, messages, cancellationToken);
    }

    private async Task<SmartsuppContactData?> FetchContactCachedAsync(
        string contactId,
        Dictionary<string, SmartsuppContactData?> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(contactId, out var cached))
            return cached;

        SmartsuppContactData? contact;
        try
        {
            contact = await _apiClient.GetContactAsync(contactId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch contact {ContactId} — conversation proceeds without contact data", contactId);
            contact = null;
        }

        cache[contactId] = contact;
        return contact;
    }

    private static string? ComposeAuthorName(SmartsuppMessageData message, SmartsuppContactData? contact) =>
        ParseAuthorType(message.SubType) switch
        {
            SmartsuppMessageAuthorType.Visitor => contact?.Name,
            SmartsuppMessageAuthorType.Bot => message.TriggerName,
            _ => null
        };

    private static SmartsuppMessageAuthorType ParseAuthorType(string? subType) =>
        subType?.ToLowerInvariant() switch
        {
            "agent" => SmartsuppMessageAuthorType.Agent,
            "bot" => SmartsuppMessageAuthorType.Bot,
            "contact" => SmartsuppMessageAuthorType.Visitor,
            _ => SmartsuppMessageAuthorType.Visitor,
        };

    private static DateTime Unspecified(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
}
```

**Important:** The `contactName` assignment in `ProcessConversationAsync` has been simplified to `contact?.Name` since we're now fetching the actual contact. The old visitor-message-author-name fallback is no longer needed because the contact entity holds the name reliably.

- [ ] **Step 2: Fix the contact name logic** — simplify to remove the convoluted fallback above. The actual content of `ProcessConversationAsync` should use:

```csharp
        var conversation = new SmartsuppConversation
        {
            // ...
            ContactName = contact?.Name,
            ContactEmail = contact?.Email,
            // ...
        };
```

The full sync job above already does this correctly. Verify that the job above doesn't have the complex (broken) fallback code.

- [ ] **Step 3: Verify the application project builds**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj 2>&1 | tail -20
```

Expected: Build succeeds (0 errors).

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppSyncJob.cs
git commit -m "feat: wire GetContactAsync into sync job with per-run caching and fix sub_type author mapping"
```

---

### Task 9: Update SmartsuppApiClientTests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs`

- [ ] **Step 1: Replace SmartsuppApiClientTests.cs with updated + new tests**

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Polly;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppApiClientTests
{
    private static SmartsuppApiClient CreateClient(HttpMessageHandler handler, ResiliencePipeline? pipeline = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.smartsupp.com/v2/") };
        factory.Setup(f => f.CreateClient("Smartsupp")).Returns(httpClient);

        var options = Options.Create(new SmartsuppOptions
        {
            ApiToken = "test-token",
            BaseUrl = "https://api.smartsupp.com/v2/",
        });

        return new SmartsuppApiClient(options, factory.Object, NullLogger<SmartsuppApiClient>.Instance, pipeline);
    }

    [Fact]
    public async Task SearchConversationsAsync_ReturnsItems_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            total = 2,
            after = (string?)null,
            items = new[]
            {
                new
                {
                    id = "coXU9u5VscuzW",
                    ext_id = (string?)null,
                    status = "open",
                    unread = true,
                    created_at = "2026-05-12T18:29:21.336Z",
                    updated_at = "2026-05-12T18:38:15.826Z",
                    finished_at = (string?)null,
                    channel = new { type = "default", id = (string?)null },
                    contact_id = "ctW5HHbqaRKv",
                    visitor_id = "vitCESEI6Lu-SL",
                    agent_ids = Array.Empty<string>(),
                    assigned_ids = Array.Empty<string>(),
                    group_id = (string?)null,
                    rating_value = (int?)null,
                    rating_text = (string?)null,
                    domain = "www.anela.cz",
                    referer = "https://l.facebook.com/",
                    is_offline = true,
                    is_served = false,
                    variables = new { shoptet_shop = "269953", authenticated = true },
                    location = new { ip = "78.102.94.30", code = "CZ", country = "Czechia", city = "Prague" },
                    last_message = new { text = "Dobrý den", created_at = "2026-05-12T18:30:58Z" }
                }
            }
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.SearchConversationsAsync(null, 50, CancellationToken.None);

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(1);
        var item = result.Items[0];
        item.Id.Should().Be("coXU9u5VscuzW");
        item.ContactId.Should().Be("ctW5HHbqaRKv");
        item.VisitorId.Should().Be("vitCESEI6Lu-SL");
        item.Domain.Should().Be("www.anela.cz");
        item.Referer.Should().Be("https://l.facebook.com/");
        item.IsOffline.Should().BeTrue();
        item.IsServed.Should().BeFalse();
        item.LocationCountry.Should().Be("Czechia");
        item.LocationCity.Should().Be("Prague");
        item.LocationIp.Should().Be("78.102.94.30");
        item.LocationCode.Should().Be("CZ");
        item.ChannelType.Should().Be("default");
        item.Unread.Should().BeTrue();
        item.VariablesJson.Should().NotBeNullOrEmpty();
        item.LastMessageText.Should().Be("Dobrý den");
    }

    [Fact]
    public async Task SearchConversationsAsync_ThrowsHttpRequestException_On429()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.SearchConversationsAsync(null, 50, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GetConversationMessagesAsync_ReturnsMessages_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            total = 3,
            items = new[]
            {
                new
                {
                    id = "msCfnmmaEDXAs",
                    ext_id = (string?)null,
                    created_at = "2026-05-12T18:30:58.499Z",
                    updated_at = "2026-05-12T18:30:59.320Z",
                    type = "message",
                    sub_type = "contact",
                    conversation_id = "coXU9u5VscuzW",
                    visitor_id = (string?)null,
                    agent_id = (string?)null,
                    content = new { type = "text", text = "Dobry den, jaky krem doporucite?" },
                    trigger_id = (string?)null,
                    trigger_name = (string?)null,
                    is_reply = false,
                    is_first_reply = false,
                    is_offline = false,
                    is_offline_reply = false,
                    response_time = (int?)null,
                    attachments = Array.Empty<object>(),
                    page_url = "https://www.anela.cz/"
                },
                new
                {
                    id = "msJZcgRsWzE4n",
                    ext_id = (string?)null,
                    created_at = "2026-05-12T18:29:28.055Z",
                    updated_at = "2026-05-12T18:30:59.283Z",
                    type = "message",
                    sub_type = "bot",
                    conversation_id = "coXU9u5VscuzW",
                    visitor_id = (string?)null,
                    agent_id = (string?)null,
                    content = new { type = "text", text = "Momentálně nejsme on-line." },
                    trigger_id = "bolCGGiw7mLz",
                    trigger_name = "2_Jsme offline_druhá zpráva",
                    is_reply = false,
                    is_first_reply = false,
                    is_offline = false,
                    is_offline_reply = false,
                    response_time = (int?)null,
                    attachments = Array.Empty<object>(),
                    page_url = "https://www.anela.cz/"
                }
            }
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetConversationMessagesAsync("coXU9u5VscuzW", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        var contactMsg = result[0];
        contactMsg.Id.Should().Be("msCfnmmaEDXAs");
        contactMsg.SubType.Should().Be("contact");
        contactMsg.Content.Should().Be("Dobry den, jaky krem doporucite?");
        contactMsg.PageUrl.Should().Be("https://www.anela.cz/");
        contactMsg.ConversationId.Should().Be("coXU9u5VscuzW");

        var botMsg = result[1];
        botMsg.Id.Should().Be("msJZcgRsWzE4n");
        botMsg.SubType.Should().Be("bot");
        botMsg.TriggerName.Should().Be("2_Jsme offline_druhá zpráva");
        botMsg.TriggerId.Should().Be("bolCGGiw7mLz");
    }

    [Fact]
    public async Task GetConversationMessagesAsync_ThrowsHttpRequestException_OnErrorResponse()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.GetConversationMessagesAsync("conv-missing", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetContactAsync_ReturnsContact_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "ct297LB_vFeHN",
            created_at = "2026-05-09T14:40:49.044Z",
            updated_at = "2026-05-12T17:12:36.745Z",
            email = "vexy@post.cz",
            name = "Monča",
            phone = (string?)null,
            properties = new { },
            note = (string?)null,
            banned_at = (string?)null,
            banned_by = (string?)null,
            tags = new { type = "list", data = Array.Empty<object>(), total = 0 },
            gdpr_approved = false
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetContactAsync("ct297LB_vFeHN", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("ct297LB_vFeHN");
        result.Email.Should().Be("vexy@post.cz");
        result.Name.Should().Be("Monča");
        result.Phone.Should().BeNull();
        result.GdprApproved.Should().BeFalse();
        result.TagsJson.Should().NotBeNullOrEmpty();
        result.PropertiesJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetContactAsync_ReturnsNull_On404()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetContactAsync("ct-missing", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetContactAsync_ThrowsHttpRequestException_On500()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.GetContactAsync("ct-error", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.InternalServerError);
    }
}
```

- [ ] **Step 2: Run the API client tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SmartsuppApiClientTests" \
  --no-build 2>&1 | tail -30
```

Expected: Build first, then all 7 tests pass (2 search + 2 messages + 3 contact).

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppApiClientTests.cs
git commit -m "test: update SmartsuppApiClientTests — real JSON shapes, sub_type, GetContactAsync"
```

---

### Task 10: Update SmartsuppSyncJobTests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs`

- [ ] **Step 1: Replace SmartsuppSyncJobTests.cs**

```csharp
using Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppSyncJobTests
{
    private readonly Mock<ISmartsuppApiClient> _apiClient = new();
    private readonly Mock<ISmartsuppRepository> _repo = new();

    private SmartsuppSyncJob CreateJob() =>
        new(_apiClient.Object, _repo.Object, NullLogger<SmartsuppSyncJob>.Instance);

    private static SmartsuppConversationData MakeConversation(string id, string? contactId = null) =>
        new()
        {
            Id = id,
            Status = "open",
            Unread = false,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow,
            ContactId = contactId,
        };

    private static SmartsuppContactData MakeContact(string id, string? name = "Test User", string? email = "test@test.cz") =>
        new()
        {
            Id = id,
            Name = name,
            Email = email,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
        };

    private void SetupRepoDefaults()
    {
        _repo.Setup(r => r.UpsertContactAsync(It.IsAny<SmartsuppContact>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertMessagesAsync(It.IsAny<string>(), It.IsAny<List<SmartsuppMessage>>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task ExecuteAsync_UpsertsSinglePage_WhenAfterIsNull()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", "Petra");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.ContactName == "Petra"),
            It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PagesThrough_WhenAfterIsNotNull()
    {
        // Arrange
        _apiClient.SetupSequence(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult
                  {
                      Total = 2,
                      After = "cursor-page2",
                      Items = [MakeConversation("c1")]
                  });
        _apiClient.Setup(c => c.SearchConversationsAsync("cursor-page2", 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult
                  {
                      Total = 2,
                      After = null,
                      Items = [MakeConversation("c2")]
                  });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((SmartsuppContactData?)null);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c1"), It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertConversationAsync(It.Is<SmartsuppConversation>(c => c.Id == "c2"), It.IsAny<CancellationToken>()), Times.Once);
        _apiClient.Verify(c => c.SearchConversationsAsync("cursor-page2", 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FetchesContact_AndUpsertsIt()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", "Monča", "vexy@post.cz");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _apiClient.Verify(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertContactAsync(
            It.Is<SmartsuppContact>(c => c.Id == "ct1" && c.Name == "Monča" && c.Email == "vexy@post.cz"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CachesContact_AcrossConversationsInSameRun()
    {
        // Arrange — two conversations share the same contact_id
        var c1 = MakeConversation("conv1", contactId: "ct-shared");
        var c2 = MakeConversation("conv2", contactId: "ct-shared");
        var contact = MakeContact("ct-shared", "Shared User");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 2, After = null, Items = [c1, c2] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct-shared", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert — GetContactAsync called exactly once despite two conversations sharing the id
        _apiClient.Verify(c => c.GetContactAsync("ct-shared", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesContactName_ForConversationContactName()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", name: "Monča");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.ContactName == "Monča"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesContactNameNull_WhenContactFetchFails()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new List<SmartsuppMessageData>());
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new HttpRequestException("500"));
        SetupRepoDefaults();

        // Act — should not throw; warning is logged and processing continues
        await CreateJob().ExecuteAsync();

        // Assert — conversation still upserted, just without a name
        _repo.Verify(r => r.UpsertConversationAsync(
            It.Is<SmartsuppConversation>(c => c.Id == "c1" && c.ContactName == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MapsSubTypeBot_ToAuthorTypeBot()
    {
        // Arrange
        var conversation = MakeConversation("c1");
        var botMessage = new SmartsuppMessageData
        {
            Id = "m1",
            SubType = "bot",
            Content = "Vítejte!",
            TriggerName = "Uvítání",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync([botMessage]);
        _apiClient.Setup(c => c.GetContactAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((SmartsuppContactData?)null);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertMessagesAsync("c1",
            It.Is<List<SmartsuppMessage>>(msgs =>
                msgs.Any(m => m.Id == "m1"
                    && m.AuthorType == SmartsuppMessageAuthorType.Bot
                    && m.AuthorName == "Uvítání"
                    && m.SubType == "bot")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MapsSubTypeContact_ToAuthorTypeVisitor()
    {
        // Arrange
        var conversation = MakeConversation("c1", contactId: "ct1");
        var contact = MakeContact("ct1", "Jana");
        var visitorMessage = new SmartsuppMessageData
        {
            Id = "m2",
            SubType = "contact",
            Content = "Potřebuji pomoc.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 1, After = null, Items = [conversation] });
        _apiClient.Setup(c => c.GetConversationMessagesAsync("c1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync([visitorMessage]);
        _apiClient.Setup(c => c.GetContactAsync("ct1", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(contact);
        SetupRepoDefaults();

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.UpsertMessagesAsync("c1",
            It.Is<List<SmartsuppMessage>>(msgs =>
                msgs.Any(m => m.Id == "m2"
                    && m.AuthorType == SmartsuppMessageAuthorType.Visitor
                    && m.AuthorName == "Jana")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotAdvanceWatermark_WhenNoConversationsReturned()
    {
        // Arrange
        _apiClient.Setup(c => c.SearchConversationsAsync(null, 50, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SmartsuppSearchResult { Total = 0, After = null, Items = new() });
        _repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        await CreateJob().ExecuteAsync();

        // Assert
        _repo.Verify(r => r.SetSyncWatermarkAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Run all Smartsupp tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Smartsupp" 2>&1 | tail -30
```

Expected: All Smartsupp tests pass (both API client and sync job).

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/test/Anela.Heblo.Tests/Features/Smartsupp/SmartsuppSyncJobTests.cs
git commit -m "test: update SmartsuppSyncJobTests — contact caching, sub_type mapping, new assertions"
```

---

### Task 11: Final build and format verification

- [ ] **Step 1: Full solution build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln 2>&1 | grep -E "(error|warning|Build succeeded)" | tail -20
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 2: Run dotnet format**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet format Anela.Heblo.sln --verify-no-changes 2>&1 | tail -10
```

If format reports changes, apply them:

```bash
dotnet format Anela.Heblo.sln
```

Then commit:

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add -u backend/
git commit -m "chore: apply dotnet format"
```

- [ ] **Step 3: Run all Smartsupp tests one final time**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Smartsupp" -v normal 2>&1 | tail -30
```

Expected: All pass.

---

## Verification Checklist

- [ ] `dotnet build` clean (0 errors)
- [ ] `dotnet format --verify-no-changes` clean
- [ ] All Smartsupp unit tests pass
- [ ] Migration created (not empty)
- [ ] `SmartsuppContacts` table exists in migration Up()
- [ ] New columns in `SmartsuppConversations` (ContactId, Domain, VariablesJson, etc.)
- [ ] New columns in `SmartsuppMessages` (SubType, PageUrl, TriggerName, etc.)
