using Anela.Heblo.Adapters.Comgate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
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

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var client = new ComgateBankClient(_httpClient, _optionsMock.Object, _loggerMock.Object);

        // Assert
        Assert.NotNull(client);
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