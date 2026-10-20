using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Domain.Features.Authorization;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Authorization;

/// <summary>
/// The spec declares Finance_PriceAnalysis as HasWrite: read to use the simulator, write to
/// save scenarios. FeatureAuthorizeAttribute defaults to Read, so a scenario mutation that
/// inherits only the class-level gate makes `finance.price_analysis.write` an unreachable role.
/// </summary>
public class PricingControllersAuthorizationTests
{
    [Fact]
    public void PricingScenariosController_IsGatedByPriceAnalysisRead()
    {
        var attribute = typeof(PricingScenariosController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Finance_PriceAnalysis);
        attribute.Level.Should().Be(AccessLevel.Read);
    }

    [Theory]
    [InlineData(nameof(PricingScenariosController.CreateScenario))]
    [InlineData(nameof(PricingScenariosController.UpdateScenario))]
    [InlineData(nameof(PricingScenariosController.DeleteScenario))]
    public void ScenarioMutations_RequirePriceAnalysisWrite(string methodName)
    {
        var method = typeof(PricingScenariosController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull(
            $"{methodName} persists or deletes a scenario and must require " +
            "Finance_PriceAnalysis Write, not the class-level Read default");
        attribute!.Feature.Should().Be(Feature.Finance_PriceAnalysis);
        attribute.Level.Should().Be(AccessLevel.Write);
    }

    [Theory]
    [InlineData(nameof(PricingScenariosController.GetScenarios))]
    [InlineData(nameof(PricingScenariosController.GetScenario))]
    public void ScenarioReads_StayAtTheClassLevelReadGate(string methodName)
    {
        var method = typeof(PricingScenariosController).GetMethod(methodName)!;

        method.GetCustomAttributes<FeatureAuthorizeAttribute>(inherit: false)
            .Should()
            .BeEmpty("reading scenarios needs only the class-level Read gate; a method-level " +
                     "attribute would either broaden or narrow access silently");
    }

    [Theory]
    [InlineData(nameof(PricingSimulatorController.GetBaseline))]
    [InlineData(nameof(PricingSimulatorController.Recalculate))]
    public void SimulatorActions_StayAtTheClassLevelReadGate(string methodName)
    {
        var method = typeof(PricingSimulatorController).GetMethod(methodName)!;

        method.GetCustomAttributes<FeatureAuthorizeAttribute>(inherit: false)
            .Should()
            .BeEmpty("the simulator never persists anything — recalculating is a read-only " +
                     "projection, so Read remains the authoritative gate for both actions");
    }

    [Fact]
    public void PricingSimulatorController_IsGatedByPriceAnalysisRead()
    {
        var attribute = typeof(PricingSimulatorController)
            .GetCustomAttribute<FeatureAuthorizeAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be(Feature.Finance_PriceAnalysis);
        attribute.Level.Should().Be(AccessLevel.Read);
    }
}
