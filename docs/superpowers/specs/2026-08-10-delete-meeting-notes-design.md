# Delete meeting notes — design

**Date:** 2026-08-10
**Branch:** `feature/delete-meeting-notes`
**Module:** MeetingTasks (`Anela_Meetings`)

## Problem

Some meetings are private and should not exist in Heblo at all. Today a meeting transcript
ingested from Plaud can only be reviewed or reimported — there is no way to remove it.
A meeting manager needs a way to erase one specific meeting note and everything attached
to it, from the meeting note detail page, behind a confirmation dialog.

## Scope

In scope:

- Hard delete of one `MeetingTranscript` with its proposed tasks and access grants.
- A tombstone that stops the Plaud polling job from re-ingesting the deleted recording.
- Delete button + confirmation dialog on the meeting note detail page, manager-only.

Out of scope:

- Bulk delete or delete from the list page.
- Restoring a deleted meeting (deletion is final by design).
- Removing tasks that were already exported to Microsoft Planner (see Decisions).

## Decisions

### D1 — Tombstone table, not soft delete, not plain hard delete

`PlaudPollingJob` runs every 5 minutes and ingests every Plaud recording younger than
`MeetingTasksOptions.MaxRecordingAgeDays` (default 7) that is not already in the database.
Idempotency is `IMeetingTranscriptRepository.ExistsByPlaudIdAsync`. A plain hard delete of a
recent meeting therefore reappears within minutes.

A soft-delete flag would keep the existence check working, but leaves the row in place —
which contradicts the requirement that nothing about the meeting remains.

Chosen: hard delete the transcript, and record a tombstone row keyed by `PlaudRecordingId`
that the ingest handler checks before importing.

### D2 — Tasks already exported to Planner are left in place

`SubmitToTodo` pushes approved tasks into Microsoft Planner via `GraphPlannerService` and
stores the returned id in `ProposedTask.ExternalTaskId`. `IMeetingTaskExporter` has no
delete operation.

Chosen: delete only Heblo data. Exported tasks are real work items in someone's Planner and
deleting them could destroy another person's work. The confirmation dialog states this
explicitly so the user can clean Planner up manually if they want to.

### D3 — Permission is `anela.meetings.write` (meeting manager)

Same gate as "Spravovat přístup" (`UpdateMeetingAccess`): `[FeatureAuthorize(Feature.Anela_Meetings,
AccessLevel.Write)]` on the endpoint plus an `IMeetingAccessGuard.IsManager()` check in the
handler. No change to `access-matrix.json`, no code generation, no new Entra role.

### D4 — The tombstone stores no meeting content

Only `PlaudRecordingId`, `DeletedAt`, `DeletedByUserEmail`. Keeping the subject would leave a
readable trace of a meeting deleted for privacy reasons. What remains is enough for
accountability ("recording X was deleted by Y at Z") and nothing about the content.

### D5 — Plain confirmation dialog

Zrušit / Smazat, no type-the-name confirmation. The button is manager-only and the dialog
enumerates the consequences; a typing ritual adds friction without adding safety here.

## Backend

### Domain

New entity `backend/src/Anela.Heblo.Domain/Features/MeetingTasks/DeletedPlaudRecording.cs`:

| Property | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | PK |
| `PlaudRecordingId` | `string` | unique index — the poller's lookup key |
| `DeletedAt` | `DateTime` | UTC |
| `DeletedByUserEmail` | `string` | who performed the delete |

### Persistence

- `DeletedPlaudRecordingConfiguration` in `backend/src/Anela.Heblo.Persistence/MeetingTasks/`,
  table `public.DeletedPlaudRecordings`, unique index `UX_DeletedPlaudRecordings_PlaudRecordingId`.
- `DbSet<DeletedPlaudRecording>` on `ApplicationDbContext` next to the existing MeetingTasks sets.
- One EF migration. Migrations are applied manually in this project.

No other schema change is needed. `ProposedTasks` and `MeetingAccessGrants` already have
`OnDelete(DeleteBehavior.Cascade)` in EF configuration **and** `ReferentialAction.Cascade` at the
database level (migrations `20260512191541_AddMeetingTasksTables` and
`20260517155655_AddMeetingAccessGating`). `Participants` is a `jsonb` column on the transcript row
itself. Nothing else in the codebase references a meeting transcript — no blob storage, no files,
no audit table; `ExplainSummary` is a live LLM call and stores nothing.

### Repository

`IMeetingTranscriptRepository` gains:

```csharp
/// Removes the transcript (cascading its tasks and access grants) and records the
/// tombstone that prevents re-ingestion — both in a single SaveChanges.
Task DeleteAsync(MeetingTranscript transcript, string deletedByUserEmail, CancellationToken ct = default);

Task<bool> IsPlaudRecordingDeletedAsync(string plaudRecordingId, CancellationToken ct = default);
```

Deleting and tombstoning in one method and one `SaveChanges` makes it impossible for a caller to
do one without the other. The implementation uses `Remove` + `Add` (not `ExecuteDelete`, which the
EF InMemory provider used in tests does not support).

### Use case

`backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/DeleteMeetingTranscript/`

- `DeleteMeetingTranscriptRequest : IRequest<DeleteMeetingTranscriptResponse>` — `Guid TranscriptId`.
- `DeleteMeetingTranscriptResponse : BaseResponse` — no payload beyond the base.
- `DeleteMeetingTranscriptHandler` — injects `IMeetingTranscriptRepository`, `IMeetingAccessGuard`,
  `ICurrentUserService`, `ILogger<T>`:

```csharp
if (!_accessGuard.IsManager())
    return new DeleteMeetingTranscriptResponse(ErrorCodes.Forbidden);

var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
if (transcript is null)
    return new DeleteMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);

var userEmail = _currentUserService.GetCurrentUser().Email;
await _repository.DeleteAsync(transcript, userEmail, cancellationToken);

_logger.LogWarning(
    "Meeting transcript {TranscriptId} (plaud {PlaudRecordingId}) deleted by {User}",
    transcript.Id, transcript.PlaudRecordingId, userEmail);

return new DeleteMeetingTranscriptResponse();
```

No new `ErrorCodes` entry — `Forbidden` and `ResourceNotFound` match the existing convention in
this feature. Handlers are auto-registered by the MediatR assembly scan; no module registration
and no FluentValidation validator (MeetingTasks validates inline).

### API

`MeetingTasksController` (`api/meeting-tasks`) gains:

```csharp
[HttpDelete("{transcriptId:guid}")]
[FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
public async Task<ActionResult<DeleteMeetingTranscriptResponse>> Delete(
    Guid transcriptId, CancellationToken ct = default)
{
    var result = await _mediator.Send(new DeleteMeetingTranscriptRequest { TranscriptId = transcriptId }, ct);
    return HandleResponse(result);
}
```

The read-only MCP surface (`MeetingTasksMcpTools`) is not touched.

### Ingest guard

In `IngestPlaudRecordingHandler.Handle`, immediately after the existing `ExistsByPlaudIdAsync`
check and before any Plaud API call:

```csharp
if (await _repository.IsPlaudRecordingDeletedAsync(request.PlaudRecordingId, cancellationToken))
{
    _logger.LogInformation(
        "Recording {RecordingId} was deleted by a user, not re-ingesting", request.PlaudRecordingId);
    return new IngestPlaudRecordingResponse { Skipped = true };
}
```

This is what makes the delete stick. Placing it before the Plaud calls also avoids pointless
network traffic for tombstoned recordings. `IngestPlaudRecordingResponse` needs no new field —
the job counts it under "already known", which is accurate enough for the job log.

## Frontend

### Hook

`useDeleteMeeting()` in `frontend/src/api/hooks/useMeetingTasks.ts`, mirroring `useReimportMeeting`:
`DELETE ${apiClient.baseUrl}/api/meeting-tasks/{id}` via the file's `fetchJson` helper.
On success: `invalidateQueries(MEETING_TASKS_KEYS.list)` and `removeQueries(MEETING_TASKS_KEYS.detail(id))`.

### Detail page

`frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`:

- A red-outlined **Smazat** button with the `Trash2` icon in the existing `shrink-0` action row,
  rendered only when `isMeetingManager` — the same guard as "Spravovat přístup".
- Dialog state next to the existing modal state; the dialog renders at the bottom of the component
  alongside `ExplainModal` / `ManageAccessModal` / `MeetingReviewLeaveDialog`.
- On success, navigate to `/automation/meeting-tasks` with `navigate(...)` **directly**, not through
  `requestNavigation`. The page installs an unsaved-review leave guard (`useUnsavedChangesDialog`)
  that would otherwise ask "mark as reviewed?" for a record that no longer exists.
- On failure, the dialog stays open and shows the error message.

### Confirmation dialog

New `frontend/src/components/pages/automation/ConfirmDeleteMeetingDialog.tsx`. There is no shared
confirmation component in this codebase — each feature defines its own; the closest reference is
`ConfirmDeleteTagDialog.tsx`, and the in-file "Odeslat do Planneru" modal is the styling reference.

Czech copy states:

- what is deleted: souhrn, přepis, navržené úkoly, přístupová oprávnění;
- that the action is irreversible;
- that the meeting will **not** be re-imported from Plaudu;
- that tasks already sent to Planneru stay there.

Buttons: Zrušit / Smazat (destructive styling), with a pending state while the mutation runs.

## Testing

Backend (`backend/test/Anela.Heblo.Tests/Features/MeetingTasks/`, xUnit + Moq + FluentAssertions):

- `DeleteMeetingTranscriptHandlerTests`
  - returns `Forbidden` when the caller is not a meeting manager, and does not touch the repository;
  - returns `ResourceNotFound` for an unknown id;
  - calls `DeleteAsync` with the transcript and the current user's email on the happy path.
- `MeetingTranscriptRepositoryTests` (EF InMemory)
  - `DeleteAsync` removes the transcript together with its proposed tasks and access grants;
  - `DeleteAsync` writes exactly one tombstone with the recording id, timestamp and user;
  - `IsPlaudRecordingDeletedAsync` returns true only for tombstoned recording ids.
- `IngestPlaudRecordingHandlerTests`
  - a tombstoned recording is skipped and no `IPlaudClient` method is called.

Frontend:

- `MeetingTaskDetailPage.delete.test.tsx`
  - the Smazat button is not rendered without `anela.meetings.write`;
  - confirming the dialog calls the delete endpoint and navigates to the list;
  - a failed delete keeps the dialog open and shows an error.

Validation before completion: `dotnet build` + `dotnet format`, `npm run build` + `npm run lint`,
and all touched tests green.

## Risks

- **Migration is manual.** The delete endpoint returns a 500 until `DeletedPlaudRecordings` exists in
  the target database. Apply the migration before or with the deployment.
- **Deletion is final.** No restore path, by design. The tombstone deliberately keeps no content, so
  a mistaken delete of a recording older than `MaxRecordingAgeDays` cannot be recovered even from Plaud.
- **Planner drift.** Exported tasks outlive their meeting (D2). Accepted and surfaced in the dialog copy.
