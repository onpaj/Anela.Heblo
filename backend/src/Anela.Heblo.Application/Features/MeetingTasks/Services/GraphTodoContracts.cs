using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.MeetingTasks.Services;

internal class GraphUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

internal class GraphUserCollection
{
    [JsonPropertyName("value")]
    public List<GraphUser> Value { get; set; } = [];
}

internal class GraphPlannerTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

