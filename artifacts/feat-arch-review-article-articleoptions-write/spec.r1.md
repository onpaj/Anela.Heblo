# Specification: Article Writing Prompt Configuration Fix

## Summary
Refactor `ArticleOptions` and `WriteArticleStep` to expose the article writing system prompt as a configurable option and rename the existing misnamed `WriteArticleSystemPromptTemplate` to accurately reflect its role as a user message template. This aligns the write step with the three sibling pipeline steps (query planner, aggregate facts, validate facts) where system/user prompt naming already matches actual chat role usage.

## Background
The article generation pipeline uses MediatR-driven steps that call an AI model. Each step exposes its prompts via `ArticleOptions` so operators can tune behaviour through `appsettings.json` without redeploying.

Three of the four steps follow a consistent pattern: a property named `*SystemPrompt` is sent with `ChatRole.System`. The write step breaks this pattern:

- `ArticleOptions.WriteArticleSystemPromptTemplate` is sent as `ChatRole.User` in `WriteArticleStep.BuildUserMessage` (`WriteArticleStep.cs:102-113`).
- The real system prompt — defining the Czech cosmetics editor persona and JSON output requirement — lives as a hardcoded `SystemInstruction` constant in `WriteArticleStep.cs:86-92` and cannot be configured.

Consequences:
1. An operator editing `WriteArticleSystemPromptTemplate` in `appsettings.json` expects to influence persona/behaviour but actually changes the user-turn article brief.
2. The actual system prompt requires a code deploy to change, defeating the purpose of `ArticleOptions`.
3. Inconsistency with sibling options increases cognitive load for anyone reading or tuning the configuration surface.

## Functional Requirements

### FR-1: Expose article writing system prompt as a configurable option
Add a new property `WriteArticleSystemPrompt` to `ArticleOptions`. Its default value MUST equal the current hardcoded `SystemInstruction` constant in `WriteArticleStep` verbatim (whitespace, Czech text, and JSON instructions preserved exactly).

**Acceptance criteria:**
- `ArticleOptions.WriteArticleSystemPrompt` exists with a string default that matches the current `SystemInstruction` constant character-for-character.
- Setting `Article:WriteArticleSystemPrompt` in `appsettings.json` (or any other configuration source bound to `ArticleOptions`) overrides the default at runtime without code changes.
- The hardcoded `SystemInstruction` constant in `WriteArticleStep` is removed.

### FR-2: Rename misnamed user template property
Rename `ArticleOptions.WriteArticleSystemPromptTemplate` to `WriteArticleUserPromptTemplate`. The default value MUST remain identical. All references in the codebase MUST be updated to the new name.

**Acceptance criteria:**
- `ArticleOptions.WriteArticleSystemPromptTemplate` no longer exists.
- `ArticleOptions.WriteArticleUserPromptTemplate` exists with the same default string the old property had.
- `WriteArticleStep.BuildUserMessage` reads from `_options.WriteArticleUserPromptTemplate`.
- `dotnet build` succeeds with zero references to the old name.

### FR-3: Wire the new system prompt into the chat call
`WriteArticleStep` MUST send `_options.WriteArticleSystemPrompt` as the system role message when invoking the AI model, replacing the inlined `SystemInstruction` constant. The user role message MUST continue to be the rendered `WriteArticleUserPromptTemplate`.

**Acceptance criteria:**
- The AI chat invocation in `WriteArticleStep` builds two messages: one with `ChatRole.System` sourced from `_options.WriteArticleSystemPrompt`, one with `ChatRole.User` sourced from the rendered user template.
- No string literal containing the editor persona instruction remains in `WriteArticleStep.cs`.
- A unit test verifies that overriding `WriteArticleSystemPrompt` via `IOptions<ArticleOptions>` changes the system message passed to the chat client.

### FR-4: Update configuration files and documentation
Any `appsettings*.json` files (Development, Production, Staging, default) that currently contain `WriteArticleSystemPromptTemplate` MUST be updated to use the new key `WriteArticleUserPromptTemplate`. If no `WriteArticleSystemPrompt` key exists in any environment-specific config, none needs to be added — the default in `ArticleOptions` is authoritative.

**Acceptance criteria:**
- Grepping the repo for `WriteArticleSystemPromptTemplate` returns zero hits.
- All existing config files that referenced the old key now use `WriteArticleUserPromptTemplate` with the same value.
- Any docs under `docs/features/` or `docs/architecture/` that mention these prompt options are updated to reflect the rename and the new `WriteArticleSystemPrompt` property.

### FR-5: Preserve existing article generation behaviour
The default behaviour of the article pipeline MUST be unchanged after the refactor. With no environment overrides, the system prompt and user template sent to the AI model MUST be byte-identical to what was sent before.

**Acceptance criteria:**
- A diff of the chat messages produced for a fixed `ArticlePipelineContext` before vs. after the change shows no difference when defaults are in effect.
- Existing tests covering `WriteArticleStep` continue to pass without semantic changes to their assertions (only renames of property references where applicable).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. This is a configuration-surface refactor; chat calls, prompt sizes, and token counts are unchanged at default values.

### NFR-2: Security
No new attack surface. The prompts remain operator-controlled via trusted configuration sources. No user input enters either prompt path that did not already enter the existing user template.

### NFR-3: Backward Compatibility
This change is a **breaking configuration change**. Any environment that has overridden `Article:WriteArticleSystemPromptTemplate` in its `appsettings.{Environment}.json` (or in Azure App Service configuration) will silently fall back to the default user template after the rename. The deployment runbook MUST include a step to rename the key in every environment where it has been overridden before the new build is deployed.

**Acceptance criteria:**
- Release notes / PR description call out the rename explicitly with the exact old → new key mapping.
- A pre-deployment checklist item is added: "Verify all environment configs use `WriteArticleUserPromptTemplate` (not `WriteArticleSystemPromptTemplate`)."

### NFR-4: Consistency
The four article pipeline steps MUST follow a uniform naming convention after this change:

| Step | System prompt option | User prompt option |
|---|---|---|
| Query planner | `QueryPlannerSystemPrompt` | (existing user side, unchanged) |
| Aggregate facts | `AggregateFactsSystemPrompt` | (existing user side, unchanged) |
| Validate facts | `ValidateFactsSystemPrompt` | (existing user side, unchanged) |
| Write article | `WriteArticleSystemPrompt` *(new)* | `WriteArticleUserPromptTemplate` *(renamed)* |

## Data Model
No data model changes. `ArticleOptions` is a configuration POCO bound from `IConfiguration`; the changes affect only its property names and one new property.

## API / Interface Design

### Affected types
- `ArticleOptions` (likely under `backend/src/Anela.Heblo.Application/Features/Article/` or equivalent — confirmed during implementation)
  - Remove: `WriteArticleSystemPromptTemplate`
  - Add: `WriteArticleSystemPrompt` (with default matching current `SystemInstruction`)
  - Add: `WriteArticleUserPromptTemplate` (with default matching old `WriteArticleSystemPromptTemplate`)

- `WriteArticleStep`
  - Remove: `private const string SystemInstruction = ...`
  - Update: chat invocation now uses `_options.WriteArticleSystemPrompt` and `_options.WriteArticleUserPromptTemplate`

### Configuration shape (illustrative)
```json
{
  "Article": {
    "QueryPlannerSystemPrompt": "...",
    "AggregateFactsSystemPrompt": "...",
    "ValidateFactsSystemPrompt": "...",
    "WriteArticleSystemPrompt": "Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině. Odpověz POUZE validním JSON bez markdown nebo code fences. ...",
    "WriteArticleUserPromptTemplate": "...{topic}... {keywords} ..."
  }
}
```

No public HTTP API, no DTO, no OpenAPI client surface is affected. No frontend changes required.

## Dependencies
- .NET 8 configuration binding (`IOptions<ArticleOptions>`) — already in use.
- AI chat client abstraction used by `WriteArticleStep` — already in use; only the message construction changes.
- Existing unit-test infrastructure for the Article module.

No new NuGet packages, no new external services.

## Out of Scope
- Tuning the default content of the system prompt or user template. Defaults MUST equal current values verbatim.
- Refactoring sibling steps (`QueryPlannerStep`, `AggregateFactsStep`, `ValidateFactsStep`) — they already conform.
- Introducing a strongly-typed prompt-template abstraction or replacing `string.Replace`-based templating.
- Localization of prompts beyond what already exists (Czech is hardcoded in the prompt content; not changing that).
- Adding per-tenant or per-request prompt overrides.
- Versioning or auditing of prompt changes.

## Open Questions
None.

## Status: COMPLETE