# Architecture Review: Encapsulate Article Feedback Submission in the Domain Entity

## Skip Design: true

## Architectural Fit Assessment

The proposal aligns perfectly with the established patterns in this codebase. Verified against the actual sources:

- **Entity convention** (`backend/src/Anela.Heblo.Domain/Features/Article/Article.cs`): `Article` already exposes lifecycle behavior via `MarkAsResearching`, `MarkAsWriting`, `MarkAsGenerated`, and `MarkAsFailed`. Feedback submission is the sole anemic outlier. The new method slots into the existing method group without altering the entity's shape, identity, or persistence mapping.
- **Domain-model guideline** (`docs/architecture/development_guidelines.md` — "Common Pitfalls #6: Don't create anemic domain models — Put behavior in entities"): this refactor is the canonical example of compliance.
- **Handler boundary** (ADR-005, `SubmitArticleFeedbackHandler.cs:34-55`): identity resolution, repository access, and typed error responses stay in the handler — the entity stays free of cross-cutting collaborators. Keeping guards in the handler is correct under the codebase's conventions.
- **Vertical-slice layout**: no boundary, contract, DTO, or DI binding moves. The OpenAPI surface and generated TypeScript client are untouched.

Integration points are minimal and local: one new method, one call-site swap, one new domain test file. No infrastructure, migrations, or cross-module ripple.

## Proposed Architecture

### Component Overview

```
HTTP POST /api/articles/{id}/feedback
        │
        ▼
ArticlesController (unchanged)
        │  MediatR send
        ▼
SubmitArticleFeedbackHandler
  ├── ICurrentUserService.GetCurrentUser()      [identity]
  ├── IArticleRepository.GetForUpdateAsync()    [load]
  ├── guard: ArticleNotFound
  ├── guard: Forbidden (RequestedBy match)
  ├── guard: ArticleNotGenerated
  ├── guard: ArticleFeedbackAlreadySubmitted
  ├── article.SubmitFeedback(p, s, c)           [NEW — entity behavior]
  └── IArticleRepository.SaveChangesAsync()     [persist via EF change tracker]
        │
        ▼
SubmitArticleFeedbackResponse
```

The only structural change: a single mutation site moves from the handler into the entity.

### Key Design Decisions

#### Decision 1: Keep guards in the handler, not the entity
**Options considered:**
1. Mirror the existing `MarkAs*` style: pure setter aggregator on the entity, guards stay in the handler.
2. Push guard logic into the entity by throwing typed domain exceptions, then catch and map in the handler.

**Chosen approach:** Option 1.

**Rationale:** The brief and spec both scope this as a pure refactor. The handler's guards depend on collaborators (`ICurrentUserService`, repository state) and need typed error responses (`ErrorCodes.*`) that the rest of the slice already shapes. Introducing a domain-exceptions-to-error-codes translation layer for one method would be a larger architectural change than the brief justifies. This matches how the other `MarkAs*` methods behave (no internal guards beyond `MarkAsFailed`'s truncation, which is normalization, not validation).

#### Decision 2: Do not change setter visibility on the three feedback properties
**Options considered:**
1. Leave public setters in place (EF Core + existing tests rely on them).
2. Tighten to `private set` and switch tests to a factory/builder.

**Chosen approach:** Option 1.

**Rationale:** The existing test factory (`CreateArticle(..., precisionScore: 4, ...)`) and the handler-guard-stage assignments in seven test scenarios depend on the public setters. Tightening visibility is a separate, larger refactor explicitly listed as Out of Scope in the spec. This decision keeps the change surgical.

#### Decision 3: Place the new domain test under `Tests/Domain/Article/`, not `Tests/Article/Domain/`
**Options considered:**
1. Follow the spec literally: `backend/test/Anela.Heblo.Tests/Article/Domain/ArticleTests.cs`.
2. Follow the established repo convention.

**Chosen approach:** Option 2 — `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs`.

**Rationale:** Verified against the existing test tree: every other domain-entity test lives under `Tests/Domain/{Feature}/{Entity}Tests.cs` (`Domain/Logistics/TransportBoxStateTransitionTests.cs`, `Domain/Purchase/PurchaseOrderTests.cs`, `Domain/Catalog/CatalogAggregateTests.cs`, `Domain/Marketing/MarketingActionConstructorTests.cs`). The spec's proposed path inverts the namespace hierarchy and would create the first inconsistent location. See **Specification Amendments** below.

## Implementation Guidance

### Directory / Module Structure

```
backend/
├── src/Anela.Heblo.Domain/Features/Article/
│   └── Article.cs                              [EDIT — add SubmitFeedback]
├── src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/
│   └── SubmitArticleFeedbackHandler.cs         [EDIT — replace lines 57-59]
└── test/Anela.Heblo.Tests/
    ├── Domain/Article/
    │   └── ArticleTests.cs                     [NEW — entity-level tests]
    └── Article/UseCases/
        └── SubmitArticleFeedbackHandlerTests.cs [UNCHANGED — must pass as-is]
```

### Interfaces and Contracts

```csharp
// Anela.Heblo.Domain.Features.Article.Article — NEW METHOD
// Placement: alongside MarkAsFailed (last method in the file)
public void SubmitFeedback(int precisionScore, int styleScore, string? comment)
{
    PrecisionScore = precisionScore;
    StyleScore = styleScore;
    FeedbackComment = comment;
}
```

```csharp
// SubmitArticleFeedbackHandler.Handle — CHANGED BODY (lines 57-59)
// Before:
article.PrecisionScore = request.PrecisionScore;
article.StyleScore = request.StyleScore;
article.FeedbackComment = request.Comment;
// After:
article.SubmitFeedback(request.PrecisionScore, request.StyleScore, request.Comment);
```

No new types, no signature changes, no contract surface changes, no DI changes, no module registration changes. Public setters on `PrecisionScore`, `StyleScore`, `FeedbackComment` remain — required by EF Core change tracking and by the existing handler-test factory.

### Data Flow

1. Controller forwards HTTP body → `SubmitArticleFeedbackRequest` (MediatR).
2. Handler loads tracked entity via `GetForUpdateAsync`.
3. Handler runs four guards → typed error responses on failure.
4. Handler calls `article.SubmitFeedback(p, s, c)` — sets three properties on the tracked entity.
5. Handler calls `SaveChangesAsync` — EF Core emits the same `UPDATE Articles SET PrecisionScore=…, StyleScore=…, FeedbackComment=… WHERE Id=…` as today.
6. Handler reads the three values back off the entity into `SubmitArticleFeedbackResponse`.

The persistence path is byte-identical to today's. The only difference is the call stack at step 4.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing handler test (`HappyPath_SubmitsFeedback`) asserts on entity properties after `Handle` returns — could regress if method body diverges from direct assignment | LOW | Method body is a 3-line setter aggregator; the existing 7 scenarios remain unmodified and must pass. Validation = `dotnet test` on `Anela.Heblo.Tests`. |
| EF Core change tracking misses one of the three properties if a future contributor adds normalization that creates a new object instead of mutating | LOW | Stay with in-place property assignment inside `SubmitFeedback`. If a future change adds bounds enforcement, the established pattern (see `MarkAsFailed`'s truncation) is in-place mutation, not record-style cloning. |
| Spec asks for the test file at a non-canonical path | LOW | Place new tests under `Tests/Domain/Article/ArticleTests.cs` per repo convention. See Amendments. |
| Future contributor reintroduces direct property assignment in another handler | LOW | Out of scope. Could be addressed later by tightening setters to `private set`, but that is a separate refactor explicitly out of scope here. |

## Specification Amendments

1. **Test file location.** Replace the proposed path `backend/test/Anela.Heblo.Tests/Article/Domain/ArticleTests.cs` with **`backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs`** and namespace **`Anela.Heblo.Tests.Domain.Article`**. This mirrors every existing domain-entity test in the repo (`Domain/Logistics`, `Domain/Purchase`, `Domain/Catalog`, `Domain/Marketing`).

2. **Method placement clarification.** Place `SubmitFeedback` **immediately after `MarkAsFailed`** at the end of `Article.cs`. The current method ordering is chronological by lifecycle (`Researching → Writing → Generated → Failed`); feedback is the post-`Generated` action, so appending it preserves the lifecycle reading order.

3. **No need to verify "first entity test"**: an existing `ArticleTests.cs` under `Tests/Domain/Article/` does **not** currently exist (verified). The new file establishes that location for this entity.

No other amendments. The spec is otherwise tight and accurate.

## Prerequisites

None.

- No migrations.
- No configuration / Key Vault changes.
- No DI registrations.
- No OpenAPI regeneration.
- No frontend changes.
- No infrastructure changes.

Implementation can begin immediately against the current `main`/feature branch.