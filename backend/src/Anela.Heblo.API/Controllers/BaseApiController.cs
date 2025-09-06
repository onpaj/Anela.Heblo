using System.Net;
using System.Reflection;
using Anela.Heblo.Application.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// Base controller that provides common functionality for all API controllers
/// </summary>
public abstract class BaseApiController : ControllerBase
{
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

        if (response.ErrorCode.HasValue)
        {
            var statusCode = GetStatusCodeForError(response.ErrorCode.Value);
            return StatusCode((int)statusCode, response);
        }

        // Default to BadRequest if no error code is specified
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