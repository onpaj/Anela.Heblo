using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class TransportBoxInventoryRestorerTests
{
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock = new();
    private readonly TransportBoxInventoryRestorer _sut;

    public TransportBoxInventoryRestorerTests()
    {
        _sut = new TransportBoxInventoryRestorer(_inventoryReservationServiceMock.Object);
    }

    [Fact]
    public async Task RestoreAsync_ItemWithSourceInventoryId_CallsRestore()
    {
        var item = new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null, null, 42);
        var timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        await _sut.RestoreAsync(new[] { item }, "tester", timestamp, boxId: 7, boxCode: "B001", CancellationToken.None);

        _inventoryReservationServiceMock.Verify(x => x.RestoreAsync(
            42, 3.0m, "tester", timestamp, 7, "B001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_ItemWithoutSourceInventoryId_SkipsRestore()
    {
        var item = new TransportBoxItem("SKU-1", "Product", 3.0, DateTime.UtcNow, "user", null);

        await _sut.RestoreAsync(new[] { item }, "tester", DateTime.UtcNow, boxId: 7, boxCode: "B001", CancellationToken.None);

        _inventoryReservationServiceMock.Verify(x => x.RestoreAsync(
            It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
