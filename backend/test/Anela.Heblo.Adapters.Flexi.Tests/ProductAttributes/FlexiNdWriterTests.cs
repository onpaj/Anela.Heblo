using System.Net;
using Anela.Heblo.Adapters.Flexi.ProductAttributes;
using FluentAssertions;
using Moq;
using Rem.FlexiBeeSDK.Client.Clients.Products.Attributes;
using Rem.FlexiBeeSDK.Model.Products.Attributes;
using Rem.FlexiBeeSDK.Model.Response;

namespace Anela.Heblo.Adapters.Flexi.Tests.ProductAttributes;

public class FlexiNdWriterTests
{
    private const int NdAttributeId = 90;

    private readonly Mock<IProductAttributeClient> _client = new();
    private readonly FlexiNdWriter _writer;

    public FlexiNdWriterTests()
    {
        _writer = new FlexiNdWriter(_client.Object);
    }

    [Fact]
    public async Task SetAsync_WhenTop_SetsBooleanTrueOnAttribute90()
    {
        // Arrange
        _client
            .Setup(c => c.SetAsync(It.IsAny<ProductReference>(), It.IsAny<int>(), It.IsAny<ProductAttributeValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<OperationResultDetail>(HttpStatusCode.OK));

        // Act
        await _writer.SetAsync("ABC123", true, CancellationToken.None);

        // Assert
        _client.Verify(c => c.SetAsync(
            It.Is<ProductReference>(p => p.Code == "ABC123"),
            NdAttributeId,
            It.Is<ProductAttributeValue>(v => v.Kind == ProductAttributeValueKind.Boolean && v.ToString() == "True"),
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.ClearAsync(It.IsAny<ProductReference>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_WhenNotTop_ClearsAttribute90InsteadOfStoringFalse()
    {
        // Arrange
        _client
            .Setup(c => c.ClearAsync(It.IsAny<ProductReference>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<OperationResultDetail>(HttpStatusCode.OK));

        // Act
        await _writer.SetAsync("ABC123", false, CancellationToken.None);

        // Assert
        _client.Verify(c => c.ClearAsync(
            It.Is<ProductReference>(p => p.Code == "ABC123"),
            NdAttributeId,
            It.IsAny<CancellationToken>()), Times.Once);
        _client.Verify(c => c.SetAsync(It.IsAny<ProductReference>(), It.IsAny<int>(), It.IsAny<ProductAttributeValue>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetAsync_WhenSetFails_ThrowsWithStatusAndErrorMessage()
    {
        // Arrange
        _client
            .Setup(c => c.SetAsync(It.IsAny<ProductReference>(), It.IsAny<int>(), It.IsAny<ProductAttributeValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<OperationResultDetail>(HttpStatusCode.Conflict, "multiple rows"));

        // Act
        var act = () => _writer.SetAsync("ABC123", true, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("ABC123").And.Contain("Conflict").And.Contain("multiple rows");
    }

    [Fact]
    public async Task SetAsync_WhenClearFails_ThrowsWithStatusAndErrorMessage()
    {
        // Arrange
        _client
            .Setup(c => c.ClearAsync(It.IsAny<ProductReference>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult<OperationResultDetail>(HttpStatusCode.InternalServerError, "boom"));

        // Act
        var act = () => _writer.SetAsync("ABC123", false, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("ABC123").And.Contain("InternalServerError").And.Contain("boom");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetAsync_WithBlankProductCode_ThrowsArgumentException(string productCode)
    {
        var act = () => _writer.SetAsync(productCode, true, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _client.VerifyNoOtherCalls();
    }
}
