using Anela.Heblo.Application.Features.Invoices.UseCases.EnqueueImportInvoices;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class EnqueueImportInvoicesHandlerTests
{
    [Fact]
    public void EnqueueImportInvoicesHandler_HasCorrectConstructor()
    {
        // Arrange & Act & Assert
        // This test ensures the handler class exists and has the expected constructor
        var constructors = typeof(EnqueueImportInvoicesHandler).GetConstructors();

        Assert.NotEmpty(constructors);

        var constructor = constructors.First();
        var parameters = constructor.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Contains(parameters, p => p.ParameterType.Name.Contains("IBackgroundWorker"));
        Assert.Contains(parameters, p => p.ParameterType.Name.Contains("ILogger"));
    }
}