namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure.Exceptions;

public class ManufactureErpUnavailableException : Exception
{
    public string OperationName { get; }

    public ManufactureErpUnavailableException(string operationName, Exception inner)
        : base($"FlexiBee ERP unavailable for operation '{operationName}' (circuit breaker open)", inner)
    {
        OperationName = operationName;
    }
}
