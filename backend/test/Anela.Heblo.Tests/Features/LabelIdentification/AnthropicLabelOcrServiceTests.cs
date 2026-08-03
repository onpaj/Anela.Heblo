using Anela.Heblo.Application.Features.LabelIdentification;
using Anela.Heblo.Application.Features.LabelIdentification.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SkiaSharp;
using Xunit;

namespace Anela.Heblo.Tests.Features.LabelIdentification;

public class AnthropicLabelOcrServiceTests
{
    private readonly Mock<IChatClient> _chatClient = new();

    private AnthropicLabelOcrService CreateService() => new(
        _chatClient.Object,
        Options.Create(new LabelIdentificationOptions()),
        NullLogger<AnthropicLabelOcrService>.Instance);

    private static Stream JpegPhoto(int width = 4000, int height = 3000)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return new MemoryStream(data.ToArray());
    }

    private void SetupResponse(string text) =>
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task Returns_the_ingredient_line_from_the_model()
    {
        SetupResponse("Tocopherol, Limonene, Linalool");

        var result = await CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        result.Should().Be("Tocopherol, Limonene, Linalool");
    }

    [Fact]
    public async Task Sends_the_photo_as_image_content()
    {
        SetupResponse("Tocopherol");
        IEnumerable<ChatMessage>? captured = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m.ToList())
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Tocopherol")]));

        await CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SelectMany(m => m.Contents).OfType<DataContent>()
            .Should().ContainSingle(c => c.MediaType == "image/jpeg");
    }

    [Fact]
    public async Task Downscales_the_photo_to_the_configured_longest_edge()
    {
        SetupResponse("Tocopherol");
        IEnumerable<ChatMessage>? captured = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((m, _, _) => captured = m.ToList())
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Tocopherol")]));

        await CreateService().ReadIngredientsAsync(JpegPhoto(4000, 3000), CancellationToken.None);

        var sent = captured!.SelectMany(m => m.Contents).OfType<DataContent>().Single();
        using var decoded = SKBitmap.Decode(sent.Data.ToArray());
        Math.Max(decoded.Width, decoded.Height).Should().Be(2048);
    }

    [Fact]
    public async Task Throws_LabelOcrException_when_the_image_cannot_be_decoded()
    {
        var service = CreateService();
        using var garbage = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5 });

        var act = () => service.ReadIngredientsAsync(garbage, CancellationToken.None);

        await act.Should().ThrowAsync<LabelOcrException>();
    }

    [Fact]
    public async Task Throws_LabelOcrException_when_the_photo_dimensions_exceed_the_pixel_limit()
    {
        var service = CreateService();
        // 10000 x 5001 = 50,010,000 px, just over the 50,000,000 cap — well under any byte
        // size limit, which is exactly the attack this guards against (a highly
        // compressible image with attacker-controlled huge dimensions).
        using var oversized = JpegPhoto(10000, 5001);

        var act = () => service.ReadIngredientsAsync(oversized, CancellationToken.None);

        await act.Should().ThrowAsync<LabelOcrException>();
    }

    [Fact]
    public async Task Returns_empty_when_the_model_returns_nothing()
    {
        SetupResponse("   ");

        var result = await CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_swallow_transport_failures()
    {
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream down"));

        var act = () => CreateService().ReadIngredientsAsync(JpegPhoto(), CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
