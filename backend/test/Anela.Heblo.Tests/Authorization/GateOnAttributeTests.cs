using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

public class GateOnAttributeTests
{
    [GateOn(Feature.Manufacture_BatchPlanning)]
    private class SampleController { }

    [Fact]
    public void GateOn_ExposesFeature()
    {
        var attr = (GateOnAttribute)Attribute.GetCustomAttribute(
            typeof(SampleController), typeof(GateOnAttribute))!;
        attr.Feature.Should().Be(Feature.Manufacture_BatchPlanning);
    }
}
