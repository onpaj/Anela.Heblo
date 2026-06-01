using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Domain.Features.Invoices;
using AutoMapper;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

/// <summary>
/// Tests for pagination functionality in GetIssuedInvoicesListHandler
/// </summary>
public class GetIssuedInvoicesListHandlerPaginationTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly GetIssuedInvoicesListHandler _handler;

    public GetIssuedInvoicesListHandlerPaginationTests()
    {
        _repositoryMock = new Mock<IIssuedInvoiceRepository>();
        _mapperMock = new Mock<IMapper>();
        _handler = new GetIssuedInvoicesListHandler(_repositoryMock.Object, _mapperMock.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<GetIssuedInvoicesListHandler>>());
    }

    [Fact]
    public async Task Handle_PageSizeZero_ShouldReturnAllInvoices()
    {
        // Arrange
        var testInvoices = CreateTestInvoices(25); // More than normal page size
        var testDtos = CreateTestInvoiceDtos(25);

        var request = new GetIssuedInvoicesListRequest
        {
            PageNumber = 1,
            PageSize = 0 // This means return all invoices
        };

        var paginatedResult = new PaginatedResult<IssuedInvoice>
        {
            Items = testInvoices,
            TotalCount = 25,
            PageNumber = 1,
            PageSize = 0
        };

        _repositoryMock
            .Setup(r => r.GetPaginatedAsync(It.Is<IssuedInvoiceFilters>(f => f.PageSize == 0), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paginatedResult);

        _mapperMock
            .Setup(m => m.Map<List<IssuedInvoiceDto>>(testInvoices))
            .Returns(testDtos);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(25);
        response.TotalCount.Should().Be(25);
        response.PageSize.Should().Be(0);
        response.PageNumber.Should().Be(1);

        _repositoryMock.Verify(r => r.GetPaginatedAsync(It.Is<IssuedInvoiceFilters>(f =>
            f.PageSize == 0 &&
            f.PageNumber == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NormalPageSize_ShouldReturnLimitedInvoices()
    {
        // Arrange
        var testInvoices = CreateTestInvoices(10); // Normal page
        var testDtos = CreateTestInvoiceDtos(10);

        var request = new GetIssuedInvoicesListRequest
        {
            PageNumber = 1,
            PageSize = 10
        };

        var paginatedResult = new PaginatedResult<IssuedInvoice>
        {
            Items = testInvoices,
            TotalCount = 25,
            PageNumber = 1,
            PageSize = 10
        };

        _repositoryMock
            .Setup(r => r.GetPaginatedAsync(It.Is<IssuedInvoiceFilters>(f => f.PageSize == 10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(paginatedResult);

        _mapperMock
            .Setup(m => m.Map<List<IssuedInvoiceDto>>(testInvoices))
            .Returns(testDtos);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(10);
        response.TotalCount.Should().Be(25);
        response.PageSize.Should().Be(10);
        response.PageNumber.Should().Be(1);

        _repositoryMock.Verify(r => r.GetPaginatedAsync(It.Is<IssuedInvoiceFilters>(f =>
            f.PageSize == 10 &&
            f.PageNumber == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    private List<IssuedInvoice> CreateTestInvoices(int count)
    {
        var invoices = new List<IssuedInvoice>();
        for (int i = 1; i <= count; i++)
        {
            invoices.Add(new IssuedInvoice
            {
                Id = $"INV{i:D3}",
                InvoiceDate = DateTime.Today.AddDays(-i),
                DueDate = DateTime.Today.AddDays(-i + 30),
                TaxDate = DateTime.Today.AddDays(-i),
                ItemsCount = i,
                Price = i * 100,
                Currency = "CZK",
                CustomerName = $"Customer {i}",
                ExtraProperties = "{}",
                CreationTime = DateTime.UtcNow.AddDays(-i)
            });
        }
        return invoices;
    }

    private List<IssuedInvoiceDto> CreateTestInvoiceDtos(int count)
    {
        var dtos = new List<IssuedInvoiceDto>();
        for (int i = 1; i <= count; i++)
        {
            dtos.Add(new IssuedInvoiceDto
            {
                Id = $"INV{i:D3}",
                InvoiceDate = DateTime.Today.AddDays(-i),
                ItemsCount = i,
                Price = i * 100,
                CustomerName = $"Customer {i}"
            });
        }
        return dtos;
    }
}