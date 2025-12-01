using System.Net;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Xcc.Application.Dtos;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure.Mocks;

/// <summary>
/// Mock implementation of IBankClient for invoice module
/// Returns success for all operations since bank functionality is out of scope
/// </summary>
public class MockBankClient : IBankClient
{
    public Task<OperationResult<OperationResultDetail>> UnPairPayment(string paymentId, CancellationToken cancellationToken = default)
    {
        // Mock implementation - always return success
        var result = new OperationResult<OperationResultDetail>(HttpStatusCode.OK)
        {
            IsSuccess = true,
            ErrorMessage = null,
            Result = new OperationResultDetail()
        };
        
        return Task.FromResult(result);
    }
}