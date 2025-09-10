using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class SimpleFileStorageTest
{
    [Fact]
    public void MockBlobStorageService_BasicTest_ShouldWork()
    {
        // Arrange
        var mockService = new MockBlobStorageService();

        // Act
        var containerCount = mockService.GetContainerNames().Count();

        // Assert
        Assert.Equal(0, containerCount);
    }

    [Fact]
    public void MockBlobStorageService_AddBlob_ShouldIncrementCount()
    {
        // Arrange
        var mockService = new MockBlobStorageService();
        var containerName = "test-container";

        // Act
        var result = mockService.DownloadFromUrlAsync(
            "https://example.com/test.pdf",
            containerName).Result;

        // Assert
        Assert.NotNull(result);
        Assert.Contains(containerName, result);
        Assert.Equal(1, mockService.GetBlobCount(containerName));
    }
}