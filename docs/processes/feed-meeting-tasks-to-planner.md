---
process: feed-meeting-tasks-to-planner
kind: feed
module: meeting-tasks
summary: On a reviewer's click, creates one Microsoft Planner task per approved meeting action item (assigned to the resolved Microsoft 365 user), stores the Planner task id on the ProposedTasks row and recomputes the meeting's review status.
owns:
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/**
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphPlannerService.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/IMeetingTaskExporter.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/NoOpMeetingTaskExporter.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphTodoContracts.cs
  - backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs
verified_at: "5e993f9e2"
related:
  - sync-plaud-recordings
---

# Approved meeting tasks → Microsoft Planner

## Purpose
After a meeting has been reviewed on the meeting detail page (**Porady** → meeting), the
reviewer sends the approved action items to Microsoft 365 so they land in the assignee's task
list. The button says **"Odeslat do TODO"** and the dialog "Odeslat schválené úlohy do
Microsoft TODO?", but technically each item becomes a **Planner task** in one shared plan;
Planner tasks assigned to a person also appear in their Microsoft To Do ("Assigned to me").
Heblo only creates the tasks; it never reads them back (completion in Planner is not visible
in Heblo).

## Trigger
On demand: button "Odeslat do TODO (n)" → `POST /api/meeting-tasks/{transcriptId}/submit`
(requires `anela.meetings.write`). No scheduled job. Can be clicked repeatedly — already-sent
tasks are skipped.

Workflow states:
- Task (`ProposedTasks.Status`): `Pending` → `Approved` / `Rejected` (reviewer, any order,
  reversible). Sent = `Approved` with `ExternalTaskId` set.
- Meeting (`MeetingTranscripts.Status`): `PendingReview` → `Approved` (all tasks sent or
  rejected, none rejected) or `PartiallyApproved` (all sent or rejected, at least one rejected).

## Data flow
Source: `public."ProposedTasks"` of the meeting with `Status = Approved` and
`ExternalTaskId IS NULL`. For each, in order (`SubmitToTodoHandler`):
1. No `AssigneeEmail` → error "Task '…' has no resolved user — assign a known user before
   submitting." (skipped).
2. Resolve the Microsoft 365 user: `GET https://graph.microsoft.com/v1.0/users?$filter=mail eq '<email>'&$select=id,displayName`
   with an **application** token (`https://graph.microsoft.com/.default`). Not found, non-2xx or
   exception → error "Could not resolve user …" (skipped). Several matches → first one.
3. `POST https://graph.microsoft.com/v1.0/planner/tasks` with a **delegated** token of the
   clicking user (scope `Tasks.ReadWrite`): `planId` = `MeetingTasks:PlannerPlanId`,
   `bucketId` = `MeetingTasks:PlannerBucketId` if set, `title`, assignment to the user id,
   `dueDateTime` = due date converted to UTC ISO 8601 if set.
4. If the description is not empty: `GET /planner/tasks/{id}/details` for the ETag, then
   `PATCH` it with `If-Match` and `{description}`. A failure here is only logged — the task
   stays created without a description.
5. Store the Planner task id in `ProposedTasks.ExternalTaskId` and **save immediately**
   (per task), so a crash mid-loop does not lose the link and cause duplicates.
6. After the loop: recompute meeting status (below), set `ReviewedAt` = now UTC, save.

Response: `successCount`, `failedCount`, `errors[]` (shown in the dialog). If an error mentions
`consent required` / `Tasks.ReadWrite`, the UI tells the user an admin must grant Microsoft 365
consent and they should sign out and in again.

## Logic & formulas
- A task counts as done if `Rejected` or it has an `ExternalTaskId`. All tasks done (also when
  the meeting has no tasks) → `Approved`, or `PartiallyApproved` if any is `Rejected`;
  otherwise `PendingReview`. A still-`Pending` task therefore keeps the meeting in review.
- `ReviewedAt` is updated on every submit, even when nothing was sent; `ReviewedByUser` is
  **not** set by this flow (only by the manual status toggle).
- The delegated token is acquired once per request and shared by all tasks, so missing consent
  fails every task with the same message instead of retrying per task.
- Per-meeting access is checked (`IMeetingAccessGuard.CanAccess`); since only writers can call
  the endpoint and writers see every meeting, this check never denies in practice.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `MeetingTasks:PlannerPlanId` | `123123123123123` (placeholder; real value from secrets) | Target Planner plan. `[Required]`; startup fails if it equals `CONFIGURE_IN_USER_SECRETS` (outside mock auth) |
| `MeetingTasks:PlannerBucketId` | not set | Optional bucket inside the plan; unset → Planner's default bucket |
| `UseMockAuth` / `BypassJwtValidation` | false | If either is true, `NoOpMeetingTaskExporter` is used: every task fails with "Planner export disabled in mock auth mode." |
| HttpClient `MicrosoftGraph` | — | Registered by this module (and KnowledgeBase) |

Azure AD requirements (not in repo config): delegated `Tasks.ReadWrite` with admin consent,
and an application permission allowing `GET /users` by mail.

## Runtime facts
None.

## Known quirks
- **UI says "Microsoft TODO", the target is Planner.** All tasks go to one shared plan, not to
  a personal To Do list; they are created in the name of whoever clicked the button.
- **Description loss is silent**: if the description PATCH fails, the task exists without a
  description and the item still counts as a success (warning in logs only).
- **No feedback loop**: deleting or completing the task in Planner does not change Heblo, and
  un-approving a sent task in Heblo does not delete it in Planner.
- **Assignee must be in `meeting-users.json` and have a matching `mail` in Entra ID.** A user
  whose mailbox address differs from the directory e-mail (alias, UPN-only account) cannot be
  resolved.
- **Manual status toggle vs. submit**: the reviewer can mark a meeting `Approved` by hand
  (`PUT /api/meeting-tasks/{id}/status`) without sending anything; the next submit recomputes
  the status from the tasks and can move it back to `PendingReview`.
- Partial failure telemetry: when any task fails the summary is logged as Warning (Information
  is dropped by `CostOptimizedTelemetryProcessor` in production).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/SubmitToTodo/SubmitToTodoHandler.cs` — selection, loop, status recompute
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/GraphPlannerService.cs` — Graph calls and tokens
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — real vs. no-op exporter, PlanId validation
- `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx` — the button and result dialog
