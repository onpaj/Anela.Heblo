using Anela.Heblo.API.Infrastructure.Hangfire;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Hangfire;

public class HangfireSchemaInitializerUnitTests
{
    private readonly Mock<ILogger<HangfireSchemaInitializer>> _loggerMock;

    public HangfireSchemaInitializerUnitTests()
    {
        _loggerMock = new Mock<ILogger<HangfireSchemaInitializer>>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenConnectionStringIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HangfireSchemaInitializer(null, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HangfireSchemaInitializer("connection", null));
    }
}