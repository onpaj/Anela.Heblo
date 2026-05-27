using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class GetEanByCodeHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly GetEanByCodeHandler _handler;

    public GetEanByCodeHandlerTests()
    {
        _handler = new GetEanByCodeHandler(NullLogger<GetEanByCodeHandler>.Instance, _eanRepo.Object, _lotRepo.Object);
    }

    [Fact]
    public async Task Handle_ExistingCode_ReturnsEanWithLot()
    {
        // Arrange
        var ean = new Ean("INT-00000001", 1, 25m, "kg", "user");
        var lot = new Lot("MAT001", "L1", new DateOnly(2027, 6, 1), DateOnly.FromDateTime(DateTime.Today), null, "user");
        _eanRepo.Setup(r => r.GetByCodeAsync("INT-00000001", default)).ReturnsAsync(ean);
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);

        // Act
        var result = await _handler.Handle(new GetEanByCodeRequest { Code = "INT-00000001" }, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("INT-00000001", result.Ean.Code);
        Assert.Equal("MAT001", result.Lot.MaterialCode);
    }

    [Fact]
    public async Task Handle_MissingCode_ReturnsEanNotFound()
    {
        // Arrange
        _eanRepo.Setup(r => r.GetByCodeAsync("MISSING", default)).ReturnsAsync((Ean?)null);

        // Act
        var result = await _handler.Handle(new GetEanByCodeRequest { Code = "MISSING" }, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.EanNotFound, result.ErrorCode);
    }
}
