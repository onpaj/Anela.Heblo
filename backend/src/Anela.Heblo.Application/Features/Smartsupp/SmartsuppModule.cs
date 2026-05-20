using Anela.Heblo.Application.Common.Behaviors;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations.Validators;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;
using Anela.Heblo.Domain.Features.Smartsupp;
using Anela.Heblo.Persistence.Smartsupp;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Smartsupp;

public static class SmartsuppModule
{
    public static IServiceCollection AddSmartsuppModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISmartsuppRepository, SmartsuppRepository>();
        services.AddScoped<ISmartsuppWebhookAuditWriter, SmartsuppWebhookAuditWriter>();

        services.AddOptions<SmartsuppDraftReplyOptions>()
            .Bind(configuration.GetSection(SmartsuppDraftReplyOptions.SectionName));

        services.AddScoped<IValidator<ListConversationsRequest>, ListConversationsValidator>();
        services.AddScoped<IPipelineBehavior<ListConversationsRequest, ListConversationsResponse>,
            ValidationBehavior<ListConversationsRequest, ListConversationsResponse>>();

        // Conversation reactions
        services.AddScoped<ISmartsuppWebhookReaction, ConversationOpenedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationClosedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationClosedByContactReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationContactRepliedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationAgentRepliedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationBotRepliedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationAgentAssignedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationAgentUnassignedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationAgentJoinedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationAgentLeftReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationRatedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationMessageDeliveredReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ConversationMessageDeliveryFailedReaction>();

        // Contact reactions
        services.AddScoped<ISmartsuppWebhookReaction, ContactCreatedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ContactUpdatedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ContactAcquiredReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ContactBannedReaction>();
        services.AddScoped<ISmartsuppWebhookReaction, ContactUnbannedReaction>();

        return services;
    }
}
