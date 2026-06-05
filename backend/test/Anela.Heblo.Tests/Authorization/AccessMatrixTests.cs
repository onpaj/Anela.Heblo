using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class DeterministicGuidTests
{
    [Fact]
    public void ForRole_IsStable_ForSameValue()
    {
        var a = DeterministicGuid.ForRole("purchase_orders.read");
        var b = DeterministicGuid.ForRole("purchase_orders.read");
        a.Should().Be(b);
        a.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ForRole_Differs_ForDifferentValues()
    {
        DeterministicGuid.ForRole("purchase_orders.read")
            .Should().NotBe(DeterministicGuid.ForRole("purchase_orders.write"));
    }
}
