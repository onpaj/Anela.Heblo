using System.ComponentModel;
using Anela.Heblo.Application.Features.Invoices.Services;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class InvoiceImportServiceTests
{
    [Fact]
    public void InvoiceImportService_HasCorrectDisplayNameAttribute()
    {
        // Arrange & Act
        var method = typeof(InvoiceImportService).GetMethod(nameof(InvoiceImportService.ImportInvoicesAsync));
        var attribute = method?.GetCustomAttributes(typeof(DisplayNameAttribute), false).FirstOrDefault() as DisplayNameAttribute;

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal("Import faktur: {0}", attribute.DisplayName);
    }

}