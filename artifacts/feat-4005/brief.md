## Module / File
`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/SubmitDraftReplyFeedback/SubmitDraftReplyFeedbackHandler.cs`

## Coverage
Line coverage: 22.6% (filter threshold: 60%)

## What's not tested
The handler has four distinct execution paths, none of which are currently covered:

1. **Log not found or wrong feature type** — returns `SmartsuppDraftReplyFeedbackLogNotFound` when `log is null` or `log.Feature != RagFeature.SmartsuppDraftReply`
2. **Ownership check (authorization guard)** — returns `Forbidden` when `log.UserId != currentUser.Id`
3. **Duplicate submission guard** — returns `SmartsuppDraftReplyFeedbackAlreadySubmitted` when either `log.PrecisionScore` or `log.StyleScore` is already set
4. **Success path** — scores and comment are written and `SaveChangesAsync` is called

## Why it matters
The ownership check is a security invariant: any user who knows a `logId` could submit feedback on someone else's RAG interaction log if this guard regresses. The duplicate-submission guard prevents double-scoring; a regression there silently overwrites prior feedback. Neither the error-response shapes nor the save-on-success path are asserted anywhere.

## Suggested approach
Unit tests mocking `IRagInteractionLogRepository` and `ICurrentUserService`. Cover:
- Null log → `SmartsuppDraftReplyFeedbackLogNotFound`
- Log with `Feature != SmartsuppDraftReply` → `SmartsuppDraftReplyFeedbackLogNotFound`
- Log owned by a different user → `Forbidden`
- Log with `PrecisionScore` already set → `AlreadySubmitted`
- Log with `StyleScore` already set → `AlreadySubmitted`
- Happy path: scores and comment written, `SaveChangesAsync` called once

Estimated effort: ~2 h.

---
_Filed by weekly coverage-gap routine on 2026-08-31. Based on CI run #33077392747 (ba8f5eef168e0058dae1787bf6bb9f53fdcdf472)._
