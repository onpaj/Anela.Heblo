using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Anthropic;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.Anthropic;

public class AnthropicChatClientVisionTests
{
    private string? _capturedBody;

    private AnthropicChatClient CreateClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken _) =>
            {
                _capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"content":[{"type":"text","text":"Tocopherol, Limonene"}]}""",
                        Encoding.UTF8, "application/json"),
                };
            });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Anthropic"))
            .Returns(new HttpClient(handler.Object) { BaseAddress = new Uri("https://api.anthropic.com") });

        return new AnthropicChatClient(
            Options.Create(new AnthropicOptions { ApiKey = "test-key" }),
            factory.Object,
            NullLogger<AnthropicChatClient>.Instance);
    }

    private static ChatMessage PhotoMessage() =>
        new(ChatRole.User, new List<AIContent>
        {
            new DataContent(new byte[] { 1, 2, 3, 4 }, "image/jpeg"),
            new TextContent("Read the ingredients."),
        });

    [Fact]
    public async Task Serializes_image_content_as_an_anthropic_image_block()
    {
        var client = CreateClient();

        await client.GetResponseAsync(new[] { PhotoMessage() });

        using var doc = JsonDocument.Parse(_capturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.Array);
        var imageBlock = content.EnumerateArray().Single(b => b.GetProperty("type").GetString() == "image");
        var source = imageBlock.GetProperty("source");
        source.GetProperty("type").GetString().Should().Be("base64");
        source.GetProperty("media_type").GetString().Should().Be("image/jpeg");
        source.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public async Task Includes_the_accompanying_text_block_alongside_the_image()
    {
        var client = CreateClient();

        await client.GetResponseAsync(new[] { PhotoMessage() });

        using var doc = JsonDocument.Parse(_capturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
        var textBlock = content.EnumerateArray().Single(b => b.GetProperty("type").GetString() == "text");

        textBlock.GetProperty("text").GetString().Should().Be("Read the ingredients.");
    }

    [Fact]
    public async Task Text_only_messages_still_serialize_content_as_a_plain_string()
    {
        var client = CreateClient();

        await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hello") });

        using var doc = JsonDocument.Parse(_capturedBody!);
        var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

        content.ValueKind.Should().Be(JsonValueKind.String);
        content.GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task Returns_the_assistant_text_from_the_response()
    {
        var client = CreateClient();

        var response = await client.GetResponseAsync(new[] { PhotoMessage() });

        response.Messages[0].Text.Should().Be("Tocopherol, Limonene");
    }
}
