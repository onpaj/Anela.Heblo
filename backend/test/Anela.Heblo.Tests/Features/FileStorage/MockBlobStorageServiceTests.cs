using System.Text;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class MockBlobStorageServiceTests
{
    private const string ContainerName = "expedition-lists";

    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ReturnsEmpty_WhenContainerDoesNotExist()
    {
        // Arrange
        var service = new MockBlobStorageService();

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ReturnsFirstSegments_WithoutTrailingSlash()
    {
        // Arrange
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "2026-03-24/list-001.pdf", "application/pdf");
        await service.UploadAsync(Bytes("b"), ContainerName, "2026-03-25/list-002.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("2026-03-24", result);
        Assert.Contains("2026-03-25", result);
        Assert.All(result, p => Assert.False(p.EndsWith('/')));
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_DeduplicatesPrefixes()
    {
        // Arrange
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "2026-03-24/list-001.pdf", "application/pdf");
        await service.UploadAsync(Bytes("b"), ContainerName, "2026-03-24/list-002.pdf", "application/pdf");
        await service.UploadAsync(Bytes("c"), ContainerName, "2026-03-24/list-003.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Single(result);
        Assert.Equal("2026-03-24", result[0]);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ExcludesLooseTopLevelBlobs()
    {
        // Arrange — mix loose top-level blobs and nested blobs.
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "readme.txt", "text/plain");
        await service.UploadAsync(Bytes("b"), ContainerName, "loose.pdf", "application/pdf");
        await service.UploadAsync(Bytes("c"), ContainerName, "2026-03-24/list.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert — only the nested blob contributes a prefix.
        Assert.Single(result);
        Assert.Equal("2026-03-24", result[0]);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_ReturnsOnlyFirstSegmentForDeepPaths()
    {
        // Arrange — deeper paths still only surface the first segment.
        var service = new MockBlobStorageService();
        await service.UploadAsync(Bytes("a"), ContainerName, "2026-03-24/archive/old/list.pdf", "application/pdf");
        await service.UploadAsync(Bytes("b"), ContainerName, "2026-03-24/list.pdf", "application/pdf");

        // Act
        var result = await service.ListVirtualDirectoriesAsync(ContainerName);

        // Assert
        Assert.Single(result);
        Assert.Equal("2026-03-24", result[0]);
    }
}
