using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class OpenToQuarantineSideEffectTests
{
    private readonly OpenToQuarantineSideEffect _sut = new();

    [Fact]
    public void Supports_OpenedToQuarantine_ReturnsTrue()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Quarantine).Should().BeTrue();
    }

    [Fact]
    public void Supports_AnyOtherPair_ReturnsFalse()
    {
        _sut.Supports(TransportBoxState.Opened, TransportBoxState.Reserve).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AlwaysReturnsNull()
    {
        var box = new TransportBox();
        var request = new ChangeTransportBoxStateRequest { BoxId = 1, NewState = TransportBoxState.Quarantine };

        var result = await _sut.ExecuteAsync(box, request, CancellationToken.None);

        result.Should().BeNull();
    }
}
