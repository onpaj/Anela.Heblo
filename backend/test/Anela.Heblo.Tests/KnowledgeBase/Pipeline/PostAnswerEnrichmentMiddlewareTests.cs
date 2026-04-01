using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class PostAnswerEnrichmentMiddlewareTests
{
    private readonly Mock<IChatClient> _inner = new();
    private readonly Mock<IProductEnrichmentCache> _cache = new();

    private PostAnswerEnrichmentMiddleware Create() =>
        new(_inner.Object, _cache.Object);

    private void SetupInner(string responseText)
    {
        _inner
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
    }

    private void SetupCache(params (string code, string name, string? url)[] entries)
    {
        var dict = entries.ToDictionary(
            e => e.code,
            e => new ProductEnrichmentEntry { ProductCode = e.code, ProductName = e.name, Url = e.url });
        _cache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);
    }

    [Fact]
    public async Task GetResponseAsync_CodeWithUrl_ReplacesWithMarkdownLink()
    {
        SetupInner("Doporučujeme aplikovat (AKL001) na čistou pleť.");
        SetupCache(("AKL001", "Sérum ABC", "https://anela.cz/produkty/serum-abc"));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(
            "Doporučujeme aplikovat [Sérum ABC (AKL001)](https://anela.cz/produkty/serum-abc) na čistou pleť.",
            result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_CodeWithoutUrl_ReplacesWithPlainText()
    {
        SetupInner("Použijte (KRM002) ráno i večer.");
        SetupCache(("KRM002", "Hydratační krém", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal("Použijte Hydratační krém (KRM002) ráno i večer.", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_CodeNotInCatalog_LeavesTokenUnchanged()
    {
        SetupInner("Odpověď obsahuje (UNKNOWN) token.");
        SetupCache(("AKL001", "Sérum ABC", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal("Odpověď obsahuje (UNKNOWN) token.", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_MultipleCodesInAnswer_AllReplaced()
    {
        SetupInner("Použijte (AKL001) a poté (KRM002).");
        SetupCache(
            ("AKL001", "Sérum ABC", "https://anela.cz/serum"),
            ("KRM002", "Hydratační krém", "https://anela.cz/krem"));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(
            "Použijte [Sérum ABC (AKL001)](https://anela.cz/serum) a poté [Hydratační krém (KRM002)](https://anela.cz/krem).",
            result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_NoCodesInAnswer_TextUnchanged()
    {
        const string text = "Tato odpověď neobsahuje žádný kód produktu.";
        SetupInner(text);
        SetupCache(("AKL001", "Sérum ABC", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(text, result.Text);
    }
}
