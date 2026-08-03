using Anela.Heblo.Application.Features.LabelIdentification;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class IdentifyLabelHandlerTests
{
    private readonly Mock<ILabelOcrService> _ocr = new();
    private readonly Mock<ICatalogRepository> _catalog = new();
    private readonly LabelReferenceIndex _index = new();
    private readonly Dictionary<string, CatalogAggregate> _catalogEntries = new();

    public IdentifyLabelHandlerTests()
    {
        // Default: no catalogue entries. Individual tests add entries via SetupCatalogName;
        // the single GetByIdsAsync stub always reflects the current contents of _catalogEntries.
        _catalog.Setup(c => c.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _catalogEntries);
    }

    private IdentifyLabelHandler CreateHandler()
    {
        var matcher = new LabelMatcher(_index, Options.Create(new LabelIdentificationOptions()));
        return new IdentifyLabelHandler(
            _ocr.Object, matcher, _catalog.Object, NullLogger<IdentifyLabelHandler>.Instance);
    }

    private string TextFor(string family) => _index.Entries.Single(e => e.Family == family).Normalized;

    private static IdentifyLabelRequest RequestWithPhoto() => new()
    {
        PhotoStream = new MemoryStream(new byte[] { 1, 2, 3 }),
        ContentType = "image/jpeg",
        SizeBytes = 3,
    };

    private void SetupCatalogName(string code, string name) =>
        _catalogEntries[code] = new CatalogAggregate { ProductCode = code, ProductName = name };

    [Fact]
    public async Task Auto_decision_returns_the_matched_family_with_resolved_product_names()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextFor("KRE005"));
        SetupCatalogName("KRE005015", "Masážní olej 15 ml");
        SetupCatalogName("KRE005030", "Masážní olej 30 ml");

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Decision.Should().Be(LabelMatchDecision.Auto);
        response.Candidates[0].Family.Should().Be("KRE005");
        response.Candidates[0].Variants.Should().HaveCount(2);
        response.Candidates[0].Variants.Select(v => v.ProductName)
            .Should().BeEquivalentTo(new[] { "Masážní olej 15 ml", "Masážní olej 30 ml" });
    }

    [Fact]
    public async Task Missing_catalog_entry_yields_an_empty_name_but_still_returns_the_code()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextFor("KRE005"));

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Candidates[0].Variants.Should().OnlyContain(v => v.ProductName == string.Empty);
        response.Candidates[0].Variants.Should().OnlyContain(v => v.ProductCode.StartsWith("KRE005"));
    }

    [Fact]
    public async Task Returns_the_raw_transcribed_text_for_troubleshooting()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Tocopherol, Limonene");

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.RawText.Should().Be("Tocopherol, Limonene");
    }

    [Fact]
    public async Task Empty_transcription_fails_with_LabelTextUnreadable()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelTextUnreadable);
    }

    [Fact]
    public async Task Undecodable_photo_fails_with_LabelPhotoUndecodable()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LabelOcrException("bad image"));

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelPhotoUndecodable);
    }

    [Fact]
    public async Task Upstream_failure_fails_with_LabelOcrServiceUnavailable()
    {
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream down"));

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelOcrServiceUnavailable);
    }

    [Fact]
    public async Task Garbage_transcription_returns_Low_as_a_successful_response()
    {
        // Low is a real answer, not an error — the UI shows a retry prompt with candidates.
        _ocr.Setup(o => o.ReadIngredientsAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("qqq www zzz nothing like an ingredient list");

        var response = await CreateHandler().Handle(RequestWithPhoto(), CancellationToken.None);

        response.Success.Should().BeTrue();
        response.Decision.Should().Be(LabelMatchDecision.Low);
    }
}
