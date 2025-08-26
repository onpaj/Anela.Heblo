namespace Anela.Heblo.Application.Features.Logistics.Transport.Contracts;

public class GetAllowedTransitionsResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CurrentState { get; set; }
    public List<AllowedTransition> AllowedTransitions { get; set; } = new();
}

public class AllowedTransition
{
    public string State { get; set; } = null!;
    public string Label { get; set; } = null!;
    public bool RequiresCondition { get; set; }
    public string? ConditionDescription { get; set; }
}