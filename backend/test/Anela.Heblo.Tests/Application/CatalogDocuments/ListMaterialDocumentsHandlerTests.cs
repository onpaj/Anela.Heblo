using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.ListMaterialDocuments;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class ListMaterialDocumentsHandlerTests
{
    private readonly Mock<ICatalogDocumentsStorage> _storageMock = new();

    private static IOptions<CatalogDocumentsOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CatalogDocumentsOptions
        {
            Materials = new CatalogDocumentsDriveOptions
            {
                DriveId = "drive-id",
                BasePath = "/Materials/Documents"
            },
            PIF = new CatalogDocumentsDriveOptions
            {
                DriveId = "drive-id-pif",
                BasePath = "/PIF/Documents"
            }
        });

    private ListMaterialDocumentsHandler CreateSut() =>
        new(_storageMock.Object, Options(), NullLogger<ListMaterialDocumentsHandler>.Instance);

    [Fact]
    public async Task Handle_ReturnsFolderNotFound_WhenStorageReturnsNotFound()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.NotFound });

        // Act
        var result = await CreateSut().Handle(
            new ListMaterialDocumentsRequest { ProductCode = "MAT001" }, CancellationToken.None);

        // Assert
        result.FolderStatus.Should().Be(FolderStatus.NotFound);
        result.ExpectedPrefix.Should().Be("MAT001__");
        result.Files.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsFiles_WhenFolderFound()
    {
        // Arrange
        var folderResult = new FolderSearchResult { Status = FolderStatus.Found, FolderId = "folder-123" };
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), "MAT001__", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(folderResult);

        var files = new List<CatalogDocumentDto>
        {
            new() { Name = "COA__L001__Bisabolol.pdf", WebUrl = "https://sp.example.com/file1.pdf", SizeBytes = 1024 }
        };
        _storageMock
            .Setup(s => s.ListFilesAsync("drive-id", "folder-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(files);

        // Act
        var result = await CreateSut().Handle(
            new ListMaterialDocumentsRequest { ProductCode = "MAT001" }, CancellationToken.None);

        // Assert
        result.FolderStatus.Should().Be(FolderStatus.Found);
        result.Files.Should().HaveCount(1);
        result.Files[0].Name.Should().Be("COA__L001__Bisabolol.pdf");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsMultipleMatches_WhenStorageReturnsMultiple()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.MultipleMatches });

        // Act
        var result = await CreateSut().Handle(
            new ListMaterialDocumentsRequest { ProductCode = "MAT001" }, CancellationToken.None);

        // Assert
        result.FolderStatus.Should().Be(FolderStatus.MultipleMatches);
        result.Files.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_PassesCorrectPrefixAndDriveId()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.NotFound });

        // Act
        await CreateSut().Handle(new ListMaterialDocumentsRequest { ProductCode = "ABC123" }, CancellationToken.None);

        // Assert — verify exact prefix and driveId were passed
        _storageMock.Verify(s => s.FindFolderAsync(
            "drive-id", "/Materials/Documents", "ABC123__", false, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
