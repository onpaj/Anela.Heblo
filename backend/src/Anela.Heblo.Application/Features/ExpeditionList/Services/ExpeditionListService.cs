using Anela.Heblo.Domain.Features.Logistics.Picking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class ExpeditionListService : IExpeditionListService
{
    private readonly IPickingListSource _pickingListSource;
    private readonly ISendGridClient _emailSender;
    private readonly TimeProvider _clock;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly ILogger<ExpeditionListService> _logger;

    public ExpeditionListService(
        IPickingListSource pickingListSource,
        ISendGridClient emailSender,
        TimeProvider clock,
        IOptions<PrintPickingListOptions> options,
        ILogger<ExpeditionListService> logger)
    {
        _pickingListSource = pickingListSource;
        _emailSender = emailSender;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task<PrintPickingListResult> PrintPickingListAsync(
        PrintPickingListRequest request,
        IList<string>? emailList = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating new expedition list");
        var result = await _pickingListSource.CreatePickingList(request, cancellationToken);
        _logger.LogDebug("Expedition list generated");

        if (emailList != null && emailList.Any())
        {
            await SendEmailCopy(result, emailList);
            _logger.LogDebug("Copy sent by email");
        }

        if (request.SendToPrinter)
        {
            await SendToPrinter(result);
            _logger.LogDebug("Sent to print queue");
        }

        await Cleanup(result);

        return result;
    }

    private Task Cleanup(PrintPickingListResult result)
    {
        foreach (var f in result.ExportedFiles)
        {
            if (File.Exists(f))
                File.Delete(f);
        }

        return Task.CompletedTask;
    }

    private Task SendToPrinter(PrintPickingListResult result)
    {
        var folder = _options.Value.PrintQueueFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            _logger.LogWarning("PrintQueueFolder is not configured. Skipping printer queue copy.");
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(folder);

        foreach (var f in result.ExportedFiles)
        {
            var fileName = Path.GetFileName(f);
            if (string.IsNullOrEmpty(fileName))
            {
                _logger.LogWarning("Skipping file with invalid path: {FilePath}", f);
                continue;
            }

            File.Copy(f, Path.Combine(folder, fileName));
        }

        return Task.CompletedTask;
    }

    private async Task SendEmailCopy(PrintPickingListResult result, IEnumerable<string> emailRecipients)
    {
        var now = _clock.GetLocalNow();
        var msg = new SendGridMessage()
        {
            From = new EmailAddress(_options.Value.EmailSender),
            Subject = $"Expedice {now:yyyy-MM-dd}",
            HtmlContent = $@"
<strong>Expedice vygenerovana {now:yyyy-MM-dd HH:mm:ss}</strong></br>
</br>
</br>
<strong>Celkem {result.TotalCount} zakazek</strong></br>
</br>
</br>
",
        };

        msg.AddTos(emailRecipients.Select(s => new EmailAddress(s)).ToList());

        foreach (var a in result.ExportedFiles)
        {
            var bytes = await File.ReadAllBytesAsync(a);
            var b64 = Convert.ToBase64String(bytes);
            msg.AddAttachment(Path.GetFileName(a), b64, "pdf");
        }

        var response = await _emailSender.SendEmailAsync(msg);
        _logger.LogDebug("Sent email with result {SendGridStatusCode}: {SendGridMessage}",
            response.StatusCode,
            await response.Body.ReadAsStringAsync());
    }
}
