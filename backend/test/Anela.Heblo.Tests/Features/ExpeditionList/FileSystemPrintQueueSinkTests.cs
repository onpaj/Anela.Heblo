using Anela.Heblo.Application.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class FileSystemPrintQueueSinkTests : IDisposable
{
    private readonly string _sourceDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public FileSystemPrintQueueSinkTests()
    {
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, recursive: true);
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, recursive: true);
    }

    private FileSystemPrintQueueSink CreateSink(string folder) =>
        new FileSystemPrintQueueSink(
            Options.Create(new PrintPickingListOptions { PrintQueueFolder = folder }),
            NullLogger<FileSystemPrintQueueSink>.Instance);

    [Fact]
    public async Task SendAsync_ValidFiles_CopiesFilesToOutputFolder()
    {
        // Arrange
        var file1 = Path.Combine(_sourceDir, "order1.pdf");
        var file2 = Path.Combine(_sourceDir, "order2.pdf");
        await File.WriteAllTextAsync(file1, "pdf1");
        await File.WriteAllTextAsync(file2, "pdf2");

        var sink = CreateSink(_outputDir);

        // Act
        await sink.SendAsync([file1, file2]);

        // Assert
        Assert.True(File.Exists(Path.Combine(_outputDir, "order1.pdf")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "order2.pdf")));
    }

    [Fact]
    public async Task SendAsync_OutputFolderDoesNotExist_CreatesItAndCopiesFiles()
    {
        // Arrange
        var file = Path.Combine(_sourceDir, "order.pdf");
        await File.WriteAllTextAsync(file, "pdf");
        var nonExistentFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var sink = CreateSink(nonExistentFolder);

        try
        {
            // Act
            await sink.SendAsync([file]);

            // Assert
            Assert.True(File.Exists(Path.Combine(nonExistentFolder, "order.pdf")));
        }
        finally
        {
            if (Directory.Exists(nonExistentFolder)) Directory.Delete(nonExistentFolder, recursive: true);
        }
    }

    [Fact]
    public async Task SendAsync_PrintQueueFolderNotConfigured_DoesNotThrow()
    {
        // Arrange
        var file = Path.Combine(_sourceDir, "order.pdf");
        await File.WriteAllTextAsync(file, "pdf");
        var sink = CreateSink(string.Empty);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => sink.SendAsync([file]));
        Assert.Null(exception);
    }
}
