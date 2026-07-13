using Anela.Heblo.Domain.Features.Invoices;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class IssuedInvoiceTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(IssuedInvoiceErrorType.General, true)]
    [InlineData(IssuedInvoiceErrorType.InvoicePaired, false)]
    [InlineData(IssuedInvoiceErrorType.ProductNotFound, true)]
    public void IsCriticalError_AgreesWithSharedPredicate_ForAllErrorTypes(IssuedInvoiceErrorType? errorType, bool expectedCritical)
    {
        // Arrange
        var invoice = new IssuedInvoice { Id = "INV-001", InvoiceDate = DateTime.Today, DueDate = DateTime.Today.AddDays(30), TaxDate = DateTime.Today };

        if (errorType.HasValue)
        {
            invoice.SyncFailed(new object(), new IssuedInvoiceError { ErrorType = errorType.Value, Message = "Error" });
        }
        else
        {
            invoice.SyncSucceeded(new object());
        }

        // Act & Assert
        Assert.Equal(expectedCritical, invoice.IsCriticalError);
        Assert.Equal(invoice.IsCriticalError, IssuedInvoice.IsCriticalErrorPredicate.Compile()(invoice));
    }
}
