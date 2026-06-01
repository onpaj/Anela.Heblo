using Anela.Heblo.Adapters.Cups;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Models.Responses;
using SharpIpp.Protocol.Models;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CupsPrintingServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly Mock<ISharpIppClient> _sharpIppClient = new();

    public CupsPrintingServiceTests()
    {
        Directory.CreateDirectory(_tempDir);

        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTempPdf(string name = "test.pdf")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]); // %PDF header
        return path;
    }

    private CupsPrintingService CreateService(string serverUrl = "http://cups.internal:631", string printerName = "default-printer") =>
        new CupsPrintingService(
            _sharpIppClient.Object,
            Options.Create(new CupsOptions
            {
                ServerUrl = serverUrl,
                PrinterName = printerName,
                Username = "admin",
                Password = "secret"
            }),
            NullLogger<CupsPrintingService>.Instance);

    [Fact]
    public async Task PrintAsync_ValidFile_SendsPrintJobWithCorrectPrinterUri()
    {
        // Arrange
        var file = CreateTempPdf();
        PrintJobRequest? captured = null;
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrintJobRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });

        var svc = CreateService(serverUrl: "http://cups.internal:631", printerName: "default-printer");

        // Act
        await svc.PrintAsync(file, printerName: "my-printer");

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("http://cups.internal:631/printers/my-printer", captured.OperationAttributes.PrinterUri.ToString());
    }

    [Fact]
    public async Task PrintAsync_ValidFile_SendsDocumentFormatAsPdf()
    {
        // Arrange
        var file = CreateTempPdf();
        PrintJobRequest? captured = null;
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrintJobRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });

        var svc = CreateService();

        // Act
        await svc.PrintAsync(file, printerName: "my-printer");

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("application/pdf", captured.OperationAttributes.DocumentFormat);
    }

    [Fact]
    public async Task PrintAsync_NullPrinterName_FallsBackToConfiguredDefault()
    {
        // Arrange
        var file = CreateTempPdf();
        PrintJobRequest? captured = null;
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrintJobRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.SuccessfulOk });

        var svc = CreateService(serverUrl: "http://cups.internal:631", printerName: "fallback-printer");

        // Act
        await svc.PrintAsync(file, printerName: null);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("http://cups.internal:631/printers/fallback-printer", captured.OperationAttributes.PrinterUri.ToString());
    }

    [Fact]
    public async Task PrintAsync_EmptyServerUrl_ThrowsInvalidOperationException()
    {
        // Arrange
        var file = CreateTempPdf();
        var svc = CreateService(serverUrl: "");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file));
    }

    [Fact]
    public async Task PrintAsync_NullPrinterNameAndNoFallback_ThrowsInvalidOperationException()
    {
        // Arrange
        var file = CreateTempPdf();
        var svc = CreateService(printerName: ""); // no fallback

        // Act & Assert: explicit null
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file, printerName: null));
    }

    [Fact]
    public async Task PrintAsync_EmptyStringPrinterNameAndNoFallback_ThrowsInvalidOperationException()
    {
        // Arrange
        var file = CreateTempPdf();
        var svc = CreateService(printerName: ""); // no fallback

        // Act & Assert: empty string
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file, printerName: ""));
    }

    [Fact]
    public async Task PrintAsync_IppErrorStatus_ThrowsInvalidOperationExceptionWithStatusCode()
    {
        // Arrange
        var file = CreateTempPdf();
        _sharpIppClient
            .Setup(x => x.PrintJobAsync(It.IsAny<PrintJobRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJobResponse { StatusCode = IppStatusCode.ClientErrorNotFound });

        var svc = CreateService();

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PrintAsync(file));

        // Assert: status code value appears in message
        Assert.Contains("ClientErrorNotFound", ex.Message);
    }
}
