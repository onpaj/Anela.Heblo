Verification complete. The spec's claims hold up against the code: only the 5 step classes and the interface file itself reference `IArticlePipelineStep` (no consumer, no test, no DI binding via interface). Writing the review now.

# Architecture Review: Remove Unused `IArticlePipelineStep` Interface

## Skip Design: true

Backend-only deletion of a dead interface — no UI surface, no visual components, no user-facing flows.

## Architectural Fit Assessment

The proposal aligns cleanly with the project's Clean Architecture + Vertical Slice conventions (`docs/architecture/filesystem.md`). The Article module already organizes its generation pipeline under `Application/Features/Article/UseCases/Generate/Pipeline/`, and the surrounding code (`ArticleModule.cs`, `GenerateArticleJob.cs`) consistently treats the steps as concrete scoped services. The interface sits orthogonally to that — it is never bound, never resolved, and never mocked.

Integration points after removal:
- **DI registration** (`ArticleModule.cs:19-25`): no change — already registers concrete types.
- **Job composition** (`GenerateArticleJob.cs:13-36`): no change — already injects concrete types and invokes them in a fixed sequence.
- **Tests** (`backend/test/Anela.Heblo.Tests/Article/Pipeline/*StepTests.cs` + `GenerateArticleJobTests.cs`): grep across `backend/test` for `IArticlePipelineStep` returns zero hits. FR-4 reduces to a verification gate, not an edit.

The change is honest about current usage and consistent with the YAGNI principle baked into the global coding standards. There is no architectural objection.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Application/Features/Article/
├── ArticleModule.cs                             (unchanged — already concrete-typed)
└── UseCases/Generate/
    ├── GenerateArticleJob.cs                    (unchanged — already concrete-typed)
    └── Pipeline/
        ├── ArticlePipelineContext.cs            (unchanged)
        ├── PipelineStepRecorder.cs              (unchanged)
        ├── IArticlePipelineStep.cs              ❌ DELETE
        ├── PlanQueriesStep.cs                   ✏️  remove ": IArticlePipelineStep"
        ├── GatherContextStep.cs                 ✏️  remove ": IArticlePipelineStep"
        ├── AggregateFactsStep.cs                ✏️  remove ": IArticlePipelineStep"
        ├── ValidateFactsStep.cs                 ✏️  remove ": IArticlePipelineStep"
        └── WriteArticleStep.cs                  ✏️  remove ": IArticlePipelineStep"
```

After the change, the pipeline shape is: `GenerateArticleJob` directly drives five concrete `*Step` services in a fixed order via the shared `ArticlePipelineContext` — the exact composition `GenerateArticleJob.cs:53-61` already encodes.

### Key Design Decisions

#### Decision 1: Option A (delete) vs Option B (actually use the interface)
**Options considered:**
- A — Delete the interface entirely.
- B — Register and inject via `IArticlePipelineStep` (e.g., `IEnumerable<IArticlePipelineStep>` or named registrations) to enable polymorphic mocking.

**Chosen approach:** A — delete.

**Rationale:** The pipeline is intentionally a fixed, ordered sequence in `GenerateArticleJob.RunAsync` with side effects (`MarkAsResearching`, `MarkAsWriting`, intermediate `SaveChangesAsync`) interleaved between steps. Option B would require either (a) keeping that interleaving in the job and still injecting concrete types — which gives no benefit over today, or (b) restructuring the job into a pure pipeline runner that loses the state-machine semantics. Neither is justified by any current requirement. Option A is reversible: re-introducing the interface is a one-file change if a future need arises.

#### Decision 2: Atomicity and scope discipline
**Options considered:** Single commit vs. five per-step commits.
**Chosen approach:** Single commit covering all six edits (delete file + five class declaration edits) plus any incidental `dotnet format` output on touched files only.
**Rationale:** NFR-4 (reversibility). One revert restores the prior state. Avoid drive-by edits to unrelated files in the same commit — surgical-changes rule from `CLAUDE.md`.

#### Decision 3: Class declarations stay `public class`, not `public sealed class`
**Options considered:** Keep `public class` as-is; or seal each step now that no interface forces extensibility expectations.
**Chosen approach:** Keep `public class`.
**Rationale:** Sealing is a behavior change outside this spec's scope. The brief asks to remove dead signal, not to add new constraints. FR-2 explicitly requires no other modifier changes.

## Implementation Guidance

### Directory / Module Structure
- **Delete:** `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/IArticlePipelineStep.cs`.
- **Edit (declaration only):** the five `*Step.cs` files in the same folder. In each, change `public class XxxStep : IArticlePipelineStep` → `public class XxxStep`. No other lines in those files should change.
- **No new files. No moves. No renames.**

### Interfaces and Contracts
- **No public/internal contract changes.** No interface, abstract base, or DI marker is introduced in place of `IArticlePipelineStep`. Step classes remain plain concrete services discoverable by their concrete type from the DI container.
- **`ExecuteAsync(ArticlePipelineContext, CancellationToken)`** stays as the de facto convention across all five steps. It is enforced by `GenerateArticleJob`'s direct calls, not by a compiler-checked interface. This is acceptable because there is exactly one consumer.
- **Documentation reference:** No mention of `IArticlePipelineStep` exists in published architecture docs (`docs/architecture/*`, `docs/features/*`). The only references outside the deleted/edited files are in a historical plan file at `docs/superpowers/plans/2026-05-08-article-generation-metadata.md`. That file is a frozen plan record, not living architecture documentation — leave it as a historical artifact; do not retroactively edit it.

### Data Flow
Unchanged. `GenerateArticleJob.RunAsync(articleId, ct)`:
1. Loads `Article`, marks `Researching`, persists.
2. Constructs `ArticlePipelineContext` with the article.
3. Invokes `_planQueries → _gatherContext → _aggregateFacts → _validateFacts` directly on the concrete services, threading the context.
4. Marks `Writing`, persists, invokes `_writeArticle`.
5. Marks `Generated`, materializes `ArticleSource` rows from `context.SourceRefs`, persists.
6. On cancel/exception: marks `Failed` with a non-cancellation token and persists.

The deletion does not touch any branch of this flow.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden external consumer outside the solution (e.g., a generated client, reflection-based discovery) | Low | Repo-wide grep confirms zero non-implementer references; module is internal to `Anela.Heblo.Application`. Confirm again post-edit with `grep -r "IArticlePipelineStep"` returning zero hits. |
| Future need for polymorphic step injection re-emerges shortly after deletion | Low | Reintroducing the interface costs ~6 lines plus DI changes. Decision is explicitly reversible (NFR-4). Mention in PR description that Option B remains available. |
| `dotnet format` rewrites unrelated whitespace and inflates the diff | Low | Run `dotnet format --include` scoped to the six touched files before committing; reject unrelated reformatting hunks at review time. |
| Test project still compiles only because no test referenced the interface today; a partially-merged branch elsewhere may add such a reference | Low | Verify against `main` (no such reference exists at HEAD) and rebase/backmerge before merging. |
| Stale references in historical planning docs cause future-reader confusion | Negligible | Leave `docs/superpowers/plans/2026-05-08-article-generation-metadata.md` untouched (historical artifact). The spec already excludes it via "Documentation updates beyond removing stale references". |

## Specification Amendments

The spec is internally consistent and grounded in the code. Two clarifications to add before implementation:

1. **FR-4 wording:** Replace "If any unit or integration test asserts against, mocks, or names `IArticlePipelineStep`, it must be updated" with the verified state: "A repository-wide search at HEAD confirms no test references `IArticlePipelineStep`. The acceptance criterion reduces to verifying that `grep -r 'IArticlePipelineStep' backend/test` returns zero matches after the change." This avoids implying that test edits are expected when none are.

2. **FR-3 byte-equivalence clause:** The phrase "byte-equivalent to pre-change state" in FR-3 is too strict if `dotnet format` runs over the file. Soften to "functionally and textually equivalent ignoring formatter-induced whitespace; no registered service, lifetime, or registration order changes." This matches NFR-3 intent without booby-trapping the implementer on incidental formatting.

3. **Historical plan file:** Add to "Out of Scope" an explicit clause: "Historical plan documents under `docs/superpowers/plans/` are frozen artifacts and must not be edited as part of this change."

No other amendments needed.

## Prerequisites

None. The change is self-contained within the Article module and requires:
- No migrations.
- No configuration changes.
- No new packages or version bumps.
- No infrastructure provisioning.
- No coordination with other in-flight branches (verified — no open PR touches these files at HEAD per `git status` clean and recent commits unrelated to the pipeline interface).

Implementation can start immediately. Validation gates per `CLAUDE.md`: `dotnet build`, `dotnet format`, and `dotnet test` on the touched test project (`Anela.Heblo.Tests`).