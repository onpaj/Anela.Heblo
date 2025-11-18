using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogList;
using Anela.Heblo.Domain.Features.Catalog;
using AutoMapper;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Tests for diacritic-insensitive search functionality in GetCatalogListHandler
/// </summary>
public class GetCatalogListHandlerDiacriticsTests
{
    private readonly Mock<ICatalogRepository> _repositoryMock;
    private readonly IMapper _mapper;
    private readonly GetCatalogListHandler _handler;

    public GetCatalogListHandlerDiacriticsTests()
    {
        _repositoryMock = new Mock<ICatalogRepository>();

        var config = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<CatalogMappingProfile>();
        });
        _mapper = config.CreateMapper();

        _handler = new GetCatalogListHandler(_repositoryMock.Object, _mapper);
    }

    [Fact]
    public async Task Handle_Should_Find_Products_With_Diacritics_When_Searching_Without()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "CHOC001", ProductName = "Čokoláda hořká" },
            new() { ProductCode = "SOAP001", ProductName = "Přírodní mýdlo" },
            new() { ProductCode = "SHAM001", ProductName = "Šampón na vlasy" },
            new() { ProductCode = "CREAM001", ProductName = "Krém na ruce" }
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            SearchTerm = "cokolada",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductName.Should().Be("Čokoláda hořká");
        result.Items[0].ProductCode.Should().Be("CHOC001");
    }

    [Fact]
    public async Task Handle_Should_Find_Products_Without_Diacritics_When_Searching_With()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "CHOC001", ProductName = "Cokolada horka" },
            new() { ProductCode = "SOAP001", ProductName = "Prirodni mydlo" },
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            SearchTerm = "čokoláda",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductName.Should().Be("Cokolada horka");
        result.Items[0].ProductCode.Should().Be("CHOC001");
    }

    [Fact]
    public async Task Handle_Should_Be_Case_Insensitive_With_Diacritics()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "CHOC001", ProductName = "ČOKOLÁDA HOŘKÁ" },
            new() { ProductCode = "CHOC002", ProductName = "čokoláda mléčná" },
            new() { ProductCode = "CHOC003", ProductName = "Čokoláda Bílá" }
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            ProductName = "COKOLADA",
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(3);
        result.Items.Select(i => i.ProductCode).Should().BeEquivalentTo(new[] { "CHOC001", "CHOC002", "CHOC003" });
    }

    [Theory]
    [InlineData("mydlo", "Přírodní mýdlo")]
    [InlineData("sampon", "Šampón na vlasy")]
    [InlineData("krem", "Krém na ruce")]
    [InlineData("KREM", "Krém na ruce")]
    [InlineData("prirodni", "Přírodní mýdlo")]
    [InlineData("PRIRODNI", "Přírodní mýdlo")]
    public async Task Handle_Should_Find_Czech_Products_With_Various_Search_Terms(string searchTerm, string expectedProductName)
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "SOAP001", ProductName = "Přírodní mýdlo" },
            new() { ProductCode = "SHAM001", ProductName = "Šampón na vlasy" },
            new() { ProductCode = "CREAM001", ProductName = "Krém na ruce" }
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            SearchTerm = searchTerm,
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductName.Should().Be(expectedProductName);
    }

    [Fact]
    public async Task Handle_Should_Search_Both_ProductName_And_ProductCode()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "CHOC001", ProductName = "Čokoláda hořká" },
            new() { ProductCode = "MYDLO01", ProductName = "Regular soap" } // ProductCode contains search term
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            SearchTerm = "mydlo", // Should find both: "Čokoláda" normalized and "MYDLO01" code
            PageNumber = 1,
            PageSize = 10
        };

        // Act  
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1); // Only MYDLO01 matches because "cokolada" != "mydlo"
        result.Items[0].ProductCode.Should().Be("MYDLO01");
    }

    [Fact]
    public async Task Handle_Should_Handle_Empty_And_Null_Search_Terms()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "CHOC001", ProductName = "Čokoláda hořká" },
            new() { ProductCode = "SOAP001", ProductName = "Přírodní mýdlo" }
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            SearchTerm = "", // Empty search term should return all products
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_Should_Find_Partial_Matches_With_Diacritics()
    {
        // Arrange
        var products = new List<CatalogAggregate>
        {
            new() { ProductCode = "CREAM001", ProductName = "Hydratační krém na obličej" },
            new() { ProductCode = "CREAM002", ProductName = "Opalovací krém" },
            new() { ProductCode = "SOAP001", ProductName = "Přírodní mýdlo" }
        };

        _repositoryMock.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken ct) =>
                products.AsQueryable().Where(predicate).ToList());

        var request = new GetCatalogListRequest
        {
            SearchTerm = "krem", // Should find both cream products
            PageNumber = 1,
            PageSize = 10
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.ProductCode).Should().BeEquivalentTo(new[] { "CREAM001", "CREAM002" });
    }
}