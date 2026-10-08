using Anela.Heblo.Adapters.GoogleAds.Api;
using FluentAssertions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Api;

public sealed class GoogleAdsErrorParserTests
{
    [Fact]
    public void reads_the_first_google_ads_failure_as_category_and_code()
    {
        const string body = """
            {"error":{"code":403,"message":"The caller does not have permission","status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.ads.googleads.v25.errors.GoogleAdsFailure",
                "errors":[{"errorCode":{"authorizationError":"CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION"},
                           "message":"The Google Cloud project is only approved for use with test accounts."}],
                "requestId":"req-123"}]}}
            """;

        var error = GoogleAdsErrorParser.Parse(body);

        error.ErrorCode.Should().Be("authorizationError.CLOUD_PROJECT_NOT_APPROVED_FOR_PRODUCTION");
        error.Message.Should().Be("The Google Cloud project is only approved for use with test accounts.");
        error.RequestId.Should().Be("req-123");
    }

    [Fact]
    public void falls_back_to_the_error_info_reason_when_there_is_no_google_ads_failure()
    {
        const string body = """
            {"error":{"code":403,"message":"Google Ads API has not been used in project 42","status":"PERMISSION_DENIED",
              "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"SERVICE_DISABLED"}]}}
            """;

        GoogleAdsErrorParser.Parse(body).ErrorCode.Should().Be("errorInfo.SERVICE_DISABLED");
    }

    [Fact]
    public void falls_back_to_the_status_and_survives_non_json()
    {
        GoogleAdsErrorParser.Parse("""{"error":{"code":503,"message":"busy","status":"UNAVAILABLE"}}""")
            .ErrorCode.Should().Be("status.UNAVAILABLE");

        var html = GoogleAdsErrorParser.Parse("<html>Bad Gateway</html>");
        html.ErrorCode.Should().BeNull();
        html.Message.Should().Be("<html>Bad Gateway</html>");
    }
}
