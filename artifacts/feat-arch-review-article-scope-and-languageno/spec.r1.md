# Specification: Wire `Article.Scope` and `Article.LanguageNote` into article writing prompt

## Summary
The article generation form already captures `scope` and `languageNote` (the latter via the API/domain but not yet via the UI), validates them, and persists them — but the final `WriteArticleStep` LLM prompt never substitutes either value, so they have no effect on the produced article. This spec defines the fix: substitute both placeholders end-to-end, update the default prompt template to match the feature spec §8.5, and surface `languageNote` in the generation form so the field stops being dead infrastructure.

## Background
`Article.Scope` (one of `overview`, `deep-dive`, `how-to`, `comparison`) and `Article.LanguageNote` (free-text tone hint, e.g. "prefer short sentences, avoid jargon") are first-class concepts in the domain model (`Article.cs`), API contract (`GenerateArticleRequest.cs`), and database schema. `Scope` is correctly threaded into `AggregateFactsStep`, but `WriteArticleStep.BuildUserMessage` (`WriteArticleStep.cs:102-113`) drops both fields:

```csharp
return _options.WriteArticleSystemPromptTemplate
    .Replace("{topic}", article.Topic)
    .Replace("{audience}", article.Audience ?? "obecné publikum")
    .Replace("{length}", article.Length)
    .Replace("{angle}", article.Angle ?? "(nevyspecifikováno)")
    .Replace("{facts}", factsText)
    .Replace("{style_guide}", context.StyleGuideText ?? "");
```

Equally, the default `WriteArticleSystemPromptTemplate` in `ArticleOptions.cs:56` contains neither `{scope}` nor `{language_note}` tokens, so even an appsettings override that adds these tokens would silently leave them unreplaced.

The feature spec (`docs/features/article-generation.md` §8.5) explicitly requires both placeholders in the writing-step user prompt:

```
Audience: {audience}
Angle: {angle}
Scope: {scope}
[Tone note: {language_note}]
```

Impact today:
- A user choosing "Hloubková analýza" (`deep-dive`) gets exactly the same output as "Přehled" (`overview`).
- `LanguageNote` is a database column and API field that no user can populate (no UI input) and that the writer model never sees.

Filed by the daily arch-review routine on 2026-05-25.

## Functional Requirements

### FR-1: Substitute `{scope}` in the write-article user message
`WriteArticleStep.BuildUserMessage` MUST replace the `{scope}` placeholder with `article.Scope`.

**Acceptance criteria:**
- Unit test: given an `Article` with `Scope = "deep-dive"` and a template containing `Scope: {scope}`, the produced user message contains `Scope: deep-dive`.
- Unit test: given a template containing no `{scope}` token, substitution is a no-op (no exception, no extra text appended).
- The raw scope value (e.g. `deep-dive`, not the Czech label `Hloubková analýza`) is passed to the LLM, matching how `AggregateFactsStep` already uses it.

### FR-2: Substitute `{language_note}` in the write-article user message
`WriteArticleStep.BuildUserMessage` MUST replace `{language_note}` with `article.LanguageNote`.

**Acceptance criteria:**
- Unit test: given `LanguageNote = "krátké věty, bez žargonu"` and a template containing `{language_note}`, the produced user message contains the exact note text.
- Unit test: given `LanguageNote = null` and the default template described in FR-3, the produced user message contains no `Tone note:` line at all (not an empty `Tone note:` line).
- Unit test: given `LanguageNote = ""` (empty string after trim), behavior matches the `null` case.

### FR-3: Update default `WriteArticleSystemPromptTemplate`
The default template in `ArticleOptions.WriteArticleSystemPromptTemplate` MUST include `{scope}` and a conditional tone-note line so out-of-the-box behavior matches feature spec §8.5.

**Acceptance criteria:**
- The default template, in Czech, contains a line of the form `Rozsah: {scope}` (or equivalent unambiguous Czech rendering of "scope").
- The default template includes a tone-note line that is rendered only when `LanguageNote` is non-empty (see FR-2 acceptance for the empty case).
- An end-to-end test (or step-level integration test) using the default template with `Scope = "deep-dive"` and `LanguageNote = "krátké věty"` produces a final user message containing both values.
- Existing `dotnet build` and `dotnet format` pass; existing article generation tests still pass without modification beyond updating any snapshot/fixture of the default template.

### FR-4: Conditional rendering of the tone-note line
Because the spec format `[Tone note: {language_note}]` should be a single self-contained line that disappears when no note is provided, simple `string.Replace` is insufficient (it would leave `[Tone note: ]` artifacts).

**Acceptance criteria:**
- When `LanguageNote` is null, empty, or whitespace, no tone-note line appears in the final user message.
- When `LanguageNote` has content, the line appears exactly once with the trimmed note value substituted in.
- Implementation MUST handle this without leaving `{language_note}` literal tokens or empty brackets in the prompt.
- Suggested implementation: render the tone-note line conditionally in `BuildUserMessage` (e.g. compose the line in code and substitute a `{tone_note_line}` placeholder) rather than relying on the consumer to author conditional template syntax.

### FR-5: Add `languageNote` input to the article generation form
`ArticleGenerationForm.tsx` MUST expose a text input that binds to the existing `GenerateArticleRequest.languageNote` field so end users can populate the value that the API and pipeline already accept.

**Acceptance criteria:**
- A new input (Czech label, e.g. "Poznámka k tónu / jazyku") appears in the form, placed after "Úhel pohledu" and before the source toggles to keep the visual grouping of LLM-prompt inputs together.
- The input is optional. Empty values submit as `undefined` (matching the existing pattern for `audience`, `angle`).
- The input has a placeholder example such as "Např. krátké věty, vyhýbat se odborným termínům".
- A reasonable max length is enforced client-side (recommended: 500 characters, matching `topic`). The exact limit is mirrored by server-side validation in FR-6.
- The submitted value reaches `GenerateArticleRequest.LanguageNote` on the backend (verified via existing API client integration test or a new lightweight one).

### FR-6: Server-side validation for `LanguageNote`
The existing `GenerateArticleRequest.LanguageNote` field has no length constraint. To prevent abuse and to keep prompt size predictable, add a `MaxLength` validation attribute.

**Acceptance criteria:**
- `GenerateArticleRequest.LanguageNote` is annotated with `[MaxLength(500)]` (same limit as the frontend input).
- A request with `LanguageNote` longer than the limit is rejected with a 400 and a clear validation message.
- A request with `LanguageNote` at or below the limit is accepted.

## Non-Functional Requirements

### NFR-1: Performance
Negligible impact. The changes add at most two `string.Replace` calls and one conditional line composition per article generation request. No additional I/O, no new LLM calls, no new DB queries.

### NFR-2: Security
- `LanguageNote` is user-supplied free text that ends up in an LLM prompt. The existing pipeline already treats topic/audience/angle/scope the same way, so the threat model is unchanged. The new `MaxLength(500)` in FR-6 caps prompt-injection payload size in line with peer fields.
- No new secrets or external services are introduced.
- Logging MUST NOT add the raw `LanguageNote` to error messages beyond what the existing pipeline recorder already captures for sibling fields.

### NFR-3: Observability
- The existing `PipelineStepRecorder` payload for `WriteArticle` (currently `{ topic, factCount, styleGuideLength }`) SHOULD be extended to include `scope` and a boolean `hasLanguageNote` (not the note text, to keep payloads small) so post-hoc debugging can confirm both fields reached the writing step.

### NFR-4: Backward compatibility
- Existing articles in the database are unaffected — this is forward-only.
- Existing custom `WriteArticleSystemPromptTemplate` overrides in `appsettings*.json` that lack `{scope}` and `{language_note}` continue to work (the new substitutions are no-ops on missing tokens). Operators must add the tokens to their overrides if they want the values surfaced — this is documented in `docs/features/article-generation.md`.

## Data Model
No schema changes.

Existing relevant entities (unchanged):
- `Article.Scope: string` — required, defaulted to `"overview"`.
- `Article.LanguageNote: string?` — optional free text.

Both columns already exist (migration `20260504195511_AddArticles`).

## API / Interface Design

### Backend (no breaking changes)
- `GenerateArticleRequest.LanguageNote` already exists; only a `[MaxLength(500)]` attribute is added (FR-6).
- `WriteArticleStep.BuildUserMessage` gains two substitutions and conditional tone-note composition (FR-1, FR-2, FR-4).
- `ArticleOptions.WriteArticleSystemPromptTemplate` default is rewritten to include the new placeholders (FR-3).

Proposed default template (Czech, reflecting §8.5):

```
Napiš {length} článek v češtině.
Téma: {topic}
Publikum: {audience}
Úhel: {angle}
Rozsah: {scope}
{tone_note_line}

Fakta k využití:
{facts}

{style_guide}

Požadavky:
- Piš výhradně v češtině
- Cituj zdroje přirozeně v textu
- Vrať validní HTML pro e-mail (bez <html>/<body>)
- Uváděj jen ty zdroje, které podporují konkrétní tvrzení
```

Where `{tone_note_line}` is composed in code as either `Tonalita: <note>` (when present) or an empty string (when absent), avoiding the empty-bracket artifact described in FR-4.

### Frontend
- `ArticleGenerationForm.tsx` adds a controlled `languageNote` text input (FR-5).
- The new field is added to the `GenerateArticleRequest` constructor call alongside `audience` and `angle`.

### UI placement
Single-line text input, full width, placed between "Úhel pohledu" and the source toggle row. Label "Poznámka k tónu / jazyku". No backend or API client regeneration is needed beyond what `npm run build` produces from the existing OpenAPI definition (the field already exists in `GenerateArticleRequest`).

## Dependencies
- None new. Existing dependencies:
  - `WriteArticleStep` → `IChatClient`, `ArticleOptions`, `PipelineStepRecorder`
  - `ArticleGenerationForm.tsx` → `useGenerateArticleMutation`, `GenerateArticleRequest` (generated client)
- The generated TypeScript client (`api-client.ts`) already exposes `languageNote` on `GenerateArticleRequest`, so no OpenAPI regeneration is required.

## Out of Scope
- Removing `LanguageNote` from the domain/API as an alternative to adding the UI. This spec commits to keeping the field because (a) the feature spec §8.5 calls it out explicitly and (b) it gives users meaningful tone control with minimal cost.
- Re-running or re-generating any historical articles whose original requests included `LanguageNote` or non-default `Scope` — the fix is forward-only.
- Changing the set of allowed `Scope` values, the way `Scope` is validated, or the way `Scope` is rendered to the LLM in `AggregateFactsStep`.
- Localizing the prompt template for non-Czech outputs.
- Adding an enum / hardcoded list for `Scope` on the backend (it is intentionally a free string and validated only by frontend selection today).
- Migrating the `WriteArticleSystemPromptTemplate` from `string.Replace` to a proper templating engine (Scriban, Handlebars, etc.). Tracked separately if ever needed.

## Open Questions
None.

## Status: COMPLETE