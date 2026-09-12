# Architecture Review: Unit tests for `DraftReplyLoggingBehavior`

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-coverage addition against an already-shipped, already-correct MediatR pipeline
behavior. No production code changes, no new interfaces, no new module. The behavior
(`DraftReplyLoggingBehavior`) is a thin adapter over shared infrastructure
(`IRagInteractionLogRepository`, `IRagInteractionRecorder`, `RagInteractionLogFactory`,
`ICurrentUserService`) that already has a fully analogous, already-tested twin:
`QuestionLoggingBehavior` in `KnowledgeBase/Pipeline`, covered by
`backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs`. Both behaviors:

- Implement `IPipelineBehavior<TRequest, TResponse>` for their respective feature's MediatR request.
- Short-circuit on `!response.Success || !_recorder.HasInteraction`.
- Build a `RagInteractionLog` via the shared `RagInteractionLogFactory.Build(...)`.
- Swallow `SaveAsync` exceptions behind a `try/catch` + `ILogger.LogError`, never failing the caller.
- Stamp `response.Id = log.Id` on success.

This is exactly the kind of "same shape, different feature" pair the RAG shared module
(`Anela.Heblo.Application.Shared.Rag`) was designed to support (see `RagInteractionRecorder`'s and
`RagInteractionLogFactory`'s XML docs, which explicitly say "shared by the KnowledgeBase and Smartsupp
logging pipeline behaviors"). The integration point for this task is therefore **the existing test,
not the existing production code** — verified by reading `DraftReplyLoggingBehavior.cs`,
`GenerateDraftReplyRequest`/`GenerateDraftReplyResponse`, `RagInteractionRecorder`,
`RagInteractionLogFactory`, `IRagInteractionLogRepository`, and `ICurrentUserService`/`CurrentUser`
directly, and cross-checking every FR in the spec against that code. No FR in the spec requires
behavior this reviewer cannot confirm from the source.

No UI/UX surface is touched (`GenerateDraftReplyRequest`/`Response` are internal MediatR contracts
consumed only by a controller elsewhere in the Smartsupp module — not modified here). `Skip Design`
is `true`.

## Proposed Architecture

### Component Overview
No new components. The test slots into the existing structure at the pipeline-behavior test layer:

```
Application layer (unchanged)
  GenerateDraftReplyHandler ──▶ DraftReplyLoggingBehavior ──▶ IRagInteractionLogRepository
                                        │        │
                                        │        └──▶ ICurrentUserService
                                        └──▶ IRagInteractionRecorder (RagInteractionRecorder)
                                                       │
                                                       └──▶ RagInteractionLogFactory.Build(...)

Test layer (new)
  DraftReplyLoggingBehaviorTests
      - Mock<IRagInteractionLogRepository>   (verifies SaveAsync calls / no-calls)
      - Mock<ICurrentUserService>            (supplies a known CurrentUser)
      - RagInteractionRecorder (concrete)    (populated the way production handlers populate it)
      - Mock<ILogger<DraftReplyLoggingBehavior>>  (present only to satisfy the constructor)
      - drives Handle(request, next, ct) directly, no DI container, no MediatR pipeline
```

This mirrors `QuestionLoggingBehaviorTests` component-for-component; the only substitutions are the
feature-specific request/response types (`GenerateDraftReplyRequest`/`Response` in place of
`AskQuestionRequest`/`Response`) and the log's `Feature` value (`RagFeature.SmartsuppDraftReply` via
`RecordInteraction(RagFeature.SmartsuppDraftReply, ...)` instead of `RagFeature.KnowledgeBase`).

### Key Design Decisions

#### Decision 1: Use a real `RagInteractionRecorder`, not a mock
**Options considered:**
- Mock `IRagInteractionRecorder` and stub `HasInteraction`/`Feature`/`Question`/etc. property-by-property.
- Use the concrete `RagInteractionRecorder` and populate it via its real `RecordRetrieval`/
  `RecordInteraction` methods, as production handlers do.

**Chosen approach:** Concrete `RagInteractionRecorder`, populated through its public API — exactly
what the sibling test does (with an explicit code comment there justifying it).

**Rationale:** `RagInteractionRecorder` is a plain scoped accumulator with no I/O and no side effects
outside its own state; mocking it buys nothing and risks the mock's stubbed `HasInteraction` value
drifting out of sync with what `RecordInteraction` actually sets in production, which is precisely
the risk the brief calls out ("if `HasInteraction` returns `false` when it should return `true`").
Driving the real recorder through its real API makes the "no interaction recorded" test case
(FR-3) fall out naturally — just don't call `RecordInteraction` — rather than requiring a mock setup
that could silently diverge from the real implementation.

#### Decision 2: Test file location — top-level `Smartsupp/Pipeline/`, not `Features/Smartsupp/`
**Options considered:**
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs` —
  consistent with where the *rest* of this project's Smartsupp tests already live
  (`Features/Smartsupp/GenerateDraftReplyHandlerTests.cs`, `SubmitDraftReplyFeedbackHandlerTests.cs`,
  `GetDraftReplyFeedbackListHandlerTests.cs`, etc. all live under `Features/Smartsupp/`).
- `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs` — a brand-new
  top-level `Smartsupp/` folder, mirroring the top-level `KnowledgeBase/Pipeline/` folder that already
  holds `QuestionLoggingBehaviorTests.cs` (itself distinct from the pre-existing
  `Features/KnowledgeBase/` folder).

**Chosen approach:** The spec's path — top-level `Smartsupp/Pipeline/`.

**Rationale:** I confirmed by directory listing that this test project already has **both** layouts
side by side today (`Features/KnowledgeBase/` *and* top-level `KnowledgeBase/`; similarly
`Features/Smartsupp/` already exists). This is a pre-existing inconsistency in the repo, not something
introduced by this task, and not something a coverage-gap ticket should attempt to resolve. Given that
choice already exists, following the *direct structural precedent* named in the spec — the sibling
`Pipeline` behavior test for the same shared RAG infrastructure — is the right anchor, because a future
reader comparing `Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs` against
`KnowledgeBase/Pipeline/QuestionLoggingBehaviorTests.cs` gets an apples-to-apples pair. **Do not**
consolidate this into `Features/Smartsupp/Pipeline/` — that would break the analogy the spec is
explicitly building on and is out of scope for a test-only ticket. No `Anela.Heblo.Tests.csproj`
changes are needed either way (folders are not registered anywhere; xUnit discovers all `[Fact]`s in
the compiled assembly).

#### Decision 3: Assertion library — plain xUnit `Assert`, not FluentAssertions
**Options considered:**
- `docs/architecture/testing-strategy.md` lists FluentAssertions as the project's assertion library.
- The sibling test (`QuestionLoggingBehaviorTests.cs`) uses only bare xUnit `Assert.*`.

**Chosen approach:** Match the sibling test — plain `Assert.*`.

**Rationale:** The spec explicitly says "mirror its conventions rather than invent new ones," and this
is a tightly-scoped, single-file addition next to a same-shape sibling. Introducing FluentAssertions
into this one file while its direct twin does not use it would create an inconsistency more confusing
than following the general project doc. This is a style choice, not an architectural one — flagging it
here only so the developer doesn't "fix" it against the doc mid-task.

## Implementation Guidance

### Directory / Module Structure
- New file: `backend/test/Anela.Heblo.Tests/Smartsupp/Pipeline/DraftReplyLoggingBehaviorTests.cs`.
- No new test project, no new `.csproj` entries, no new NuGet packages — `Moq` and `xUnit` are already
  referenced by `Anela.Heblo.Tests.csproj` (confirmed via the sibling test's usings).
- No changes anywhere under `backend/src/` — `DraftReplyLoggingBehavior.cs` is untouched; it is already
  fully unit-testable via its four constructor-injected interfaces.

### Interfaces and Contracts
No new or changed interfaces. The test exercises exactly the public surface already defined:

- `DraftReplyLoggingBehavior(IRagInteractionLogRepository, IRagInteractionRecorder, ICurrentUserService, ILogger<DraftReplyLoggingBehavior>)`
- `Task<GenerateDraftReplyResponse> Handle(GenerateDraftReplyRequest request, RequestHandlerDelegate<GenerateDraftReplyResponse> next, CancellationToken cancellationToken)`
- `RagInteractionRecorder.RecordInteraction(RagFeature.SmartsuppDraftReply, question, systemPrompt, answer, conversationId, topic)` and, optionally, `RecordRetrieval(...)` — call these directly on the concrete recorder instance in test setup, matching how `GenerateDraftReplyHandler` calls them in production.
- `GenerateDraftReplyRequest.ConversationId` is non-nullable (`string = null!`) in production (set by the controller from the route) — tests must always supply a concrete string (e.g. `"conv-1"`) since the catch block interpolates it into a log message; a null there is a latent NRE risk the spec already flags (FR-5) and the test should pin down, not work around.

### Data Flow
Same as `QuestionLoggingBehaviorTests`, substituting the Smartsupp types: `Handle` is invoked directly
with a hand-built `GenerateDraftReplyRequest` and a `next` delegate (`() => Task.FromResult(response)`)
standing in for the inner `GenerateDraftReplyHandler`. No MediatR pipeline, no DI container, no
database — every collaborator is either a Moq mock or the concrete in-memory `RagInteractionRecorder`.
There is nothing beyond what the FRs in the spec already fully describe (skip/happy/exception paths);
no additional data-flow design is needed for a test-only change.

### Test cases to implement (from the spec, mapped 1:1 to FRs — no additions needed)
1. `Handle_WhenResponseUnsuccessful_DoesNotSaveOrSetId` (FR-2)
2. `Handle_WhenNoInteractionRecorded_DoesNotSaveOrSetId` (FR-3)
3. `Handle_WritesLogRow_AndReturnsResponse` / `Handle_WhenLogSaved_SetsResponseIdToLogId` (FR-4 — the
   sibling splits "log row written with correct fields" and "response.Id stamped" into two `[Fact]`s;
   follow that split rather than combining, for the same single-assertion-purpose-per-test reason the
   sibling suite follows it)
4. `Handle_WhenDbWriteFails_StillReturnsResponse` / `Handle_WhenDbWriteFails_ResponseIdRemainsNull`
   (FR-5 — again, sibling splits into two facts)

This yields 5–6 `[Fact]`s (matching the sibling's 6), not the brief's "four" — the brief undercounts
by collapsing FR-4 and FR-5 each into a single case; the spec's acceptance criteria (and the sibling
file) correctly split them. Use the spec's granularity.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Test asserts against internal `RagInteractionLogFactory` fields not actually guaranteed by this behavior's contract, coupling the suite to factory internals | Low | Per spec FR-6 / sibling convention, assert only `UserId`, and whichever of `Question`/`Answer`/`TopK` were actually recorded via `RecordRetrieval`/`RecordInteraction` in that test's arrange step — do not assert `SystemPrompt`, `RetrievedChunksJson`, `ExpandedQuery`, etc. |
| `ConversationId` left `null` in the exception-path test causes a `NullReferenceException` inside the `catch` block's string interpolation, masking the real assertion (that no exception propagates) | Medium | Always set `ConversationId` to a concrete string (e.g. `"conv-1"`) in every test's request, not just the exception-path one — cheap and removes any ambiguity about whether a caught NRE vs. the *original* exception is what "no exception propagates" is proving |
| Placing the file at a new top-level `Smartsupp/` test folder while `Features/Smartsupp/` already holds the rest of the module's tests looks like an accidental duplicate folder to a future reader | Low | This review's Decision 2 stands as the documented rationale (mirrors `KnowledgeBase/Pipeline/`); no further mitigation needed — do not "fix" it by relocating during this ticket |
| None of these tests exercise the real DI wiring (i.e. that `DraftReplyLoggingBehavior` is actually registered in the MediatR pipeline for `GenerateDraftReplyRequest`) | Low | Out of scope per spec — pipeline registration is either already covered by a wiring/composition test elsewhere or is trivial `services.AddScoped(typeof(IPipelineBehavior<,>), typeof(DraftReplyLoggingBehavior))`-style registration; do not add one here, note it only if grep shows no such test exists anywhere in the suite |

## Specification Amendments
- **Test-case count**: the brief says "four test cases"; the spec's own FR-4/FR-5 acceptance criteria
  (and the sibling file) actually require the happy path and the exception path to each be split into
  two `[Fact]`s (log-written-with-correct-fields vs. id-stamped; response-still-returned vs.
  id-remains-null), for a total of 5–6 facts, matching `QuestionLoggingBehaviorTests`'s six. Implement
  at the spec's granularity, not the brief's undercount — no spec change needed, just flagging the
  discrepancy so the developer doesn't stop at four.
- No other amendments. FR-1 through FR-6 and the Data Model / Dependencies / Out-of-Scope sections were
  all verified directly against the current source and require no correction.

## Prerequisites
None. `backend/test/Anela.Heblo.Tests` already compiles and references `Moq`/`xUnit`; the production
types under test are unmodified and already fully testable; no migrations, config, or infrastructure
changes are needed before implementation can start.
