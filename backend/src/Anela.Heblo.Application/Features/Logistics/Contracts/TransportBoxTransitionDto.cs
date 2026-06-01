namespace Anela.Heblo.Application.Features.Logistics.Contracts;

public class TransportBoxTransitionDto
{
    public string NewState { get; set; } = string.Empty;
    public string TransitionType { get; set; } = string.Empty;
    public bool SystemOnly { get; set; }
    public string Label { get; set; } = string.Empty;
}