# Task Plan: Route Smartsupp Webhook-Audit Access Through a Repository Contract (feat-3877)

Backend-only, behavior-preserving .NET 8 refactor. It removes the five direct
`ApplicationDbContext` dependencies the arch-review found in the Smartsupp Application slice by
introducing a Domain-declared `ISmartsuppWebhookAuditRepository` (absorbing the misplaced
`ISmartsuppWebhookAuditWriter`) for the four audit-table classes plus the webhook controller, and by
adding one narrowly-scoped method to the existing `ISmartsuppRepository` for the
`RefreshOrphanContactsHandler` outlier. Two independent tasks — different files, no shared code
between them, either can be implemented and verified on its own.

---

### task: create-smartsupp-webhook-audit-repository

**Goal**

Introduce `ISmartsuppWebhookAuditRepository` (Domain) / `SmartsuppWebhookAuditRepository`
(Persistence), covering create, update-outcome, list, get, get-for-replay, save, and purge for
`SmartsuppWebhookAuditEntry`. Rewire `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`,
`ReplayWebhookEventHandler`, `SmartsuppWebhookAuditCleanupJob`, and `SmartsuppWebhookController` to
use it instead of `ApplicationDbContext` / the old `ISmartsuppWebhookAuditWriter`. Delete
`ISmartsuppWebhookAuditWriter` and `SmartsuppWebhookAuditWriter`. Update the DI binding in
`SmartsuppModule.cs`. Update the four existing handler/job unit test files (wrap the in-memory
`ApplicationDbContext` in a real `SmartsuppWebhookAuditRepository` instead of passing the context
straight to the unit under test) and rename/extend `SmartsuppWebhookAuditWriterTests.cs` into
`SmartsuppWebhookAuditRepositoryTests.cs`. No behavior, HTTP contract, or schema change.

**Context** (self-contained — you only read this section)

Today `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`, `ReplayWebhookEventHandler`, and
`SmartsuppWebhookAuditCleanupJob` each inject `Anela.Heblo.Persistence.ApplicationDbContext`
directly and query `_context.SmartsuppWebhookAuditEntries` inline. `SmartsuppWebhookController`
injects `Anela.Heblo.Persistence.Smartsupp.ISmartsuppWebhookAuditWriter` (create + update-outcome
only) — the only interface in `Anela.Heblo.Persistence` and the only feature-level
`using Anela.Heblo.Persistence` in the API project. Every other Smartsupp contract
(`ISmartsuppRepository`, `ISmartsuppPresenceRepository`, `ISmartsuppApiClient`) lives in
`Anela.Heblo.Domain/Features/Smartsupp/`, implemented in `Anela.Heblo.Persistence/Smartsupp/`, bound
in `SmartsuppModule.cs` (ADR-004). This task brings the audit table in line with that pattern,
following `SmartsuppPresenceRepository`'s precedent exactly (own table, own lifecycle, own
repository, sitting next to `ISmartsuppRepository` rather than folded into it).

`SmartsuppWebhookAuditEntry` (`backend/src/Anela.Heblo.Domain/Features/Smartsupp/
SmartsuppWebhookAuditEntry.cs`, unchanged by this task):
```csharp
namespace Anela.Heblo.Domain.Features.Smartsupp;

public class SmartsuppWebhookAuditEntry
{
    public Guid Id { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string RemoteIp { get; set; } = "";
    public string? SignatureHeader { get; set; }
    public SmartsuppWebhookSignatureStatus SignatureStatus { get; set; }
    public string HeadersJson { get; set; } = "";
    public string RawBody { get; set; } = "";
    public int BodySizeBytes { get; set; }
    public string? EventName { get; set; }
    public string? AccountId { get; set; }
    public string? AppId { get; set; }
    public DateTime? EventTimestamp { get; set; }
    public SmartsuppWebhookProcessingStatus ProcessingStatus { get; set; }
    public string? ProcessingError { get; set; }
    public int ProcessingDurationMs { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int ReplayCount { get; set; }
    public DateTime? LastReplayedAt { get; set; }
    public string? LastReplayedBy { get; set; }
}
```

No DB migration, no DTO/route/error-code changes. `ListAsync` returns domain entities, not
`WebhookAuditSummaryDto` — DTO projection stays in the handler (Domain must not know about
Application-layer DTOs); this is a deliberate, approved change from today's code (which projects
inside the EF query) — see arch-review Specification Amendment #2. `skip`/`take` passed into
`ListAsync` are already clamped by the handler (`MaxTake = 200`); the repository does not re-clamp.
`PurgeOlderThanAsync` takes an already-computed `cutoff` (the job keeps `RetentionDays = 7` and the
`DateTime.UtcNow.AddDays(-RetentionDays)` computation) and returns the deleted count, mirroring
`ISmartsuppPresenceRepository.PurgeExpiredAsync`'s existing return-count convention used by the same
job today.

**Files to create/modify/delete**

- Create: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppWebhookAuditRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditRepository.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditWriter.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/SmartsuppModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ListWebhookAudit/ListWebhookAuditHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GetWebhookAuditEntry/GetWebhookAuditEntryHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/ReplayWebhookEvent/ReplayWebhookEventHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Infrastructure/Jobs/SmartsuppWebhookAuditCleanupJob.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/SmartsuppWebhookController.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ListWebhookAuditHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/GetWebhookAuditEntryHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/ReplayWebhookEventHandlerTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditCleanupJobTests.cs`
- Rename + extend: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditWriterTests.cs`
  → `backend/test/Anela.Heblo.Tests/Features/Smartsupp/WebhookAudit/SmartsuppWebhookAuditRepositoryTests.cs`

**Implementation steps**

1. **Domain contract — create `ISmartsuppWebhookAuditRepository.cs`**

   ```csharp
   namespace Anela.Heblo.Domain.Features.Smartsupp;

   public interface ISmartsuppWebhookAuditRepository
   {
       Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken);

       Task UpdateOutcomeAsync(
           Guid id,
           SmartsuppWebhookProcessingStatus status,
           string? error,
           int durationMs,
           CancellationToken cancellationToken);

       Task<(List<SmartsuppWebhookAuditEntry> Items, int Total)> ListAsync(
           DateTime? from,
           DateTime? to,
           string? eventName,
           SmartsuppWebhookSignatureStatus? signatureStatus,
           SmartsuppWebhookProcessingStatus? processingStatus,
           int skip,
           int take,
           CancellationToken cancellationToken);

       Task<SmartsuppWebhookAuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

       /// <summary>
       /// Tracked read for the replay flow — caller mutates ReplayCount/LastReplayedAt/LastReplayedBy
       /// on the returned entity, then calls SaveChangesAsync.
       /// </summary>
       Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken cancellationToken);

       Task SaveChangesAsync(CancellationToken cancellationToken);

       /// <summary>Deletes entries with ReceivedAt older than <paramref name="cutoff"/>; returns the count deleted.</summary>
       Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken);
   }
   ```

2. **Persistence implementation — create `SmartsuppWebhookAuditRepository.cs`**

   ```csharp
   using Anela.Heblo.Domain.Features.Smartsupp;
   using Microsoft.EntityFrameworkCore;

   namespace Anela.Heblo.Persistence.Smartsupp;

   public sealed class SmartsuppWebhookAuditRepository : ISmartsuppWebhookAuditRepository
   {
       private readonly ApplicationDbContext _db;

       public SmartsuppWebhookAuditRepository(ApplicationDbContext db)
       {
           _db = db;
       }

       public async Task<Guid> CreateAsync(SmartsuppWebhookAuditEntry entry, CancellationToken cancellationToken)
       {
           if (entry.Id == Guid.Empty)
               entry.Id = Guid.NewGuid();

           _db.SmartsuppWebhookAuditEntries.Add(entry);
           await _db.SaveChangesAsync(cancellationToken);
           return entry.Id;
       }

       public async Task UpdateOutcomeAsync(
           Guid id,
           SmartsuppWebhookProcessingStatus status,
           string? error,
           int durationMs,
           CancellationToken cancellationToken)
       {
           var entry = await _db.SmartsuppWebhookAuditEntries
               .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
           if (entry is null) return;

           entry.ProcessingStatus = status;
           entry.ProcessingError = error;
           entry.ProcessingDurationMs = durationMs;
           entry.ProcessedAt = DateTime.UtcNow;
           await _db.SaveChangesAsync(cancellationToken);
       }

       public async Task<(List<SmartsuppWebhookAuditEntry> Items, int Total)> ListAsync(
           DateTime? from,
           DateTime? to,
           string? eventName,
           SmartsuppWebhookSignatureStatus? signatureStatus,
           SmartsuppWebhookProcessingStatus? processingStatus,
           int skip,
           int take,
           CancellationToken cancellationToken)
       {
           var query = _db.SmartsuppWebhookAuditEntries.AsNoTracking().AsQueryable();
           if (from.HasValue) query = query.Where(e => e.ReceivedAt >= from.Value);
           if (to.HasValue) query = query.Where(e => e.ReceivedAt <= to.Value);
           if (!string.IsNullOrWhiteSpace(eventName)) query = query.Where(e => e.EventName == eventName);
           if (signatureStatus.HasValue) query = query.Where(e => e.SignatureStatus == signatureStatus.Value);
           if (processingStatus.HasValue) query = query.Where(e => e.ProcessingStatus == processingStatus.Value);

           var total = await query.CountAsync(cancellationToken);
           var items = await query
               .OrderByDescending(e => e.ReceivedAt)
               .Skip(skip)
               .Take(take)
               .ToListAsync(cancellationToken);

           return (items, total);
       }

       public async Task<SmartsuppWebhookAuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
           await _db.SmartsuppWebhookAuditEntries
               .AsNoTracking()
               .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

       public async Task<SmartsuppWebhookAuditEntry?> GetForReplayAsync(Guid id, CancellationToken cancellationToken) =>
           await _db.SmartsuppWebhookAuditEntries
               .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

       public async Task SaveChangesAsync(CancellationToken cancellationToken) =>
           await _db.SaveChangesAsync(cancellationToken);

       public async Task<int> PurgeOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken)
       {
           var stale = await _db.SmartsuppWebhookAuditEntries
               .Where(e => e.ReceivedAt < cutoff)
               .ToListAsync(cancellationToken);

           if (stale.Count == 0)
               return 0;

           _db.SmartsuppWebhookAuditEntries.RemoveRange(stale);
           await _db.SaveChangesAsync(cancellationToken);
           return stale.Count;
       }
   }
   ```

   Note `ListAsync` adds `.AsNoTracking()`, which the original inline handler query did not state
   explicitly (it relied on `.Select(...)` into a DTO to avoid materializing tracked entities). Since
   this method now returns full entities, `AsNoTracking()` is required to keep the same no-tracking
   read semantics — this is not a behavior change from the caller's perspective.

3. **Delete the superseded writer** — remove
   `backend/src/Anela.Heblo.Persistence/Smartsupp/ISmartsuppWebhookAuditWriter.cs` and
   `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppWebhookAuditWriter.cs` entirely.

4. **DI binding — `SmartsuppModule.cs`.** Replace:
   ```csharp
   services.AddScoped<ISmartsuppWebhookAuditWriter, SmartsuppWebhookAuditWriter>();
   ```
   with:
   ```csharp
   services.AddScoped<ISmartsuppWebhookAuditRepository, SmartsuppWebhookAuditRepository>();
   ```
   Also add `using Anela.Heblo.Persistence.Smartsupp;` if not already present (it already is, per the
   existing `using` block) — no import changes needed beyond the interface itself, which lives in
   `Anela.Heblo.Domain.Features.Smartsupp`, already imported.

5. **`ListWebhookAuditHandler.cs` — full replacement:**
   ```csharp
   using Anela.Heblo.Domain.Features.Smartsupp;
   using MediatR;

   namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;

   public class ListWebhookAuditHandler
       : IRequestHandler<ListWebhookAuditRequest, ListWebhookAuditResponse>
   {
       private const int MaxTake = 200;

       private readonly ISmartsuppWebhookAuditRepository _repository;

       public ListWebhookAuditHandler(ISmartsuppWebhookAuditRepository repository)
       {
           _repository = repository;
       }

       public async Task<ListWebhookAuditResponse> Handle(
           ListWebhookAuditRequest request,
           CancellationToken cancellationToken)
       {
           var skip = Math.Max(0, request.Skip);
           var take = Math.Clamp(request.Take, 1, MaxTake);

           var (entries, total) = await _repository.ListAsync(
               request.From,
               request.To,
               request.EventName,
               request.SignatureStatus,
               request.ProcessingStatus,
               skip,
               take,
               cancellationToken);

           var rows = entries.Select(e => new WebhookAuditSummaryDto
           {
               Id = e.Id,
               ReceivedAt = e.ReceivedAt,
               EventName = e.EventName,
               AccountId = e.AccountId,
               AppId = e.AppId,
               SignatureStatus = e.SignatureStatus,
               ProcessingStatus = e.ProcessingStatus,
               BodySizeBytes = e.BodySizeBytes,
               ProcessingDurationMs = e.ProcessingDurationMs,
               ReplayCount = e.ReplayCount,
               LastReplayedAt = e.LastReplayedAt,
               ProcessedAt = e.ProcessedAt,
           }).ToList();

           return new ListWebhookAuditResponse
           {
               Items = rows,
               Total = total,
               Skip = skip,
               PageSize = take,
           };
       }
   }
   ```

6. **`GetWebhookAuditEntryHandler.cs` — full replacement:**
   ```csharp
   using Anela.Heblo.Application.Shared;
   using Anela.Heblo.Domain.Features.Smartsupp;
   using MediatR;

   namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetWebhookAuditEntry;

   public class GetWebhookAuditEntryHandler
       : IRequestHandler<GetWebhookAuditEntryRequest, GetWebhookAuditEntryResponse>
   {
       private readonly ISmartsuppWebhookAuditRepository _repository;

       public GetWebhookAuditEntryHandler(ISmartsuppWebhookAuditRepository repository)
       {
           _repository = repository;
       }

       public async Task<GetWebhookAuditEntryResponse> Handle(
           GetWebhookAuditEntryRequest request,
           CancellationToken cancellationToken)
       {
           var entry = await _repository.GetByIdAsync(request.Id, cancellationToken);

           if (entry is null)
               return new GetWebhookAuditEntryResponse(ErrorCodes.ResourceNotFound);

           return new GetWebhookAuditEntryResponse
           {
               Entry = new WebhookAuditEntryDto
               {
                   Id = entry.Id,
                   ReceivedAt = entry.ReceivedAt,
                   RemoteIp = entry.RemoteIp,
                   SignatureHeader = entry.SignatureHeader,
                   SignatureStatus = entry.SignatureStatus,
                   HeadersJson = entry.HeadersJson,
                   RawBody = entry.RawBody,
                   BodySizeBytes = entry.BodySizeBytes,
                   EventName = entry.EventName,
                   AccountId = entry.AccountId,
                   AppId = entry.AppId,
                   EventTimestamp = entry.EventTimestamp,
                   ProcessingStatus = entry.ProcessingStatus,
                   ProcessingError = entry.ProcessingError,
                   ProcessingDurationMs = entry.ProcessingDurationMs,
                   ProcessedAt = entry.ProcessedAt,
                   ReplayCount = entry.ReplayCount,
                   LastReplayedAt = entry.LastReplayedAt,
                   LastReplayedBy = entry.LastReplayedBy,
               },
           };
       }
   }
   ```

7. **`ReplayWebhookEventHandler.cs` — full replacement:**
   ```csharp
   using System.Text.Json;
   using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
   using Anela.Heblo.Application.Shared;
   using Anela.Heblo.Domain.Features.Smartsupp;
   using MediatR;

   namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;

   public class ReplayWebhookEventHandler
       : IRequestHandler<ReplayWebhookEventRequest, ReplayWebhookEventResponse>
   {
       private readonly ISmartsuppWebhookAuditRepository _repository;
       private readonly IMediator _mediator;

       public ReplayWebhookEventHandler(ISmartsuppWebhookAuditRepository repository, IMediator mediator)
       {
           _repository = repository;
           _mediator = mediator;
       }

       public async Task<ReplayWebhookEventResponse> Handle(
           ReplayWebhookEventRequest request,
           CancellationToken cancellationToken)
       {
           var entry = await _repository.GetForReplayAsync(request.Id, cancellationToken);

           if (entry is null)
               return new ReplayWebhookEventResponse(ErrorCodes.ResourceNotFound);

           JsonElement data;
           try
           {
               using var doc = JsonDocument.Parse(entry.RawBody);
               data = doc.RootElement.TryGetProperty("data", out var d) ? d.Clone() : default;
           }
           catch (JsonException)
           {
               return new ReplayWebhookEventResponse(ErrorCodes.InvalidOperation);
           }

           var timestamp = entry.EventTimestamp ?? DateTime.UtcNow;

           await _mediator.Send(new ProcessWebhookEventRequest
           {
               EventName = entry.EventName ?? "",
               Timestamp = timestamp,
               AccountId = entry.AccountId ?? "",
               AppId = entry.AppId ?? "",
               Data = data,
           }, cancellationToken);

           entry.ReplayCount += 1;
           entry.LastReplayedAt = DateTime.UtcNow;
           entry.LastReplayedBy = request.ReplayedBy;
           await _repository.SaveChangesAsync(cancellationToken);

           return new ReplayWebhookEventResponse
           {
               ReplayCount = entry.ReplayCount,
               LastReplayedAt = entry.LastReplayedAt,
           };
       }
   }
   ```

8. **`SmartsuppWebhookAuditCleanupJob.cs` — full replacement:**
   ```csharp
   using Anela.Heblo.Domain.Features.BackgroundJobs;
   using Anela.Heblo.Domain.Features.Smartsupp;
   using Microsoft.Extensions.Logging;

   namespace Anela.Heblo.Application.Features.Smartsupp.Infrastructure.Jobs;

   public class SmartsuppWebhookAuditCleanupJob : IRecurringJob
   {
       private const int RetentionDays = 7;

       // Presence rows expire on read within minutes (heartbeat/webhook TTLs). Anything older than a
       // day is certainly dead — purge it so the table never accumulates abandoned rows.
       private const int PresenceRetentionDays = 1;

       private readonly ISmartsuppWebhookAuditRepository _auditRepository;
       private readonly ISmartsuppPresenceRepository _presenceRepository;
       private readonly ILogger<SmartsuppWebhookAuditCleanupJob> _logger;

       public RecurringJobMetadata Metadata { get; } = new()
       {
           JobName = "smartsupp-webhook-audit-cleanup",
           DisplayName = "Smartsupp Webhook Audit Cleanup",
           Description = "Deletes Smartsupp webhook audit entries older than 7 days.",
           CronExpression = "30 3 * * *",
           DefaultIsEnabled = true,
       };

       public SmartsuppWebhookAuditCleanupJob(
           ISmartsuppWebhookAuditRepository auditRepository,
           ISmartsuppPresenceRepository presenceRepository,
           ILogger<SmartsuppWebhookAuditCleanupJob> logger)
       {
           _auditRepository = auditRepository;
           _presenceRepository = presenceRepository;
           _logger = logger;
       }

       public async Task ExecuteAsync(CancellationToken cancellationToken = default)
       {
           var presenceCutoff = DateTime.SpecifyKind(
               DateTime.UtcNow.AddDays(-PresenceRetentionDays), DateTimeKind.Unspecified);
           var deletedPresence = await _presenceRepository.PurgeExpiredAsync(
               presenceCutoff, presenceCutoff, cancellationToken);
           if (deletedPresence > 0)
               _logger.LogInformation("smartsupp presence cleanup: deleted {Count} stale rows", deletedPresence);

           var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

           var deletedCount = await _auditRepository.PurgeOlderThanAsync(cutoff, cancellationToken);

           if (deletedCount == 0)
           {
               _logger.LogInformation("smartsupp webhook audit cleanup: nothing to delete");
               return;
           }

           _logger.LogInformation("smartsupp webhook audit cleanup: deleted {Count} entries older than {Cutoff:o}",
               deletedCount, cutoff);
       }
   }
   ```

9. **`SmartsuppWebhookController.cs` — swap the injected dependency.**
   - Change the `using Anela.Heblo.Persistence.Smartsupp;` line to nothing (remove it — no longer
     needed; `Anela.Heblo.Domain.Features.Smartsupp` is already imported for
     `SmartsuppWebhookAuditEntry`/`SmartsuppWebhookProcessingStatus`/`SmartsuppWebhookSignatureStatus`).
   - Change the field/constructor parameter:
     ```csharp
     private readonly ISmartsuppWebhookAuditRepository _audit;

     public SmartsuppWebhookController(
         IMediator mediator,
         IOptions<SmartsuppOptions> options,
         ISmartsuppWebhookMetrics metrics,
         ISmartsuppWebhookAuditRepository audit,
         ILogger<SmartsuppWebhookController> logger)
     {
         _mediator = mediator;
         _options = options.Value;
         _metrics = metrics;
         _audit = audit;
         _logger = logger;
     }
     ```
   - The four call sites inside `Receive` (`await _audit.CreateAsync(entry, cancellationToken);` ×3
     and `await _audit.UpdateOutcomeAsync(auditId, ...)` at the end) are unchanged — same method
     names, same arguments, same call order relative to `return Unauthorized()`/`return Ok()`.

10. **Migrate the four existing handler/job test files to construct a real
    `SmartsuppWebhookAuditRepository` around the in-memory `ApplicationDbContext`, instead of
    passing the context straight to the unit under test.** This preserves every existing
    arrange/assert block verbatim — only the constructor call for the unit under test changes.

    `ListWebhookAuditHandlerTests.cs` — add `using Anela.Heblo.Persistence.Smartsupp;`, change every
    `new ListWebhookAuditHandler(ctx)` to `new ListWebhookAuditHandler(new SmartsuppWebhookAuditRepository(ctx))`.

    `GetWebhookAuditEntryHandlerTests.cs` — add `using Anela.Heblo.Persistence.Smartsupp;`, change
    `new GetWebhookAuditEntryHandler(ctx)` to
    `new GetWebhookAuditEntryHandler(new SmartsuppWebhookAuditRepository(ctx))` (both call sites, in
    `Handle_ReturnsEntry_WhenIdExists` and `Handle_ReturnsResourceNotFound_WhenIdMissing`).

    `ReplayWebhookEventHandlerTests.cs` — add `using Anela.Heblo.Persistence.Smartsupp;`, change every
    `new ReplayWebhookEventHandler(ctx, ...)` to
    `new ReplayWebhookEventHandler(new SmartsuppWebhookAuditRepository(ctx), ...)` (three call sites:
    `Handle_DispatchesProcessWebhookEvent_AndIncrementsReplayCount`,
    `Handle_ReturnsResourceNotFound_WhenIdMissing`, `Handle_ReturnsInvalidOperation_WhenRawBodyIsMalformedJson`).
    Leave the `ctx.SmartsuppWebhookAuditEntries...` seeding/assertion lines untouched — the test still
    owns the `ApplicationDbContext` for arrange/assert, only the constructor argument to the handler
    under test changes.

    `SmartsuppWebhookAuditCleanupJobTests.cs` — add `using Anela.Heblo.Persistence.Smartsupp;`, change
    both `new SmartsuppWebhookAuditCleanupJob(ctx, CreatePresenceRepo(), ...)` calls to
    `new SmartsuppWebhookAuditCleanupJob(new SmartsuppWebhookAuditRepository(ctx), CreatePresenceRepo(), ...)`.

11. **Rename `SmartsuppWebhookAuditWriterTests.cs` to `SmartsuppWebhookAuditRepositoryTests.cs`** (same
    directory), update the class name to `SmartsuppWebhookAuditRepositoryTests`, and replace
    `new SmartsuppWebhookAuditWriter(ctx)` with `new SmartsuppWebhookAuditRepository(ctx)` in both
    existing tests (`CreateAsync_PersistsEntry_WithGeneratedId`,
    `UpdateOutcomeAsync_SetsProcessingStatusAndDuration` — keep their bodies otherwise unchanged). Then
    append the new tests listed under "Tests to write" below to cover `ListAsync`, `GetByIdAsync`,
    `GetForReplayAsync`, and `PurgeOlderThanAsync`.

**Tests to write** (appended to the renamed `SmartsuppWebhookAuditRepositoryTests.cs`)

```csharp
[Fact]
public async Task ListAsync_ReturnsRowsOrderedByReceivedAtDescending_WithTotal()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);
    ctx.SmartsuppWebhookAuditEntries.AddRange(
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow.AddMinutes(-2), EventName = "a",
            RawBody = "{}", SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        },
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow.AddMinutes(-1), EventName = "b",
            RawBody = "{}", SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
    await ctx.SaveChangesAsync();

    var (items, total) = await repo.ListAsync(
        null, null, null, null, null, skip: 0, take: 50, default);

    items.Should().HaveCount(2);
    items[0].EventName.Should().Be("b");
    items[1].EventName.Should().Be("a");
    total.Should().Be(2);
}

[Fact]
public async Task ListAsync_FiltersByEventNameAndProcessingStatus()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);
    ctx.SmartsuppWebhookAuditEntries.AddRange(
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow, EventName = "conv.opened",
            RawBody = "{}", SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        },
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow, EventName = "conv.opened",
            RawBody = "{}", SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.HandlerException,
        },
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow, EventName = "conv.closed",
            RawBody = "{}", SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
    await ctx.SaveChangesAsync();

    var (items, _) = await repo.ListAsync(
        null, null, "conv.opened", null, SmartsuppWebhookProcessingStatus.HandlerException,
        skip: 0, take: 50, default);

    items.Should().ContainSingle()
        .Which.ProcessingStatus.Should().Be(SmartsuppWebhookProcessingStatus.HandlerException);
}

[Fact]
public async Task ListAsync_AppliesSkipAndTake()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);
    for (var i = 0; i < 5; i++)
    {
        ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = DateTime.UtcNow.AddSeconds(-i), EventName = $"e{i}",
            RawBody = "{}", SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
    }
    await ctx.SaveChangesAsync();

    var (items, total) = await repo.ListAsync(
        null, null, null, null, null, skip: 1, take: 2, default);

    items.Should().HaveCount(2);
    total.Should().Be(5);
}

[Fact]
public async Task GetByIdAsync_ReturnsEntry_WhenExists()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);
    var id = Guid.NewGuid();
    ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
    {
        Id = id, ReceivedAt = DateTime.UtcNow, RawBody = "{\"k\":1}",
        SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
        ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
    });
    await ctx.SaveChangesAsync();

    var entry = await repo.GetByIdAsync(id, default);

    entry.Should().NotBeNull();
    entry!.RawBody.Should().Be("{\"k\":1}");
}

[Fact]
public async Task GetByIdAsync_ReturnsNull_WhenMissing()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);

    var entry = await repo.GetByIdAsync(Guid.NewGuid(), default);

    entry.Should().BeNull();
}

[Fact]
public async Task GetForReplayAsync_ReturnsTrackedEntry_MutationsPersistOnSaveChanges()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);
    var id = Guid.NewGuid();
    ctx.SmartsuppWebhookAuditEntries.Add(new SmartsuppWebhookAuditEntry
    {
        Id = id, ReceivedAt = DateTime.UtcNow, RawBody = "{}",
        SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
        ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
    });
    await ctx.SaveChangesAsync();

    var entry = await repo.GetForReplayAsync(id, default);
    entry.Should().NotBeNull();
    entry!.ReplayCount = 1;
    entry.LastReplayedAt = DateTime.UtcNow;
    entry.LastReplayedBy = "tester";
    await repo.SaveChangesAsync(default);

    var reloaded = await repo.GetByIdAsync(id, default);
    reloaded!.ReplayCount.Should().Be(1);
    reloaded.LastReplayedBy.Should().Be("tester");
}

[Fact]
public async Task PurgeOlderThanAsync_DeletesOnlyEntriesOlderThanCutoff_AndReturnsCount()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);
    var now = DateTime.UtcNow;
    ctx.SmartsuppWebhookAuditEntries.AddRange(
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = now.AddDays(-1), RawBody = "{}",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        },
        new SmartsuppWebhookAuditEntry
        {
            Id = Guid.NewGuid(), ReceivedAt = now.AddDays(-8), RawBody = "{}",
            SignatureStatus = SmartsuppWebhookSignatureStatus.Valid,
            ProcessingStatus = SmartsuppWebhookProcessingStatus.Success,
        });
    await ctx.SaveChangesAsync();

    var deleted = await repo.PurgeOlderThanAsync(now.AddDays(-7), default);

    deleted.Should().Be(1);
    (await ctx.SmartsuppWebhookAuditEntries.CountAsync()).Should().Be(1);
}

[Fact]
public async Task PurgeOlderThanAsync_ReturnsZero_WhenNothingToDelete()
{
    using var ctx = CreateContext();
    var repo = new SmartsuppWebhookAuditRepository(ctx);

    var deleted = await repo.PurgeOlderThanAsync(DateTime.UtcNow.AddDays(-7), default);

    deleted.Should().Be(0);
}
```
(`SmartsuppWebhookAuditRepositoryTests.cs` needs `using Microsoft.EntityFrameworkCore;` already present
from the original writer tests, for the `CountAsync()` call above.)

**Acceptance criteria**

- `ISmartsuppWebhookAuditRepository` exists in `Anela.Heblo.Domain/Features/Smartsupp/`;
  `SmartsuppWebhookAuditRepository` exists in `Anela.Heblo.Persistence/Smartsupp/` and implements it
  fully.
- `ISmartsuppWebhookAuditWriter.cs` and `SmartsuppWebhookAuditWriter.cs` no longer exist anywhere in
  the repo (`grep -rn "ISmartsuppWebhookAuditWriter\|SmartsuppWebhookAuditWriter" backend/` returns
  nothing).
- None of `ListWebhookAuditHandler`, `GetWebhookAuditEntryHandler`, `ReplayWebhookEventHandler`,
  `SmartsuppWebhookAuditCleanupJob` references `Anela.Heblo.Persistence.ApplicationDbContext` or
  `Microsoft.EntityFrameworkCore` any longer.
- `SmartsuppWebhookController.cs` contains no `using Anela.Heblo.Persistence` of any form and injects
  `ISmartsuppWebhookAuditRepository`.
- `SmartsuppModule.cs` registers `ISmartsuppWebhookAuditRepository` → `SmartsuppWebhookAuditRepository`
  and no longer registers `ISmartsuppWebhookAuditWriter`.
- No repository binding for this interface exists in `PersistenceModule.cs`.
- All existing assertions in the four migrated test files pass unchanged (row ordering, filters,
  `Take` clamp at 200, 404 mapping, replay side effects incl. "no new row created", malformed-JSON
  handling, 7-day retention).
- `SmartsuppWebhookAuditRepositoryTests.cs` covers `CreateAsync`, `UpdateOutcomeAsync`, `ListAsync`
  (ordering, filtering, paging), `GetByIdAsync` (found/not-found), `GetForReplayAsync` +
  `SaveChangesAsync` (tracked mutation persists), `PurgeOlderThanAsync` (deletes correct rows, returns
  count, returns 0 when nothing to delete).
- `cd backend && dotnet build` succeeds.
- `cd backend && dotnet format --verify-no-changes` reports no changes.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Smartsupp.WebhookAudit"`
  passes, and `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~PersistenceModuleTests"`
  passes (confirms the ADR-004 guard still holds).

---

### task: encapsulate-refresh-orphan-contacts-lookup

**Goal**

Remove `RefreshOrphanContactsHandler`'s direct `ApplicationDbContext` dependency by adding a single
tracked by-id conversation lookup to the existing `ISmartsuppRepository`/`SmartsuppRepository`, and
drop the now-orphaned `_db.ChangeTracker.Clear()` call from the handler's catch block. No behavior,
HTTP contract, or schema change. Independent of the `create-smartsupp-webhook-audit-repository` task
— touches entirely different files.

**Context** (self-contained — you only read this section)

`RefreshOrphanContactsHandler` (`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/
RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`) already injects `ISmartsuppRepository` for
`ListOrphanContactConversationIdsAsync`, `UpsertConversationAsync`, and `SaveChangesAsync`. Its only
direct-context use is a bare tracked lookup:
```csharp
var local = await _db.SmartsuppConversations
    .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
```
`ISmartsuppRepository.GetConversationAsync` already exists but is `AsNoTracking()` and `Include`s
`Messages`/`Contact` — wrong shape here, since the handler needs a *tracked* bare entity so it can set
`local.ContactId`/`local.SyncedAt` in place before `UpsertConversationAsync`. Add a new,
narrowly-scoped method instead of overloading `GetConversationAsync`.

Full current handler (`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/
RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`):
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

    public async Task<RefreshOrphanContactsResponse> Handle(
        RefreshOrphanContactsRequest request,
        CancellationToken cancellationToken)
    {
        var response = new RefreshOrphanContactsResponse();
        var ids = await _repository.ListOrphanContactConversationIdsAsync(cancellationToken);
        response.Scanned = ids.Count;

        foreach (var conversationId in ids)
        {
            try
            {
                var remote = await _apiClient.GetConversationAsync(conversationId, cancellationToken);
                if (remote?.ContactId is null)
                {
                    response.SkippedNoContactId++;
                    continue;
                }

                var local = await _db.SmartsuppConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
                if (local is null)
                {
                    response.SkippedNoContactId++;
                    continue;
                }

                local.ContactId = remote.ContactId;
                local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                await _repository.UpsertConversationAsync(local, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                response.Updated++;
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.FailedIds.Add(conversationId);
                _logger.LogError(ex,
                    "smartsupp: orphan-contacts backfill failed for conversation {ConversationId}",
                    conversationId);
                _db.ChangeTracker.Clear();
            }
        }

        _logger.LogInformation(
            "smartsupp orphan-contacts backfill done: scanned={Scanned} updated={Updated} skipped={Skipped} failed={Failed}",
            response.Scanned, response.Updated, response.SkippedNoContactId, response.Failed);

        return response;
    }
}
```

`_db.ChangeTracker.Clear()` in the catch block exists to stop a failed iteration's partially-tracked
`SmartsuppConversation` from poisoning the next iteration's lookup. It is dropped, not relocated: no
other `ISmartsuppRepository` consumer resets the tracker between calls, and re-exposing a
`ClearTracking()` escape hatch on the interface purely to preserve this one caller's defensive habit
re-introduces the exact EF-leaking-into-Application-layer problem this issue exists to close. The new
regression test below (`Handle_ContinuesToNextConversation_WhenOneFailsMidLoop`) is required to prove
this is safe.

**Files to create/modify**

- Modify: `backend/src/Anela.Heblo.Domain/Features/Smartsupp/ISmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Smartsupp/SmartsuppRepository.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/RefreshOrphanContacts/RefreshOrphanContactsHandler.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Smartsupp/RefreshOrphanContactsHandlerTests.cs`
  (no test file exists for this handler today — confirmed by search)

**Implementation steps**

1. **`ISmartsuppRepository.cs`** — add this method to the interface, immediately after
   `GetConversationAsync` (which ends at line 358 in the current file, right before
   `Task UpsertContactAsync(...)`):
   ```csharp
   /// <summary>
   /// Tracked, bare lookup by primary key (no Includes) — for callers that need to mutate the
   /// returned entity in place before calling UpsertConversationAsync/SaveChangesAsync. Use
   /// GetConversationAsync instead for read-only display (no-tracking, includes Messages/Contact).
   /// </summary>
   Task<SmartsuppConversation?> FindConversationByIdAsync(
       string conversationId,
       CancellationToken cancellationToken);
   ```

2. **`SmartsuppRepository.cs`** — add the implementation immediately after `GetConversationAsync`
   (currently lines 46–52):
   ```csharp
   public async Task<SmartsuppConversation?> FindConversationByIdAsync(
       string conversationId,
       CancellationToken cancellationToken) =>
       await _db.SmartsuppConversations
           .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken);
   ```
   (Tracked by default — no `.AsNoTracking()` — matching the original inline query exactly.)

3. **`RefreshOrphanContactsHandler.cs` — full replacement:**
   ```csharp
   using Anela.Heblo.Domain.Features.Smartsupp;
   using MediatR;
   using Microsoft.Extensions.Logging;

   namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;

   public class RefreshOrphanContactsHandler
       : IRequestHandler<RefreshOrphanContactsRequest, RefreshOrphanContactsResponse>
   {
       private readonly ISmartsuppRepository _repository;
       private readonly ISmartsuppApiClient _apiClient;
       private readonly ILogger<RefreshOrphanContactsHandler> _logger;

       public RefreshOrphanContactsHandler(
           ISmartsuppRepository repository,
           ISmartsuppApiClient apiClient,
           ILogger<RefreshOrphanContactsHandler> logger)
       {
           _repository = repository;
           _apiClient = apiClient;
           _logger = logger;
       }

       public async Task<RefreshOrphanContactsResponse> Handle(
           RefreshOrphanContactsRequest request,
           CancellationToken cancellationToken)
       {
           var response = new RefreshOrphanContactsResponse();
           var ids = await _repository.ListOrphanContactConversationIdsAsync(cancellationToken);
           response.Scanned = ids.Count;

           foreach (var conversationId in ids)
           {
               try
               {
                   var remote = await _apiClient.GetConversationAsync(conversationId, cancellationToken);
                   if (remote?.ContactId is null)
                   {
                       response.SkippedNoContactId++;
                       continue;
                   }

                   var local = await _repository.FindConversationByIdAsync(conversationId, cancellationToken);
                   if (local is null)
                   {
                       response.SkippedNoContactId++;
                       continue;
                   }

                   // Re-attach the contact_id Smartsupp still knows about and let UpsertConversationAsync
                   // pull the contact via REST (same path as the runtime fix).
                   local.ContactId = remote.ContactId;
                   local.SyncedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

                   await _repository.UpsertConversationAsync(local, cancellationToken);
                   await _repository.SaveChangesAsync(cancellationToken);

                   response.Updated++;
               }
               catch (Exception ex)
               {
                   response.Failed++;
                   response.FailedIds.Add(conversationId);
                   _logger.LogError(ex,
                       "smartsupp: orphan-contacts backfill failed for conversation {ConversationId}",
                       conversationId);
               }
           }

           _logger.LogInformation(
               "smartsupp orphan-contacts backfill done: scanned={Scanned} updated={Updated} skipped={Skipped} failed={Failed}",
               response.Scanned, response.Updated, response.SkippedNoContactId, response.Failed);

           return response;
       }
   }
   ```
   Note the constructor drops the `ApplicationDbContext db` parameter entirely (3 parameters instead
   of 4) and the `using Anela.Heblo.Persistence;` / `using Microsoft.EntityFrameworkCore;` imports are
   removed since nothing in the file references them any longer. The `catch` block no longer calls
   `_db.ChangeTracker.Clear()` — it has nothing to clear.

**Tests to write** (new file `backend/test/Anela.Heblo.Tests/Features/Smartsupp/
RefreshOrphanContactsHandlerTests.cs`)

```csharp
using Anela.Heblo.Application.Features.Smartsupp.UseCases.RefreshOrphanContacts;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class RefreshOrphanContactsHandlerTests
{
    private static SmartsuppConversation MakeConversation(string id) => new()
    {
        Id = id,
        Status = SmartsuppConversationStatus.Open,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        SyncedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Handle_ReattachesContactId_ForEachOrphanWithARemoteContact()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });
        var local = MakeConversation("conv-1");
        repository.Setup(r => r.FindConversationByIdAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.Scanned.Should().Be(1);
        response.Updated.Should().Be(1);
        response.Failed.Should().Be(0);
        local.ContactId.Should().Be("contact-9");
        repository.Verify(r => r.UpsertConversationAsync(local, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsConversation_WhenRemoteHasNoContactId()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = null });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        repository.Verify(r => r.FindConversationByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsConversation_WhenLocalRowNoLongerExists()
    {
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-1" });
        repository.Setup(r => r.FindConversationByIdAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmartsuppConversation?)null);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync("conv-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmartsuppConversationData { Id = "conv-1", ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.SkippedNoContactId.Should().Be(1);
        response.Updated.Should().Be(0);
        repository.Verify(r => r.UpsertConversationAsync(It.IsAny<SmartsuppConversation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContinuesToNextConversation_WhenOneFailsMidLoop()
    {
        // Regression test for dropping _db.ChangeTracker.Clear(): confirms a failure on the first
        // conversation in a batch does not prevent the second one from being processed and updated.
        var repository = new Mock<ISmartsuppRepository>();
        repository.Setup(r => r.ListOrphanContactConversationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "conv-fail", "conv-ok" });

        var local = MakeConversation("conv-ok");
        repository.Setup(r => r.FindConversationByIdAsync("conv-fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        repository.Setup(r => r.FindConversationByIdAsync("conv-ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(local);

        var apiClient = new Mock<ISmartsuppApiClient>();
        apiClient.Setup(a => a.GetConversationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                new SmartsuppConversationData { Id = id, ContactId = "contact-9" });

        var handler = new RefreshOrphanContactsHandler(
            repository.Object, apiClient.Object, NullLogger<RefreshOrphanContactsHandler>.Instance);

        var response = await handler.Handle(new RefreshOrphanContactsRequest(), default);

        response.Scanned.Should().Be(2);
        response.Failed.Should().Be(1);
        response.FailedIds.Should().ContainSingle().Which.Should().Be("conv-fail");
        response.Updated.Should().Be(1);
        local.ContactId.Should().Be("contact-9");
    }
}
```

**Acceptance criteria**

- `ISmartsuppRepository` gains exactly one new method (`FindConversationByIdAsync`), implemented in
  `SmartsuppRepository` as a tracked, `Include`-free lookup by primary key.
- `RefreshOrphanContactsHandler` no longer references `ApplicationDbContext`, `using
  Anela.Heblo.Persistence;`, or `using Microsoft.EntityFrameworkCore;` in any form; its constructor
  takes exactly `ISmartsuppRepository`, `ISmartsuppApiClient`, `ILogger<RefreshOrphanContactsHandler>`.
- The `catch` block no longer calls `_db.ChangeTracker.Clear()` (or any equivalent) and the new
  `Handle_ContinuesToNextConversation_WhenOneFailsMidLoop` test proves a mid-batch failure does not
  block subsequent conversations from updating.
- All four new tests in `RefreshOrphanContactsHandlerTests.cs` pass; existing loop semantics (`Scanned`,
  `Updated`, `SkippedNoContactId`, `Failed`, `FailedIds`) are unchanged from before the refactor.
- `cd backend && dotnet build` succeeds.
- `cd backend && dotnet format --verify-no-changes` reports no changes.
- `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshOrphanContactsHandlerTests"`
  passes.
