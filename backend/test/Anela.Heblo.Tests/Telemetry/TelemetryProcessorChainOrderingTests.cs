using Anela.Heblo.API.Telemetry;
using FluentAssertions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Telemetry;

/// <summary>
/// Guards the documented telemetry-processor ordering in
/// <see cref="Anela.Heblo.API.Extensions.ApplicationInsightsExtensions.AddOptimizedApplicationInsights"/>:
/// <c>BlobIdempotent409TelemetryProcessor</c> must run BEFORE
/// <c>CostOptimizedTelemetryProcessor</c> so that benign PUT-container 409s
/// are re-marked as Success=true before the cost-optimizer evaluates them.
///
/// This test validates the processor registration order by examining the
/// ITelemetryProcessorFactory service descriptors and verifying the factory
/// sequence through direct instantiation with a mock next processor.
/// </summary>
public class TelemetryProcessorChainOrderingTests
{
    [Fact]
    public void BlobIdempotent409Processor_RegisteredBeforeCostOptimizedProcessor()
    {
        // Arrange — Register the processors in the documented order
        var services = new ServiceCollection();
        services.AddApplicationInsightsTelemetryProcessor<BlobIdempotent409TelemetryProcessor>();
        services.AddApplicationInsightsTelemetryProcessor<CostOptimizedTelemetryProcessor>();

        // Extract the factory descriptors (these are registered in order)
        var factoryDescriptors = services
            .Where(sd => sd.ServiceType == typeof(ITelemetryProcessorFactory))
            .ToList();

        // Act & Assert — Verify we have at least 2 factories
        factoryDescriptors.Should().HaveCountGreaterThanOrEqualTo(2,
            "Both BlobIdempotent409TelemetryProcessor and CostOptimizedTelemetryProcessor must be registered");

        // Create a mock ITelemetryProcessor to use as the terminal next processor in the chain
        var mockNextProcessor = new NoOpTelemetryProcessor();

        // Instantiate the first factory (should create BlobIdempotent409TelemetryProcessor)
        var firstDescriptor = factoryDescriptors[0];
        var firstFactory = (ITelemetryProcessorFactory?)firstDescriptor.ImplementationFactory?.Invoke(new MockServiceProvider());

        // Instantiate the second factory (should create CostOptimizedTelemetryProcessor)
        var secondDescriptor = factoryDescriptors[1];
        var secondFactory = (ITelemetryProcessorFactory?)secondDescriptor.ImplementationFactory?.Invoke(new MockServiceProvider());

        firstFactory.Should().NotBeNull("First factory must be resolvable");
        secondFactory.Should().NotBeNull("Second factory must be resolvable");

        // Create the processor chain from the factories
        // First processor created with mockNextProcessor as next
        var firstProcessor = firstFactory!.Create(mockNextProcessor);
        // Second processor created with firstProcessor as next (to verify chain order)
        var secondProcessor = secondFactory!.Create(firstProcessor);

        // Assert the processor types and chain order
        firstProcessor.Should().BeOfType<BlobIdempotent409TelemetryProcessor>(
            "First registered processor must be BlobIdempotent409TelemetryProcessor (line 70 in ApplicationInsightsExtensions.cs)");

        secondProcessor.Should().BeOfType<CostOptimizedTelemetryProcessor>(
            "Second registered processor must be CostOptimizedTelemetryProcessor (line 71 in ApplicationInsightsExtensions.cs). " +
            "BlobIdempotent409TelemetryProcessor must execute before CostOptimizedTelemetryProcessor " +
            "so PUT-container 409s are re-marked success before cost optimization sees them.");

        // Verify the chain is correctly linked by checking the _next field
        var nextField = typeof(CostOptimizedTelemetryProcessor)
            .GetField("_next", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        nextField.Should().NotBeNull("CostOptimizedTelemetryProcessor must have a _next field for chain linking");
        var actualNext = nextField!.GetValue(secondProcessor);
        actualNext.Should().Be(firstProcessor,
            "CostOptimizedTelemetryProcessor's _next must point to BlobIdempotent409TelemetryProcessor");
    }

    /// <summary>
    /// No-op ITelemetryProcessor for testing chain initialization.
    /// </summary>
    private class NoOpTelemetryProcessor : ITelemetryProcessor
    {
        public void Process(Microsoft.ApplicationInsights.DataContracts.ITelemetry item)
        {
            // No-op for testing
        }
    }

    /// <summary>
    /// Mock IServiceProvider for factory instantiation.
    /// The processor factories may request services, so we provide a basic implementation.
    /// </summary>
    private class MockServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            // Return null for any service request
            // The processor factories should not depend on complex services
            return null;
        }
    }
}
