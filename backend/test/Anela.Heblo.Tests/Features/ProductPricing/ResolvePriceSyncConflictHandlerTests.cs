using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Application.Features.ProductPricing.UseCases.ResolvePriceSyncConflict;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ProductPricing;

public class ResolvePriceSyncConflictHandlerTests
{
    private readonly Mock<IProductPriceRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly List<ProductPriceSyncState> _savedStates = new();
    private readonly List<ProductPrice> _savedPrices = new();

    public ResolvePriceSyncConflictHandlerTests()
    {
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Resolving User", "resolving.user@anela.cz", true));
    }

    private ResolvePriceSyncConflictHandler CreateHandler()
    {
        _repository
            .Setup(r => r.UpsertSyncStateAsync(It.IsAny<ProductPriceSyncState>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPriceSyncState, CancellationToken>((s, _) => _savedStates.Add(s))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.UpsertAsync(It.IsAny<ProductPrice>(), It.IsAny<CancellationToken>()))
            .Callback<ProductPrice, CancellationToken>((p, _) => _savedPrices.Add(p))
            .Returns(Task.CompletedTask);

        return new ResolvePriceSyncConflictHandler(_repository.Object, _currentUserService.Object);
    }

    private void GivenConflict(decimal hebloPrice, decimal remoteValue)
    {
        _repository
            .Setup(r => r.GetSyncStateAsync("A", PriceSyncTarget.Shoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Status = PriceSyncStatus.Conflict,
                LastPushedPriceWithVat = 190.00m,
                RemoteValueAtConflict = remoteValue,
            });
        _repository
            .Setup(r => r.GetAsync("A", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPrice { ProductCode = "A", PriceWithVat = hebloPrice, VatRate = 21m });
    }

    [Fact]
    public async Task keeping_heblos_price_rebases_last_pushed_so_the_next_run_overwrites()
    {
        // Arrange
        GivenConflict(hebloPrice: 210.00m, remoteValue: 175.00m);
        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.KeepHebloPrice,
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        var state = _savedStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(PriceSyncStatus.Pending);
        state.LastPushedPriceWithVat.Should().Be(175.00m);
        state.RemoteValueAtConflict.Should().BeNull();
        _savedPrices.Should().BeEmpty();
    }

    [Fact]
    public async Task accepting_the_remote_price_writes_it_into_heblo_and_marks_in_sync()
    {
        // Arrange
        GivenConflict(hebloPrice: 210.00m, remoteValue: 175.00m);
        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.AcceptRemotePrice,
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _savedPrices.Should().ContainSingle().Which.PriceWithVat.Should().Be(175.00m);
        var state = _savedStates.Should().ContainSingle().Subject;
        state.Status.Should().Be(PriceSyncStatus.InSync);
        state.LastPushedPriceWithVat.Should().Be(175.00m);
    }

    [Fact]
    public async Task accepting_the_remote_price_records_the_resolving_user_as_modified_by()
    {
        // Arrange
        GivenConflict(hebloPrice: 210.00m, remoteValue: 175.00m);
        var handler = CreateHandler();

        // Act
        await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.AcceptRemotePrice,
            },
            CancellationToken.None);

        // Assert
        _savedPrices.Should().ContainSingle().Which.ModifiedBy.Should().Be("resolving.user@anela.cz");
    }

    [Fact]
    public async Task returns_not_found_when_the_state_is_not_in_conflict()
    {
        // Arrange
        _repository
            .Setup(r => r.GetSyncStateAsync("A", PriceSyncTarget.Shoptet, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductPriceSyncState { ProductCode = "A", Status = PriceSyncStatus.InSync });
        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(
            new ResolvePriceSyncConflictRequest
            {
                ProductCode = "A",
                Target = PriceSyncTarget.Shoptet,
                Resolution = PriceConflictResolution.KeepHebloPrice,
            },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductPriceConflictNotFound);
    }
}
