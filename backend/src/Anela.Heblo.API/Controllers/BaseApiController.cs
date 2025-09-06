using System.Net;
using System.Reflection;
using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Base controller that provides common functionality for all API controllers
/// </summary>
public abstract class BaseApiController : ControllerBase
{
    private ILogger? _logger;
    
    /// <summary>
    /// Gets the logger for the current controller type
    /// </summary>
    protected ILogger Logger => _logger ??= HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

    /// <summary>
    /// Handles a response from a MediatR handler and returns the appropriate HTTP status code
    /// based on the Success property and ErrorCode attribute
    /// </summary>
    /// <typeparam name="T">The response type</typeparam>
    /// <param name="response">The response from the MediatR handler</param>
    /// <returns>ActionResult with appropriate HTTP status code</returns>
    protected ActionResult<T> HandleResponse<T>(T response) where T : BaseResponse
    {
        if (response.Success)
        {
            return Ok(response);
        }

        // Log warning for failed responses
        if (response.ErrorCode.HasValue)
        {
            Logger.LogWarning("Request failed with error code {ErrorCode}: {Params}", 
                response.ErrorCode, 
                response.Params != null ? string.Join(", ", response.Params.Select(p => $"{p.Key}={p.Value}")) : "no params");
            
            var statusCode = GetStatusCodeForError(response.ErrorCode.Value);
            return StatusCode((int)statusCode, response);
        }

        Logger.LogWarning("Request failed without error code");
        return BadRequest(response);
    }

    /// <summary>
    /// Gets the HTTP status code for a given error code based on its HttpStatusCodeAttribute
    /// </summary>
    /// <param name="errorCode">The error code</param>
    /// <returns>The HTTP status code</returns>
    private static HttpStatusCode GetStatusCodeForError(ErrorCodes errorCode)
    {
        var field = typeof(ErrorCodes).GetField(errorCode.ToString());
        var attribute = field?.GetCustomAttribute<HttpStatusCodeAttribute>();
        
        return attribute?.StatusCode ?? HttpStatusCode.BadRequest;
    }
}