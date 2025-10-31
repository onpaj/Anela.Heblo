using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Adapters.Flexi.InvoiceClassification;

public class FlexiInvoiceClassificationsClient : IInvoiceClassificationsClient
{
    private readonly ILogger<FlexiInvoiceClassificationsClient> _logger;

    public FlexiInvoiceClassificationsClient(ILogger<FlexiInvoiceClassificationsClient> logger)
    {
        _logger = logger;
    }

    public async Task<List<AccountingPrescriptionDto>> GetValidAccountingPrescriptionsAsync()
    {
        _logger.LogInformation("Fetching valid accounting prescriptions from ABRA Flexi - IMPLEMENTATION PLACEHOLDER");
        
        // TODO: Implement actual ABRA Flexi API integration
        await Task.Delay(100); // Simulate API call
        
        return new List<AccountingPrescriptionDto>
        {
            new AccountingPrescriptionDto
            {
                Code = "501/001",
                Name = "Software",
                Description = "Software development costs"
            },
            new AccountingPrescriptionDto
            {
                Code = "502/001",
                Name = "Materials",
                Description = "Raw materials and supplies"
            },
            new AccountingPrescriptionDto
            {
                Code = "518/001",
                Name = "Services",
                Description = "External services"
            }
        };
    }

    public async Task<bool> UpdateInvoiceClassificationAsync(string invoiceId, string accountingPrescription)
    {
        _logger.LogInformation("Updating invoice {InvoiceId} classification to {AccountingPrescription} in ABRA Flexi - IMPLEMENTATION PLACEHOLDER", 
            invoiceId, accountingPrescription);
        
        // TODO: Implement actual ABRA Flexi API integration
        await Task.Delay(200); // Simulate API call
        
        // For now, always return success
        _logger.LogInformation("Successfully updated invoice {InvoiceId} classification", invoiceId);
        return true;
    }

    public async Task<bool> MarkInvoiceForManualReviewAsync(string invoiceId, string reason)
    {
        _logger.LogInformation("Marking invoice {InvoiceId} for manual review with reason: {Reason} - IMPLEMENTATION PLACEHOLDER", 
            invoiceId, reason);
        
        // TODO: Implement actual ABRA Flexi API integration
        await Task.Delay(150); // Simulate API call
        
        // For now, always return success
        _logger.LogInformation("Successfully marked invoice {InvoiceId} for manual review", invoiceId);
        return true;
    }
}