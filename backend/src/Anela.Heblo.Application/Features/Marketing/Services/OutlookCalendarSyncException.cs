using System.Net;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public class OutlookCalendarSyncException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string? GraphResponse { get; }

        public OutlookCalendarSyncException(HttpStatusCode statusCode, string? graphResponse, string message)
            : base(message)
        {
            StatusCode = statusCode;
            GraphResponse = graphResponse;
        }
    }
}
