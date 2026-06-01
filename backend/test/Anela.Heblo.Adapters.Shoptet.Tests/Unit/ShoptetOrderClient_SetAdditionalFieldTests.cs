using System.Net;
using System.Text;
using System.Text.Json;
using Anela.Heblo.Adapters.ShoptetApi.Orders;
using FluentAssertions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Unit;

public class ShoptetOrderClient_SetAdditionalFieldTests
{
    private static HttpResponseMessage OkNullData() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":null,"errors":null}""", Encoding.UTF8, "application/json"),
        };

    private static (ShoptetOrderClient client, Mock<HttpMessageHandler> handler) BuildClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("https://test.myshoptet.com") };
        return (new ShoptetOrderClient(http), handler);
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_NullOrderCode_Throws()
    {
        var (client, _) = BuildClient();
        var act = () => client.SetAdditionalFieldAsync(null!, 1, "X", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_EmptyOrderCode_Throws()
    {
        var (client, _) = BuildClient();
        var act = () => client.SetAdditionalFieldAsync("", 1, "X", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(-1)]
    public async Task SetAdditionalFieldAsync_IndexOutOfRange_Throws(int index)
    {
        var (client, _) = BuildClient();
        var act = () => client.SetAdditionalFieldAsync("ABC001", index, "X", CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SetAdditionalFieldAsync_TextExceeds255OnLowIndex_Throws(int index)
    {
        var (client, _) = BuildClient();
        var longText = new string('A', 256);
        var act = () => client.SetAdditionalFieldAsync("ABC001", index, longText, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task SetAdditionalFieldAsync_TextExceeds255OnHighIndex_DoesNotThrow(int index)
    {
        var (client, handler) = BuildClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(OkNullData());

        var longText = new string('A', 256);
        var act = () => client.SetAdditionalFieldAsync("ABC001", index, longText, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_ValidArgs_PatchesCorrectUrl()
    {
        var (client, handler) = BuildClient();
        HttpRequestMessage? captured = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(OkNullData());

        await client.SetAdditionalFieldAsync("0012345678", 1, "CHLAZENE", CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Patch);
        captured.RequestUri!.PathAndQuery.Should().Be("/api/orders/0012345678/notes");
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_ValidArgs_SendsCorrectBody()
    {
        var (client, handler) = BuildClient();
        string? capturedBody = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkNullData());

        await client.SetAdditionalFieldAsync("0012345678", 1, "CHLAZENE", CancellationToken.None);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var fields = doc.RootElement
            .GetProperty("data")
            .GetProperty("additionalFields");
        fields.GetArrayLength().Should().Be(1);
        fields[0].GetProperty("index").GetInt32().Should().Be(1);
        fields[0].GetProperty("text").GetString().Should().Be("CHLAZENE");
    }

    [Fact]
    public async Task SetAdditionalFieldAsync_NullText_SendsNullInBody()
    {
        var (client, handler) = BuildClient();
        string? capturedBody = null;
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
                capturedBody = await req.Content!.ReadAsStringAsync())
            .ReturnsAsync(OkNullData());

        await client.SetAdditionalFieldAsync("0012345678", 1, null, CancellationToken.None);

        capturedBody.Should().NotBeNull();
        using var doc = JsonDocument.Parse(capturedBody!);
        var textElem = doc.RootElement
            .GetProperty("data")
            .GetProperty("additionalFields")[0]
            .GetProperty("text");
        textElem.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SetAdditionalFieldAsync_NonSuccessResponse_ThrowsHttpRequestException(HttpStatusCode status)
    {
        var (client, handler) = BuildClient();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"errors":[{"errorCode":"INVALID"}]}"""),
            });

        var act = () => client.SetAdditionalFieldAsync("0012345678", 1, "CHLAZENE", CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
