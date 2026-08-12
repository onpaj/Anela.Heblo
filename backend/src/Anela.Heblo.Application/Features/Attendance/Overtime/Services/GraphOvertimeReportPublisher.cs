using System.Net.Http.Headers;
using Anela.Heblo.Application.Common.Graph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Anela.Heblo.Application.Features.Attendance.Overtime.Services;

public class GraphOvertimeReportPublisher : IOvertimeReportPublisher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly IOptions<OvertimeOptions> _options;
    private readonly ILogger<GraphOvertimeReportPublisher> _logger;

    public GraphOvertimeReportPublisher(
        IHttpClientFactory httpClientFactory,
        ITokenAcquisition tokenAcquisition,
        IOptions<OvertimeOptions> options,
        ILogger<GraphOvertimeReportPublisher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenAcquisition = tokenAcquisition;
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Value.ExportDriveId);

    public async Task PublishAsync(byte[] content, string fileName, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Overtime report publishing is not configured (Overtime:ExportDriveId is empty).");
        }

        var options = _options.Value;
        var path = string.IsNullOrWhiteSpace(options.ExportFolderPath)
            ? fileName
            : $"{options.ExportFolderPath.Trim('/')}/{fileName}";

        var url = $"{GraphApiHelpers.GraphBaseUrl}/drives/{options.ExportDriveId}/root:/{path}:/content" +
                  "?@microsoft.graph.conflictBehavior=replace";

        var token = await _tokenAcquisition.GetAccessTokenForAppAsync(GraphApiHelpers.GraphScope);
        var client = _httpClientFactory.CreateClient("MicrosoftGraph");

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var response = await client.SendAsync(request, cancellationToken);
        await GraphApiHelpers.EnsureSuccessAsync(response, "upload overtime report", cancellationToken);

        _logger.LogInformation("Overtime report published to drive {DriveId} at {Path}", options.ExportDriveId, path);
    }
}
