using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.ErrorFilters;

public class ManufactureErrorTransformerTests
{
    [Fact]
    public void Transform_WhenNoFilterMatches_ReturnsFallbackWithExceptionMessage()
    {
        var filters = new List<IManufactureErrorFilter>();
        var transformer = new ManufactureErrorTransformer(filters);
        var ex = new InvalidOperationException("Some unknown Flexi error");

        var result = transformer.Transform(ex);

        result.Should().Be("Při zpracování výroby došlo k neočekávané chybě. Technické detaily: Some unknown Flexi error");
    }

    [Fact]
    public void Constructor_WithNullFilters_ThrowsArgumentNullException()
    {
        var act = () => new ManufactureErrorTransformer(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Transform_WhenFirstFilterMatches_ReturnsItsMessage()
    {
        var matchingFilter = new Mock<IManufactureErrorFilter>();
        matchingFilter.Setup(f => f.CanHandle(It.IsAny<Exception>())).Returns(true);
        matchingFilter.Setup(f => f.Transform(It.IsAny<Exception>())).Returns("Uživatelsky přívětivá zpráva");

        var transformer = new ManufactureErrorTransformer(new[] { matchingFilter.Object });
        var ex = new InvalidOperationException("raw error");

        var result = transformer.Transform(ex);

        result.Should().Be("Uživatelsky přívětivá zpráva");
    }

    [Fact]
    public void Transform_WhenFirstFilterDoesNotMatch_TriesNextFilter()
    {
        var nonMatchingFilter = new Mock<IManufactureErrorFilter>();
        nonMatchingFilter.Setup(f => f.CanHandle(It.IsAny<Exception>())).Returns(false);

        var matchingFilter = new Mock<IManufactureErrorFilter>();
        matchingFilter.Setup(f => f.CanHandle(It.IsAny<Exception>())).Returns(true);
        matchingFilter.Setup(f => f.Transform(It.IsAny<Exception>())).Returns("Zpráva z druhého filtru");

        var transformer = new ManufactureErrorTransformer(new[] { nonMatchingFilter.Object, matchingFilter.Object });
        var ex = new InvalidOperationException("raw error");

        var result = transformer.Transform(ex);

        result.Should().Be("Zpráva z druhého filtru");
        nonMatchingFilter.Verify(f => f.Transform(It.IsAny<Exception>()), Times.Never);
    }
}
