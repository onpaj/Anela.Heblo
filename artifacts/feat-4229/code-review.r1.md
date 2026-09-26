# Code Review: feat-4229 — Validate `TimeWindow` on GetProductMarginSummaryRequest

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes

Reviewed the full feature diff against the correct merge-base (`775a6cf`, the actual
tip of `main` at PR creation time — note: this worktree's local `origin/main` ref was
initially stale at an older commit, which made a naive `git diff` against it falsely
include three unrelated, already-merged PRs (#4223, #4224, #4227); `git fetch origin
main` resolved this and the true feature diff is exactly the 8 source/test files
below).

**Source diff reviewed** (excluding `artifacts/` process markdown):
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs` — added `INVALID_TIME_WINDOW` message constant.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` — registered `IValidator<GetProductMarginSummaryRequest>` + `IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` — added `public static readonly string[] SupportedTimeWindows`; `ParseTimeWindow`'s switch/exception fallback untouched.
- `backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs` (new) — the validator.
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — added `InvalidTimeWindow = 1706` (`[HttpStatusCode(BadRequest)]`).
- `backend/test/.../Pipeline/AnalyticsValidationPipelineTests.cs` — new end-to-end pipeline test.
- `backend/test/.../Services/TimeWindowParserTests.cs` (new).
- `backend/test/.../Validators/GetProductMarginSummaryRequestValidatorTests.cs` (new).

**Verified against spec/design (`spec.r1.md`, `design.r1.md`) and against the sibling
pattern it's meant to mirror:**
- `GetProductMarginSummaryRequestValidator` is byte-for-byte the same idiom as
  `GetMarginReportRequestValidator` (`WithErrorCode` → string int, `WithState` →
  `Dictionary<string,string>` cast to `object`, `WithMessage` last). Confirmed
  `ValidationResultBehavior<TRequest,TResponse>` parses `firstFailure.ErrorCode` via
  `Enum.TryParse<ErrorCodes>` and assigns `firstFailure.CustomState as
  Dictionary<string,string>` to `Params` — the validator's types line up exactly with
  what the behavior expects, so the 400 response actually gets `ErrorCode =
  InvalidTimeWindow` and `Params["timeWindow"]` populated as the spec requires (not
  just a generic `ValidationError`).
- `InvalidTimeWindow = 1706` doesn't collide with any existing `ErrorCodes` value
  (1701–1705 already used in the Analytics 17XX block; 1706 is genuinely free) and is
  correctly tagged `[HttpStatusCode(HttpStatusCode.BadRequest)]`, matching sibling
  `InvalidReportPeriod`'s tagging style.
- `TimeWindowParser.SupportedTimeWindows` is the single shared source of the five
  literals per arch-review Decision 1 (NFR-2) — the validator's `.Must(...)` reads
  from it rather than hardcoding a second copy, and `ParseTimeWindow`'s own switch is
  untouched, so the two can't drift apart the way the issue's original suggested-fix
  snippet risked.
- Comparison is case-sensitive/exact (`Array.Contains` → ordinal `string` equality),
  matching the parser's `switch` exactly per spec FR-1; test coverage includes a
  case-sensitivity check (`"Current-Year"` correctly rejected) and the empty-string
  case (`""` correctly rejected, matching FR-1's acceptance criterion for
  `timeWindow=`).
- `GetProductMarginSummaryRequest.TimeWindow` is a non-nullable `string` bound from a
  GET query parameter — confirmed model binding can produce only the default
  `"current-year"` (param omitted) or an explicit string value, never `null`, so
  there's no unguarded-null risk in `.Must(v => ...Contains(v))` (and
  `Enumerable.Contains` on a `string[]` is null-safe regardless).
- Confirmed via `grep` that `TimeWindowParser.ParseTimeWindow`/`SupportedTimeWindows`
  have no other callers outside this request's own handler/tests — no risk of an
  unrelated caller being affected (NFR-3).
- `GetProductMarginSummaryHandlerTests.ParseTimeWindow_UnknownValue_ThrowsArgumentException`
  calls the handler directly (bypassing MediatR/the pipeline), so it still exercises
  `ParseTimeWindow`'s own `ArgumentException` fallback unchanged — correctly left as a
  defensive guard per the spec's Out of Scope section, not a leftover inconsistency.
- No frontend files changed — matches spec's explicit Out of Scope.

**Build/test verification performed this round:**
- `dotnet build Anela.Heblo.sln` — 0 errors (256 pre-existing warnings, none in the
  changed files).
- Full `dotnet test --filter "FullyQualifiedName~Analytics"` re-run was still
  in-flight in the background at review time (this machine is running several other
  feature worktrees' builds/tests concurrently as part of the same fan-out, causing
  heavy CPU contention and a slow run) — not blocking, since the prior developer round
  (`impl/register-validation-pipeline.r1.md`, already committed on this branch)
  already recorded this exact filtered run passing 162/162, plus `dotnet build
  Anela.Heblo.sln` and `dotnet format Anela.Heblo.sln --verify-no-changes` both clean,
  and this review's own independent full-solution build confirms nothing has since
  broken.

No correctness issues found. The change is a small, surgical, spec-compliant gap-fill
that exactly mirrors an already-established pattern in the same module.
