# Article Writing Prompt Configuration Fix — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the article writing system prompt as a configurable `ArticleOptions` property and rename the misnamed `WriteArticleSystemPromptTemplate` to `WriteArticleUserPromptTemplate` to match its actual role.

**Architecture:** Pure refactor with surgical edits in three files. (1) `ArticleOptions` gains `WriteArticleSystemPrompt` (default = current `SystemInstruction` constant verbatim) and renames `WriteArticleSystemPromptTemplate` → `WriteArticleUserPromptTemplate`. (2) `WriteArticleStep` deletes the inline `SystemInstruction` constant, switches `BuildSystemPrompt` to read `_options.WriteArticleSystemPrompt`, and updates `BuildUserMessage` to read `_options.WriteArticleUserPromptTemplate`. The conditional `"STYLE GUIDE — follow this exactly:\n…\n\n"` wrapper stays in code (FR-5: byte-identical defaults). (3) Documentation in `docs/features/article-generation.md` updates the key example.

**Tech Stack:** .NET 8, `Microsoft.Extensions.Options` (Options Pattern with `ValidateDataAnnotations().ValidateOnStart()`), `Microsoft.Extensions.AI.IChatClient`, xUnit + FluentAssertions + Moq for tests.

---

## File Structure

| Path | Action | Responsibility |
|---|---|---|
| `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs` | Modify | Add `WriteArticleSystemPrompt`; rename `WriteArticleSystemPromptTemplate` → `WriteArticleUserPromptTemplate`. |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` | Modify | Delete `SystemInstruction` const; make `BuildSystemPrompt` instance method reading `_options.WriteArticleSystemPrompt`; update `BuildUserMessage` to read `_options.WriteArticleUserPromptTemplate`. |
| `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs` | Modify | Add two tests: (a) override of `WriteArticleSystemPrompt` flows through as `ChatRole.System` message; (b) style-guide wrapper preserved when `StyleGuideText` is set. |
| `docs/features/article-generation.md` | Modify | Rename `"WriteArticleSystemPromptTemplate"` → `"WriteArticleUserPromptTemplate"` in the JSON example at line 307; add `"WriteArticleSystemPrompt"` next to it for discoverability. |

No new files. No DI registration changes. No migrations. No frontend impact.

---

## Validation Commands

Run from the worktree root unless noted otherwise.

| Step | Command | Working directory |
|---|---|---|
| Build backend | `dotnet build` | `backend/` |
| Run `WriteArticleStep` tests only | `dotnet test --filter "FullyQualifiedName~WriteArticleStep"` | `backend/test/Anela.Heblo.Tests/` |
| Run all article-pipeline tests | `dotnet test --filter "FullyQualifiedName~Anela.Heblo.Tests.Article.Pipeline"` | `backend/test/Anela.Heblo.Tests/` |
| Format check | `dotnet format --verify-no-changes` | `backend/` |

All four MUST succeed before the final commit.

---

## Task 1: Rename `WriteArticleSystemPromptTemplate` → `WriteArticleUserPromptTemplate`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs:56-62`
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs:107`

This task is a pure mechanical rename. The default string is preserved exactly. No semantic behaviour change — existing `WriteArticleStepTests` should pass before and after.

- [ ] **Step 1: Confirm tests are green before the rename**

Run: `dotnet test --filter "FullyQualifiedName~WriteArticleStep"` from `backend/test/Anela.Heblo.Tests/`.

Expected: PASS with the existing 8 tests in `WriteArticleStepTests.cs`. If anything fails here, stop and investigate — the failure is pre-existing and outside this plan's scope.

- [ ] **Step 2: Rename the property in `ArticleOptions.cs`**

In `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs`, replace lines 56–62:

```csharp
    public string WriteArticleSystemPromptTemplate { get; set; } =
        """
        Napiš článek na téma {topic} pro publikum {audience}.
        Délka: {length}. Úhel pohledu: {angle}.
        Využij tato fakta: {facts}
        {style_guide}
        """;
```

with:

```csharp
    public string WriteArticleUserPromptTemplate { get; set; } =
        """
        Napiš článek na téma {topic} pro publikum {audience}.
        Délka: {length}. Úhel pohledu: {angle}.
        Využij tato fakta: {facts}
        {style_guide}
        """;
```

- [ ] **Step 3: Update the reference in `WriteArticleStep.cs`**

In `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`, line 107, change:

```csharp
        return _options.WriteArticleSystemPromptTemplate
```

to:

```csharp
        return _options.WriteArticleUserPromptTemplate
```

- [ ] **Step 4: Verify the build succeeds and old name has no references**

Run from `backend/`:
```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

Then from the worktree root:
```bash
grep -rn "WriteArticleSystemPromptTemplate" backend/ frontend/ docs/features/
```

Expected: zero hits. (Per arch-review amendment #1, `docs/superpowers/plans/` and `artifacts/` are excluded — they are immutable historical records.)

- [ ] **Step 5: Run `WriteArticleStep` tests**

Run from `backend/test/Anela.Heblo.Tests/`:
```bash
dotnet test --filter "FullyQualifiedName~WriteArticleStep"
```

Expected: all 8 existing tests PASS. No new tests yet.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs
git commit -m "refactor: rename WriteArticleSystemPromptTemplate to WriteArticleUserPromptTemplate

The property is sent as ChatRole.User in WriteArticleStep.BuildUserMessage,
so its 'SystemPrompt' name was misleading. Default value is preserved
verbatim; no behavioural change."
```

---

## Task 2: Expose `WriteArticleSystemPrompt` as a configurable option

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs` (add new property after `WriteArticleUserPromptTemplate`)
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs:86-100` (delete `SystemInstruction` const; switch `BuildSystemPrompt` to instance method reading `_options.WriteArticleSystemPrompt`)
- Test: `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs` (add two new tests)

This task uses a TDD-aligned flow: add the new property unused first (compiles, no behaviour change), then write a failing test that proves the override doesn't flow through yet, then refactor the code to make it pass. Finally add a regression test for the style-guide wrapper composition (the only piece of system-prompt-building logic that stays in code).

- [ ] **Step 1: Add the `WriteArticleSystemPrompt` property to `ArticleOptions`**

In `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs`, immediately **before** the `WriteArticleUserPromptTemplate` property (so the system/user pair sits together), insert:

```csharp
    public string WriteArticleSystemPrompt { get; set; } =
        """
        Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
        Odpověz POUZE validním JSON bez markdown nebo code fences.
        V poli article_html použij výhradně HTML tagy – nikdy nepište doslovný text "\n" jako obsah.
        {"article_title":"...","article_html":"<article>...</article>","sources_used":[{"title":"...","url":"..."}]}
        """;

```

The string MUST equal the current `SystemInstruction` constant in `WriteArticleStep.cs:86-92` character-for-character. Per arch-review Decision 2, do **not** add `[Required, MinLength(1)]` — consistency with `QueryPlannerSystemPrompt`, `AggregateFactsSystemPrompt`, `ValidateFactsSystemPrompt`.

- [ ] **Step 2: Verify the build still succeeds with the new (unused) property**

Run from `backend/`:
```bash
dotnet build
```

Expected: Build succeeded, 0 errors, 0 warnings related to this property.

Then run the existing tests to confirm no behaviour change:
```bash
cd backend/test/Anela.Heblo.Tests/
dotnet test --filter "FullyQualifiedName~WriteArticleStep"
```

Expected: all 8 tests PASS. The new property is defined but unused — behaviour is unchanged.

- [ ] **Step 3: Write the failing test — override of `WriteArticleSystemPrompt` must flow through**

Append to `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`, immediately before the closing `}` of the class:

```csharp
    [Fact]
    public async Task ExecuteAsync_OverriddenSystemPrompt_PassesOverriddenStringAsSystemMessage()
    {
        // Arrange
        const string customPrompt = "CUSTOM SYSTEM PROMPT FOR TEST";
        _options.WriteArticleSystemPrompt = customPrompt;
        SetupChatResponse("""{"article_title":"T","article_html":"<p>x</p>","sources_used":[]}""");

        // Act
        await CreateStep().ExecuteAsync(CreateContext(), default);

        // Assert
        _chat.Verify(c => c.GetResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Any(m => m.Role == ChatRole.System && m.Text == customPrompt)),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 4: Run the new test and verify it FAILS**

Run from `backend/test/Anela.Heblo.Tests/`:
```bash
dotnet test --filter "FullyQualifiedName~ExecuteAsync_OverriddenSystemPrompt_PassesOverriddenStringAsSystemMessage"
```

Expected: FAIL with a Moq verification message indicating the system message text was the hardcoded `SystemInstruction` (Czech editor persona) instead of `"CUSTOM SYSTEM PROMPT FOR TEST"`. This proves the override does not yet flow through.

If the test passes here, the test is wrong — investigate before moving on.

- [ ] **Step 5: Refactor `BuildSystemPrompt` to read from `_options.WriteArticleSystemPrompt`**

In `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`, **delete** lines 86–100 (the `SystemInstruction` constant block and the static `BuildSystemPrompt` method):

```csharp
    private const string SystemInstruction =
        """
        Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
        Odpověz POUZE validním JSON bez markdown nebo code fences.
        V poli article_html použij výhradně HTML tagy – nikdy nepište doslovný text "\n" jako obsah.
        {"article_title":"...","article_html":"<article>...</article>","sources_used":[{"title":"...","url":"..."}]}
        """;

    private static string BuildSystemPrompt(string? styleGuideText)
    {
        if (styleGuideText == null)
            return SystemInstruction;

        return $"STYLE GUIDE — follow this exactly:\n{styleGuideText}\n\n{SystemInstruction}";
    }
```

**Replace** with (note: now an instance method, no longer `static`, so it can access `_options`):

```csharp
    private string BuildSystemPrompt(string? styleGuideText)
    {
        if (styleGuideText == null)
            return _options.WriteArticleSystemPrompt;

        return $"STYLE GUIDE — follow this exactly:\n{styleGuideText}\n\n{_options.WriteArticleSystemPrompt}";
    }
```

The call site at line 45 (`var systemPrompt = BuildSystemPrompt(context.StyleGuideText);`) does **not** change — it was already an instance-method-style call.

- [ ] **Step 6: Verify the build still succeeds**

Run from `backend/`:
```bash
dotnet build
```

Expected: Build succeeded, 0 errors. There must be **no** reference to `SystemInstruction` remaining anywhere in `WriteArticleStep.cs`.

- [ ] **Step 7: Run the new test and verify it PASSES**

Run from `backend/test/Anela.Heblo.Tests/`:
```bash
dotnet test --filter "FullyQualifiedName~ExecuteAsync_OverriddenSystemPrompt_PassesOverriddenStringAsSystemMessage"
```

Expected: PASS.

- [ ] **Step 8: Run the full `WriteArticleStep` test suite to confirm no regressions**

Run from `backend/test/Anela.Heblo.Tests/`:
```bash
dotnet test --filter "FullyQualifiedName~WriteArticleStep"
```

Expected: all 9 tests PASS (8 existing + 1 new). Default behaviour is unchanged because `ArticleOptions.WriteArticleSystemPrompt` default equals the deleted `SystemInstruction` constant verbatim (FR-5).

- [ ] **Step 9: Add the style-guide wrapper regression test**

Per arch-review risk mitigation (MEDIUM severity), append a second test to `backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs`, immediately before the closing `}` of the class:

```csharp
    [Fact]
    public async Task ExecuteAsync_WithStyleGuide_SystemMessageWrapsStyleGuideAroundSystemPrompt()
    {
        // Arrange
        _options.WriteArticleSystemPrompt = "BASE PROMPT";
        SetupChatResponse("""{"article_title":"T","article_html":"<p>x</p>","sources_used":[]}""");
        var context = CreateContext();
        context.StyleGuideText = "Use a friendly tone.";
        const string expected = "STYLE GUIDE — follow this exactly:\nUse a friendly tone.\n\nBASE PROMPT";

        // Act
        await CreateStep().ExecuteAsync(context, default);

        // Assert
        _chat.Verify(c => c.GetResponseAsync(
            It.Is<IEnumerable<ChatMessage>>(msgs =>
                msgs.Any(m => m.Role == ChatRole.System && m.Text == expected)),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 10: Run the regression test and verify it PASSES**

Run from `backend/test/Anela.Heblo.Tests/`:
```bash
dotnet test --filter "FullyQualifiedName~ExecuteAsync_WithStyleGuide_SystemMessageWrapsStyleGuideAroundSystemPrompt"
```

Expected: PASS. This guards against accidentally dropping the `"STYLE GUIDE — follow this exactly:\n"` wrapper in future refactors.

- [ ] **Step 11: Run the full backend test suite**

Run from `backend/test/Anela.Heblo.Tests/`:
```bash
dotnet test
```

Expected: all tests PASS. Confirms no cross-feature regression.

- [ ] **Step 12: Run formatter and verify**

Run from `backend/`:
```bash
dotnet format
dotnet format --verify-no-changes
```

Expected: first command runs cleanly; second returns 0 (no formatting drift).

- [ ] **Step 13: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs \
        backend/test/Anela.Heblo.Tests/Article/Pipeline/WriteArticleStepTests.cs
git commit -m "feat: expose article writing system prompt as configurable ArticleOptions property

Add ArticleOptions.WriteArticleSystemPrompt (default = previous hardcoded
SystemInstruction verbatim) and switch WriteArticleStep.BuildSystemPrompt
to read from it. The conditional 'STYLE GUIDE — follow this exactly:\\n'
wrapper composition stays in code. Default behaviour is byte-identical.

Adds two unit tests:
- override of WriteArticleSystemPrompt flows through as ChatRole.System
- style-guide wrapper still wraps the configured prompt"
```

---

## Task 3: Update feature documentation

**Files:**
- Modify: `docs/features/article-generation.md:307` (JSON example in section 12. Configuration)

The arch-review notes this update is optional for `WriteArticleSystemPrompt` discoverability but mandatory for the rename. We include the new key in the example so operators can find it.

- [ ] **Step 1: Update the JSON example**

In `docs/features/article-generation.md`, replace line 307:

```
  "WriteArticleSystemPromptTemplate": "..."
```

with:

```
  "WriteArticleSystemPrompt": "...",
  "WriteArticleUserPromptTemplate": "..."
```

So the surrounding block becomes:

```json
"Articles": {
  "DefaultModel": "claude-sonnet-4-6",
  "WriteMaxTokens": 4096,
  "AggregateMaxTokens": 1024,
  "WebSearchTopK": 5,
  "KnowledgeBaseTopK": 8,
  "DefaultLength": "medium (1000w)",
  "QueryPlannerModel": "claude-haiku-4-5-20251001",
  "AggregateFactsModel": "claude-sonnet-4-6",
  "ValidateFactsModel": "claude-haiku-4-5-20251001",
  "QueryPlannerSystemPrompt": "...",
  "AggregateFactsSystemPrompt": "...",
  "ValidateFactsSystemPrompt": "...",
  "WriteArticleSystemPrompt": "...",
  "WriteArticleUserPromptTemplate": "..."
},
```

- [ ] **Step 2: Confirm zero stale references in user-facing docs and source code**

Run from the worktree root:
```bash
grep -rn "WriteArticleSystemPromptTemplate" backend/ frontend/ docs/features/
```

Expected: zero hits. (Per arch-review amendment #1, `docs/superpowers/plans/` and `artifacts/` are excluded — they are immutable historical records.)

- [ ] **Step 3: Commit**

```bash
git add docs/features/article-generation.md
git commit -m "docs: update article generation config example with renamed prompt keys

WriteArticleSystemPromptTemplate split into WriteArticleSystemPrompt
(persona/system) and WriteArticleUserPromptTemplate (article brief/user)."
```

---

## Final Validation Checklist

After Task 3 commits, run these in order and confirm each green before declaring the change ready for PR.

- [ ] **From `backend/`:** `dotnet build` → 0 errors, 0 warnings related to this change.
- [ ] **From `backend/`:** `dotnet format --verify-no-changes` → exit 0.
- [ ] **From `backend/test/Anela.Heblo.Tests/`:** `dotnet test` → all tests PASS (full suite).
- [ ] **From worktree root:** `grep -rn "WriteArticleSystemPromptTemplate" backend/ frontend/ docs/features/` → zero hits.
- [ ] **From worktree root:** `grep -rn "SystemInstruction" backend/src/Anela.Heblo.Application/Features/Article/` → zero hits.

---

## PR Description / Release Notes Requirements

Per spec NFR-3 and arch-review risk row 1, the PR description MUST include:

1. **Key rename mapping** (exact strings):
   - `Articles:WriteArticleSystemPromptTemplate` → `Articles:WriteArticleUserPromptTemplate` (renamed; same default; controls user-turn article brief)
   - `Articles:WriteArticleSystemPrompt` (new; default = previous hardcoded `SystemInstruction`; controls system-turn editor persona)

2. **Pre-deployment runbook step:**

   > Before deploying this change to Development / Staging / Production, audit Azure App Service Configuration for each environment. If the key `Articles:WriteArticleSystemPromptTemplate` is set as an override, rename it to `Articles:WriteArticleUserPromptTemplate` in lockstep with the deploy. No source-controlled `appsettings*.json` in this repo currently overrides this key, so the audit is exclusively against Azure App Service Configuration.

3. **Behavioural guarantee:** With no environment overrides, the chat messages produced for any `ArticlePipelineContext` are byte-identical before vs. after the change (FR-5).
