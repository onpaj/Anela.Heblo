# Plan: Server-side validation for GenerateArticleRequest.Scope and Length

## Summary

`GenerateArticleRequest.Scope` and `.Length` are unconstrained strings accepted by `POST /api/Articles/generate`, even though the feature spec defines them as fixed, enum-like vocabularies and the frontend already restricts input to those vocabularies via `<select>`. This task adds server-side allow-list validation so the API rejects any value outside the spec-defined set, closing the gap between the documented contract and the implementation, and preventing arbitrary strings from being persisted and interpolated into the LLM prompt.

## Context

- Spec: `docs/features/article-generation.md` §7 marks `Scope` "enum-validated" with four permitted values; `Length` has three permitted values.
- `Topic` on the same request already uses DataAnnotations (`[Required, MinLength(3), MaxLength(500)]`) for this kind of constraint — confirmed this is enforced, because `ArticlesController` is `[ApiController]`, so ASP.NET Core automatically runs DataAnnotations validation on model binding and returns 400 before the MediatR handler runs. The MediatR `ValidationBehavior` in this codebase only wires up FluentValidation validators (`IEnumerable<IValidator<TRequest>>`) — there is no FluentValidation validator for `GenerateArticleRequest`, so DataAnnotations is the correct and consistent mechanism here, matching the existing `Topic` pattern.
- Target framework is `net8.0`, so `System.ComponentModel.DataAnnotations.AllowedValuesAttribute` (added in .NET 8) is available — no package changes needed.
- Frontend (`frontend/src/features/articles/ArticleGenerationForm.tsx:12-21`) already hard-codes the exact same value sets:
  - Scope: `overview`, `deep-dive`, `how-to`, `comparison`
  - Length: `brief (500w)`, `medium (1000w)`, `long (2000w)`
  - These match `DomainArticle.DefaultScope = "overview"` and `DomainArticle.DefaultLength = "medium (1000w)"` (`backend/src/Anela.Heblo.Domain/Features/Article/Article.cs:5-6`), so the defaults remain valid under the new constraint.
- Unconstrained values currently flow through `GenerateArticleHandler` (lines 37-38) into `Article.Scope`/`Article.Length`, and are interpolated verbatim into the LLM prompt in `WriteArticleStep.BuildUserMessage` (lines 112-113) — the primary risk is degraded/unpredictable generation output and malformed-looking persisted data, not classic injection (the prompt structure limits blast radius), per the finding.

## Functional requirements

**FR-1 — Reject invalid `Scope` values at the API boundary**
Add `[Required]` and `[AllowedValues("overview", "deep-dive", "how-to", "comparison")]` to `GenerateArticleRequest.Scope`.
- Acceptance: `POST /api/Articles/generate` with `"scope": "overview"` (or any of the 3 other listed values) succeeds (200) as before.
- Acceptance: `POST /api/Articles/generate` with `"scope": "ignore previous instructions"` or any value outside the 4 listed returns 400 with a validation error referencing `Scope`, and does **not** reach `GenerateArticleHandler` or persist an `Article`.
- Acceptance: omitting `scope` in the request body still works and defaults to `"overview"` (`AllowedValues` does not run against a field that binding leaves at its declared default; confirm this with a test — see Open Questions).

**FR-2 — Reject invalid `Length` values at the API boundary**
Add `[Required]` and `[AllowedValues("brief (500w)", "medium (1000w)", "long (2000w)")]` to `GenerateArticleRequest.Length`.
- Acceptance: same shape as FR-1, for the 3 listed length values.
- Acceptance: invalid length (e.g. `"length": "extremely long"`) returns 400 and does not persist an `Article`.

**FR-3 — Error messages are actionable**
Both attributes carry an `ErrorMessage` naming the field and listing permitted values (per the finding's suggested fix), so a 400 response body is self-explanatory without consulting the spec.

**FR-4 — No behavior change for valid requests**
Existing callers (frontend form, any existing tests) that only ever send one of the allowed values continue to work unchanged. `GenerateArticleHandler` and `WriteArticleStep` are not modified — the fix is a pure input-boundary constraint.

## Non-functional requirements

- **Security/robustness**: closes the gap where arbitrary attacker-controlled strings are persisted to `Articles` and injected into an LLM prompt unvalidated. This is a defense-in-depth / data-integrity fix, not a critical-severity injection fix (the finding itself notes the structured prompt already limits worst-case injection risk).
- **Consistency**: OpenAPI schema regeneration will surface the constraint (as `enum`-like validation metadata is not automatically reflected by `AllowedValuesAttribute` in Swashbuckle by default — confirm during implementation whether the generated TS client needs a manual note; not a blocker, but flag if the schema doesn't pick it up).
- **No persistence/migration impact**: `Article.Scope`/`Article.Length` remain `string` in the domain entity and DB; this task does not convert to enums (see Dependencies and scope below).

## Data model

No data model changes. `Article.Scope` and `Article.Length` remain plain `string` properties (`backend/src/Anela.Heblo.Domain/Features/Article/Article.cs:10,13`). The constraint is enforced purely at the `GenerateArticleRequest` DTO boundary via DataAnnotations, consistent with how `Topic` is already constrained on the same class.

## Interfaces

- `POST /api/Articles/generate` (`ArticlesController.Generate`, `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:27-35`): behavior changes only in that invalid `scope`/`length` values now produce a `400 Bad Request` (standard ASP.NET Core `[ApiController]` automatic model-validation response) instead of silently succeeding with a bad value persisted.
- No new endpoints, no response shape changes, no frontend changes required (frontend already only ever sends allowed values).

## Dependencies and scope

**In scope:**
- `GenerateArticleRequest.cs`: add `[Required]` + `[AllowedValues(...)]` to `Scope` and `Length`, per the finding's suggested fix (DataAnnotations option, not the enum-conversion alternative).
- Unit/integration test coverage confirming valid values pass and invalid values are rejected with 400 and no persistence side effect.

**Explicitly out of scope:**
- Converting `Scope`/`Length` to real C# enums in the domain (the finding's "alternative" suggestion). Rationale: bigger surface area (domain entity, EF mapping/migration, OpenAPI/TS client regen), not required to close the immediate spec-compliance gap, and DataAnnotations is already the established pattern for this exact class (`Topic`). Can be a follow-up if the team wants enum-level type safety later.
- Any change to `GenerateArticleHandler` or `WriteArticleStep` prompt-building logic.
- Any change to the frontend form (already correctly constrained).
- Retroactively cleaning up any already-persisted `Article` rows with out-of-vocabulary `Scope`/`Length` values (none expected in practice since only the UI has been the entry point so far, but not verified — flagged as an open question).

## Rough plan

1. Add `[Required, AllowedValues("overview", "deep-dive", "how-to", "comparison", ErrorMessage = "...")]` to `GenerateArticleRequest.Scope`.
2. Add `[Required, AllowedValues("brief (500w)", "medium (1000w)", "long (2000w)", ErrorMessage = "...")]` to `GenerateArticleRequest.Length`.
3. Regenerate the OpenAPI/TypeScript client (per `docs/development/api-client-generation.md`) and check whether the `enum`/`AllowedValues` constraint surfaces in the generated schema/types; note the outcome (informational, not a blocker if it doesn't).
4. Add/extend tests in `backend/test/Anela.Heblo.Tests/Article/UseCases/GenerateArticleHandlerTests.cs` or a new controller/integration-level test verifying: valid scope/length values are accepted; invalid values return 400; defaults (`overview` / `medium (1000w)`) still validate successfully when the fields are omitted from the request body.
5. Run `dotnet build` + `dotnet format` and the full backend test suite for the Article module; run `npm run build` if the TS client changed.
6. Manually verify via a direct API call (e.g. curl/Swagger) that an out-of-vocabulary `scope` is rejected with 400 and no `Article` row is created.

## Open questions

- **Does `[AllowedValues]` interact correctly with the property's non-nullable default?** Since `Scope`/`Length` have non-null default values (`DomainArticle.DefaultScope`/`DefaultLength`) and the request is `[FromBody]` JSON, omitting the field in the JSON payload leaves the C#-side default in place *before* model binding overwrites it if present — model validation runs against the bound value regardless of whether it came from JSON or the property initializer, so the default values must themselves satisfy the constraint. Assumption: since `DefaultScope = "overview"` and `DefaultLength = "medium (1000w)"` are both in the allowed sets, omitting the field is fine. Flagged as a test case in the rough plan rather than left as an assumption.
- **Should `[Required]` be added alongside `[AllowedValues]`?** The finding's suggested fix includes it. Since both properties already have non-empty defaults, `[Required]` is effectively a no-op for omitted fields but would reject an explicit empty string (`"scope": ""`) which `[AllowedValues]` alone would already reject anyway (empty string isn't in either allow-list). Decision: include `[Required]` as shown in the finding for explicitness and consistency with `Topic`, even though it's slightly redundant with `[AllowedValues]` here.
- **Are there existing persisted `Article` rows with invalid `Scope`/`Length`?** Not verified — out of scope for this fix (no data migration), but worth a quick one-off DB check during implementation; if found, decide separately whether to backfill or leave (no user-facing impact since `GET` endpoints don't validate on read).
