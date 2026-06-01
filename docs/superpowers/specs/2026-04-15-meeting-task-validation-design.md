# Meeting Task Validation Checkpoint

## Problem

Meeting transcripts are processed by an external automation (Plaud → email → n8n) that extracts a summary and action items, then creates tasks in Microsoft TODO. There is no human checkpoint — tasks are created blindly, which can result in incorrect, duplicate, or low-quality tasks reaching TODO lists.

## Solution

Introduce a validation UI in Heblo where users review, edit, approve, or reject AI-extracted tasks before they are pushed to Microsoft TODO. The n8n automation submits proposed tasks to Heblo instead of creating them directly. A user reviews them in the Heblo UI, and on approval, Heblo's backend creates the tasks in each assignee's Microsoft TODO list via Graph API.

## Flow

```
Plaud → Email → n8n → [extracts summary + tasks] → Heblo API (POST)
                                                        ↓
                                             Stored in DB (PendingReview)
                                                        ↓
                                             User reviews in Heblo UI
                                             (approve / reject / edit / add)
                                                        ↓
                                             Heblo → Graph API → MS TODO
                                             (per-assignee task creation)
```

## Domain Model

### MeetingTranscript (aggregate root)

| Field            | Type       | Description                                  |
|------------------|------------|----------------------------------------------|
| Id               | Guid       | Primary key                                  |
| Subject          | string     | Meeting title / email subject                |
| Summary          | string     | AI-generated meeting summary                 |
| SourceEmail      | string     | Sender email for traceability                |
| Status           | enum       | PendingReview, Approved, PartiallyApproved   |
| ReceivedAt       | DateTime   | When n8n submitted it                        |
| ReviewedAt       | DateTime?  | When user completed review                   |
| ReviewedByUser   | string?    | Who reviewed                                 |

### ProposedTask (child entity)

| Field              | Type       | Description                                  |
|--------------------|------------|----------------------------------------------|
| Id                 | Guid       | Primary key                                  |
| MeetingTranscriptId| Guid       | FK to MeetingTranscript                      |
| Title              | string     | Task title                                   |
| Description        | string     | Task description/context                     |
| Assignee           | string     | Who should do this                           |
| DueDate            | DateTime?  | Optional deadline                            |
| Status             | enum       | Pending, Approved, Rejected                  |
| ExternalTaskId     | string?    | MS TODO task ID after creation               |
| IsManuallyAdded    | bool       | True if user added during review             |

## API Design

### n8n-facing endpoint (API key auth)

```
POST /api/meeting-tasks
Header: X-Api-Key: {configured-key}
Body: {
  subject: string,
  summary: string,
  sourceEmail: string,
  tasks: [{ title, description, assignee, dueDate }]
}
→ 201 Created { transcriptId: guid }
```

Authentication: Simple API key in `X-Api-Key` header. Key stored in app configuration (secrets). This avoids requiring n8n to handle Entra ID OAuth.

### UI-facing endpoints (Entra ID auth)

```
GET  /api/meeting-tasks                              → List transcripts (newest-first, filterable by status)
GET  /api/meeting-tasks/{id}                         → Transcript detail + all proposed tasks
PUT  /api/meeting-tasks/{id}/tasks/{taskId}          → Edit task (title, description, assignee, dueDate)
PUT  /api/meeting-tasks/{id}/tasks/{taskId}/status   → Approve or reject a task
POST /api/meeting-tasks/{id}/tasks                   → Add a new task manually
POST /api/meeting-tasks/{id}/submit                  → Push all approved tasks to MS TODO
```

## UI Design

### Page location

`/automation/meeting-tasks` — under the existing Automation section in navigation.

### List view

- Table: Subject, Received, Tasks (count), Status (badge)
- Sorted newest-first
- Pending items visually highlighted
- Click row to open detail

### Detail view (validation screen)

**Top section:**
- Meeting subject + received date
- Meeting summary in a card

**Tasks section:**
- Each task as an editable card/row with:
  - Title (editable)
  - Description (editable)
  - Assignee (editable)
  - Due date (date picker)
  - Approve / Reject buttons per task
- Visual states: approved = green check, rejected = strikethrough/grey, pending = neutral

**Bottom actions:**
- "Add Task" — adds blank task to list
- "Approve All" — bulk approve remaining pending tasks
- "Submit to TODO" — pushes approved tasks to MS TODO (disabled until ≥1 approved)

### Patterns reused

- Status badges (same as invoices, KB documents)
- Confirmation dialog before submit (same as manufacture order state transitions)
- Table layout consistent with catalog, stock operations pages

## Microsoft Graph Integration

### Auth & permissions

- **Permission type:** Application (`Tasks.ReadWrite.All`)
- **Consent:** Admin-consented in Entra ID app registration
- **Token acquisition:** `GetAccessTokenForAppAsync("https://graph.microsoft.com/.default")` — same pattern as existing OneDrive integration

### Task creation flow

1. Resolve assignee name → Entra ID user ID (via existing Graph user resolution)
2. Find or create a TODO list for the user (e.g., "Meeting Actions" list)
3. Create task with title, body (description), dueDateTime
4. Store returned task ID in `ExternalTaskId`

### Service

`GraphTodoService` — new service reusing `GraphApiHelpers.cs` for HTTP request creation and Bearer token handling.

### Graph API endpoints used

```
GET  /users/{userId}/todo/lists                      → find/create target list
POST /users/{userId}/todo/lists/{listId}/tasks       → create task
```

## Error Handling

| Scenario                        | Handling                                                    |
|---------------------------------|-------------------------------------------------------------|
| Duplicate transcript from n8n   | Deduplicate by sourceEmail + subject + receivedAt (±5 min)  |
| Graph API fails mid-submit      | Track per-task success/failure. Failed tasks show error, allow retry |
| Assignee not found in Entra ID  | Warning in UI during review. On submit, skip or use fallback |
| Stale pending reviews           | No auto-expiry. Dashboard indicator for unreviewed count    |

## Testing Strategy

### Backend

- **Unit tests:** Domain model validation, status transitions, deduplication logic
- **Unit tests:** MediatR handler tests for each endpoint (mock Graph service)
- **Integration test:** API key auth middleware

### Frontend

- **Component tests:** Task card editing, approve/reject state changes, submit flow

### Manual verification

1. POST a mock transcript via API (curl/Postman with API key)
2. Verify it appears in Heblo UI at `/automation/meeting-tasks`
3. Edit a task, approve some, reject some, add a new one
4. Submit and verify tasks appear in assignees' Microsoft TODO
