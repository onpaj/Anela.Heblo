namespace Anela.Heblo.Application.Shared;

/// <summary>
/// Base response class that all API responses must inherit from
/// </summary>
public abstract class BaseResponse
{
    /// <summary>
    /// Indicates whether the operation was successful
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error code if the operation failed
    /// </summary>
    public ErrorCodes? ErrorCode { get; set; }

    /// <summary>
    /// Parameters for error message localization
    /// </summary>
    public Dictionary<string, string>? Params { get; set; }

    /// <summary>
    /// Creates a successful response
    /// </summary>
    protected BaseResponse()
    {
        Success = true;
    }

    /// <summary>
    /// Creates an error response
    /// </summary>
    protected BaseResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
    {
        Success = false;
        ErrorCode = errorCode;
        Params = parameters;
    }
}