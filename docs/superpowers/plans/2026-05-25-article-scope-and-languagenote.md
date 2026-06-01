# Wire `Article.Scope` and `Article.LanguageNote` into Article Writing Prompt — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Article.Scope` and `Article.LanguageNote` actually reach the LLM writing step (currently both fields are dropped) and surface `LanguageNote` in the UI so users can populate it.

**Architecture:** Bug-fix + UI completion. Two new `string.Replace` placeholders in `WriteArticleStep.BuildUserMessage`: a raw `{language_note}` substitution (back-compat for any operator who customized the template) plus a composed `{tone_note_line}` placeholder that resolves to `"Tonalita: <note>"` when present and `""` when empty (avoiding `[Tone note: ]` artifacts). The default `WriteArticleSystemPromptTemplate` in `ArticleOptions.cs` is rewritten to use the new tokens. A 500-character `MaxLength` is added to the existing `GenerateArticleRequest.LanguageNote` DTO field. The frontend gains a single optional text input. The pipeline recorder payload is extended with `scope` + `hasLanguageNote` (boolean only — never the raw note text) for post-hoc debugging.

**Tech Stack:** .NET 8, MediatR, xUnit + FluentAssertions + Moq, React 18 + TypeScript, React Testing Library + Jest.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` | Modify | `BuildUserMessage` substitutes `{scope}`, `{language_note}` (raw), and `{tone_note_line}` (composed). Recorder payload includes `scope` + `hasLanguageNote`. New private static helper `BuildToneNoteLine`. |
| `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs` | Modify | Default `WriteArticleSystemPromptTemplate` rewritten to include `{scope}` and `{tone_note_line}`. |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleRequest.cs` | Modify | Add `[MaxLength(500)]` to `LanguageNote`. |
| `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs` | Modify | Add tests for FR-1, FR-2, FR-3, FR-4 covering scope substitution, conditional tone-note rendering, default-template integration, raw `{language_note}` back-compat. |
| `frontend/src/features/articles/ArticleGenerationForm.tsx` | Modify | Add `languageNote` state, input field between "Úhel pohledu" and source toggles, wire into `GenerateArticleRequest` constructor. |
| `frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx` | Create | RTL unit test that asserts the new input renders, has `maxLength=500`, and submitting populates `languageNote` on the request. |
| `docs/features/article-generation.md` | Modify | Add a "Custom prompt templates" operator note listing available placeholders and warning that overrides without `{scope}` / `{tone_note_line}` are silently inert. |

No new files outside the test files. No migrations. No DI changes. No OpenAPI regeneration (the generated client already exposes `languageNote`).

---

## Notes for the implementer

- **DTOs are classes, never C# records.** `GenerateArticleRequest` is already a class — keep it that way.
- The project rule from `CLAUDE.md`: BE validation = `dotnet build` + `dotnet format`. FE validation = `npm run build` + `npm run lint`. Touched tests must pass. E2E suite is nightly, not part of this work.
- The raw `Scope` value (`"deep-dive"`, kebab-case) is what `AggregateFactsStep.cs` already feeds the LLM. Pass it raw — do not localize to Czech labels.
- `WriteArticleStep.BuildUserMessage` should substitute placeholders in this order: `{topic} → {scope} → {audience} → {length} → {angle} → {language_note} (raw) → {tone_note_line} (composed) → {facts} → {style_guide}`. Both `{language_note}` and `{tone_note_line}` exist intentionally — `{language_note}` is for back-compat with any operator who pre-existing-customized their template; `{tone_note_line}` is what the default template uses.
- `BuildToneNoteLine` MUST trim and treat null/empty/whitespace as equivalent. Output is `""` for absent and `"Tonalita: <trimmed>"` for present. No trailing newline (the template already has line breaks around the placeholder).
- **NFR-3 promoted to MUST** by the arch review: the recorder payload MUST include `scope` and `hasLanguageNote` (boolean). The raw note text MUST NOT be recorded.

---

## Task 1: Add private `BuildToneNoteLine` helper + tests

**Goal:** Introduce the conditional tone-note line composition. This is the smallest unit and unblocks all other backend work.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`
- Test: `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`

- [ ] **Step 1: Write a failing test for the present-case**

Open `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`. Add the following test method to the existing `WriteArticleStepTests` class (e.g. at the end, before the closing brace):

```csharp
[Fact]
public async Task ExecuteAsync_LanguageNotePresent_PromptContainsTonalitaLine()
{
    _options.WriteArticleSystemPromptTemplate = "{tone_note_line}";
    SetupChatResponse("{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}");

    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.LanguageNote = "krátké věty, bez žargonu";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("Tonalita: krátké věty, bez žargonu");
}
```

- [ ] **Step 2: Run the test and verify it fails**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_LanguageNotePresent_PromptContainsTonalitaLine"
```

Expected: FAIL — the literal `{tone_note_line}` token will appear in the user message because no substitution exists yet.

- [ ] **Step 3: Implement `BuildToneNoteLine` and wire into `BuildUserMessage`**

In `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`, add a new private static helper just below `BuildUserMessage` (before `BuildFactsList`):

```csharp
private static string BuildToneNoteLine(string? languageNote)
{
    var trimmed = languageNote?.Trim();
    return string.IsNullOrEmpty(trimmed) ? "" : $"Tonalita: {trimmed}";
}
```

Update `BuildUserMessage` (currently lines 102-114) to add the two new substitutions. Replace the existing method body with:

```csharp
private string BuildUserMessage(ArticlePipelineContext context)
{
    var article = context.Article;
    var factsText = BuildFactsList(context.Facts);
    var toneNoteLine = BuildToneNoteLine(article.LanguageNote);

    return _options.WriteArticleSystemPromptTemplate
        .Replace("{topic}", article.Topic)
        .Replace("{scope}", article.Scope)
        .Replace("{audience}", article.Audience ?? "obecné publikum")
        .Replace("{length}", article.Length)
        .Replace("{angle}", article.Angle ?? "(nevyspecifikováno)")
        .Replace("{language_note}", article.LanguageNote ?? "")
        .Replace("{tone_note_line}", toneNoteLine)
        .Replace("{facts}", factsText)
        .Replace("{style_guide}", context.StyleGuideText ?? "");
}
```

- [ ] **Step 4: Run the test and verify it passes**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_LanguageNotePresent_PromptContainsTonalitaLine"
```

Expected: PASS.

- [ ] **Step 5: Add tests for the absent / null / whitespace / empty cases**

Add the following three test methods to `WriteArticleStepTests.cs`:

```csharp
[Fact]
public async Task ExecuteAsync_LanguageNoteNull_ToneLineIsEmpty()
{
    _options.WriteArticleSystemPromptTemplate = "{tone_note_line}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.LanguageNote = null;

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("");
}

[Fact]
public async Task ExecuteAsync_LanguageNoteEmpty_ToneLineIsEmpty()
{
    _options.WriteArticleSystemPromptTemplate = "{tone_note_line}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.LanguageNote = "";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("");
}

[Fact]
public async Task ExecuteAsync_LanguageNoteWhitespace_ToneLineIsEmpty()
{
    _options.WriteArticleSystemPromptTemplate = "{tone_note_line}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.LanguageNote = "   ";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("");
}
```

- [ ] **Step 6: Run new tests and verify all pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_LanguageNote"
```

Expected: all four PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs \
        backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs
git commit -m "feat: render conditional Tonalita line in WriteArticleStep"
```

---

## Task 2: Substitute `{scope}` in the writing prompt

**Goal:** The `Scope` field of the article entity must reach the LLM. Substitution was added in Task 1 — this task adds explicit test coverage for FR-1.

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`

- [ ] **Step 1: Write failing tests for scope substitution**

Append the following tests to `WriteArticleStepTests.cs`:

```csharp
[Fact]
public async Task ExecuteAsync_ScopePlaceholder_IsReplacedWithRawScopeValue()
{
    _options.WriteArticleSystemPromptTemplate = "Scope: {scope}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.Scope = "deep-dive";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("Scope: deep-dive");
}

[Fact]
public async Task ExecuteAsync_TemplateWithoutScopeToken_NoOp()
{
    _options.WriteArticleSystemPromptTemplate = "Téma: {topic}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext("Sun Care");
    context.Article.Scope = "deep-dive";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("Téma: Sun Care");
    userMessage.Should().NotContain("deep-dive");
}
```

- [ ] **Step 2: Run scope tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_Scope|FullyQualifiedName~ExecuteAsync_TemplateWithoutScopeToken"
```

Expected: both PASS (substitution was added in Task 1).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs
git commit -m "test: cover {scope} placeholder substitution in WriteArticleStep"
```

---

## Task 3: Add back-compat test for raw `{language_note}` substitution

**Goal:** Lock in the FR-NFR-4 contract: operators with custom templates using `{language_note}` directly still get their value substituted.

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`

- [ ] **Step 1: Write a failing test**

Append to `WriteArticleStepTests.cs`:

```csharp
[Fact]
public async Task ExecuteAsync_RawLanguageNoteToken_IsReplacedForCustomTemplateBackCompat()
{
    _options.WriteArticleSystemPromptTemplate = "Note: {language_note}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.LanguageNote = "krátké věty";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("Note: krátké věty");
}

[Fact]
public async Task ExecuteAsync_RawLanguageNoteToken_NullSubstitutesEmptyString()
{
    _options.WriteArticleSystemPromptTemplate = "Note: {language_note}";
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.LanguageNote = null;

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Be("Note: ");
}
```

- [ ] **Step 2: Run tests**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_RawLanguageNoteToken"
```

Expected: both PASS (substitutions added in Task 1).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs
git commit -m "test: lock in {language_note} back-compat substitution"
```

---

## Task 4: Rewrite default `WriteArticleSystemPromptTemplate`

**Goal:** Out-of-the-box behavior matches feature spec §8.5 — the default template includes `{scope}` and `{tone_note_line}`, in Czech, matching the existing template style.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs:56-62`
- Test: `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`

- [ ] **Step 1: Write a failing integration test using the default template**

Append the following test to `WriteArticleStepTests.cs`:

```csharp
[Fact]
public async Task ExecuteAsync_DefaultTemplate_WithScopeAndLanguageNote_RendersBoth()
{
    // Use the default template (do not override _options.WriteArticleSystemPromptTemplate)
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.Scope = "deep-dive";
    context.Article.LanguageNote = "krátké věty";

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Contain("Rozsah: deep-dive");
    userMessage.Should().Contain("Tonalita: krátké věty");
    userMessage.Should().NotContain("{scope}");
    userMessage.Should().NotContain("{tone_note_line}");
    userMessage.Should().NotContain("{language_note}");
}

[Fact]
public async Task ExecuteAsync_DefaultTemplate_WithoutLanguageNote_OmitsTonalitaLine()
{
    IEnumerable<ChatMessage>? captured = null;
    _chat
        .Setup(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()))
        .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m)
        .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

    var context = CreateContext();
    context.Article.Scope = "overview";
    context.Article.LanguageNote = null;

    await CreateStep().ExecuteAsync(context, default);

    var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
    userMessage.Should().Contain("Rozsah: overview");
    userMessage.Should().NotContain("Tonalita");
    userMessage.Should().NotContain("[Tone note");
    userMessage.Should().NotContain("{tone_note_line}");
}
```

- [ ] **Step 2: Run new tests to verify they FAIL against the current default template**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_DefaultTemplate"
```

Expected: both FAIL — the current default template (`ArticleOptions.cs:56-62`) contains neither `{scope}` nor `{tone_note_line}`.

- [ ] **Step 3: Rewrite the default `WriteArticleSystemPromptTemplate`**

In `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs`, replace the existing initializer at lines 56-62:

```csharp
    public string WriteArticleSystemPromptTemplate { get; set; } =
        """
        Napiš článek na téma {topic} pro publikum {audience}.
        Délka: {length}. Úhel pohledu: {angle}.
        Využij tato fakta: {facts}
        {style_guide}
        """;
```

with this new default:

```csharp
    public string WriteArticleSystemPromptTemplate { get; set; } =
        """
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
        """;
```

- [ ] **Step 4: Run the new tests and the full test class**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~WriteArticleStepTests"
```

Expected: all tests PASS, including the eight existing ones (`ExecuteAsync_ValidJson_...`, `ExecuteAsync_GarbageResponse_...`, etc.) — none of them assert template literal content, so they should still pass unchanged.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs \
        backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs
git commit -m "feat: rewrite default WriteArticleSystemPromptTemplate to include scope and tone note"
```

---

## Task 5: Extend `WriteArticleStep` recorder payload with `scope` and `hasLanguageNote`

**Goal:** Post-hoc debugging can confirm both values reached the writing step. Per arch review §"Specification Amendments" item 2, this is promoted from SHOULD to MUST.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs:41`
- Test: `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`

- [ ] **Step 1: Look at how `PipelineStepRecorder` stores the payload**

Read `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PipelineStepRecorder.cs` to understand whether the input payload is serialized to JSON and where it lands on the `ArticleGenerationStep` entity. This determines how to assert.

- [ ] **Step 2: Write a failing test that asserts payload contents**

Inspect the `Mock<IArticleRepository>.AddStepAsync` call captured by the recorder. Append to `WriteArticleStepTests.cs`:

```csharp
[Fact]
public async Task ExecuteAsync_RecorderPayload_IncludesScopeAndHasLanguageNoteFlag()
{
    SetupChatResponse("{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}");

    ArticleGenerationStep? recordedStep = null;
    var repo = new Mock<IArticleRepository>();
    repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
        .Callback<ArticleGenerationStep, CancellationToken>((s, _) => recordedStep = s)
        .Returns(Task.CompletedTask);
    repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    var step = new WriteArticleStep(
        _chat.Object,
        Options.Create(_options),
        NullLogger<WriteArticleStep>.Instance,
        new PipelineStepRecorder(repo.Object));

    var context = CreateContext();
    context.Article.Scope = "deep-dive";
    context.Article.LanguageNote = "krátké věty";

    await step.ExecuteAsync(context, default);

    recordedStep.Should().NotBeNull();
    var payload = recordedStep!.InputPayload ?? "";
    payload.Should().Contain("\"scope\":\"deep-dive\"");
    payload.Should().Contain("\"hasLanguageNote\":true");
    payload.Should().NotContain("krátké věty"); // raw note text MUST NOT appear in payload
}

[Fact]
public async Task ExecuteAsync_RecorderPayload_HasLanguageNoteFalseWhenAbsent()
{
    SetupChatResponse("{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}");

    ArticleGenerationStep? recordedStep = null;
    var repo = new Mock<IArticleRepository>();
    repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
        .Callback<ArticleGenerationStep, CancellationToken>((s, _) => recordedStep = s)
        .Returns(Task.CompletedTask);
    repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

    var step = new WriteArticleStep(
        _chat.Object,
        Options.Create(_options),
        NullLogger<WriteArticleStep>.Instance,
        new PipelineStepRecorder(repo.Object));

    var context = CreateContext();
    context.Article.Scope = "overview";
    context.Article.LanguageNote = "   ";

    await step.ExecuteAsync(context, default);

    recordedStep!.InputPayload.Should().Contain("\"hasLanguageNote\":false");
}
```

**Note:** If `ArticleGenerationStep.InputPayload` is not a simple string column (e.g. it's an object/dictionary), adapt the assertions accordingly — but the field name to inspect is the one that `PipelineStepRecorder.RecordAsync` writes its `inputPayload` parameter into.

- [ ] **Step 3: Run new tests and verify they FAIL**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_RecorderPayload"
```

Expected: both FAIL — the existing payload at `WriteArticleStep.cs:41` only contains `topic`, `factCount`, `styleGuideLength`.

- [ ] **Step 4: Extend the recorder payload**

In `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`, replace the anonymous object at line 41:

```csharp
            new { topic = context.Article.Topic, factCount = context.Facts.Count, styleGuideLength = context.StyleGuideText?.Length },
```

with:

```csharp
            new
            {
                topic = context.Article.Topic,
                factCount = context.Facts.Count,
                styleGuideLength = context.StyleGuideText?.Length,
                scope = context.Article.Scope,
                hasLanguageNote = !string.IsNullOrWhiteSpace(context.Article.LanguageNote)
            },
```

- [ ] **Step 5: Run the new tests and verify they PASS**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_RecorderPayload"
```

Expected: both PASS.

- [ ] **Step 6: Run the full `WriteArticleStepTests` class to confirm no regressions**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~WriteArticleStepTests"
```

Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs \
        backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs
git commit -m "feat: record scope and hasLanguageNote on WriteArticle pipeline step"
```

---

## Task 6: Add `[MaxLength(500)]` to `GenerateArticleRequest.LanguageNote`

**Goal:** Server-side validation caps the free-text field to 500 characters, matching the frontend limit added in Task 7.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleRequest.cs:16`

- [ ] **Step 1: Apply the attribute**

Open `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleRequest.cs`. Replace line 16:

```csharp
    public string? LanguageNote { get; set; }
```

with:

```csharp
    [MaxLength(500)]
    public string? LanguageNote { get; set; }
```

(The `using System.ComponentModel.DataAnnotations;` is already at line 1.)

- [ ] **Step 2: Build the backend**

```bash
cd backend && dotnet build
```

Expected: build succeeds with no new warnings.

- [ ] **Step 3: Format**

```bash
cd backend && dotnet format
```

Expected: no diff output (or a small whitespace fix).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleRequest.cs
git commit -m "feat: cap GenerateArticleRequest.LanguageNote at 500 chars"
```

---

## Task 7: Add `languageNote` input to `ArticleGenerationForm.tsx`

**Goal:** Users can finally populate the field that the API has accepted all along. Single-line text input, optional, placed between "Úhel pohledu" and source toggles to keep the LLM-prompt inputs grouped.

**Files:**
- Modify: `frontend/src/features/articles/ArticleGenerationForm.tsx`

- [ ] **Step 1: Add the `languageNote` state, input, and request wiring**

In `frontend/src/features/articles/ArticleGenerationForm.tsx`:

(a) Add state declaration alongside the other `useState` calls (after `const [angle, setAngle] = useState('');` at line 32):

```typescript
  const [languageNote, setLanguageNote] = useState('');
```

(b) Pass `languageNote` to the `GenerateArticleRequest` constructor (the call at lines 45-55). The new line is added right after `angle`:

```typescript
    const request = new GenerateArticleRequest({
      topic: trimmedTopic,
      scope,
      length,
      audience: audience.trim() || undefined,
      angle: angle.trim() || undefined,
      languageNote: languageNote.trim() || undefined,
      useKnowledgeBase,
      useWebSearch,
      styleGuideDriveId: styleGuideDriveId.trim() || undefined,
      styleGuideItemPath: styleGuideItemPath.trim() || undefined,
    });
```

(c) Add the new form field JSX between the "Úhel pohledu" block (currently ending at line 130 `</div>`) and the source-toggle `<div className="flex gap-6">` (currently line 132). Insert this block immediately after the closing `</div>` of the "Úhel pohledu" field:

```tsx
      <div>
        <label className="block text-sm font-medium text-gray-700 mb-1">Poznámka k tónu / jazyku</label>
        <input
          type="text"
          value={languageNote}
          onChange={(e) => setLanguageNote(e.target.value)}
          placeholder="Např. krátké věty, vyhýbat se odborným termínům"
          maxLength={500}
          className="w-full border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
      </div>
```

- [ ] **Step 2: Type-check and build the frontend**

```bash
cd frontend && npm run build
```

Expected: build succeeds. `GenerateArticleRequest` already exposes `languageNote` in the generated client, so no type errors.

- [ ] **Step 3: Lint**

```bash
cd frontend && npm run lint
```

Expected: no errors. If Prettier reports formatting issues, fix them.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/articles/ArticleGenerationForm.tsx
git commit -m "feat: add LanguageNote input to article generation form"
```

---

## Task 8: Frontend test for `ArticleGenerationForm` (RTL)

**Goal:** Lock in the FR-5 behavior — input renders, has the right limit, and submitting populates `languageNote` on the request.

**Files:**
- Create: `frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx`

- [ ] **Step 1: Inspect existing test conventions**

Read `frontend/src/features/articles/__tests__/ArticleSourceList.test.tsx` and `frontend/src/features/articles/__tests__/ArticleFeedbackSection.test.tsx` to confirm the import paths, jest mock conventions, and how hooks are mocked in this folder. The form depends on `useGenerateArticleMutation` and `useMarketingWriterPermission` — both need to be mocked.

- [ ] **Step 2: Write a failing test**

Create `frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx`:

```tsx
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ArticleGenerationForm from '../ArticleGenerationForm';

const generateMock = jest.fn();

jest.mock('../../../api/hooks/useArticles', () => ({
  useGenerateArticleMutation: () => ({
    mutate: generateMock,
    isPending: false,
    error: null,
  }),
}));

jest.mock('../../../api/hooks/useMarketingWriterPermission', () => ({
  useMarketingWriterPermission: () => true,
}));

describe('ArticleGenerationForm', () => {
  beforeEach(() => {
    generateMock.mockReset();
  });

  it('renders the language note input with a 500-character limit', () => {
    render(<ArticleGenerationForm onArticleCreated={() => {}} />);
    const input = screen.getByLabelText(/Poznámka k tónu \/ jazyku/) as HTMLInputElement;
    expect(input).toBeInTheDocument();
    expect(input.maxLength).toBe(500);
    expect(input.required).toBe(false);
  });

  it('passes the trimmed languageNote on submit', () => {
    render(<ArticleGenerationForm onArticleCreated={() => {}} />);

    fireEvent.change(screen.getByLabelText(/Téma/), {
      target: { value: 'Sun care basics' },
    });
    fireEvent.change(screen.getByLabelText(/Poznámka k tónu \/ jazyku/), {
      target: { value: '  krátké věty  ' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Generovat článek/ }));

    expect(generateMock).toHaveBeenCalledTimes(1);
    const request = generateMock.mock.calls[0][0];
    expect(request.languageNote).toBe('krátké věty');
  });

  it('submits languageNote as undefined when empty', () => {
    render(<ArticleGenerationForm onArticleCreated={() => {}} />);

    fireEvent.change(screen.getByLabelText(/Téma/), {
      target: { value: 'Sun care basics' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Generovat článek/ }));

    expect(generateMock).toHaveBeenCalledTimes(1);
    const request = generateMock.mock.calls[0][0];
    expect(request.languageNote).toBeUndefined();
  });
});
```

- [ ] **Step 3: Run the test**

```bash
cd frontend && npx jest src/features/articles/__tests__/ArticleGenerationForm.test.tsx
```

Expected: all three tests PASS (the input was added in Task 7).

If the `getByLabelText` selectors fail because labels lack the `htmlFor`/`id` association, fall back to `getByPlaceholderText` for the language-note input and `screen.getByPlaceholderText(/Např\. Výhody/)` for the topic input. Adjust the assertions if needed — the goal is to exercise the binding, not to dictate accessibility refactors here.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/features/articles/__tests__/ArticleGenerationForm.test.tsx
git commit -m "test: cover languageNote input in ArticleGenerationForm"
```

---

## Task 9: Document available placeholders in `docs/features/article-generation.md`

**Goal:** Operators with custom `WriteArticleSystemPromptTemplate` overrides know which placeholders are available and that the new ones (`{scope}`, `{tone_note_line}`) must be added explicitly to surface those values.

**Files:**
- Modify: `docs/features/article-generation.md` (insert a new subsection after §8.5, before §8.6)

- [ ] **Step 1: Insert the operator note**

Open `docs/features/article-generation.md`. After the existing §8.5 `WriteArticleHandler` block ends (around line 239, just before the line `### 8.6 Persistence`), insert this new subsection:

```markdown
#### 8.5.1 Custom prompt templates

`ArticleOptions.WriteArticleSystemPromptTemplate` is rendered via `string.Replace`. Available placeholders:

| Placeholder | Value | Notes |
|---|---|---|
| `{topic}` | `Article.Topic` | Always present. |
| `{audience}` | `Article.Audience` or `"obecné publikum"` if null. | |
| `{length}` | `Article.Length` | E.g. `"medium (1000w)"`. |
| `{angle}` | `Article.Angle` or `"(nevyspecifikováno)"` if null. | |
| `{scope}` | `Article.Scope` raw value | One of `overview`, `deep-dive`, `how-to`, `comparison`. |
| `{language_note}` | `Article.LanguageNote` raw value, or `""` if null. | Use this for full-line custom templates. |
| `{tone_note_line}` | Composed line: `Tonalita: <note>` when present, `""` when absent. | Use this to add a single self-contained line that disappears when no note is supplied. |
| `{facts}` | Numbered list of aggregated facts. | |
| `{style_guide}` | Style guide body, or `""` if none. | |

**Back-compat:** appsettings overrides that omit `{scope}` or `{tone_note_line}` continue to work — those values are simply not surfaced to the LLM. To surface them, add the placeholders to the override.
```

- [ ] **Step 2: Commit**

```bash
git add docs/features/article-generation.md
git commit -m "docs: list custom-template placeholders for WriteArticleSystemPromptTemplate"
```

---

## Task 10: Full validation pass

**Goal:** Confirm the whole change set builds, lints, and passes tests before declaring done.

- [ ] **Step 1: Backend build + format**

```bash
cd backend && dotnet build && dotnet format --verify-no-changes
```

Expected: build succeeds; `dotnet format --verify-no-changes` exits 0.

- [ ] **Step 2: Run the touched backend test class**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~WriteArticleStepTests"
```

Expected: all PASS (including the new tests added in Tasks 1, 2, 3, 4, 5 and the eight pre-existing ones).

- [ ] **Step 3: Run the full backend test suite to catch any unforeseen regressions**

```bash
cd backend && dotnet test
```

Expected: all PASS. Article-related tests in `Anela.Heblo.Tests.Article.*` are the most likely affected; nothing else should break since the public surface of `WriteArticleStep` is unchanged.

- [ ] **Step 4: Frontend build + lint**

```bash
cd frontend && npm run build && npm run lint
```

Expected: build succeeds; lint clean.

- [ ] **Step 5: Run the frontend article tests**

```bash
cd frontend && npx jest src/features/articles
```

Expected: all PASS, including the new `ArticleGenerationForm.test.tsx`.

- [ ] **Step 6: Acceptance walk-through (manual)**

Spot-check the spec acceptance criteria against the running code:

- FR-1: with `Scope = "deep-dive"` and a custom template containing `Scope: {scope}`, the user message contains `Scope: deep-dive`. **(Task 2 test)**
- FR-1: template without `{scope}` is unchanged. **(Task 2 test)**
- FR-2: `LanguageNote = "krátké věty, bez žargonu"` substitutes into `{language_note}` and `{tone_note_line}`. **(Tasks 1, 3 tests)**
- FR-2: null/empty/whitespace `LanguageNote` results in no `Tonalita:` line. **(Task 1 tests)**
- FR-3: default template includes `Rozsah: {scope}` and `{tone_note_line}`; end-to-end test with `Scope = "deep-dive"` + `LanguageNote = "krátké věty"` shows both. **(Task 4 tests)**
- FR-4: no `[Tone note: ]` or `{language_note}` literal artifacts in produced prompts. **(Task 4 + Task 1 tests)**
- FR-5: form renders new input, submits trimmed value or `undefined`. **(Task 8 tests)**
- FR-6: request with `LanguageNote` > 500 chars is rejected with HTTP 400. **(Validated by `[MaxLength(500)]` attribute; covered by ASP.NET Core model validation. Optional spot check: open Swagger or curl `POST /api/articles/generate` with a 501-char `languageNote` and confirm 400.)**
- NFR-3: `WriteArticle` step record contains `scope` and `hasLanguageNote`, never the raw note text. **(Task 5 tests)**

- [ ] **Step 7: Final commit (if any leftover formatting changes)**

```bash
git status
```

If `dotnet format` or `npm run lint` produced any uncommitted whitespace fixes, commit them:

```bash
git add -A
git commit -m "chore: apply formatter output"
```

Otherwise skip this step.

---

## Self-Review

**Spec coverage:**

| Requirement | Task |
|---|---|
| FR-1 `{scope}` substitution + behavior | Tasks 1, 2 |
| FR-2 `{language_note}` substitution + null/empty handling | Tasks 1, 3 |
| FR-3 Default template rewrite | Task 4 |
| FR-4 No `{language_note}` literal or empty-bracket artifacts | Task 1 (helper) + Task 4 (default template) |
| FR-5 Frontend `languageNote` input | Tasks 7, 8 |
| FR-6 `[MaxLength(500)]` on `GenerateArticleRequest.LanguageNote` | Task 6 |
| NFR-1 Performance — negligible by construction | — |
| NFR-2 Security — `MaxLength` caps payload; note not logged | Task 6 + Task 5 (`hasLanguageNote` flag only) |
| NFR-3 Observability — scope + hasLanguageNote in recorder payload | Task 5 |
| NFR-4 Back-compat — raw `{language_note}` still substituted; documented | Task 3 (test) + Task 9 (docs) |

All requirements have at least one task. No gaps.

**Placeholder scan:** No "TBD", "implement later", "add validation", "similar to Task N", or unspecified test bodies. Every code-changing step shows the code.

**Type / name consistency:**
- `BuildToneNoteLine(string?)` returns `string` — used consistently across Tasks 1, 4.
- `{tone_note_line}` placeholder — same spelling in `WriteArticleStep`, `ArticleOptions`, docs (Task 9), tests.
- `hasLanguageNote` — same casing in recorder payload (Task 5) and tests.
- Frontend `languageNote` field — same casing in `GenerateArticleRequest` (already generated), state variable, form field, and tests.
- Backend `LanguageNote` (PascalCase) ↔ frontend `languageNote` (camelCase) — standard ASP.NET serialization, already established in the existing API.

No inconsistencies found.
