using System.Net;
using System.Text;

namespace Anela.Heblo.Tests.Helpers
{
    /// <summary>
    /// Fake HTTP message handler that captures the last outbound request
    /// and returns a pre-configured response for testing HTTP-level interactions.
    /// </summary>
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;

            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
