using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class RunExpeditionListPrintFixResponseTests
{
    [Fact]
    public void DefaultConstructor_ReportsSuccessTrue_WithoutExplicitAssignment()
    {
        // Arrange & Act
        var response = new RunExpeditionListPrintFixResponse();

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Params.Should().BeNull();
    }
}
