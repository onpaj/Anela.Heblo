namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public sealed class SmartsuppSendMessageOptions
{
    public const string SectionName = "Smartsupp";

    public Dictionary<string, string> AgentMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
