using System.Net;
using Anela.Heblo.Adapters.Comgate;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Abo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Polly;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public class ComgateBankClientTests
{
    private readonly ComgateSettings _settings;
    private readonly Mock<IOptions<ComgateSettings>> _optionsMock;
    private readonly Mock<ILogger<ComgateBankClient>> _loggerMock;
    private readonly HttpClient _httpClient;

    public ComgateBankClientTests()
    {
        _settings = new ComgateSettings
        {
            MerchantId = "test-merchant-123",
            Secret = "test-secret-456"
        };

        _optionsMock = new Mock<IOptions<ComgateSettings>>();
        _optionsMock.Setup(x => x.Value).Returns(_settings);
        _loggerMock = new Mock<ILogger<ComgateBankClient>>();
        _httpClient = new HttpClient();
    }

    private ComgateBankClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new ComgateBankClient(httpClient, _optionsMock.Object, _loggerMock.Object, ResiliencePipeline.Empty);
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var client = new ComgateBankClient(_httpClient, _optionsMock.Object, _loggerMock.Object);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Provider_ReturnsComgate()
    {
        var client = new ComgateBankClient(_httpClient, _optionsMock.Object, _loggerMock.Object);
        Assert.Equal(BankClientProvider.Comgate, client.Provider);
    }

    [Fact]
    public async Task GetStatementAsync_When521Response_ThrowsPaymentGatewayUnavailableException()
    {
        // Arrange
        var handler = new FakeHttpHandler(() => new HttpResponseMessage((HttpStatusCode)521));
        var client = CreateClient(handler);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => client.GetStatementAsync("transfer-123"));

        Assert.Equal(521, ex.StatusCode);
    }

    [Fact]
    public async Task GetStatementAsync_WhenServerError500_ThrowsPaymentGatewayUnavailableException()
    {
        // Arrange
        var handler = new FakeHttpHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => client.GetStatementAsync("transfer-123"));

        Assert.Equal(500, ex.StatusCode);
    }

    [Fact]
    public async Task GetStatementAsync_WhenClientError404_DoesNotThrowPaymentGatewayUnavailableException()
    {
        // Arrange
        var handler = new FakeHttpHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        // Act & Assert — should throw HttpRequestException, NOT PaymentGatewayUnavailableException
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetStatementAsync("transfer-123"));

        Assert.NotNull(ex);
    }

    [Fact]
    public void PaymentGatewayUnavailableException_StoresStatusCode()
    {
        var ex = new PaymentGatewayUnavailableException("gateway down", 521);

        Assert.Equal(521, ex.StatusCode);
        Assert.Contains("521", ex.Message);
    }

    [Fact]
    public void PaymentGatewayUnavailableException_WithoutStatusCode_HasNullStatusCode()
    {
        var ex = new PaymentGatewayUnavailableException("circuit breaker open");

        Assert.Null(ex.StatusCode);
    }

    [Fact]
    public void AboFile_Parse_WithValidData_ParsesCorrectly()
    {
        // Arrange
        var aboData = @"Header line with account info
Line1: Transaction 1
Line2: Transaction 2
Line3: Transaction 3";

        // Act
        var aboFile = AboFile.Parse(aboData);

        // Assert
        Assert.NotNull(aboFile.Header);
        Assert.Equal("Header line with account info", aboFile.Header.Raw);
        Assert.Equal(3, aboFile.Lines.Count);
        Assert.Equal("Line1: Transaction 1", aboFile.Lines[0].Raw);
        Assert.Equal("Line2: Transaction 2", aboFile.Lines[1].Raw);
        Assert.Equal("Line3: Transaction 3", aboFile.Lines[2].Raw);
    }

    [Fact]
    public void AboFile_Parse_WithEmptyLines_SkipsEmptyLines()
    {
        // Arrange
        var aboData = @"Header line

Line1: Transaction 1

Line2: Transaction 2

";

        // Act
        var aboFile = AboFile.Parse(aboData);

        // Assert
        Assert.Equal("Header line", aboFile.Header.Raw);
        Assert.Equal(2, aboFile.Lines.Count);
        Assert.Equal("Line1: Transaction 1", aboFile.Lines[0].Raw);
        Assert.Equal("Line2: Transaction 2", aboFile.Lines[1].Raw);
    }

    [Fact]
    public void AboFile_Parse_WithDifferentLineEndings_HandlesCorrectly()
    {
        // Arrange - Test different line ending combinations
        var aboDataUnix = "Header\nLine1\nLine2";
        var aboDataWindows = "Header\r\nLine1\r\nLine2";
        var aboDataMixed = "Header\r\nLine1\nLine2\r\n";

        // Act
        var aboUnix = AboFile.Parse(aboDataUnix);
        var aboWindows = AboFile.Parse(aboDataWindows);
        var aboMixed = AboFile.Parse(aboDataMixed);

        // Assert
        Assert.Equal(2, aboUnix.Lines.Count);
        Assert.Equal(2, aboWindows.Lines.Count);
        Assert.Equal(2, aboMixed.Lines.Count);

        Assert.Equal("Line1", aboUnix.Lines[0].Raw);
        Assert.Equal("Line1", aboWindows.Lines[0].Raw);
        Assert.Equal("Line1", aboMixed.Lines[0].Raw);
    }

    [Fact]
    public void AboFile_Parse_WithOnlyHeader_ReturnsEmptyLines()
    {
        // Arrange
        var aboData = "Only header line";

        // Act
        var aboFile = AboFile.Parse(aboData);

        // Assert
        Assert.Equal("Only header line", aboFile.Header.Raw);
        Assert.Empty(aboFile.Lines);
    }

    [Fact]
    public void AboFile_Parse_WithEmptyString_HandlesGracefully()
    {
        // Arrange
        var aboData = "";

        // Act
        var aboFile = AboFile.Parse(aboData);

        // Assert
        Assert.Equal(string.Empty, aboFile.Header.Raw);
        Assert.Empty(aboFile.Lines);
    }

    [Fact]
    public void AboLine_Constructor_WithValidLine_StoresRawLine()
    {
        // Arrange
        var rawLine = "Sample transaction line";

        // Act
        var aboLine = new AboLine(rawLine);

        // Assert
        Assert.Equal(rawLine, aboLine.Raw);
    }

    [Fact]
    public void AboLine_Constructor_WithNullLine_HandlesGracefully()
    {
        // Act
        var aboLine = new AboLine(null);

        // Assert
        Assert.Equal(string.Empty, aboLine.Raw);
    }

    [Fact]
    public void AboHeader_Constructor_WithValidHeader_StoresRawHeader()
    {
        // Arrange
        var headerLine = "Sample header line";

        // Act
        var aboHeader = new AboHeader(headerLine);

        // Assert
        Assert.Equal(headerLine, aboHeader.Raw);
    }

    [Fact]
    public void AboHeader_Constructor_WithEmptyHeader_HandlesGracefully()
    {
        // Act
        var aboHeader = new AboHeader();

        // Assert
        Assert.Equal(string.Empty, aboHeader.Raw);
    }
}

internal class FakeHttpHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(responseFactory());
}