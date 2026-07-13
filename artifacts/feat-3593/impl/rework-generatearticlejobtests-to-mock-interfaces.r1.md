# Implementation: rework-generatearticlejobtests-to-mock-interfaces

## What was implemented
Reworked `GenerateArticleJobTests.cs` to mock the five new pipeline-step interfaces (`IPlanQueriesStep`, `IGatherContextStep`, `IAggregateFactsStep`, `IValidateFactsStep`, `IWriteArticleStep`) directly with `Mock<T>` fields instead of constructing real step instances wired to mocked leaf dependencies (`IChatClient`, `IArticleKnowledgeSource`, `IWebSearchClient`, `IArticleStyleGuideSource`). A constructor now defaults all five step mocks to a no-op `Task.CompletedTask` so each test only needs to override the specific step(s) relevant to its scenario. `CreateJob()` is now a no-arg method that wires the five mocks directly into `GenerateArticleJob`. The happy-path test sets the expected output via a `Callback` on the `WriteArticleStep` mock; the "step throws" and cancellation tests set up the relevant step mock to throw directly instead of arranging JSON responses through a shared `IChatClient` mock.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs` — replaced leaf-dependency mocks and `CreateNoOpRecorder()`/`SetupChatResponses()` helpers with direct step-interface mocks; rewrote all three test cases (happy path, step-throws, cancellation) to drive behavior through the step mocks.

## Tests
`GenerateArticleJobTests.cs` — all 4 existing tests (`RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted`, the step-throws test, the cancellation test, and one additional existing test) continue to cover the job's orchestration logic (status transitions, error handling, source persistence), now isolated from the steps' internal JSON-parsing logic.

## How to verify
```bash
cd /home/user/worktrees/feature-3593-Arch-Review-Article-Generatearticlejob-Depends-On
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateArticleJobTests"
dotnet format Anela.Heblo.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs
```
All pass: build succeeds with 0 errors, 4/4 tests pass, no formatting diffs.

## Notes
None — implementation matches the task context's before/after code exactly. Task 1's interface types (already committed in `4570e3b`) were used as-is; no changes were needed to `GenerateArticleJob.cs`, the step classes, or `ArticleModule.cs` for this task.

## PR Summary
Reworks the Article-generation orchestration tests to mock the new per-step interfaces directly rather than constructing real step implementations wired to mocked leaf dependencies. This isolates `GenerateArticleJobTests` from each step's internal JSON-parsing/LLM-call logic, so a test like "job marks article Failed when a step throws" only needs to arrange that one step mock to throw — no need to first wire up a successful upstream step via a fake `IChatClient` response.

### Changes
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs` — mocks step interfaces directly; removed now-unused `IChatClient`/`IArticleKnowledgeSource`/`IWebSearchClient`/`IArticleStyleGuideSource`/`ArticleOptions` test scaffolding.

## Status
DONE
