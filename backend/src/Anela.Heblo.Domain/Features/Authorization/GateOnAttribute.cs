namespace Anela.Heblo.Domain.Features.Authorization;

/// <summary>
/// Declares which Feature a controller class or action method belongs to.
/// A method-level attribute overrides the class-level for that method.
/// Validated by GateConsistencyTests.EveryAuthorizeRole_MatchesGateOn.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class GateOnAttribute : Attribute
{
    public Feature Feature { get; }
    public GateOnAttribute(Feature feature) => Feature = feature;
}
