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
        var message = new EmailMessage
        {
            From = _options.Value.EmailSender,
            Subject = $"Expedice {now:yyyy-MM-dd}",
            HtmlContent = $@"
<strong>Expedice vygenerovana {now:yyyy-MM-dd HH:mm:ss}</strong></br>
</br>
</br>
<strong>Celkem {result.TotalCount} zakazek</strong></br>
</br>
</br>
",
            To = emailRecipients.ToList()
        };

        foreach (var a in result.ExportedFiles)
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
