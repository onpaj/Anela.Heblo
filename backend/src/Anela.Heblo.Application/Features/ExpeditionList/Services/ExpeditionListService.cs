using Anela.Heblo.Application.Features.ExpeditionList.Contracts;
using Anela.Heblo.Xcc.Services.Email;
using Anela.Heblo.Application.Shared.Printing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList.Services;

public class ExpeditionListService : IExpeditionListService
{
    private readonly IExpeditionPickingSource _pickingSource;
    private readonly IEmailSender _emailSender;
    private readonly TimeProvider _clock;
    private readonly IOptions<PrintPickingListOptions> _options;
    private readonly IPrintQueueSink _printQueueSink;
    private readonly ILogger<ExpeditionListService> _logger;

    public ExpeditionListService(
        IExpeditionPickingSource pickingSource,
        IEmailSender emailSender,
        TimeProvider clock,
        IOptions<PrintPickingListOptions> options,
        IPrintQueueSink printQueueSink,
        ILogger<ExpeditionListService> logger)
    {
        _pickingSource = pickingSource;
        _emailSender = emailSender;
        _clock = clock;
        _options = options;
        _printQueueSink = printQueueSink;
        _logger = logger;
    }

    public async Task<ExpeditionPickingResult> PrintPickingListAsync(
        ExpeditionPickingRequest request,
        IList<string>? emailList = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating new expedition list");

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

        var result = await _pickingSource.CreatePickingListAsync(request, batchCallback, cancellationToken);

        _logger.LogDebug("Expedition list complete — {Total} orders processed", result.TotalCount);

        await Cleanup(result);

        return result;
    }

    private Task Cleanup(ExpeditionPickingResult result)
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
