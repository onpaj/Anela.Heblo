using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Infrastructure;

/// <summary>
/// Helper class for creating standardized error responses
/// </summary>
public static class ErrorResponseHelper
{
    /// <summary>
    /// Creates an error response with the specified error code and parameters
    /// </summary>
    public static T CreateErrorResponse<T>(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) where T : BaseResponse, new()
    {
        var response = new T
        {
            Success = false,
            ErrorCode = errorCode,
            Params = parameters
        };
        return response;
    }

    /// <summary>
    /// Creates a validation error response
    /// </summary>
    public static T CreateValidationError<T>(string? fieldName = null) where T : BaseResponse, new()
    {
        var parameters = fieldName != null
            ? new Dictionary<string, string> { { "field", fieldName } }
            : null;
        return CreateErrorResponse<T>(ErrorCodes.ValidationError, parameters);
    }

    /// <summary>
    /// Creates a not found error response
    /// </summary>
    public static T CreateNotFoundError<T>(ErrorCodes specificErrorCode, string resourceId) where T : BaseResponse, new()
    {
        return CreateErrorResponse<T>(specificErrorCode, new Dictionary<string, string> { { "id", resourceId } });
    }

    /// <summary>
    /// Creates a business rule violation error response
    /// </summary>
    public static T CreateBusinessError<T>(ErrorCodes errorCode, string? detail = null) where T : BaseResponse, new()
    {
        var parameters = detail != null
            ? new Dictionary<string, string> { { "detail", detail } }
            : null;
        return CreateErrorResponse<T>(errorCode, parameters);
    }
    
    /// <summary>
    /// Creates a business rule violation error response
    /// </summary>
    public static T CreateError<T>(Exception ex) where T : BaseResponse, new()
    {
        return CreateErrorResponse<T>(ErrorCodes.Exception, new Dictionary<string, string>()
        {
            { "message", ex.Message},
            { "exceptionType", ex.GetType().Name}
        });
    }
}