using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.LabelIdentification.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LabelMatchDecision
{
    Auto,
    Choose,
    Low,
}
