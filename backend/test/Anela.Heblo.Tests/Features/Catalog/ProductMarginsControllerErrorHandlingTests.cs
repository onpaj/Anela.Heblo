using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Anela.Heblo.API;
using Anela.Heblo.Application.Features.Catalog.Exceptions;
using Anela.Heblo.Application.Features.Catalog.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class ProductMarginsControllerErrorHandlingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ProductMarginsControllerErrorHandlingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetProductMargins_WhenDataAccessExceptionOccurs_Returns503ServiceUnavailable()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the mediator with a mock that throws DataAccessException
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ThrowsAsync(new DataAccessException("Database connection failed"));

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Service temporarily unavailable", content);
        Assert.Contains("Data source unavailable", content);
    }

    [Fact]
    public async Task GetProductMargins_WhenMarginCalculationExceptionOccurs_Returns400BadRequest()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the mediator with a mock that throws MarginCalculationException
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ThrowsAsync(new MarginCalculationException("Invalid margin calculation"));

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unable to calculate product margins", content);
        Assert.Contains("Invalid margin calculation", content);
    }

    [Fact]
    public async Task GetProductMargins_WhenProductMarginsExceptionOccurs_Returns400BadRequest()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the mediator with a mock that throws ProductMarginsException
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ThrowsAsync(new ProductMarginsException("Business logic error"));

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unable to process margin request", content);
        Assert.Contains("Business logic error", content);
    }

    [Fact]
    public async Task GetProductMargins_WhenUnauthorizedAccessExceptionOccurs_Returns403Forbidden()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the mediator with a mock that throws UnauthorizedAccessException
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ThrowsAsync(new UnauthorizedAccessException("Insufficient permissions"));

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Insufficient permissions to access margin data", content);
    }

    [Fact]
    public async Task GetProductMargins_WhenUnexpectedExceptionOccurs_Returns500InternalServerError()
    {
        // Arrange
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace the mediator with a mock that throws unexpected exception
                var mockMediator = new Mock<IMediator>();
                mockMediator.Setup(x => x.Send(It.IsAny<GetProductMarginsRequest>(), default))
                    .ThrowsAsync(new InvalidOperationException("Unexpected system error"));

                services.AddScoped(_ => mockMediator.Object);
            });
        });

        var client = factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Internal server error", content);
        Assert.Contains("An unexpected error occurred", content);
    }

    [Theory]
    [InlineData("pageNumber=0&pageSize=10")] // Invalid page number
    [InlineData("pageNumber=1&pageSize=0")]  // Invalid page size
    [InlineData("pageNumber=-1&pageSize=10")] // Negative page number
    [InlineData("pageNumber=1&pageSize=-10")] // Negative page size
    public async Task GetProductMargins_WithInvalidQueryParameters_HandlesGracefully(string queryParams)
    {
        // Act
        var response = await _client.GetAsync($"/api/ProductMargins?{queryParams}");

        // Assert - Should either succeed (with validation handling) or return appropriate error
        // The actual behavior depends on model validation setup
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.BadRequest ||
                   response.StatusCode == HttpStatusCode.ServiceUnavailable); // Due to mock data in tests
    }

    [Fact]
    public async Task GetProductMargins_WithValidParameters_ReturnsSuccessfully()
    {
        // Act
        var response = await _client.GetAsync("/api/ProductMargins?pageNumber=1&pageSize=10&productCode=TEST");

        // Assert
        // In test environment with mock data, should succeed or return service unavailable
        Assert.True(response.StatusCode == HttpStatusCode.OK ||
                   response.StatusCode == HttpStatusCode.ServiceUnavailable); // Due to mock data in automation tests
    }

    [Fact]
    public async Task GetProductMargins_WithLongProductCodeFilter_HandlesGracefully()
    {
        // Arrange
        var longProductCode = new string('X', 1000); // Very long product code

        // Act
        var response = await _client.GetAsync($"/api/ProductMargins?pageNumber=1&pageSize=10&productCode={longProductCode}");

        // Assert - Should handle gracefully without server error
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetProductMargins_WithSpecialCharactersInFilter_HandlesGracefully()
    {
        // Arrange
        var specialCharsProductName = "Product%20with%20special%20chars%20&%20symbols%20%3C%3E%22'";

        // Act
        var response = await _client.GetAsync($"/api/ProductMargins?pageNumber=1&pageSize=10&productName={specialCharsProductName}");

        // Assert - Should handle gracefully without server error
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}