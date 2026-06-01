namespace Anela.Heblo.Xcc.Services.Email;

public interface IEmailSender
{
    Task SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
