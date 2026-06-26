# Architecture Review: Wire `Article.Scope` and `Article.LanguageNote` into article writing prompt

## Skip Design: false

A new form input ("Poznámka k tónu / jazyku") will be added to `ArticleGenerationForm.tsx`. The change is small and follows the established input pattern (label + single-line text input, matching `audience` and `angle`), but it adds a visible control that must respect the form's visual grouping and copy conventions.

## Architectural Fit Assessment

The change is a **bug fix layered with a small UI completion**, not a structural addition. All architectural elements already exist:

- **Domain** (`Article.cs`): `Scope` and `LanguageNote` are first-class properties.
- **Persistence**: columns exist (`20260504195511_AddArticles`).
- **Application contract** (`GenerateArticleRequest.cs`): both fields present.
- **Pipeline orchestration** (`ArticlePipelineContext` → `WriteArticleStep`): the article object is already in scope.
- **Frontend client**: `GenerateArticleRequest` (generated) already exposes `languageNote`.

The only architectural mismatch is **internal to `WriteArticleStep.BuildUserMessage`**: the simple chain of `string.Replace` calls cannot model an *optional, single-line* fragment (the `[Tone note: …]` line) without leaving artifacts when `LanguageNote` is null/empty. This requires a tiny structural change — composing the conditional line in code and substituting a single `{tone_note_line}` placeholder — rather than the naive "just add two more `.Replace(...)` calls" pattern. The spec already captures this; this review confirms it is the right shape.

The pipeline pattern (`PipelineStepRecorder.RecordAsync` wrapping `IChatClient` calls, with anonymous-object input payloads) is consistent across all five steps. Extending the `WriteArticle` recorder payload with `scope` and `hasLanguageNote` matches the existing convention (`AggregateFactsStep` records `topic` + `snippetCount`; `WriteArticleStep` records `topic` + `factCount` + `styleGuideLength`) — see `WriteArticleStep.cs:41` and `AggregateFactsStep.cs:39`.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────┐
│ ArticleGenerationForm.tsx               │
│   • new <input> bound to languageNote   │  (FR-5)
│   • maxLength=500 (mirrors backend)     │
└───────────────┬──────────────────────────┘
                │ POST /api/articles
                ▼
┌──────────────────────────────────────────┐
│ GenerateArticleRequest                   │
│   • LanguageNote: [MaxLength(500)]       │  (FR-6 — new)
└───────────────┬──────────────────────────┘
                │ MediatR
                ▼
┌──────────────────────────────────────────┐
│ GenerateArticleHandler (unchanged)       │
│   → maps request → Article entity        │
└───────────────┬──────────────────────────┘
                │ enqueues job
                ▼
┌──────────────────────────────────────────┐
│ ArticlePipelineContext.Article           │
│   (Scope, LanguageNote already present)  │
└───────────────┬──────────────────────────┘
                ▼
┌──────────────────────────────────────────┐
│ WriteArticleStep.BuildUserMessage        │
│   1. Compose tone-note line in C#        │  (FR-2, FR-4)
│   2. Replace {scope}, {tone_note_line}   │  (FR-1)
│ WriteArticleStep recorder payload        │
│   → { topic, factCount, styleGuideLength,│
│       scope, hasLanguageNote }           │  (NFR-3)
└──────────────────────────────────────────┘
                ▲
                │ template
┌──────────────────────────────────────────┐
│ ArticleOptions                           │
│   WriteArticleSystemPromptTemplate       │  (FR-3 — default rewritten)
└──────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Conditional rendering of the tone-note line

**Options considered:**
- **A.** Add `.Replace("{language_note}", article.LanguageNote ?? "")` and let the template handle bracketing.
- **B.** Compose the *entire line* in C# (the line is either empty or `Tonalita: <note>`) and substitute a single `{tone_note_line}` placeholder.
- **C.** Switch to a templating engine (Scriban / Handlebars) for conditionals.

**Chosen approach:** **B** — compose-in-code with a `{tone_note_line}` placeholder.

**Rationale:**
- **A** is what the brief's "suggested fix" proposes but produces `[Tone note: ]` artifacts when the note is empty — explicitly disallowed by FR-4 and visible to the model as noise.
- **C** is out of scope (the spec explicitly defers it under "Out of Scope") and is over-engineering for two placeholders.
- **B** keeps the existing `string.Replace` pipeline intact, isolates the conditional logic to one private helper in `WriteArticleStep`, and gives operators a single token (`{tone_note_line}`) they can place or omit in custom templates. The default template will use `Tonalita: …` (more idiomatic Czech than transliterating "Tone note") so the rendered line reads naturally.

#### Decision 2: Pass raw scope value vs. localized label

**Options considered:**
- **A.** Pass `article.Scope` raw (`"deep-dive"`) — matches `AggregateFactsStep.cs:93`.
- **B.** Map to a Czech label (`"Hloubková analýza"`) before substitution.

**Chosen approach:** **A** — pass raw.

**Rationale:** Consistency with `AggregateFactsStep` is more important than micro-translation. The LLM understands the kebab-case tokens, and any future change to the allowed scope values lives in one place (the frontend `SCOPE_OPTIONS` table). Localization is explicitly out of scope per the spec.

#### Decision 3: `MaxLength(500)` placement and source of truth

**Options considered:**
- **A.** Validation attribute on `GenerateArticleRequest.LanguageNote` only.
- **B.** Attribute on the request + an additional invariant on the domain `Article`.
- **C.** FluentValidation validator.

**Chosen approach:** **A**.

**Rationale:** All sibling fields (`Topic`, `Audience` defaults) rely on `System.ComponentModel.DataAnnotations` on the request DTO — see `GenerateArticleRequest.cs:9`. Adding a domain-level invariant would diverge from established practice. FluentValidation is not used in this project. The frontend `maxLength=500` mirrors this; the request DTO is the single server-side source of truth.

#### Decision 4: Default template line wording

**Options considered:**
- **A.** `Rozsah: {scope}` + `{tone_note_line}` (spec proposal).
- **B.** Keep English keys (`Scope: {scope}`) to match `§8.5` literally.

**Chosen approach:** **A** with `Tonalita: …` as the rendered prefix for the tone line.

**Rationale:** The existing default template is entirely in Czech (`Téma:`, `Délka:`, `Úhel pohledu:`). Mixing English headers is inconsistent. `§8.5` is specified in English but is documenting structure; the actual deployed prompt has always been Czech. Document the divergence inline in the default template comment.

## Implementation Guidance

### Directory / Module Structure

No new files. All changes are local edits to existing files:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` | Extend `BuildUserMessage`; add private `BuildToneNoteLine` helper; extend recorder input payload at line 41 |
| `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs` | Rewrite `WriteArticleSystemPromptTemplate` default (line 56–62) |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleRequest.cs` | Add `[MaxLength(500)]` to `LanguageNote` (line 16) |
| `frontend/src/features/articles/ArticleGenerationForm.tsx` | Add `languageNote` state, input field, request constructor wiring |
| `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs` | Add tests for FR-1, FR-2, FR-3, FR-4 |
| `docs/features/article-generation.md` | Add operator note: appsettings overrides without `{scope}` / `{tone_note_line}` are silently inert (NFR-4) |

No DI registration changes. No migrations. No OpenAPI regeneration.

### Interfaces and Contracts

**Backend — `WriteArticleStep` (private helper, suggested signature):**

```csharp
private static string BuildToneNoteLine(string? languageNote)
{
    var trimmed = languageNote?.Trim();
    return string.IsNullOrEmpty(trimmed) ? "" : $"Tonalita: {trimmed}";
}
```

**Backend — `BuildUserMessage` (substitution order, kept stable):**

```
{topic} → {scope} → {audience} → {length} → {angle}
       → {language_note} (raw, kept for custom-template back-compat)
       → {tone_note_line} (composed, conditional)
       → {facts} → {style_guide}
```

Substituting both `{language_note}` (raw) **and** `{tone_note_line}` (composed) preserves backward compatibility for any operator who already wrote a custom template using `{language_note}` directly — even though the default template will only use `{tone_note_line}`.

**Backend — `GenerateArticleRequest`:**

```csharp
[MaxLength(500)]
public string? LanguageNote { get; set; }
```

**Backend — recorder payload (NFR-3):**

```csharp
new {
    topic = context.Article.Topic,
    factCount = context.Facts.Count,
    styleGuideLength = context.StyleGuideText?.Length,
    scope = context.Article.Scope,
    hasLanguageNote = !string.IsNullOrWhiteSpace(context.Article.LanguageNote)
}
```

The note **text itself MUST NOT** be added to the recorded payload (per NFR-2/NFR-3).

**Frontend — form state addition (signature, not implementation):**

```typescript
const [languageNote, setLanguageNote] = useState('');
// ...
languageNote: languageNote.trim() || undefined,
```

### Data Flow

For a request with `scope = "deep-dive"`, `languageNote = "krátké věty"`:

1. **Browser** → `ArticleGenerationForm.tsx` builds `GenerateArticleRequest` with both values; submits.
2. **API** → DataAnnotations validate (`MaxLength(500)` on `LanguageNote`). Validation failure ⇒ 400 with model-state error.
3. **`GenerateArticleHandler`** (unchanged) → maps request → `Article` entity → persists → enqueues pipeline.
4. **Pipeline** → context built; `AggregateFactsStep` already uses `Scope` (unchanged).
5. **`WriteArticleStep.ExecuteAsync`** → records step with new payload `{ ..., scope: "deep-dive", hasLanguageNote: true }` → `BuildUserMessage` composes `Tonalita: krátké věty` line → substitutes both `{scope}` and `{tone_note_line}` → LLM receives complete prompt.
6. For `languageNote = null/""/"   "`: step 5 still records `hasLanguageNote: false` and `{tone_note_line}` resolves to empty string — no `Tonalita:` line in the prompt.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing custom `WriteArticleSystemPromptTemplate` overrides in `appsettings*.json` silently ignore new values | Medium | Keep raw `{language_note}` substitution for back-compat; document in `docs/features/article-generation.md` that operators must add `{scope}` and `{tone_note_line}` (or `{language_note}`) to surface the values |
| Prompt-injection via `LanguageNote` free-text | Low | `[MaxLength(500)]` caps payload; existing fields (Topic/Audience/Angle/Scope) already pass through unsanitized — threat model unchanged (NFR-2) |
| Empty/whitespace `LanguageNote` rendering an orphan label line | Medium | `BuildToneNoteLine` trims and returns `""`; covered by dedicated tests (FR-2, FR-4) |
| Recorder payload bloat / accidental note leakage in logs | Low | Record only the boolean `hasLanguageNote` flag, not the text (NFR-3); existing recorder JSON-serializes anonymous objects only |
| Empty `[Tone note: ]` artifact if a future operator uses the spec's `[Tone note: {language_note}]` syntax verbatim with the raw substitution | Low | Default template uses `{tone_note_line}`; mention in operator docs that conditional bracketed forms require the composed token |
| Test fixture/snapshot of default template breaks | Low | No existing test asserts the default `WriteArticleSystemPromptTemplate` text; verified by reading `WriteArticleStepTests.cs` — tests run `ChatRetry` against a mocked `IChatClient` and don't assert prompt body. New tests should assert the user message *content*, not the template literal |
| Frontend `MaxLength(500)` mismatch with backend | Low | Both pinned to 500; add a brief comment in the form near `maxLength={500}` referencing the backend invariant (per coding-style.md "Why is non-obvious") |

## Specification Amendments

1. **Substitution order:** Spec FR-1/FR-2 do not specify ordering. Implementation should substitute both `{language_note}` (raw) and `{tone_note_line}` (composed) — the former for operator back-compat with any pre-existing custom templates, the latter for the conditional rendering required by FR-4. Spec implies a single replacement strategy; this review proposes the dual-token approach.

2. **Recorder payload (NFR-3):** Spec says "SHOULD" — promote to **MUST** in implementation. The cost is one extra serialized field and a single boolean; the diagnostic value is high because without it, post-hoc debugging cannot confirm the fix landed in production.

3. **Default template tone-line label:** Spec proposes `Tonalita: <note>`. Confirmed appropriate — more idiomatic Czech than direct translation of "Tone note". This review adopts that wording without change.

4. **Frontend test (FR-5):** Spec lists an "API client integration test" as one option. Given the project's E2E suite is nightly (per `CLAUDE.md` project facts) and PR CI doesn't run it, the more pragmatic verification is a React Testing Library unit test on `ArticleGenerationForm.tsx` that asserts the new input renders and that submitting populates `languageNote` on the request. Add an E2E scenario later if value warrants it.

5. **Operator documentation (NFR-4):** The spec mentions documenting back-compat in `docs/features/article-generation.md` — explicitly add a short "Custom prompt templates" subsection listing the available placeholders (`{topic}`, `{audience}`, `{length}`, `{angle}`, `{scope}`, `{language_note}`, `{tone_note_line}`, `{facts}`, `{style_guide}`) so operators know what's available without reading source.

## Prerequisites

None. Specifically:

- **No database migration** — `Scope` and `LanguageNote` columns already exist (migration `20260504195511_AddArticles`).
- **No OpenAPI regeneration** — the generated TypeScript `GenerateArticleRequest` already exposes `languageNote`.
- **No new DI registration** — `WriteArticleStep` already receives `IOptions<ArticleOptions>`.
- **No appsettings changes required** for default operation; operators with custom `WriteArticleSystemPromptTemplate` overrides should be advised (via release notes / docs) to add the new placeholders if they want scope/tone surfaced.
- **No new package dependencies.**

Implementation can start immediately.