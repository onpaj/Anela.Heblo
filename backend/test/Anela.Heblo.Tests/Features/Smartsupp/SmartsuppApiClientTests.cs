using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.Smartsupp;
using Anela.Heblo.Domain.Features.Smartsupp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Polly;
using Xunit;

namespace Anela.Heblo.Tests.Features.Smartsupp;

public class SmartsuppApiClientTests
{
    private static SmartsuppApiClient CreateClient(
        HttpMessageHandler handler,
        ResiliencePipeline? pipeline = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.smartsupp.com/v2/") };
        factory.Setup(f => f.CreateClient("Smartsupp")).Returns(httpClient);

        var options = Options.Create(new SmartsuppOptions
        {
            ApiToken = "test-token",
            BaseUrl = "https://api.smartsupp.com/v2/",
        });

        return new SmartsuppApiClient(options, factory.Object, NullLogger<SmartsuppApiClient>.Instance, pipeline);
    }

    [Fact]
    public async Task SearchConversationsAsync_ReturnsItems_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            total = 2,
            after = (string?)null,
            items = new[]
            {
                new
                {
                    id = "coXU9u5VscuzW",
                    ext_id = (string?)null,
                    status = "open",
                    unread = true,
                    created_at = "2026-05-12T18:29:21.336Z",
                    updated_at = "2026-05-12T18:38:15.826Z",
                    finished_at = (string?)null,
                    channel = new { type = "default", id = (string?)null },
                    contact_id = "ctW5HHbqaRKv",
                    visitor_id = "vitCESEI6Lu-SL",
                    agent_ids = Array.Empty<string>(),
                    assigned_ids = Array.Empty<string>(),
                    group_id = (string?)null,
                    rating_value = (int?)null,
                    rating_text = (string?)null,
                    domain = "www.anela.cz",
                    referer = "https://l.facebook.com/",
                    is_offline = true,
                    is_served = false,
                    variables = new { shoptet_shop = "269953", authenticated = true },
                    location = new { ip = "78.102.94.30", code = "CZ", country = "Czechia", city = "Prague" },
                    last_message = new { text = "Dobrý den", created_at = "2026-05-12T18:30:58Z" }
                }
            }
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.SearchConversationsAsync(null, 50, CancellationToken.None);

        // Assert
        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(1);
        var item = result.Items[0];
        item.Id.Should().Be("coXU9u5VscuzW");
        item.ContactId.Should().Be("ctW5HHbqaRKv");
        item.VisitorId.Should().Be("vitCESEI6Lu-SL");
        item.Domain.Should().Be("www.anela.cz");
        item.Referer.Should().Be("https://l.facebook.com/");
        item.IsOffline.Should().BeTrue();
        item.IsServed.Should().BeFalse();
        item.LocationCountry.Should().Be("Czechia");
        item.LocationCity.Should().Be("Prague");
        item.LocationIp.Should().Be("78.102.94.30");
        item.LocationCode.Should().Be("CZ");
        item.ChannelType.Should().Be("default");
        item.Unread.Should().BeTrue();
        item.VariablesJson.Should().NotBeNullOrEmpty();
        item.LastMessageText.Should().Be("Dobrý den");
    }

    [Fact]
    public async Task SearchConversationsAsync_ThrowsHttpRequestException_On429()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.SearchConversationsAsync(null, 50, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task GetConversationMessagesAsync_ReturnsMessages_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            total = 2,
            items = new[]
            {
                new
                {
                    id = "msCfnmmaEDXAs",
                    ext_id = (string?)null,
                    created_at = "2026-05-12T18:30:58.499Z",
                    updated_at = "2026-05-12T18:30:59.320Z",
                    type = "message",
                    sub_type = "contact",
                    conversation_id = "coXU9u5VscuzW",
                    visitor_id = (string?)null,
                    agent_id = (string?)null,
                    content = new { type = "text", text = "Dobry den, jaky krem doporucite?" },
                    trigger_id = (string?)null,
                    trigger_name = (string?)null,
                    is_reply = false,
                    is_first_reply = false,
                    is_offline = false,
                    is_offline_reply = false,
                    response_time = (int?)null,
                    attachments = Array.Empty<object>(),
                    page_url = "https://www.anela.cz/"
                },
                new
                {
                    id = "msJZcgRsWzE4n",
                    ext_id = (string?)null,
                    created_at = "2026-05-12T18:29:28.055Z",
                    updated_at = "2026-05-12T18:30:59.283Z",
                    type = "message",
                    sub_type = "bot",
                    conversation_id = "coXU9u5VscuzW",
                    visitor_id = (string?)null,
                    agent_id = (string?)null,
                    content = new { type = "text", text = "Momentálně nejsme on-line." },
                    trigger_id = "bolCGGiw7mLz",
                    trigger_name = "2_Jsme offline_druhá zpráva",
                    is_reply = false,
                    is_first_reply = false,
                    is_offline = false,
                    is_offline_reply = false,
                    response_time = (int?)null,
                    attachments = Array.Empty<object>(),
                    page_url = "https://www.anela.cz/"
                }
            }
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetConversationMessagesAsync("coXU9u5VscuzW", CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        var contactMsg = result[0];
        contactMsg.Id.Should().Be("msCfnmmaEDXAs");
        contactMsg.SubType.Should().Be("contact");
        contactMsg.Content.Should().Be("Dobry den, jaky krem doporucite?");
        contactMsg.PageUrl.Should().Be("https://www.anela.cz/");
        contactMsg.ConversationId.Should().Be("coXU9u5VscuzW");

        var botMsg = result[1];
        botMsg.Id.Should().Be("msJZcgRsWzE4n");
        botMsg.SubType.Should().Be("bot");
        botMsg.TriggerName.Should().Be("2_Jsme offline_druhá zpráva");
        botMsg.TriggerId.Should().Be("bolCGGiw7mLz");
    }

    [Fact]
    public async Task GetConversationMessagesAsync_ThrowsHttpRequestException_OnErrorResponse()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.GetConversationMessagesAsync("conv-missing", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetContactAsync_ReturnsContact_WhenApiResponds()
    {
        // Arrange
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "ct297LB_vFeHN",
            created_at = "2026-05-09T14:40:49.044Z",
            updated_at = "2026-05-12T17:12:36.745Z",
            email = "vexy@post.cz",
            name = "Monča",
            phone = (string?)null,
            properties = new { },
            note = (string?)null,
            banned_at = (string?)null,
            banned_by = (string?)null,
            tags = new { type = "list", data = Array.Empty<object>(), total = 0 },
            gdpr_approved = false
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetContactAsync("ct297LB_vFeHN", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("ct297LB_vFeHN");
        result.Email.Should().Be("vexy@post.cz");
        result.Name.Should().Be("Monča");
        result.Phone.Should().BeNull();
        result.GdprApproved.Should().BeFalse();
        result.TagsJson.Should().NotBeNullOrEmpty();
        result.PropertiesJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetContactAsync_ReturnsNull_On404()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.GetContactAsync("ct-missing", CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetContactAsync_ThrowsHttpRequestException_On500()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        var act = () => client.GetContactAsync("ct-error", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task SendMessageAsync_ReturnsMessageData_WhenApiResponds()
    {
        // Arrange — response shape must match the actual Smartsupp v2 POST /conversations/{id}/messages response
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "msNewMessage123",
            created_at = "2026-05-20T10:00:00Z",
            type = "message",
            sub_type = "agent",
        });

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r =>
                    r.Method == HttpMethod.Post &&
                    r.RequestUri!.PathAndQuery.Contains("conversations/conv123/messages")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        var result = await client.SendMessageAsync("conv123", "Dobrý den!", "agt-ondra", CancellationToken.None);

        // Assert
        result.Id.Should().Be("msNewMessage123");
        result.CreatedAt.Should().Be(new DateTime(2026, 5, 20, 10, 0, 0));
    }

    [Fact]
    public async Task SendMessageAsync_SendsAgentId_AndNeverIncludesAgentBlock()
    {
        // Regression: Smartsupp returns 422 "agent_id is required when sub_type is \"agent\""
        // if we include the `agent` block. We send agent_id only (the agent name is
        // resolved by Smartsupp from the agent profile).
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = "m1", created_at = "2026-05-20T10:00:00Z" }),
                    Encoding.UTF8,
                    "application/json"),
            });

        var client = CreateClient(handler.Object);

        await client.SendMessageAsync("conv123", "Ahoj", "agt-123", CancellationToken.None);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"agent_id\":\"agt-123\"");
        capturedBody.Should().NotContain("\"agent\":");
        capturedBody.Should().NotContain("\"name\":");
    }

    [Fact]
    public async Task SendMessageAsync_OmitsAgentId_WhenCallerPassesNull()
    {
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { id = "m1", created_at = "2026-05-20T10:00:00Z" }),
                    Encoding.UTF8,
                    "application/json"),
            });

        var client = CreateClient(handler.Object);

        await client.SendMessageAsync("conv123", "Ahoj", null, CancellationToken.None);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().NotContain("agent_id");
        capturedBody.Should().NotContain("\"agent\":");
    }

    [Fact]
    public async Task SendMessageAsync_ThrowsHttpRequestException_OnErrorResponse()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        var act = () => client.SendMessageAsync("conv123", "Text", "agt-1", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
