namespace Anela.Heblo.Adapters.Comgate;

public class PaymentGatewayUnavailableException : Exception
{
    public int? StatusCode { get; }

    public override string Message =>
        StatusCode.HasValue
            ? $"{base.Message} (HTTP {StatusCode})"
            : base.Message;

    public PaymentGatewayUnavailableException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
