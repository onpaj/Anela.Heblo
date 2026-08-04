# Architecture Assessment: GenerateArticleRequest.Scope/Length validation

## Verdict

Approved as scoped. The plan and design (`plan-01.md`, `design-01.md`) correctly identify the DataAnnotations `[AllowedValues]` fix as the right-sized solution, and every factual claim they rest on has been verified directly against the current code. No architectural changes are needed to what's proposed; this document adds the one missing piece — a concrete, codebase-consistent test strategy — and confirms the remaining assumptions.

## Verification against actual code

All checked and confirmed accurate:

- `GenerateArticleRequest.cs:9-14` — `Topic` is `[Required, MinLength(3), MaxLength(500)]`; `Scope` and `Length` have no attributes. Matches the finding exactly.
- `ArticlesController.cs:27-35` — `Generate` is a plain `[FromBody]` action on a class-level `[ApiController]` controller. No custom model-validation handling exists on this action, so ASP.NET Core's built-in automatic 400 short-circuit applies unmodified — confirms the design's core claim that no controller code changes are needed.
- `Article.cs:5-6` — `DefaultScope = "overview"`, `DefaultLength = "medium (1000w)"`, both members of the proposed allow-lists. Confirms the "omitting the field still validates" assumption is correct in principle (still worth a test, as the plan already says).
- `Anela.Heblo.Application.csproj` — `TargetFramework net8.0`. `AllowedValuesAttribute` is available, no package changes needed.
- `ArticleConfiguration.cs` — `Scope`/`Length` columns are `HasMaxLength(50)`. All seven allowed values fit comfortably; no persistence-layer conflict.
- `grep -r AllowedValues backend/` — zero existing usages anywhere in the codebase. This will be the **first** use of this attribute. Flagging so the implementer doesn't go looking for a precedent to copy; `Topic`'s `[Required, MinLength, MaxLength]` on the same class is the closest and sufficient precedent for "DataAnnotations is idiomatic here."
- `ValidationBehavior`/`ValidationResultBehavior` (`Common/Behaviors/`) — confirmed FluentValidation-only, no `IValidator<GenerateArticleRequest>` registered anywhere. The plan's claim that DataAnnotations (not a new FluentValidation validator) is the correct mechanism for this specific request type is correct — MediatR's pipeline never sees this request before/independent of ASP.NET Core model binding rejects it.
- `BaseApiController.HandleResponse` — only invoked for `BaseResponse`-shaped handler results; never touches `[ApiController]` automatic model-validation failures. Confirms the design's noted asymmetry (raw `ValidationProblemDetails` vs. the app's own error envelope) is pre-existing and orthogonal to this fix — correctly called out as "not introduced, not fixed" rather than something to paper over.

## One gap closed: test strategy

The plan's rough-plan step 4 was tentative about *how* to test the 400 path ("Add/extend tests in `GenerateArticleHandlerTests.cs` or a new controller/integration-level test"). I checked `GenerateArticleHandlerTests.cs` directly: it constructs `GenerateArticleHandler` and calls `.Handle(request, default)` directly, bypassing ASP.NET Core model binding entirely. **DataAnnotations validation never runs in that test class** — `[AllowedValues]` is enforced by the framework at model-binding time, not by the handler, so a handler-level unit test physically cannot observe a 400 or a rejected request. Extending `GenerateArticleHandlerTests.cs` would not test the fix.

The codebase already has the right tool for this: `HebloWebApplicationFactory` (`test/Anela.Heblo.Tests/Common/HebloWebApplicationFactory.cs`), used by `PurchaseOrdersControllerTests.cs` and others for exactly this shape of test — real `HttpClient` → real routing → real model binding → real status code, via `IClassFixture<...TestFactory>`.

**Directive for implementation:** add a new `ArticlesControllerTests.cs` under `backend/test/Anela.Heblo.Tests/Controllers/`, following the `PurchaseOrdersControllerTests` pattern (own `IClassFixture<ArticlesTestFactory>` or reuse an existing shared factory if one already covers the `Marketing_Article` feature — check `WebAppTestCollection.cs` first to avoid duplicate `WebApplicationFactory` instances, which are expensive). Cases:
1. POST with a valid `scope`/`length` combination → 200, matches current handler-level test expectations.
2. POST with `scope` outside the allow-list → 400, response body is `ValidationProblemDetails` with an `errors.Scope` entry, and (assert via repository/DB or a follow-up GET) no `Article` row was created.
3. Same for `length`.
4. POST omitting `scope`/`length` entirely → 200, defaults applied (`overview` / `medium (1000w)`) — this directly resolves the plan's first open question with an executable check instead of an assumption.

This keeps `GenerateArticleHandlerTests.cs` unchanged (it correctly continues to test handler logic with pre-validated inputs) and adds the missing boundary-level coverage in the layer that actually owns the behavior.

## Design decisions endorsed as-is

- **DataAnnotations over FluentValidation or enum conversion.** Correct call. FluentValidation isn't wired for this request type and adding it would be new pipeline machinery for a problem `[AllowedValues]` solves in two lines, consistent with `Topic`. Enum conversion is a legitimate future improvement (would surface in the OpenAPI/TS client as a real union type) but is materially larger — domain entity change, EF column semantics, generated client regen — and isn't required to close the spec-compliance gap. Correctly deferred.
- **No handler/pipeline changes.** Correct. Once the boundary is enforced, `GenerateArticleHandler` and `WriteArticleStep` continue to trust `request.Scope`/`request.Length` — that trust is now backed instead of unbacked. No reason to touch either.
- **No domain/DB/migration changes.** Correct — enforcement one layer above the entity is sufficient and avoids any migration risk on a solo-maintained, manually-migrated database.
- **Frontend untouched.** Confirmed — the frontend already source-of-truth constrains via `<select>` with the identical value sets; this change only makes the server agree with what the client already enforces.

## Risks and mitigations

- **Risk:** any out-of-band caller (script, Postman collection, future integration) currently sending a scope/length outside the allow-list will start getting 400s where it previously got 200. **Mitigation:** none needed structurally — this is the intended effect of the fix (closing an under-validated boundary). Worth a one-line release note since there's no PR-blocking CI to catch a break in an external caller the E2E suite doesn't cover.
- **Risk (low):** pre-existing `Articles` rows with out-of-vocabulary `Scope`/`Length` (if any) remain un-flagged on read, since `GET` endpoints don't validate. Plan already scopes this out correctly — it's a read-path concern, not something the write-path fix should silently take on. Recommend the one-off DB check mentioned in the plan's open questions actually happen during implementation (`SELECT DISTINCT "Scope", "Length" FROM "Articles" WHERE "Scope" NOT IN (...) OR "Length" NOT IN (...)`), purely to close the open question with a fact, not to trigger any remediation work.
- **Risk (informational only):** Swashbuckle may not render `[AllowedValues]` as an OpenAPI `enum`, leaving the generated TS client typed as `string`. This does not weaken the fix (enforcement is server-side regardless of what the client type says) and the plan already treats it as non-blocking. No action required beyond noting the actual outcome in the PR description.

## Prerequisites before implementation

None. No new dependencies, no schema/migration work, no design decisions left open that block writing code. The only addition to the plan is the concrete test-file location and pattern above (`ArticlesControllerTests.cs` via `HebloWebApplicationFactory`), which should be treated as part of the implementation plan's step 4, not a separate task.
