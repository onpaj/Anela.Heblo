using System.Net;

namespace Anela.Heblo.Application.Shared;

/// <summary>
/// Attribute to specify the HTTP status code that should be returned for a specific error code
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class HttpStatusCodeAttribute : Attribute
{
    /// <summary>
    /// The HTTP status code to return for this error
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Creates a new instance of the HttpStatusCodeAttribute
    /// </summary>
    /// <param name="statusCode">The HTTP status code to associate with the error</param>
    public HttpStatusCodeAttribute(HttpStatusCode statusCode)
    {
        StatusCode = statusCode;
    }
}