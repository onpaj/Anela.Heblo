using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenToReserveSideEffectTests
{
    private readonly OpenToReserveSideEffect _sut = new();

    [Fact]
    public void Supports_OpenedToReserve_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Reserve).Should().BeTrue();
    }

    [Fact]
    public void Supports_AnyOtherPair_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Quarantine).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_MissingLocation_ReturnsRequiredFieldMissing()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Reserve };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ErrorCode.Should().Be(ErrorCodes.RequiredFieldMissing);
        result.Params.Should().Contain("field", "Location");
    }

    [Fact]
    public async Task ExecuteAsync_LocationProvided_ReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Reserve,
            Location = "A1"
        };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
    }
}
