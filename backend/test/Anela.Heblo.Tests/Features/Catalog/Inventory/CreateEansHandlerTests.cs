using Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Inventory;

public class CreateEansHandlerTests
{
    private readonly Mock<IEanRepository> _eanRepo = new();
    private readonly Mock<ILotRepository> _lotRepo = new();
    private readonly Mock<IEanCodeGenerator> _generator = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly CreateEansHandler _handler;

    public CreateEansHandlerTests()
    {
        _currentUser.Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("u1", "Test User", null, true));
        _handler = new CreateEansHandler(
            NullLogger<CreateEansHandler>.Instance,
            _eanRepo.Object,
            _lotRepo.Object,
            _generator.Object,
            _currentUser.Object);
    }

    [Fact]
    public async Task Handle_ValidRequest_GeneratesAndPersistsEans()
    {
        // Arrange
        var lot = new Lot("MAT001", "L1", null, DateOnly.FromDateTime(DateTime.Today), null, "user");
        _lotRepo.Setup(r => r.GetByIdAsync(1, default)).ReturnsAsync(lot);
        _generator.Setup(g => g.GenerateAsync(2, default))
            .ReturnsAsync(new List<string> { "INT-00000001", "INT-00000002" });
        _eanRepo.Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<Ean>>(), default))
            .ReturnsAsync((IEnumerable<Ean> eans, CancellationToken _) => eans);
        _eanRepo.Setup(r => r.SaveChangesAsync(default)).ReturnsAsync(2);

        var request = new CreateEansRequest
        {
            LotId = 1,
            Items = new List<CreateEanItem>
            {
                new() { Amount = 25m, Unit = "kg" },
                new() { Amount = 25m, Unit = "kg" }
            }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Eans.Count);
        Assert.Equal("INT-00000001", result.Eans[0].Code);
        Assert.Equal("INT-00000002", result.Eans[1].Code);
        _generator.Verify(g => g.GenerateAsync(2, default), Times.Once);
        _eanRepo.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<Ean>>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_LotNotFound_ReturnsLotNotFound()
    {
        // Arrange
        _lotRepo.Setup(r => r.GetByIdAsync(99, default)).ReturnsAsync((Lot?)null);

        var request = new CreateEansRequest
        {
            LotId = 99,
            Items = new List<CreateEanItem> { new() { Amount = 1m, Unit = "kg" } }
        };

        // Act
        var result = await _handler.Handle(request, default);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.LotNotFound, result.ErrorCode);
        _generator.Verify(g => g.GenerateAsync(It.IsAny<int>(), default), Times.Never);
    }
}
