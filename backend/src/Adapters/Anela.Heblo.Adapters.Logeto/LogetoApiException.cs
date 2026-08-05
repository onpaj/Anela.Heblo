namespace Anela.Heblo.Adapters.Logeto;

public class LogetoApiException : Exception
{
    public int StatusCode { get; }
    public string? ApiErrorCode { get; }

    public LogetoApiException(int statusCode, string? apiErrorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiErrorCode = apiErrorCode;
    }
}
