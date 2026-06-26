Plan saved. Per the pipeline note in my instructions, I'm skipping the execution-handoff choice prompt — there is no human in this automated run.

**Summary:** Implementation plan saved to `docs/superpowers/plans/2026-06-01-decouple-article-gathercontextstep-from-knowledgebase.md`. Eight bite-sized tasks, TDD-ordered:

1. Add `IArticleKnowledgeSource` contract on the Article side.
2. Write failing tests for the KB adapter.
3. Implement `KnowledgeBaseArticleKnowledgeSource` (`internal sealed`, MediatR delegation, matches arch review amendments).
4. Register the binding in `KnowledgeBaseModule` next to the existing `IArticleStyleGuideSource` line.
5. Rewrite `GatherContextStepTests` to use `Mock<IArticleKnowledgeSource>`.
6. Switch `GatherContextStep` to consume the contract; drop `IMediator`, drop the KB `using` and `MediatR` using.
7. Delete the three Article allowlist entries in `ModuleBoundariesTests` (this is the FR-6 enforcement — existing theory does the work).
8. Full build / format / test validation pass.

The plan absorbs every arch-review amendment (adapter is `internal sealed`, named `KnowledgeBaseArticleKnowledgeSource`, parameter named `cancellationToken`, allowlist deletion in lieu of new architecture test, `IMediator` removal made assertive) and includes a self-review checklist mapping each spec FR/NFR to a task.