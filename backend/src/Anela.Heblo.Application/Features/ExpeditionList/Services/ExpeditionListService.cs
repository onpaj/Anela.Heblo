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
    private readonly IPrintQueueSink _printQueueSink;
    private readonly ILogger<ExpeditionListService> _logger;

    public ExpeditionListService(
        IPickingListSource pickingListSource,
        ISendGridClient emailSender,
        TimeProvider clock,
        IOptions<PrintPickingListOptions> options,
        IPrintQueueSink printQueueSink,
        ILogger<ExpeditionListService> logger)
    {
        _pickingListSource = pickingListSource;
        _emailSender = emailSender;
        _clock = clock;
        _options = options;
        _printQueueSink = printQueueSink;
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
            await _printQueueSink.SendAsync(result.ExportedFiles, cancellationToken);
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
