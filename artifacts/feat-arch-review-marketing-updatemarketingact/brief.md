## Module
Marketing

## Finding

`CreateMarketingActionHandler` wraps the DB save in a try/catch and compensates (deletes the Outlook event) if the save fails:

```csharp
// CreateMarketingActionHandler.cs:87–112
try
{
    await _repository.AddAsync(action, cancellationToken);
    await _repository.SaveChangesAsync(cancellationToken);
}
catch (Exception dbEx)
{
    _logger.LogError(dbEx, "DB save failed after Outlook create; compensating Outlook event {EventId}", outlookEventId);
    if (outlookEventId != null)
    {
        try { await _outlookSync.DeleteEventAsync(outlookEventId, cancellationToken); }
        catch (Exception compEx) { /* logs orphan */ }
    }
    return new CreateMarketingActionResponse(ErrorCodes.DatabaseError);
}
```

`UpdateMarketingActionHandler` has **no such protection**:

```csharp
// UpdateMarketingActionHandler.cs:112–113
await _repository.UpdateAsync(action, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken);  // ← unguarded; exception propagates
```

The Outlook push happens at lines 70–91 (before the DB save). If the DB save throws, the Outlook calendar already reflects the new data while the database still holds the old data. The exception surfaces to MediatR with no compensation attempt and no structured error response — the caller sees a 500 instead of the module's `ErrorCodes.DatabaseError`.

The Delete handler (`DeleteMarketingActionHandler.cs:75–76`) has the same gap: it calls `_repository.DeleteSoftAsync` (which includes `SaveChangesAsync`) after a successful Outlook deletion, with no catch.

## Why it matters

- **Data consistency**: on any transient DB error (deadlock, connection drop), Outlook and the database diverge silently. The marketing action appears updated in the team calendar but unchanged in the app.
- **Error surfacing**: the unhandled exception bypasses the response-object pattern used by every other handler in this module, causing the API to return 500 rather than a structured `{ success: false, errorCode: "..." }` response.
- **Inconsistency**: the Create handler clearly documents the intended compensating-transaction pattern. The Update and Delete handlers don't follow it, which will confuse the next developer extending this module.

## Suggested fix

Wrap the DB save in `UpdateMarketingActionHandler` with the same compensation pattern used in `CreateMarketingActionHandler`. For an update, a full revert is harder (you'd need to re-issue a `PATCH` with the original values), so the minimal fix is: catch the DB exception, log the Outlook-vs-DB inconsistency explicitly, and return `ErrorCodes.DatabaseError` so the caller knows the operation failed:

```csharp
// UpdateMarketingActionHandler.cs — replace lines 112-113
try
{
    await _repository.UpdateAsync(action, cancellationToken);
    await _repository.SaveChangesAsync(cancellationToken);
}
catch (Exception dbEx)
{
    _logger.LogError(dbEx,
        "DB save failed after Outlook update for MarketingAction {ActionId}; " +
        "Outlook event {EventId} may now be out of sync",
        action.Id, action.OutlookEventId);
    return new UpdateMarketingActionResponse(ErrorCodes.DatabaseError);
}
```

Apply the same pattern to `DeleteMarketingActionHandler` around its `DeleteSoftAsync` call.

---
_Filed by daily arch-review routine on 2026-05-29._