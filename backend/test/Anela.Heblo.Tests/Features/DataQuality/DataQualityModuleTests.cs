using Anela.Heblo.Application.Features.DataQuality;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class DataQualityModuleTests
{
    [Fact]
    public void AddDataQualityModule_RegistersBothRunnersUnderIDqtJobRunner()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDataQualityModule();

        // Assert
        var dqtJobRunnerDescriptors = services
            .Where(s => s.ServiceType == typeof(IDqtJobRunner))
            .ToList();

        Assert.Equal(2, dqtJobRunnerDescriptors.Count);
        Assert.Contains(dqtJobRunnerDescriptors, d => d.ImplementationType == typeof(InvoiceDqtJobRunner));
        Assert.Contains(dqtJobRunnerDescriptors, d => d.ImplementationType == typeof(DriftDqtJobRunner));
    }

    [Fact]
    public void AddDataQualityModule_RetainsExistingNarrowInterfaceRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDataQualityModule();

        // Assert — narrow interfaces are retained, additive-only change
        Assert.Contains(services, d => d.ServiceType == typeof(IInvoiceDqtJobRunner) && d.ImplementationType == typeof(InvoiceDqtJobRunner));
        Assert.Contains(services, d => d.ServiceType == typeof(IDriftDqtJobRunner) && d.ImplementationType == typeof(DriftDqtJobRunner));
    }
}
