namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

/// <param name="WasRun">False when the day was already processed and the run was skipped.</param>
/// <param name="MaterialsProcessed">Number of materials whose quantity was actually decremented.</param>
public sealed record ProcessDailyConsumptionResult(bool WasRun, int MaterialsProcessed);
