using System.Text.Json;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ProcessWebhookEvent.Reactions;

public class WebhookEventContext
{
    public string EventName { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string AccountId { get; set; } = "";
    public string AppId { get; set; } = "";
    public JsonElement Data { get; set; }

    public JsonElement? GetConversation() =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty("conversation", out var c)
            ? c : null;

    public JsonElement? GetMessage() =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty("message", out var m)
            ? m : null;

    public JsonElement? GetContact() =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty("contact", out var ct)
            ? ct : null;

    public static WebhookEventContext From(ProcessWebhookEventRequest request) =>
        new()
        {
            EventName = request.EventName,
            Timestamp = request.Timestamp,
            AccountId = request.AccountId,
            AppId = request.AppId,
            Data = request.Data,
        };
}
