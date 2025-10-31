using Microsoft.Extensions.Logging;
using Anela.Heblo.Domain.Features.InvoiceClassification;

namespace Anela.Heblo.Adapters.Flexi.InvoiceClassification;

public class FlexiReceivedInvoicesClient : IReceivedInvoicesClient
{
    private readonly ILogger<FlexiReceivedInvoicesClient> _logger;

    public FlexiReceivedInvoicesClient(ILogger<FlexiReceivedInvoicesClient> logger)
    {
        _logger = logger;
    }

    public async Task<List<ReceivedInvoiceDto>> GetUnclassifiedInvoicesAsync()
    {
        _logger.LogInformation("Fetching unclassified invoices from ABRA Flexi - IMPLEMENTATION PLACEHOLDER");
        
        // TODO: Implement actual ABRA Flexi API integration
        // This is a placeholder implementation that returns mock data
        
        await Task.Delay(100); // Simulate API call
        
        return new List<ReceivedInvoiceDto>
        {
            new ReceivedInvoiceDto
            {
                Id = "1",
                InvoiceNumber = "FV2024001",
                CompanyName = "Test Company s.r.o.",
                CompanyIco = "12345678",
                InvoiceDate = DateTime.Today.AddDays(-5),
                TotalAmount = 15000.00m,
                Description = "Software development services",
                Items = new List<ReceivedInvoiceItemDto>
                {
                    new ReceivedInvoiceItemDto
                    {
                        Description = "Development work",
                        Quantity = 10,
                        UnitPrice = 1500.00m,
                        TotalPrice = 15000.00m
                    }
                }
            }
        };
    }

    public async Task<ReceivedInvoiceDto?> GetInvoiceByIdAsync(string invoiceId)
    {
        _logger.LogInformation("Fetching invoice {InvoiceId} from ABRA Flexi - IMPLEMENTATION PLACEHOLDER", invoiceId);
        
        // TODO: Implement actual ABRA Flexi API integration
        await Task.Delay(50); // Simulate API call
        
        if (invoiceId == "1")
        {
            return new ReceivedInvoiceDto
            {
                Id = "1",
                InvoiceNumber = "FV2024001",
                CompanyName = "Test Company s.r.o.",
                CompanyIco = "12345678",
                InvoiceDate = DateTime.Today.AddDays(-5),
                TotalAmount = 15000.00m,
                Description = "Software development services",
                Items = new List<ReceivedInvoiceItemDto>
                {
                    new ReceivedInvoiceItemDto
                    {
                        Description = "Development work",
                        Quantity = 10,
                        UnitPrice = 1500.00m,
                        TotalPrice = 15000.00m
                    }
                }
            };
        }
        
        return null;
    }
}