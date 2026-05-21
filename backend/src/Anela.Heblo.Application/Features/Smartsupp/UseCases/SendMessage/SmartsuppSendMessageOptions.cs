namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

/// <summary>
/// Options for the Smartsupp SendMessage feature. Bound from the "Smartsupp"
/// configuration section so the agent map lives alongside other Smartsupp
/// settings (Smartsupp:AgentMap in secrets.json / Azure config).
/// </summary>
public class SmartsuppSendMessageOptions
{
    public const string SectionName = "Smartsupp";

    /// <summary>
    /// Maps Heblo user email → Smartsupp agent_id. Each Heblo user who is
    /// allowed to send messages must have an entry here; otherwise SendMessage
    /// returns <c>SmartsuppAgentMappingNotFound</c>. Lookups are case-insensitive.
    /// </summary>
    public Dictionary<string, string> AgentMap { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
