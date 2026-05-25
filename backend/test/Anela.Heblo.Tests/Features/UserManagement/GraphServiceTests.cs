using Anela.Heblo.Application.Features.UserManagement.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Moq;

namespace Anela.Heblo.Tests.Features.UserManagement;

public class GraphServiceTests
{
    [Fact]
    public void Constructor_AcceptsIHttpClientFactory_AsFourthParameter()
    {
        // Arrange
        var tokenAcquisition = Mock.Of<ITokenAcquisition>();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Mock.Of<ILogger<GraphService>>();
        var httpClientFactory = Mock.Of<IHttpClientFactory>();

        // Act
        var service = new GraphService(tokenAcquisition, cache, logger, httpClientFactory);

        // Assert
        service.Should().NotBeNull();
    }
}
