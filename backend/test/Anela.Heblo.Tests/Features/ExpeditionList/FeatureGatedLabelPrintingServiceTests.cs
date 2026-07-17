using Anela.Heblo.Application.Features.FeatureFlags;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class FeatureGatedLabelPrintingServiceTests
{
    private static FeatureGatedLabelPrintingService CreateSut(
        Mock<ILabelPrintingService> inner, Mock<IFeatureFlagChecker> flags) =>
        new(inner.Object, flags.Object, NullLogger<FeatureGatedLabelPrintingService>.Instance);

    [Fact]
    public async Task PrintZplAsync_DelegatesToInner_WhenFlagEnabled()
    {
        var inner = new Mock<ILabelPrintingService>();
        var flags = new Mock<IFeatureFlagChecker>();
        flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.LabelPrintingEnabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await CreateSut(inner, flags).PrintZplAsync("^XA^XZ");

        inner.Verify(i => i.PrintZplAsync("^XA^XZ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrintZplAsync_SkipsInner_WhenFlagDisabled()
    {
        var inner = new Mock<ILabelPrintingService>();
        var flags = new Mock<IFeatureFlagChecker>();
        flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.LabelPrintingEnabled, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateSut(inner, flags).PrintZplAsync("^XA^XZ");

        inner.Verify(i => i.PrintZplAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
