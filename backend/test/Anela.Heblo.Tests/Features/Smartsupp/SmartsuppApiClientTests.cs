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

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Mock<HttpMessageHandler> CapturingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, List<HttpRequestMessage>? requests = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                requests?.Add(req);
                return respond(req);
            });
        return handler;
    }

    private const string SentJson = "{\"id\":\"m1\",\"created_at\":\"2026-05-20T10:00:00Z\"}";

    [Fact]
    public async Task GetUploadTokenAsync_GetsFilesUploadToken_WithBearer()
    {
        HttpMethod? method = null; string? url = null; string? auth = null;
        var handler = CapturingHandler(req =>
        {
            method = req.Method; url = req.RequestUri!.ToString(); auth = req.Headers.Authorization?.ToString();
            return Task.FromResult(Json("{\"token\":\"tok1\",\"url\":\"https://files.smartsupp.com/upload/tok1\"}"));
        });
        var client = CreateClient(handler.Object);

        var result = await client.GetUploadTokenAsync(CancellationToken.None);

        method.Should().Be(HttpMethod.Get);
        url.Should().Be("https://api.smartsupp.com/v2/files/upload/token");
        auth.Should().Be("Bearer test-token");
        result.Token.Should().Be("tok1");
        result.Url.Should().Be("https://files.smartsupp.com/upload/tok1");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"token\":\"x\"}")]
    public async Task GetUploadTokenAsync_MissingTokenOrUrl_ThrowsHttpRequestException(string body)
    {
        var handler = CapturingHandler(_ => Task.FromResult(Json(body)));
        var client = CreateClient(handler.Object);

        var act = () => client.GetUploadTokenAsync(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task UploadFileAsync_PostsMultipartFieldFile_WithoutAuthorization()
    {
        HttpMethod? method = null; string? url = null; bool hasAuth = true;
        string? partName = null; string? partFileName = null; string? partType = null; byte[]? partBytes = null;
        var handler = CapturingHandler(async req =>
        {
            method = req.Method; url = req.RequestUri!.ToString(); hasAuth = req.Headers.Authorization is not null;
            var multipart = (MultipartFormDataContent)req.Content!;
            var part = multipart.Single();
            partName = part.Headers.ContentDisposition!.Name!.Trim('"');
            partFileName = part.Headers.ContentDisposition.FileName!.Trim('"');
            partType = part.Headers.ContentType!.MediaType;
            partBytes = await part.ReadAsByteArrayAsync();
            return Json("{}");
        });
        var client = CreateClient(handler.Object);
        var bytes = new byte[] { 1, 2, 3, 4 };

        await client.UploadFileAsync("https://files.smartsupp.com/upload/tok1", "a.pdf", "application/pdf", bytes, CancellationToken.None);

        method.Should().Be(HttpMethod.Post);
        url.Should().Be("https://files.smartsupp.com/upload/tok1");
        hasAuth.Should().BeFalse();
        partName.Should().Be("file");
        partFileName.Should().Be("a.pdf");
        partType.Should().Be("application/pdf");
        partBytes.Should().Equal(bytes);
    }

    [Fact]
    public async Task UploadFileAsync_SendsRawUtf8Filename_OnTheWire()
    {
        string? wire = null;
        var handler = CapturingHandler(async req =>
        {
            wire = Encoding.UTF8.GetString(await req.Content!.ReadAsByteArrayAsync());
            return Json("{}");
        });
        var client = CreateClient(handler.Object);

        await client.UploadFileAsync("https://files.smartsupp.com/upload/tok1", "faktura č.1.pdf", "application/pdf", new byte[] { 1 }, CancellationToken.None);

        wire.Should().Contain("filename=\"faktura č.1.pdf\"");
        wire.Should().NotContain("=?utf-8?");
        wire.Should().NotContain("filename*=");
    }

    [Fact]
    public async Task UploadFileAsync_SanitizesQuotesAndControlCharsInFilename()
    {
        string? wire = null;
        var handler = CapturingHandler(async req =>
        {
            wire = Encoding.UTF8.GetString(await req.Content!.ReadAsByteArrayAsync());
            return Json("{}");
        });
        var client = CreateClient(handler.Object);

        await client.UploadFileAsync("https://files.smartsupp.com/upload/tok1", "a\"b\\c\r\nX-Evil: 1.pdf", "application/pdf", new byte[] { 1 }, CancellationToken.None);

        wire.Should().Contain("filename=\"a_b_c__X-Evil: 1.pdf\"");
        wire.Should().NotContain("\r\nX-Evil");
    }

    [Theory]
    [InlineData("https://evil.example/upload/x")]
    [InlineData("http://files.smartsupp.com/upload/x")]
    [InlineData("https://files.smartsupp.com.evil.example/upload/x")]
    [InlineData("/upload/x")]
    public async Task UploadFileAsync_RejectsNonSmartsuppUrl(string uploadUrl)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = CapturingHandler(_ => Task.FromResult(Json("{}")), requests);
        var client = CreateClient(handler.Object);

        var act = () => client.UploadFileAsync(uploadUrl, "a.pdf", "application/pdf", new byte[] { 1 }, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadFileAsync_ThrowsHttpRequestException_OnNon2xx()
    {
        var handler = CapturingHandler(_ => Task.FromResult(Json("{}", HttpStatusCode.BadRequest)));
        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        var act = () => client.UploadFileAsync("https://files.smartsupp.com/upload/t", "a.pdf", "application/pdf", new byte[] { 1 }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<HttpRequestException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendMessageAsync_WithAttachments_SendsUploadItems()
    {
        string? body = null;
        var handler = CapturingHandler(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return Json(SentJson);
        });
        var client = CreateClient(handler.Object);
        var attachments = new[]
        {
            new SmartsuppOutgoingAttachment { Token = "tok1", FileName = "a.pdf", FileType = "application/pdf" },
        };

        await client.SendMessageAsync("conv1", "Ahoj", "agt-1", attachments, CancellationToken.None);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        root.GetProperty("agent_id").GetString().Should().Be("agt-1");
        root.GetProperty("content").GetProperty("type").GetString().Should().Be("text");
        root.GetProperty("content").GetProperty("text").GetString().Should().Be("Ahoj");
        var item = root.GetProperty("attachments").EnumerateArray().Single();
        item.GetProperty("type").GetString().Should().Be("upload");
        item.GetProperty("token").GetString().Should().Be("tok1");
        item.GetProperty("file_name").GetString().Should().Be("a.pdf");
        item.GetProperty("file_type").GetString().Should().Be("application/pdf");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SendMessageAsync_WithAttachmentsAndEmptyText_OmitsContent(string? text)
    {
        string? body = null;
        var handler = CapturingHandler(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return Json(SentJson);
        });
        var client = CreateClient(handler.Object);
        var attachments = new[]
        {
            new SmartsuppOutgoingAttachment { Token = "tok1", FileName = "a.pdf", FileType = "application/pdf" },
        };

        await client.SendMessageAsync("conv1", text, "agt-1", attachments, CancellationToken.None);

        using var doc = JsonDocument.Parse(body!);
        doc.RootElement.TryGetProperty("content", out _).Should().BeFalse();
        doc.RootElement.GetProperty("attachments").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task CloseConversationAsync_SendsPatchToCloseSubresource()
    {
        // Arrange
        string? capturedUrl = null;
        string? capturedMethod = null;
        HttpContent? capturedContent = null;
        string? capturedAuth = null;

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.ToString();
                capturedMethod = req.Method.Method;
                capturedContent = req.Content;
                capturedAuth = req.Headers.Authorization?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object);

        // Act
        await client.CloseConversationAsync("conv-abc", CancellationToken.None);

        // Assert
        capturedUrl.Should().EndWith("conversations/conv-abc/close");
        capturedMethod.Should().Be("PATCH");
        capturedContent.Should().BeNull();
        capturedAuth.Should().StartWith("Bearer test-token");
    }

    [Fact]
    public async Task CloseConversationAsync_ThrowsHttpRequestException_OnNon2xx()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"error\":\"unavailable\"}", Encoding.UTF8, "application/json")
            });

        var client = CreateClient(handler.Object, ResiliencePipeline.Empty);

        // Act
        Func<Task> act = () => client.CloseConversationAsync("conv-abc", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
