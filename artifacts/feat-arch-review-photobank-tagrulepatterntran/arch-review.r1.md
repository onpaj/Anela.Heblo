I have enough information to ground the architecture review. The translator has zero non-test references; the doc section is in Czech and incorrectly describes glob syntax. Writing the review now.

```markdown
# Architecture Review: Remove Dead `TagRulePatternTranslator` and Sync Photobank Tag Rules Documentation

## Skip Design: true

No UI components, screens, layouts, or visual design decisions are involved. The change is a dead-code deletion in the Domain layer plus an in-place edit to the Czech "Záložka Tag Rules" section of `docs/features/photobank.md`. The frontend tag-rules editor (`frontend/src/components/marketing/photobank/settings/TagRulesTab.tsx`) is explicitly out of scope.

## Architectural Fit Assessment

The change strengthens, rather than challenges, the existing Clean Architecture layout. The Photobank vertical slice currently has a Domain-layer utility (`TagRulePatternTranslator`) that violates the project's "no dead code in Domain" expectation (`CLAUDE.md` § Coding behavior: "Don't add features, refactor, or introduce abstractions beyond what the task requires"). Verified state:

- **Production callers of `TagRulePatternTranslator.Translate`**: zero. The only references in the repo are the class itself, its test file, and the artifacts/brief markdown files generated for this very review.
- **Production write paths** (`AddRuleHandler` line 21, `UpdateRuleHandler` line 24) store `request.PathPattern.Trim()` directly as a raw .NET regex.
- **Production read path** (`TagRuleMatcher.Matches`, line 36–38) compiles `r.PathPattern` directly with `RegexOptions.Compiled | IgnoreCase | CultureInvariant` and caches it in a `ConcurrentDictionary<string, Regex>`.
- **Validation** (`AddRuleRequestValidator`, `UpdateRuleRequestValidator`) uses `PhotobankValidationHelpers.BeValidRegex`, confirming the contract is "raw .NET regex".

Integration points: none new. This is removal-only in the Domain layer plus a documentation correction. The vertical-slice boundary (`Domain/Features/Photobank` ↔ `Application/Features/Photobank/UseCases/AddRule|UpdateRule`) is preserved.

The doc says `/PROFI_FOCENI/Produkty/*` (glob), which is misleading: that exact string is *not* a valid regex against virtual paths constructed by `TagRuleMatcher` as `folderPath + "/" + fileName` (no leading slash). The doc must align with what the matcher actually consumes.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ Application/Features/Photobank/UseCases                                │
│                                                                         │
│   AddRuleHandler ───────► TagRule.PathPattern = request.Trim()  (raw)  │
│   UpdateRuleHandler ────► TagRule.PathPattern = request.Trim()  (raw)  │
│                                  │                                      │
│   AddRuleRequestValidator ──► BeValidRegex(PathPattern)                │
│   UpdateRuleRequestValidator ► BeValidRegex(PathPattern)               │
└────────────────────────────────────────┬───────────────────────────────┘
                                         │
                                         ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Domain/Features/Photobank                                              │
│                                                                         │
│   TagRule (entity)         { PathPattern : string (raw regex) }        │
│   TagRuleMatcher.GetMatchingTags(folderPath, fileName, rules)          │
│                  └─► new Regex(PathPattern, IgnoreCase|Compiled|...)   │
│                                                                         │
│   TagRulePatternTranslator   ◄── DELETE                                │
└────────────────────────────────────────────────────────────────────────┘
```

After this change, the Photobank Domain layer contains no glob-aware code; "regex-only" becomes the single, enforced contract.

### Key Design Decisions

#### Decision 1: Delete the translator instead of wiring it in
**Options considered:**
- (A) Delete `TagRulePatternTranslator` + tests; update docs to describe raw regex.
- (B) Wire `TagRulePatternTranslator.Translate(...)` into both handlers and change validators to validate the *post-translation* result.
- (C) Leave as-is and add a code comment marking it dead.

**Chosen approach:** (A), gated by a one-time DB check (see Prerequisites).

**Rationale:** The translator's idempotence test (`Translate_IdempotentCheck_SkipsAlreadyMigratedPatterns`) confirms it was a one-shot migration utility. Existing validators reject input the matcher can't compile, so the production contract is already "raw regex". Option (B) would silently change semantics for existing customers (e.g. a user-entered `[a-z]+` would be `Regex.Escape`-d if it lacks a leading `^`), and is also explicitly out-of-scope per the brief. Option (C) leaves a long-lived footgun in Domain.

#### Decision 2: Keep raw-regex as the input contract; do not introduce glob UX
**Options considered:** Add a separate UI-friendly DSL layer; keep raw regex; switch to glob entirely.

**Chosen approach:** Keep raw regex. Spec explicitly lists glob-syntax reintroduction as out of scope.

**Rationale:** Tag rules are configured by power-users (admins). The validator already gives immediate feedback on invalid patterns. A DSL would add Application-layer complexity not justified by current users.

#### Decision 3: Documentation lives in Czech, alongside the rest of `docs/features/photobank.md`
**Options considered:** Rewrite the section in English; keep Czech.

**Chosen approach:** Keep Czech to match the surrounding sections (`### Záložka Tag Rules`, `## Synchronizace a štítky`, …). Only the pattern-syntax explanation is updated.

**Rationale:** Tone/locale consistency. The doc's audience is the Anela team.

#### Decision 4: Verification gate before deletion runs against staging DB, not production
**Options considered:** Production DB query; staging DB query; rely on EF migration history only.

**Chosen approach:** Run the staging-DB inspection check, with production as fallback if staging is empty.

**Rationale:** Per `CLAUDE.md`, staging is `kv-heblo-stg`; the team's standard pre-prod check surface. Production access from the implementer's environment is not required for a low-risk dead-code removal once staging confirms regex shape.

## Implementation Guidance

### Directory / Module Structure

Files to **delete**:
- `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRulePatternTranslator.cs`
- `backend/test/Anela.Heblo.Tests/Features/Photobank/TagRulePatternTranslatorTests.cs` (path now confirmed by repo search — resolves Open Question #2)

Files to **edit** (documentation only):
- `docs/features/photobank.md` — replace lines 80–86 (`### Záložka Tag Rules` block) so the "Vzor cesty" bullet describes a .NET regex compiled against `{folderPath}/{fileName}` (no leading `/`), include one worked example, and mention `BeValidRegex` rejection at Add/Update time.

Files **not to touch** (regression risk):
- `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRuleMatcher.cs`
- `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRule.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/AddRule/AddRuleHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/UseCases/UpdateRule/UpdateRuleHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Validators/AddRuleRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Features/Photobank/Validators/UpdateRuleRequestValidator.cs`
- `frontend/src/components/marketing/photobank/settings/TagRulesTab.tsx`

The Anela.Heblo.Tests project is a single test assembly per the repo layout, so no `csproj` reference adjustments are required after deleting the test file. Verify by inspecting `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` for any explicit `<Compile Include=...>` entry naming the deleted file (the project uses standard SDK-style globbing, so no edit should be needed — but check before committing).

### Interfaces and Contracts

No interface changes. The persistent contract is unchanged:

- `TagRule.PathPattern : string` — interpreted as a .NET-compatible regular expression, compiled with `RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled`.
- Match target: virtual path = `folderPath + "/" + fileName` if a file name exists, otherwise just `folderPath`. Note: **no leading `/`**. Any rule documentation example must reflect this.
- Validation rejects patterns the .NET regex engine cannot compile (via `PhotobankValidationHelpers.BeValidRegex`) and patterns longer than 500 chars.

Example to include verbatim in the updated docs (suggested):
> `^PROFI_FOCENI/Produkty/[^/]+(/|$)` matches any item directly under `PROFI_FOCENI/Produkty/`. `^` anchors to the start of the virtual path, `[^/]+` matches exactly one path segment (no slashes), and `(/|$)` ensures the segment ends at a boundary.

### Data Flow

For the **AddRule** flow:
1. HTTP `POST /api/photobank/tag-rules` → `PhotobankController` → `AddRuleRequest` → MediatR pipeline → `AddRuleRequestValidator.BeValidRegex` → `AddRuleHandler.Handle` → `IPhotobankRepository.AddRuleAsync` → DB.
2. Stored value is `request.PathPattern.Trim()` — verbatim.

For the **Match** flow (during indexing / re-apply):
1. Index job loads active rules, ordered by `SortOrder`.
2. For each photo, `TagRuleMatcher.GetMatchingTags(folderPath, fileName, rules)` compiles each rule's `PathPattern` once (cached in `RegexCache`) and tests it against `{folderPath}/{fileName}`.
3. All matching rules contribute their `TagName.ToLowerInvariant()` (de-duplicated).

Neither flow is modified by this change.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Legacy glob-shaped rows still exist in the DB and silently stop matching after the translator is deleted | **High** | Run the data-inspection query in **Prerequisites** before merging. Halt and pivot to "Option B (wire translator into handlers)" as a separate spec if any non-regex row is found. |
| Reflection or DI registration references the static class (unlikely — class is `static`, has no interface) | Low | FR-3 mandates a final repo-wide grep for `TagRulePatternTranslator` across `*.cs`, `*.json`, `*.xml`, `Program.cs`, and any `AddPhotobank`/`AddDomain` registration extensions. Already verified clean during this review. |
| `Anela.Heblo.Tests.csproj` has an explicit `<Compile Include>` for the deleted file (would break the build) | Low | The build uses default SDK-style globbing; spot-check the csproj before committing. |
| Docs update introduces an example regex that doesn't actually match the matcher's virtual-path format (e.g. an example with a leading `/`) | Medium | Include the example anchored without a leading `/` and cross-verify mentally against `TagRuleMatcher` line 21–23. Optional: add the example as an inline-data row to `TagRuleMatcherTests` to lock the contract — but this is **not required** by the spec and adds scope. |
| A future regression silently reintroduces glob handling | Low | The deletion itself is the mitigation: there is no glob-aware code left in Domain to "accidentally call". |

## Specification Amendments

1. **Resolve Open Question #2 in the spec.** Confirmed test path: `backend/test/Anela.Heblo.Tests/Features/Photobank/TagRulePatternTranslatorTests.cs`. Update the spec so FR-2 names this path directly instead of "to be confirmed at implementation time".

2. **Tighten FR-3's search scope.** The spec says "DI registration, reflection-based lookups, configuration files, and serialization metadata". Make the search concrete: `*.cs`, `*.csproj`, `*.json`, `*.xml`, `*.yml`, plus `Program.cs` and any `Add*Services` extension methods. (No matches were found during this review, so the gate is expected to pass.)

3. **Strengthen FR-4 to specify the documentation language and exact section.** The current `docs/features/photobank.md` is Czech; the section to edit is `### Záložka Tag Rules` (lines 80–86 at the time of writing). The amended bullet should state the virtual-path format (no leading slash) and include the worked example given above. Validation behaviour (`BeValidRegex` rejecting un-compilable patterns at Add/Update) must be mentioned in the same section.

4. **Add an explicit verification step** to FR-1/FR-2: after deletion, run `dotnet build` and `dotnet test --filter FullyQualifiedName~Photobank` from `backend/`. The Photobank-scoped filter is faster than the full suite and catches the only realistic regression surface.

5. **Add a guard** to NFR-3: if the prerequisite DB inspection finds legacy glob rows, this spec must not be executed; the work moves to a new spec under the "Option B" path. Currently this is buried in Open Question #1; promote it to a hard gate in NFR-3.

## Prerequisites

Before implementation begins, the following **must** be completed:

1. **DB shape inspection (gate)** — Run against staging (`kv-heblo-stg` connection string → `ConnectionStrings--Staging`), and, if available, production:
   ```sql
   SELECT "Id", "PathPattern"
   FROM "PhotobankTagRules"
   WHERE "PathPattern" NOT LIKE '^%'           -- any unanchored pattern
      OR "PathPattern" LIKE '%/\*%' ESCAPE '\' -- legacy glob segment
      OR "PathPattern" LIKE '\/%' ESCAPE '\';  -- leading slash (legacy)
   ```
   *(Exact table name to be confirmed against `TagRuleConfiguration.cs` — likely `PhotobankTagRules` or `TagRules`; the implementer should confirm via `dotnet ef dbcontext info` or by inspecting `TagRuleConfiguration`.)*
   
   **Pass criterion:** zero rows. If any rows are returned, **halt this spec** and escalate as described in NFR-3 amendment above.

2. **Repo-wide reference verification** — Already performed during this review: only the deletion targets (`TagRulePatternTranslator.cs`, `TagRulePatternTranslatorTests.cs`) and the artifacts/spec/brief markdown contain the symbol. No DI, Program.cs, or configuration reference exists. Re-run before deletion as the spec's FR-3 requires.

3. **No prerequisite migration, no infrastructure change, no config change** is needed. This is the cheapest possible deletion path provided gate (1) passes.

4. **Branch hygiene** — Work on the existing feature branch `feat-arch-review-photobank-tagrulepatterntran`. No new branch required.
```