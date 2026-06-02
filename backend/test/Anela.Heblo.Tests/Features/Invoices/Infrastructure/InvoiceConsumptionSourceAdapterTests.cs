using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;
using IIssuedInvoiceRepository = Anela.Heblo.Application.Features.Invoices.Contracts.IIssuedInvoiceRepository;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public class InvoiceConsumptionSourceAdapterTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repository = new();

    private InvoiceConsumptionSourceAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task GetHeadersByDateAsync_forwards_date_and_token_to_repository()
    {
        var date = new DateOnly(2025, 7, 4);
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        _repository
            .Setup(r => r.GetHeadersByDateAsync(date, ct))
            .ReturnsAsync(new List<IssuedInvoice>());

        var adapter = CreateAdapter();

        await adapter.GetHeadersByDateAsync(date, ct);

        _repository.Verify(r => r.GetHeadersByDateAsync(date, ct), Times.Once);
    }

    [Fact]
    public async Task GetHeadersByDateAsync_projects_each_invoice_to_header_with_id_and_items_count()
    {
        var date = new DateOnly(2025, 7, 4);
        var invoices = new List<IssuedInvoice>
        {
            new IssuedInvoice { Id = "INV-1", ItemsCount = 3 },
            new IssuedInvoice { Id = "INV-2", ItemsCount = 7 },
        };

        _repository
            .Setup(r => r.GetHeadersByDateAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);

        var adapter = CreateAdapter();

        var result = await adapter.GetHeadersByDateAsync(date, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().Be(new InvoiceConsumptionHeader("INV-1", 3));
        result[1].Should().Be(new InvoiceConsumptionHeader("INV-2", 7));
    }

    [Fact]
    public async Task GetHeadersByDateAsync_returns_empty_list_when_repository_returns_empty()
    {
        _repository
            .Setup(r => r.GetHeadersByDateAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoice>());

        var adapter = CreateAdapter();

        var result = await adapter.GetHeadersByDateAsync(new DateOnly(2025, 7, 4), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
