# Architecture Review: Correct Authorization Role in Article Generation Feature Doc

## Skip Design: true

## Architectural Fit Assessment

The spec is correct that this is a single-file documentation correction with no production code impact. However, **the spec's premise about the current authorization mechanism is stale and would, if followed verbatim, replace one inaccurate snippet with another inaccurate snippet.**

The brief was filed 2026-05-29. Between then and now, the authorization model was completely rebuilt under the "permission source of truth" initiative (see `docs/superpowers/plans/2026-06-08-permission-source-of-truth.md` and `docs/superpowers/specs/2026-06-08-feature-authorize-attribute-design.md`). Concretely:

- `AuthorizationConstants.cs` **no longer exists** — it was explicitly deleted as part of the migration. The spec's `AuthorizationConstants.Policies.MarketingReader` reference points at a removed file.
- `ArticlesController.Generate` at `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:27-32` does **not** use `[Authorize(Policy = ...)]`. It uses `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` (a custom `AuthorizeAttribute` subclass at `backend/src/Anela.Heblo.Domain/Features/Authorization/FeatureAuthorizeAttribute.cs`).
- The class itself is gated with `[FeatureAuthorize(Feature.Marketing_Article)]` (defaulting to `AccessLevel.Read`), so `GET` endpoints inherit read-level gating — they are **not** open to all `heblo_user` holders as line 143 currently claims.
- The actual role strings are generated from `access-matrix.json` into `AccessRoles.generated.cs`. For this controller they are `marketing.article.read` and `marketing.article.write` — not `marketing_reader` and not `marketing_writer`. Those legacy role names are no longer present in the codebase.

The integration point for the documentation fix is therefore not "swap `marketing_writer` for `marketing_reader`" — it is "rewrite section 14 (and the matching line in section 7) to reflect the current `FeatureAuthorize`/`Feature.Marketing_Article`/permission-string model."

## Proposed Architecture

### Component Overview

```
                          access-matrix.json (source of truth)
                                       │
                          AccessMatrixGen generator
                          ┌────────────┼────────────────────────────┐
                          ▼            ▼                            ▼
            Feature.generated.cs   AccessRoles.generated.cs   accessMatrix.generated.ts
                    │                       │                       │
                    │   ┌───────────────────┘                       │
                    ▼   ▼                                           ▼
         [FeatureAuthorize(Feature.Marketing_Article,        Frontend route guards
            AccessLevel.Write)]   ◄── ArticlesController.Generate
                    │
                    ▼
            Roles = "marketing.article.write"
                    │
                    ▼
        Azure AD app role / group claim
```

The documentation in `docs/features/article-generation.md` must describe **this** pipeline, not the legacy "hand-written `AuthorizationConstants` policy" pipeline.

### Key Design Decisions

#### Decision 1: Use `marketing.article.write` (not `marketing.article.read`) for `POST /generate`
**Options considered:**
- (a) Document `marketing.article.write` — matches the controller exactly.
- (b) Document `marketing.article.read` — matches the spec's stated intent ("MarketingReader was correct").
- (c) Document both, marking write as the actual gate.

**Chosen approach:** (a). The controller attribute at line 28 is `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]`, which `AccessRoles.For(Feature.Marketing_Article, AccessLevel.Write)` resolves to `marketing.article.write` (see `AccessRoles.generated.cs:46,105`). The spec was drafted against the pre-migration code where the gate was `MarketingReader` → `marketing_reader`; the migration changed both the attribute style **and** the access level (Read → Write) to align with the matrix's read/write split.

**Rationale:** The doc must match what the build produces. Anyone reading the spec's recommendation ("use `marketing_reader`") would currently grant a role that does not exist and that the controller would not honor.

#### Decision 2: Document the `FeatureAuthorize` attribute, not `[Authorize(Policy=...)]`
**Options considered:**
- (a) Show `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` verbatim from the controller.
- (b) Keep the legacy `[Authorize(Policy = ...)]` snippet from the spec.

**Chosen approach:** (a). The whole point of FR-2 ("snippet matches the attribute on `ArticlesController.Generate` verbatim") is verbatim accuracy; the spec's proposed verbatim string is no longer verbatim.

**Rationale:** Same accuracy criterion the spec already invokes — applied against the real file rather than the brief's quote of it.

#### Decision 3: Also correct section 7 (line 143), not only section 14
**Options considered:**
- (a) Stay strictly inside section 14 per the spec's wording.
- (b) Extend FR-4's "document-wide consistency sweep" to fix section 7 line 143 ("POST `/generate` requires role `marketing_writer`; reads require `heblo_user`") which is doubly wrong: wrong role name *and* wrong read-side claim (reads also require `Feature.Marketing_Article` Read, not just authenticated).

**Chosen approach:** (b). FR-4 already mandates a sweep; line 143 is the most prominent remaining inaccuracy and trivially in scope.

**Rationale:** A reader who only reads section 7 still gets the wrong picture if 143 is left alone. The cost of fixing it is one line.

## Implementation Guidance

### Directory / Module Structure
- Touch only `docs/features/article-generation.md`. No code changes.
- Do **not** revive `AuthorizationConstants.cs` references anywhere — that file is gone by design.

### Interfaces and Contracts

**Replace section 7 line 143** with:
> All endpoints are gated through `[FeatureAuthorize(Feature.Marketing_Article, …)]` (class-level default is `AccessLevel.Read`). `POST /generate` requires `AccessLevel.Write` (`marketing.article.write`); `GET /{id}`, `GET /` and `POST /{id}/feedback` require `AccessLevel.Read` (`marketing.article.read`).

**Replace section 14 bullets** with prose that:
1. Names `marketing.article.write` as the role required by `POST /generate` and `marketing.article.read` as the role required by the read endpoints and feedback POST.
2. Shows the class-level attribute (`[FeatureAuthorize(Feature.Marketing_Article)]` on `ArticlesController`) AND the method-level override (`[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` on `Generate`) — both lifted verbatim from `ArticlesController.cs:15` and `:28`.
3. References the source of truth: `access-matrix.json`, which is compiled by `Anela.Heblo.AccessMatrixGen` into `Feature.generated.cs`, `AccessRoles.generated.cs`, and `accessMatrix.generated.ts`.
4. Rewrites the historical note to: *"The earlier `article_generator` and short-lived `marketing_reader`/`marketing_writer` role strings have been retired. Access is now declared in `access-matrix.json` and surfaced as `marketing.article.read` / `marketing.article.write` permissions."*

### Data Flow
No runtime data flow change. The documentation flow is:
- Read `ArticlesController.cs:15-35` → record class-level + method-level `FeatureAuthorize` attributes.
- Read `AccessRoles.generated.cs:45-46,104-105` → confirm the two permission strings.
- Read `access-matrix.json:26` → confirm `{ "key": "Marketing_Article", "label": "Články", "hasWrite": true }`.
- Edit section 7 (line 143) and section 14 of `docs/features/article-generation.md` to match.
- Grep the file for any remaining `marketing_writer`, `marketing_reader`, `article_generator`, or `AuthorizationConstants` literal — none should remain after the edit.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Implementer follows the spec verbatim and writes `[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]` into the doc — pointing at a deleted file and inventing a constant that does not exist. | **HIGH** | Spec amendment below; implementer must read `ArticlesController.cs:15-35` and `AccessRoles.generated.cs` before writing the new section 14 text. |
| Writing `marketing_reader` (per the spec) when the controller actually demands `marketing.article.write` would leave the doc wrong in a different direction and a developer onboarding a user would still grant the wrong role. | **HIGH** | Make Decision 1 explicit: the correct role is `marketing.article.write`. |
| Section 7 (line 143) is left in its current state and continues to claim reads only need `heblo_user`. | MEDIUM | Extend FR-4 sweep to include line 143 explicitly. |
| Other module feature docs (`leaflet-generator.md`, `photobank` references) carry the same stale role strings. | LOW | Out of scope per spec; the brief notes follow-up arch-review findings should be filed if drift is seen — `docs/features/leaflet-generator.md` already matched in the grep and should get a separate finding. |
| The `FeatureAuthorize` design itself is in flux (see `docs/superpowers/plans/2026-06-10-permission-based-gates*.md`). | LOW | Pin the doc to the *current* attribute and matrix-generated string. Future renames will be picked up by future doc updates; that is consistent with how every other feature doc in the repo treats this. |

## Specification Amendments

The spec must be updated before implementation begins. The current text would actively introduce inaccuracies.

1. **Background section — replace** the description of the controller's current attribute. The accurate statement is: *"Implementation (`ArticlesController.cs:15` class-level, `:28` method-level) uses `[FeatureAuthorize(Feature.Marketing_Article)]` and `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]` respectively. `AccessRoles.For(Feature.Marketing_Article, AccessLevel.Write)` resolves to `marketing.article.write` (see `AccessRoles.generated.cs:46, 105`). The legacy `AuthorizationConstants` class was removed by the permission-source-of-truth migration."*

2. **FR-1 — replace** the target role with `marketing.article.write` (for `POST /generate`). Also document `marketing.article.read` for the GET endpoints. Drop any text describing `marketing_reader` as the correct role.

3. **FR-2 — replace** the directed snippet. The doc must contain:
   ```csharp
   // backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:15
   [FeatureAuthorize(Feature.Marketing_Article)]
   public sealed class ArticlesController : BaseApiController
   {
       [HttpPost("generate")]
       [FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
       public async Task<ActionResult<GenerateArticleResponse>> Generate(...)
   }
   ```
   Drop the `[Authorize(Policy = AuthorizationConstants.Policies.MarketingReader)]` directive entirely — that snippet does not match the controller.

4. **FR-3 — replace** the historical note text with: *"The earlier `article_generator` role was removed. The interim `marketing_reader` / `marketing_writer` role strings were retired with the 2026-06-08 permission-source-of-truth migration. Access is now declared in `access-matrix.json` and exposed via the generated `AccessRoles.MarketingArticleRead` / `AccessRoles.MarketingArticleWrite` constants (permission strings `marketing.article.read` / `marketing.article.write`)."*

5. **FR-4 — extend** the document-wide sweep to **also fix section 7, line 143**: replace `"POST /generate requires role marketing_writer; reads require heblo_user"` with `"POST /generate requires marketing.article.write; reads require marketing.article.read (inherited from the class-level [FeatureAuthorize])."`

6. **NFR-1 — replace** the cited line ranges. The byte-accurate references are now:
   - `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:15` (class-level attribute)
   - `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:28` (method-level attribute)
   - `backend/src/Anela.Heblo.Domain/Features/Authorization/AccessRoles.generated.cs:46, 105` (constant + mapping)
   - `access-matrix.json:26` (feature declaration)
   - References to `AuthorizationConstants.cs` and `AuthenticationExtensions.cs:116-118` must be removed — the constants file is gone and `AuthenticationExtensions.cs` no longer maps individual roles (it only defines the default policy in lines 108-114 and registers `PermissionClaimsTransformation` in line 116).

7. **NFR-2 — replace** the PR traceability links with the current paths above and add a link to the permission-source-of-truth design doc (`docs/superpowers/specs/2026-06-08-permission-source-of-truth-design.md`) so reviewers can see why the brief's references are obsolete.

8. **Out of Scope — add an explicit exclusion**: *"Reviving or re-documenting `AuthorizationConstants` symbols. That class was intentionally removed in the 2026-06-08 migration."*

## Prerequisites
- None for the documentation change itself.
- Implementer must read `ArticlesController.cs:1-50`, `FeatureAuthorizeAttribute.cs`, `AccessRoles.generated.cs:45-46,104-105`, and `access-matrix.json:26` before drafting the replacement text — the spec's quotations of these files are out of date and cannot be trusted as a substitute.
- After edit, run `grep -nE 'marketing_(writer|reader)|article_generator|AuthorizationConstants' docs/features/article-generation.md` and confirm zero matches before marking the task done.