## Module / File
`backend/src/Anela.Heblo.Application/Features/Smartsupp/Pipeline/DraftReplyLoggingBehavior.cs`

## Coverage
Line coverage: 0% (filter threshold: 60%)
No test file exists for this pipeline behavior.

## What's not tested
The behavior has three distinct paths:
1. **Skip on failure** — if `response.Success` is `false` OR `_recorder.HasInteraction` is `false`, the log is not written and the response is returned unmodified (`response.Id` is NOT set).
2. **Happy path** — log is persisted via `_repository.SaveAsync`, and `response.Id` is stamped with `log.Id` so the frontend can link subsequent feedback to this draft.
3. **Exception-swallow** — if `SaveAsync` throws, the exception is caught and logged as an error; `response` is still returned (the caller's draft is not failed).

None of these paths are exercised. In particular, the **stamp** (`response.Id = log.Id`) and the **skip condition** (`!_recorder.HasInteraction`) are entirely dark.

## Why it matters
`response.Id` is the foreign-key linking a sent Smartsupp reply back to its RAG interaction log. If the skip condition is incorrectly evaluated (e.g. `HasInteraction` returns `false` when it should return `true`), logs are never written and the eval dataset is silently empty. Conversely, if the exception-swallow path regresses and starts re-throwing, every failed log write breaks the draft-reply feature for the operator.

## Suggested approach
Unit tests with mock `IRagInteractionLogRepository`, `IRagInteractionRecorder`, and `ICurrentUserService`:
- **Skip — generation failed**: `response.Success = false` → `SaveAsync` never called, `response.Id` not set.
- **Skip — no interaction recorded**: `Success = true`, `HasInteraction = false` → same skip behavior.
- **Happy path**: `Success = true`, `HasInteraction = true` → `SaveAsync` called once, `response.Id` equals `log.Id`.
- **Exception swallow**: `SaveAsync` throws → response is still returned (no exception propagated), error is logged.

Effort: small — four test cases, Moq for repository and recorder.

---
_Filed by weekly coverage-gap routine on 2026-09-07. Based on CI run #33791274852 (a21f134808e61a338c3261f8523316d2752ebea3)._
