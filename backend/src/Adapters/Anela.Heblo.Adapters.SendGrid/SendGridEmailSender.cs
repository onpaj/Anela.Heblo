using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Anela.Heblo.Adapters.SendGrid;

public class SendGridEmailSender : IEmailSender
{
    private readonly ISendGridClient _client;
    private readonly ILogger<SendGridEmailSender> _logger;

    public SendGridEmailSender(ISendGridClient client, ILogger<SendGridEmailSender> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var msg = new SendGridMessage
        {
            From = new EmailAddress(message.From),
            Subject = message.Subject,
            HtmlContent = message.HtmlContent,
            PlainTextContent = message.PlainTextContent
        };

        msg.AddTos(message.To.Select(to => new EmailAddress(to)).ToList());

        foreach (var attachment in message.Attachments)
        {
            msg.AddAttachment(attachment.FileName, attachment.Content, attachment.ContentType);
        }

        var response = await _client.SendEmailAsync(msg, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError("SendGrid failed with {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"Email sending failed: {response.StatusCode}");
        }

        _logger.LogDebug("SendGrid response {StatusCode}", response.StatusCode);
    }
}
