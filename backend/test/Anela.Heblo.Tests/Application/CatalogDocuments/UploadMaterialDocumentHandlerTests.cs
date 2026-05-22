using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Infrastructure;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using Anela.Heblo.Application.Features.CatalogDocuments.UseCases.UploadMaterialDocument;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public class UploadMaterialDocumentHandlerTests
{
    private readonly Mock<ICatalogDocumentsStorage> _storageMock = new();

    private static IOptions<CatalogDocumentsOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new CatalogDocumentsOptions
        {
            Materials = new CatalogDocumentsDriveOptions { DriveId = "drive-id", BasePath = "/Materials/Documents" },
            PIF = new CatalogDocumentsDriveOptions { DriveId = "pif-drive", BasePath = "/PIF/Documents" }
        });

    private UploadMaterialDocumentHandler CreateSut() =>
        new(_storageMock.Object, Options(), NullLogger<UploadMaterialDocumentHandler>.Instance);

    [Fact]
    public async Task Handle_UploadAsIs_SkipsValidationAndUploadsWithOriginalFilename()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), "MAT001__", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.Found, FolderId = "f1" });
        _storageMock
            .Setup(s => s.UploadFileAsync(It.IsAny<string>(), "f1", "test.pdf", It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test.pdf");

        var request = new UploadMaterialDocumentRequest
        {
            ProductCode = "MAT001",
            OriginalFilename = "test.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            FileStream = Stream.Null,
            DocumentTypeCode = string.Empty,
            Lot = string.Empty,
            CommonName = "Test",
            UploadAsIs = true,
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.UploadedFilename.Should().Be("test.pdf");
        _storageMock.Verify(s => s.FindFolderAsync(
            "drive-id", "/Materials/Documents", "MAT001__", false, It.IsAny<CancellationToken>()),
            Times.Once);
        _storageMock.Verify(s => s.UploadFileAsync(
            "drive-id", "f1", "test.pdf", It.IsAny<Stream>(), "application/pdf", 1024, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // NOTE: MaterialDocumentTypes.All is currently empty; any type code fails validation.
    // This test will remain correct when types are populated because "UNKNOWN_TYPE" will
    // still not be in the list. Add a complementary test for a valid type code once types are seeded.
    [Fact]
    public async Task Handle_ReturnsError_WhenTypeCodeUnknownAndNotUploadAsIs()
    {
        // Arrange — no storage setup needed; validation fails before any storage call

        var request = new UploadMaterialDocumentRequest
        {
            ProductCode = "MAT001",
            OriginalFilename = "test.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            FileStream = Stream.Null,
            DocumentTypeCode = "UNKNOWN_TYPE",
            Lot = string.Empty,
            CommonName = "Test",
            UploadAsIs = false,
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CatalogDocumentInvalidTypeCode);
        _storageMock.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _storageMock.Verify(s => s.FindFolderAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundError_WhenFolderMissing()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.NotFound });

        var request = new UploadMaterialDocumentRequest
        {
            ProductCode = "MAT001",
            OriginalFilename = "test.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            FileStream = Stream.Null,
            DocumentTypeCode = string.Empty,
            Lot = string.Empty,
            CommonName = "Test",
            UploadAsIs = true,
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CatalogDocumentFolderNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsMultipleMatchesError_WhenFolderHasMultipleMatches()
    {
        // Arrange
        _storageMock
            .Setup(s => s.FindFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FolderSearchResult { Status = FolderStatus.MultipleMatches });

        var request = new UploadMaterialDocumentRequest
        {
            ProductCode = "MAT001",
            OriginalFilename = "test.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            FileStream = Stream.Null,
            DocumentTypeCode = string.Empty,
            Lot = string.Empty,
            CommonName = "Test",
            UploadAsIs = true,
        };

        // Act
        var result = await CreateSut().Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.CatalogDocumentFolderMultipleMatches);
        _storageMock.Verify(s => s.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
