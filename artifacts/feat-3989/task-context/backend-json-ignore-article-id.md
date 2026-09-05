### task: backend-json-ignore-article-id

## Goal
Add `[System.Text.Json.Serialization.JsonIgnore]` to `SubmitArticleFeedbackRequest.ArticleId` so it is never populated from the inbound JSON body and never serialized out, proven by a new unit test written first (TDD), with no change to `ArticlesController` or `SubmitArticleFeedbackHandler`.

## Files to change

**Edit:**
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs`

**Create:**
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackRequestSerializationTests.cs`

**Verify only, no change expected:**
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — `request.ArticleId = id;` (line 79) must keep compiling and behaving identically; `[JsonIgnore]` only affects JSON (de)serialization, not normal C# property get/set.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` — reads `request.ArticleId` in four places (ownership check, not-found, not-generated, already-submitted branches); must keep working unchanged since it reads the property after the controller has already set it.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` — constructs `SubmitArticleFeedbackRequest` directly with an `ArticleId` in C# (not via JSON), so it is unaffected by `[JsonIgnore]`; must still pass unmodified.
- `backend/test/Anela.Heblo.Tests/Controllers/ArticlesControllerTests.cs` — does not exercise the feedback endpoint; unaffected.

**Do not touch:**
- The other six controllers with the same `request.<X>Id = id` idiom (`AuthorizationController.cs`, `InvoiceClassificationController.cs`, `JournalController.cs`, `MarketingCalendarController.cs`, `MindMapsController.cs`, `TransportBoxController.cs`) — explicitly out of scope per the spec and arch-review; this PR is the reference example, not a sweep.
- `PrecisionScore`, `StyleScore`, `Comment` properties and their `[Range]`/`[MaxLength]` attributes on `SubmitArticleFeedbackRequest` — unaffected.

## Steps

- [ ] **Step 1: Write the failing serialization test**

Create `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackRequestSerializationTests.cs`:

```csharp
using System.Text.Json;
using Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;
using FluentAssertions;

namespace Anela.Heblo.Tests.Article.UseCases;

public class SubmitArticleFeedbackRequestSerializationTests
{
    [Fact]
    public void Deserialize_ArticleIdInBody_IsIgnored()
    {
        var bodyArticleId = Guid.NewGuid();
        var json = $$"""
            {
                "articleId": "{{bodyArticleId}}",
                "precisionScore": 4,
                "styleScore": 5,
                "comment": "great"
            }
            """;

        var request = JsonSerializer.Deserialize<SubmitArticleFeedbackRequest>(json)!;

        request.ArticleId.Should().Be(Guid.Empty);
        request.PrecisionScore.Should().Be(4);
        request.StyleScore.Should().Be(5);
        request.Comment.Should().Be("great");
    }

    [Fact]
    public void Deserialize_ArticleIdOmittedFromBody_BehavesIdenticallyToWhenPresent()
    {
        var json = """
            {
                "precisionScore": 2,
                "styleScore": 3
            }
            """;

        var request = JsonSerializer.Deserialize<SubmitArticleFeedbackRequest>(json)!;

        request.ArticleId.Should().Be(Guid.Empty);
        request.PrecisionScore.Should().Be(2);
        request.StyleScore.Should().Be(3);
    }

    [Fact]
    public void Serialize_DoesNotIncludeArticleId()
    {
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = Guid.NewGuid(),
            PrecisionScore = 3,
            StyleScore = 2,
            Comment = "ok",
        };

        var json = JsonSerializer.Serialize(request);

        json.Should().NotContain("articleId");
        json.Should().Contain("\"precisionScore\":3");
        json.Should().Contain("\"styleScore\":2");
        json.Should().Contain("\"comment\":\"ok\"");
    }
}
```

- [ ] **Step 2: Run the new tests and confirm they fail**

```bash
dotnet test --filter "FullyQualifiedName~SubmitArticleFeedbackRequestSerializationTests"
```

Expected: build succeeds, but all 3 tests **fail**. Specifically:
- `Deserialize_ArticleIdInBody_IsIgnored` fails because `request.ArticleId` deserializes to `bodyArticleId`, not `Guid.Empty`.
- `Deserialize_ArticleIdOmittedFromBody_BehavesIdenticallyToWhenPresent` passes already (nothing to ignore when the field is absent) — that's fine, it exists to pin the "omitted" half of FR-1's acceptance criteria going forward.
- `Serialize_DoesNotIncludeArticleId` fails because the emitted JSON contains an `"articleId":"..."` key.

If `Deserialize_ArticleIdOmittedFromBody_BehavesIdenticallyToWhenPresent` also fails for an unexpected reason, stop and re-read `SubmitArticleFeedbackRequest.cs` before continuing — the plan assumes today's shape (no `[Required]`/custom converter on `ArticleId`).

- [ ] **Step 3: Add `[JsonIgnore]` to `ArticleId`**

Read the current file first:

```bash
cat backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs
```

Current top of file:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }
```

Change to:

```csharp
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;

public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    [JsonIgnore]
    public Guid ArticleId { get; set; }
```

(Only the `using System.Text.Json.Serialization;` line and the `[JsonIgnore]` attribute are added. `PrecisionScore`, `StyleScore`, `Comment`, and `SubmitArticleFeedbackResponse` below are untouched.)

- [ ] **Step 4: Run the tests again and confirm they pass**

```bash
dotnet test --filter "FullyQualifiedName~SubmitArticleFeedbackRequestSerializationTests"
```

Expected: all 3 tests pass.

- [ ] **Step 5: Run the full existing Article test suite to confirm no regression**

```bash
dotnet test --filter "FullyQualifiedName~Article"
```

Expected: all tests pass, including every test in `SubmitArticleFeedbackHandlerTests` (unaffected — it constructs the request in C#, not via JSON) and `ArticlesControllerTests`.

- [ ] **Step 6: Build the whole backend solution**

```bash
dotnet build
```

Expected: build succeeds with no errors or new warnings.

- [ ] **Step 7: Run dotnet format**

```bash
dotnet format --verify-no-changes
```

Expected: no formatting changes needed. If it reports changes, run `dotnet format` (without `--verify-no-changes`) and re-stage the affected files before committing.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackRequestSerializationTests.cs
git commit -m "fix(article): exclude ArticleId from SubmitArticleFeedbackRequest JSON contract

ArticlesController.SubmitFeedback already overwrites request.ArticleId
from the route parameter before dispatch, so any articleId a client
sent in the body was silently discarded. [JsonIgnore] makes that
explicit: the property is no longer bound from the request body or
emitted in the OpenAPI schema, while remaining a normal in-memory
property the controller and handler read/write exactly as before.

Refs #3989"
```

---
