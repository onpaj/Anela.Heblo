using System.Collections.Concurrent;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class BlobContainerEnsuranceTests
{
    private readonly Mock<BlobContainerClient> _containerClient = new();
    private readonly ConcurrentDictionary<string, Lazy<Task>> _cache = new();

    public BlobContainerEnsuranceTests()
    {
        _containerClient.SetupGet(c => c.Name).Returns("test-container");
    }

    [Fact]
    public async Task EnsureExistsAsync_ContainerExists_DoesNotCallCreate()
    {
        // Arrange — ExistsAsync returns true
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        // Act
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object,
            _cache,
            NullLogger.Instance,
            CancellationToken.None);

        // Assert — ExistsAsync called once, CreateAsync never
        _containerClient.Verify(x => x.ExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _containerClient.Verify(
            x => x.CreateAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureExistsAsync_ContainerMissing_CallsCreateOnce()
    {
        // Arrange — ExistsAsync returns false, CreateAsync succeeds
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));
        _containerClient
            .Setup(x => x.CreateAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        // Act
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object,
            _cache,
            NullLogger.Instance,
            CancellationToken.None);

        // Assert
        _containerClient.Verify(x => x.ExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _containerClient.Verify(
            x => x.CreateAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureExistsAsync_CalledTwice_ProbesExistenceOnlyOnce()
    {
        // Arrange
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        // Act — two sequential calls
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None);
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None);

        // Assert — ExistsAsync called exactly once (second call is a cache hit)
        _containerClient.Verify(x => x.ExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureExistsAsync_FourParallelFirstCalls_ProbesExistenceAtMostOnce()
    {
        // Arrange — block ExistsAsync until released so all 4 callers race on the cache
        var release = new TaskCompletionSource();
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return Response.FromValue(true, Mock.Of<Response>());
            });

        // Act — fire 4 concurrent calls, then release
        var calls = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => BlobContainerEnsurance.EnsureExistsAsync(
                _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None)))
            .ToArray();

        await Task.Delay(50); // let all tasks converge on the cache
        release.SetResult();
        await Task.WhenAll(calls);

        // Assert
        _containerClient.Verify(
            x => x.ExistsAsync(It.IsAny<CancellationToken>()),
            Times.AtMostOnce());
    }

    [Fact]
    public async Task EnsureExistsAsync_CreateAsyncRaces409_SwallowsAndContinues()
    {
        // Arrange — ExistsAsync false, CreateAsync throws a 409 (another writer beat us)
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));
        _containerClient
            .Setup(x => x.CreateAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(
                status: 409,
                message: "container already exists",
                errorCode: "ContainerAlreadyExists",
                innerException: null));

        // Act — should NOT throw; race is benign
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None);

        // Assert — cache is populated so a follow-up call does not probe again
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None);
        _containerClient.Verify(x => x.ExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureExistsAsync_CreateAsyncThrowsNon409_PropagatesAndDoesNotCacheSuccess()
    {
        // Arrange — ExistsAsync false, CreateAsync throws a 500
        _containerClient
            .Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));
        _containerClient
            .Setup(x => x.CreateAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(
                status: 500,
                message: "internal server error",
                errorCode: "InternalError",
                innerException: null));

        // Act + Assert — first call throws
        await Assert.ThrowsAsync<RequestFailedException>(() =>
            BlobContainerEnsurance.EnsureExistsAsync(
                _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None));

        // Arrange — make CreateAsync succeed for the retry
        _containerClient
            .Setup(x => x.CreateAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContainerInfo>>());

        // Act — second call retries (failed cache entry was evicted)
        await BlobContainerEnsurance.EnsureExistsAsync(
            _containerClient.Object, _cache, NullLogger.Instance, CancellationToken.None);

        // Assert — ExistsAsync called twice total (once per attempt) confirming retry
        _containerClient.Verify(x => x.ExistsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
