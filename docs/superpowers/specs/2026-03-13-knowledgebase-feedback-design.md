# Knowledge Base Feedback Design

**Date:** 2026-03-13
**Status:** Approved
**Scope:** Allow users to rate Knowledge Base Ask responses on precision and style (1–10), with an optional written comment.

---

## Overview

After a user asks a question and receives an AI-generated answer, a feedback form appears directly below the answer. The user rates the response on two dimensions and optionally adds a comment. Feedback is stored in the existing `KnowledgeBaseQuestionLog` table by linking via the log entry ID returned from the ask endpoint.

---

## Data Layer

### `KnowledgeBaseQuestionLog` — new nullable columns

| Column | Type | Constraint |
|---|---|---|
| `PrecisionScore` | `int?` | 1–10, nullable |
| `StyleScore` | `int?` | 1–10, nullable |
| `FeedbackComment` | `string?` | nullable, intentionally unconstrained — EF configuration must use `.HasColumnType("text")` (PostgreSQL `text` type, no upper bound — do not add `HasMaxLength`) |

No new table. EF migration required. `KnowledgeBaseQuestionLogConfiguration` must be updated to declare all three new columns, including the explicit `.HasColumnType("text")` for `FeedbackComment`.

### `AskQuestionResponse` — new field

Add `Guid? Id` as a new property with a public setter to `AskQuestionResponse` (currently only has `Answer` and `Sources`). It is populated when the log entry is successfully persisted, and left null if logging fails. The JSON field name will be `id` (camelCase — consistent with existing camelCase serialization in the project). The frontend hides the feedback form when `id` is null.

### Populating `Id` in `QuestionLoggingBehavior`

The `QuestionLoggingBehavior` already creates the `KnowledgeBaseQuestionLog` entry after `next()` returns, wrapped in a `try/catch`. The behavior must set `response.Id = log.Id` **only inside the `try` block**, after `SaveQuestionLogAsync` succeeds. If an exception is caught, `response.Id` must remain null — the frontend will suppress the feedback form in that case.

### TypeScript interface update

`AskQuestionResponse` is hand-written in `frontend/src/api/hooks/useKnowledgeBase.ts` (not auto-generated). This interface must be updated to add `id: string | null`.

---

## Backend

### Error codes

The 20XX range is being newly established for the KnowledgeBase module. Add a new comment block header in `ErrorCodes.cs` and two new entries:

```csharp
// KnowledgeBase module errors (20XX)
[HttpStatusCode(HttpStatusCode.NotFound)]
KnowledgeBaseFeedbackLogNotFound = 2001,
[HttpStatusCode(HttpStatusCode.Conflict)]
KnowledgeBaseFeedbackAlreadySubmitted = 2002,
```

The existing `Forbidden = 0014` error code is reused for the ownership check (403).

### Repository — new method

Add to `IKnowledgeBaseRepository` and `KnowledgeBaseRepository`:

```csharp
Task<KnowledgeBaseQuestionLog?> GetQuestionLogByIdAsync(Guid id, CancellationToken ct = default);
```

Implemented as a simple EF `FirstOrDefaultAsync` by primary key.

### New use case: `SubmitFeedback`

**Request:** `SubmitFeedbackRequest` (class with properties, not record)
```
LogId           Guid     required
PrecisionScore  int      required, 1–10
StyleScore      int      required, 1–10
Comment         string?  optional
```

**Response:** `SubmitFeedbackResponse` — concrete class inheriting `BaseResponse`, no additional fields. Must expose two public constructors following the pattern used by all other response classes in the project (e.g., `DeleteJournalEntryResponse`):
- `public SubmitFeedbackResponse()` — success case
- `public SubmitFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null) : base(errorCode, details)` — error case

**Handler:** `SubmitFeedbackHandler`

Errors are signaled by returning `new SubmitFeedbackResponse(ErrorCodes.X, new Dictionary<string, string> { ... })`.

1. Fetch log via `GetQuestionLogByIdAsync(request.LogId)` — return `KnowledgeBaseFeedbackLogNotFound` (404) if null
2. Verify `log.UserId == currentUserId` — return `Forbidden` (403) if mismatch (the endpoint requires `[Authorize]`, so `currentUserId` is always non-null; no special null handling needed)
3. Check if either `PrecisionScore` or `StyleScore` is non-null — return `KnowledgeBaseFeedbackAlreadySubmitted` (409) if so. Note: a direct API call with only one score could permanently lock the entry in a partially-filled state; this is accepted behavior.
4. Set `PrecisionScore`, `StyleScore`, `FeedbackComment`
5. `SaveChangesAsync`

No transaction needed — EF `SaveChangesAsync` is atomic for this single-entity update. Race condition between concurrent feedback submissions is accepted as out of scope.

**Endpoint:** `POST /api/knowledgebase/feedback`
Authorization: `[Authorize]` (same as ask endpoint)

---

## Frontend

### Flow

1. User submits question → `useKnowledgeBaseAskMutation()` returns answer + `id`
2. Answer renders in existing blue box
3. If `id` is null (logging failed): feedback form is hidden — no feedback possible
4. If `id` is present: feedback form renders directly below the answer
5. User selects both scores using a radio button row (1–10 options) for Precision and Style
6. Submit button enabled only when both scores are selected
7. On success (200): form replaced with "Thank you for your feedback" message (permanent)
8. On 409 (already rated): form replaced with "Feedback already submitted" message — no retry
9. On other error: inline error shown, user can retry

### Score input widget

Use a radio button row with options 1–10 for each dimension. This prevents out-of-range values and gives clear selection state, enabling reliable "both scores set" detection without additional validation.

### New hook: `useSubmitFeedbackMutation()`

`POST /api/knowledgebase/feedback` with `{ logId, precisionScore, styleScore, comment? }`
Uses absolute URL pattern: `` `${(apiClient as any).baseUrl}/api/knowledgebase/feedback` ``

The hook must inspect `response.status` before throwing. A 409 is returned as a typed result `{ alreadySubmitted: true }` rather than thrown as an error, so the component can distinguish it from retry-eligible failures. All other non-OK statuses are thrown as errors.

### Component: feedback section in `KnowledgeBaseSearchAskTab.tsx`

- Hidden when `logId` is null
- Two labeled radio rows (1–10) for **Precision** and **Style**
- Optional `<textarea>` for comment
- Submit button, disabled until both scores selected
- Post-submit state: confirmation or "already submitted" message replaces form (permanent, no retry for either)
- On other errors: inline error with retry available
- Local component state — no global state

---

## Out of Scope

- Management UI for stored feedback data
- Editing or deleting submitted feedback
- Feedback on Search results (Ask only)
- Aggregated feedback analytics
- Optimistic concurrency protection for simultaneous feedback submissions
