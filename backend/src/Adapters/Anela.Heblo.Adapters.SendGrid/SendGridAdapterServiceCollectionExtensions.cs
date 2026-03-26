using Anela.Heblo.Xcc.Services.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SendGrid;

namespace Anela.Heblo.Adapters.SendGrid;

public static class SendGridAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddSendGridAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SendGridOptions>(
            configuration.GetSection(SendGridOptions.ConfigurationKey));

        var apiKey = configuration[$"{SendGridOptions.ConfigurationKey}:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"SendGrid API key is not configured. Set '{SendGridOptions.ConfigurationKey}:ApiKey' in configuration.");

        services.AddSingleton<ISendGridClient>(new SendGridClient(apiKey));

        services.AddSingleton<IEmailSender, SendGridEmailSender>();

        return services;
    }
}
