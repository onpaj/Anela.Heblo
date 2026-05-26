# Architecture Review: Article Writing Prompt Configuration Fix

## Skip Design: true

## Architectural Fit Assessment

The change aligns cleanly with patterns already established in this codebase. `ArticleOptions` is a strongly-typed POCO bound via the standard `IOptions<>` pattern (`AddOptions<ArticleOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`), and three of the four pipeline steps (`PlanQueriesStep`, `AggregateFactsStep`, `ValidateFactsStep`) already expose their system prompts as configurable string properties named `<Step>SystemPrompt` and pass them as `ChatRole.System`. `WriteArticleStep` is the lone outlier — this refactor brings it into conformance.

Integration points are tightly scoped:
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs` (property surface)
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` (chat invocation + `BuildSystemPrompt` style-guide wrapper)
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs` (new test for FR-3)
- `docs/features/article-generation.md:307` (key rename in docs)

No DI changes, no migration, no API/DTO/OpenAPI surface change, no frontend impact, no new dependencies. This is a pure refactor.

## Proposed Architecture

### Component Overview

```
appsettings.json ("Articles" section)
        │  (binds via Options Pattern, ValidateDataAnnotations)
        ▼
ArticleOptions (POCO)
  ├── WriteArticleSystemPrompt       ← NEW, default = current SystemInstruction
  └── WriteArticleUserPromptTemplate ← RENAMED from WriteArticleSystemPromptTemplate
        │
        ▼ (injected via IOptions<ArticleOptions>)
WriteArticleStep
  ├── BuildSystemPrompt(styleGuide)   ← still wraps with "STYLE GUIDE — follow this exactly:\n..."
  │      └── reads _options.WriteArticleSystemPrompt
  ├── BuildUserMessage(context)
  │      └── reads _options.WriteArticleUserPromptTemplate
  └── IChatClient.GetResponseAsync([System, User], …)
```

### Key Design Decisions

#### Decision 1: Style-guide composition stays in code, not in the configured prompt
**Options considered:**
- (A) Move the `"STYLE GUIDE — follow this exactly:\n{styleGuide}\n\n"` wrapper into the configured default value, parametrised with a `{style_guide}` placeholder.
- (B) Keep `BuildSystemPrompt(styleGuideText)` in code; have it concatenate the wrapper around `_options.WriteArticleSystemPrompt` only when `styleGuideText != null`.

**Chosen approach:** (B). The configured `WriteArticleSystemPrompt` default equals the existing `SystemInstruction` constant **verbatim** (the editor persona + JSON output contract). The conditional style-guide prefix remains in `BuildSystemPrompt`.

**Rationale:** FR-1 explicitly says "default MUST equal the current `SystemInstruction` constant character-for-character." The style-guide prefix is a runtime composition — its presence depends on `context.StyleGuideText != null`. Encoding that in the static configured string would require either a placeholder + always-on string.Replace (changing behaviour when style guide is null because the prefix would leak) or a separate config key. Keeping the wrapper in code preserves FR-5 (byte-identical defaults) with zero behavioural risk.

#### Decision 2: No `[Required, MinLength(1)]` data annotations on the new properties
**Options considered:**
- (A) Add `[Required, MinLength(1)]` to both `WriteArticleSystemPrompt` and `WriteArticleUserPromptTemplate`, matching the model-name properties.
- (B) Leave them unannotated, matching the sibling `*SystemPrompt` properties (`QueryPlannerSystemPrompt`, `AggregateFactsSystemPrompt`, `ValidateFactsSystemPrompt`).

**Chosen approach:** (B). No validation attributes.

**Rationale:** Consistency with the three siblings is the stated goal of NFR-4. Adding attributes only to the write step would re-introduce the inconsistency this refactor is removing. Defaults are non-null and non-empty; `ValidateOnStart` already guarantees the section binds successfully.

#### Decision 3: Section name is `Articles` (plural), not `Article` — spec needs correction
**Options considered:** N/A — pure factual correction.

**Chosen approach:** The configuration section is `ArticleOptions.SectionName = "Articles"`. All examples and runbook items in the spec referring to `Article:WriteArticleSystemPromptTemplate` must be updated to `Articles:WriteArticleSystemPromptTemplate` / `Articles:WriteArticleUserPromptTemplate`. See Specification Amendments below.

#### Decision 4: Do not add the new keys to any `appsettings*.json`
**Options considered:**
- (A) Populate `appsettings.json` with the verbatim default values to make the configurable surface discoverable.
- (B) Leave `appsettings.json` clean; rely on the POCO default.

**Chosen approach:** (B). The base `appsettings.json` `Articles` section currently contains only the model/token/topK knobs — it does **not** contain any prompt strings today. Match that pattern.

**Rationale:** Duplicating long Czech prompt strings between code and JSON creates a drift hazard and offers no value when the default is authoritative. Operators who need to override learn the key from the POCO or docs.

## Implementation Guidance

### Directory / Module Structure
No new files. Touch only:
1. `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs`
2. `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`
3. `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs` (add one test for FR-3)
4. `docs/features/article-generation.md` (rename key at line 307; add `WriteArticleSystemPrompt` if it improves discoverability — optional)

### Interfaces and Contracts

`ArticleOptions` final shape for the affected properties:

```csharp
public string WriteArticleSystemPrompt { get; set; } =
    """
    Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
    Odpověz POUZE validním JSON bez markdown nebo code fences.
    V poli article_html použij výhradně HTML tagy – nikdy nepište doslovný text "\n" jako obsah.
    {"article_title":"...","article_html":"<article>...</article>","sources_used":[{"title":"...","url":"..."}]}
    """;

public string WriteArticleUserPromptTemplate { get; set; } =
    """
    Napiš článek na téma {topic} pro publikum {audience}.
    Délka: {length}. Úhel pohledu: {angle}.
    Využij tato fakta: {facts}
    {style_guide}
    """;
```

`WriteArticleStep.cs` mechanical edits:
- Delete the `private const string SystemInstruction = ...` block (lines 86–92).
- In `BuildSystemPrompt`, replace both references to `SystemInstruction` with `_options.WriteArticleSystemPrompt`. The `"STYLE GUIDE — follow this exactly:\n..."` wrapper stays.
- In `BuildUserMessage`, rename `_options.WriteArticleSystemPromptTemplate` → `_options.WriteArticleUserPromptTemplate`.
- No changes to `ChatRole.System` / `ChatRole.User` wiring at lines 56–58 — it already does the right thing.

### Data Flow

For the two key cases:

**No style guide (`context.StyleGuideText == null`):**
```
_options.WriteArticleSystemPrompt ────────────────────────► ChatRole.System
_options.WriteArticleUserPromptTemplate
  → string.Replace({topic}, {audience}, {length},
                   {angle}, {facts}, {style_guide}="") ───► ChatRole.User
```

**With style guide:**
```
"STYLE GUIDE — follow this exactly:\n{styleGuide}\n\n"
  + _options.WriteArticleSystemPrompt ────────────────────► ChatRole.System
_options.WriteArticleUserPromptTemplate
  → string.Replace(..., {style_guide}=styleGuideText) ───► ChatRole.User
```

(This double-injection of the style guide into both system and user messages is pre-existing behavior and is preserved as-is per FR-5.)

### New unit test (FR-3 acceptance)
Add to `WriteArticleStepTests.cs` — use `Moq.Verify` on `IChatClient.GetResponseAsync` to capture the `IEnumerable<ChatMessage>` and assert the `System`-role message equals `_options.WriteArticleSystemPrompt` after overriding it via a fresh `ArticleOptions` instance. Use the existing `_chat` mock + AAA structure mirroring the file's conventions (xUnit + FluentAssertions + Moq).

Suggested name: `ExecuteAsync_OverriddenSystemPrompt_PassesOverriddenStringAsSystemMessage`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Azure App Service Configuration` (not in source control) has an override under the old key `Articles:WriteArticleSystemPromptTemplate`. After deploy, that override silently becomes dead config; the runtime falls back to the default user template — no error, no log. | HIGH | Pre-deploy: SSH/Portal-verify Azure App Service settings for Dev/Staging/Production. Rename or remove the old key in lockstep with the deploy. Add an explicit step to the release notes / PR description (NFR-3 already mandates this). |
| Style-guide composition (`BuildSystemPrompt`) accidentally lost during the refactor, causing a silent behaviour change for runs with a style guide. | MEDIUM | Add a unit test (or extend an existing one) that supplies `context.StyleGuideText = "X"` and asserts the `System` message **starts with** `"STYLE GUIDE — follow this exactly:\nX"` and **ends with** the configured prompt. Cheap, prevents regression. |
| Future developer notices the now-renamed property in `appsettings.json` keys differs from what's bound by `Options` and silently re-introduces the old name. | LOW | One-line note in `docs/features/article-generation.md` (next to the JSON sample) explaining the system/user role mapping and naming convention. |
| Historical plan file `docs/superpowers/plans/2026-05-08-article-generation-metadata.md:1421` references the old name; future search confuses readers. | LOW | Leave it — historical plans are immutable records. Spec's FR-4 acceptance ("grepping the repo for `WriteArticleSystemPromptTemplate` returns zero hits") must be relaxed to exclude `docs/superpowers/plans/`. See Specification Amendments. |

## Specification Amendments

1. **FR-4 grep scope** — Change "Grepping the repo for `WriteArticleSystemPromptTemplate` returns zero hits" to "Grepping `backend/`, `frontend/`, and `docs/features/` returns zero hits. Historical plans under `docs/superpowers/plans/` are excluded." Rationale: those plans are point-in-time records; rewriting them rewrites history.

2. **Section name** — Replace every occurrence of `Article:WriteArticleSystemPromptTemplate` and `Article:WriteArticleUserPromptTemplate` in the spec (Background, NFR-3, NFR-3 acceptance) with the correct `Articles:` prefix. The bound section name is `Articles` (`ArticleOptions.SectionName = "Articles"`).

3. **FR-3 acceptance — preserve style-guide wrapper** — Add: "The `BuildSystemPrompt(string? styleGuideText)` style-guide prefix composition is preserved. When `styleGuideText != null`, the `ChatRole.System` message MUST equal `\"STYLE GUIDE — follow this exactly:\\n{styleGuideText}\\n\\n\" + _options.WriteArticleSystemPrompt`. When null, it MUST equal `_options.WriteArticleSystemPrompt` exactly." This is implicit in FR-5 but worth making explicit since the constant is being deleted.

4. **FR-4 reality check** — No `appsettings*.json` in this repo currently overrides `WriteArticleSystemPromptTemplate`. The runbook step in NFR-3 only applies to environments where the override exists outside source control (Azure App Service config). Strike "appsettings*.json files (Development, Production, Staging, default) that currently contain..." since none do — replace with "Any environment-specific config source (file or Azure App Service Configuration) that currently sets `Articles:WriteArticleSystemPromptTemplate` must be migrated."

5. **NFR-4 user-prompt column** — The table column "User prompt option" lists "(existing user side, unchanged)" for the three sibling steps, implying they have configurable user templates. They do not — those steps pass dynamic input (topic, snippets, facts) directly as the user message. Only `WriteArticleStep` uses a configurable user template. Reword the column header to "User prompt source" with values "topic string (literal)", "snippet text (literal)", "facts JSON (literal)", and "`WriteArticleUserPromptTemplate` (configurable)" respectively. Clarifies — and prevents — future sibling-step "consistency" refactors based on a misreading.

## Prerequisites

None. No migrations, no infrastructure, no new packages. Implementation can start immediately.

The only **deployment-time** prerequisite (not implementation-time) is auditing Azure App Service Configuration in Dev/Staging/Production for an override of the old key — this is captured in the runbook step required by NFR-3.