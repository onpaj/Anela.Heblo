using Anela.Heblo.Domain.Features.Logistics.Picking;
using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class ExpeditionListService : IExpeditionListService
{
    private readonly IPickingListSource _pickingListSource;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _clock;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly IPrintQueueSink _printQueueSink;
    private readonly ILogger<ExpeditionListService> _logger;

    public ExpeditionListService(
        IPickingListSource pickingListSource,
        IEmailSender emailSender,
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

        // Build a per-batch callback that handles upload/email/printer for each printed batch.
        // State change in Shoptet happens inside CreatePickingList only after this callback succeeds,
        // ensuring orders are never moved to the new state when a downstream action fails.
        Func<IList<string>, Task>? batchCallback = null;

        if (request.SendToPrinter || (emailList != null && emailList.Any()))
        {
            batchCallback = async files =>
            {
                if (request.SendToPrinter)
                {
                    await _printQueueSink.SendAsync(files, cancellationToken);
                    _logger.LogDebug("Batch sent to print queue");
                }

                if (emailList != null && emailList.Any())
                {
                    await SendEmailCopy(files, emailList);
                    _logger.LogDebug("Batch email copy sent");
                }
            };
        }

        var result = await _pickingListSource.CreatePickingList(request, batchCallback, cancellationToken);

        _logger.LogDebug("Expedition list complete — {Total} orders processed", result.TotalCount);

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

    private async Task SendEmailCopy(IList<string> files, IEnumerable<string> emailRecipients)
    {
        var now = _clock.GetLocalNow();
        var message = new EmailMessage
        {
            From = _options.Value.EmailSender,
            Subject = $"Expedice {now:yyyy-MM-dd}",
            HtmlContent = $@"
<strong>Expedice vygenerovana {now:yyyy-MM-dd HH:mm:ss}</strong></br>
</br>
</br>
",
            To = emailRecipients.ToList()
        };

        foreach (var a in files)
        {
            var bytes = await File.ReadAllBytesAsync(a);
            message.Attachments.Add(new EmailAttachment
            {
                FileName = Path.GetFileName(a),
                Content = Convert.ToBase64String(bytes),
                ContentType = "application/pdf"
            });
        }

        await _emailSender.SendEmailAsync(message);
        _logger.LogDebug("Sent email copy");
    }
}
