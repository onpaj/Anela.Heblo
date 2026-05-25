using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class WriteArticleStepTests
{
    private readonly Mock<IChatClient> _chat = new();
    private readonly ArticleOptions _options = new();

    private static PipelineStepRecorder CreateNoOpRecorder()
    {
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new PipelineStepRecorder(repo.Object);
    }

    private WriteArticleStep CreateStep() =>
        new(_chat.Object, Options.Create(_options), NullLogger<WriteArticleStep>.Instance, CreateNoOpRecorder());

    private static ArticlePipelineContext CreateContext(string topic = "Topic") =>
        new()
        {
            Article = new DomainArticle { Topic = topic, Length = "medium (1000w)" }
        };

    private void SetupChatResponse(string text) =>
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task ExecuteAsync_ValidJson_SetsTitleHtmlAndSources()
    {
        SetupChatResponse(
            """
            {"article_title":"My Title","article_html":"<article>Body</article>","sources_used":[
              {"title":"Web Source","url":"https://example.com"},
              {"title":"KB Source","url":null}
            ]}
            """);

        var context = CreateContext();

        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("My Title");
        context.GeneratedHtml.Should().Be("<article>Body</article>");
        context.SourceRefs.Should().HaveCount(2);
        context.SourceRefs[0].Title.Should().Be("Web Source");
        context.SourceRefs[0].Url.Should().Be("https://example.com");
        context.SourceRefs[0].Type.Should().Be(SourceType.Web);
        context.SourceRefs[1].Title.Should().Be("KB Source");
        context.SourceRefs[1].Url.Should().BeNull();
        context.SourceRefs[1].Type.Should().Be(SourceType.KnowledgeBase);
    }

    [Fact]
    public async Task ExecuteAsync_GarbageResponse_FallsBackToTopicTitleAndWrappedHtml()
    {
        SetupChatResponse("garbage");

        var context = CreateContext("Sun Care");

        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("Sun Care");
        context.GeneratedHtml.Should().Be("<p>garbage</p>");
        context.SourceRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_KbSourceWithMatchingSnippetAndFact_PopulatesAllFields()
    {
        SetupChatResponse(
            """
            {"article_title":"T","article_html":"<p>x</p>","sources_used":[
              {"title":"KB Source","url":null}
            ]}
            """);

        var chunkId = Guid.NewGuid();
        var context = CreateContext();
        context.ContextSnippets =
        [
            new ContextSnippet
            {
                Source = SourceType.KnowledgeBase,
                Title = "KB Source",
                Excerpt = "ex",
                ChunkId = chunkId,
                Score = 0.87
            }
        ];
        context.Facts =
        [
            new AggregatedFact
            {
                Claim = "Important claim about the topic.",
                Confidence = 0.9,
                SourceTitle = "KB Source",
                ValidationNote = "validated"
            }
        ];

        await CreateStep().ExecuteAsync(context, default);

        context.SourceRefs.Should().HaveCount(1);
        var sourceRef = context.SourceRefs[0];
        sourceRef.Type.Should().Be(SourceType.KnowledgeBase);
        sourceRef.ChunkId.Should().Be(chunkId);
        sourceRef.Confidence.Should().Be(0.87);
        sourceRef.Excerpt.Should().Be("Important claim about the topic.");
        sourceRef.ValidationNote.Should().Be("validated");
    }

    [Fact]
    public async Task ExecuteAsync_SourceWithoutMatch_LeavesEnrichmentFieldsNull()
    {
        SetupChatResponse(
            """
            {"article_title":"T","article_html":"<p>x</p>","sources_used":[
              {"title":"Unknown KB Title","url":null}
            ]}
            """);

        var context = CreateContext();
        // No matching snippet or fact.

        await CreateStep().ExecuteAsync(context, default);

        var sourceRef = context.SourceRefs.Single();
        sourceRef.ChunkId.Should().BeNull();
        sourceRef.Confidence.Should().BeNull();
        sourceRef.Excerpt.Should().BeNull();
        sourceRef.ValidationNote.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_LongFactClaim_TruncatesExcerptTo200Chars()
    {
        SetupChatResponse(
            """
            {"article_title":"T","article_html":"<p>x</p>","sources_used":[
              {"title":"KB Source","url":null}
            ]}
            """);

        var context = CreateContext();
        context.Facts =
        [
            new AggregatedFact
            {
                Claim = new string('a', 250),
                SourceTitle = "KB Source"
            }
        ];

        await CreateStep().ExecuteAsync(context, default);

        context.SourceRefs.Single().Excerpt!.Length.Should().Be(200);
    }

    [Fact]
    public async Task ExecuteAsync_FencedJsonNoClosingFence_ValidBody_ParsesSuccessfully()
    {
        // Opening fence, no closing fence, but the JSON body is complete
        SetupChatResponse("```json\n{\"article_title\":\"Fenced Title\",\"article_html\":\"<p>content</p>\",\"sources_used\":[]}");

        var context = CreateContext("Fenced Topic");
        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("Fenced Title");
        context.GeneratedHtml.Should().Be("<p>content</p>");
        context.SourceRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_FencedTruncatedJson_RescuesPartialArticleHtml()
    {
        // Simulates max-token truncation: response starts with fence + JSON but
        // is cut off in the middle of article_html, before any closing quote/brace/fence
        const string truncated = "```json\n{\"article_title\":\"Bisabolol\",\"article_html\":\"<article><p>Partial";

        SetupChatResponse(truncated);

        var context = CreateContext("Bisabolol");
        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("Bisabolol");
        context.GeneratedHtml.Should().NotStartWith("```json");
        context.GeneratedHtml.Should().Contain("<article><p>Partial");
        // The rescue must extract just the HTML — the JSON field names must NOT appear in the output
        context.GeneratedHtml.Should().NotContain("\"article_html\"");
        context.SourceRefs.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_GarbageResponse_FallbackIsHtmlEncoded()
    {
        // Raw garbage that contains < and > must not be injected as HTML
        SetupChatResponse("<script>alert('xss')</script>");

        var context = CreateContext("Topic");
        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("Topic");
        context.GeneratedHtml.Should().NotContain("<script>");
        context.GeneratedHtml.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task ExecuteAsync_LanguageNotePresent_PromptContainsTonalitaLine()
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
        context.Article.LanguageNote = "krátké věty, bez žargonu";

        await CreateStep().ExecuteAsync(context, default);

        var userMessage = captured!.Single(m => m.Role == ChatRole.User).Text;
        userMessage.Should().Be("Tonalita: krátké věty, bez žargonu");
    }

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

    [Fact]
    public async Task ExecuteAsync_RecorderPayload_IncludesScopeAndHasLanguageNoteFlag()
    {
        ArticleGenerationStep? recordedStep = null;
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) => recordedStep = s)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

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
        var payload = recordedStep!.InputJson ?? "";
        payload.Should().Contain("\"scope\":\"deep-dive\"");
        payload.Should().Contain("\"hasLanguageNote\":true");
        payload.Should().NotContain("krátké věty"); // raw note text MUST NOT appear in payload
    }

    [Fact]
    public async Task ExecuteAsync_RecorderPayload_HasLanguageNoteFalseWhenAbsent()
    {
        ArticleGenerationStep? recordedStep = null;
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
            .Callback<ArticleGenerationStep, CancellationToken>((s, _) => recordedStep = s)
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "{\"article_title\":\"T\",\"article_html\":\"<p>x</p>\",\"sources_used\":[]}")]));

        var step = new WriteArticleStep(
            _chat.Object,
            Options.Create(_options),
            NullLogger<WriteArticleStep>.Instance,
            new PipelineStepRecorder(repo.Object));

        var context = CreateContext();
        context.Article.Scope = "overview";
        context.Article.LanguageNote = "   ";

        await step.ExecuteAsync(context, default);

        recordedStep!.InputJson.Should().Contain("\"hasLanguageNote\":false");
    }
}
