// Interface lives in Domain to avoid circular dependency (Application → Persistence → Application).
// Re-exported here so Application use cases can reference the canonical namespace.
global using IFeatureFlagOverrideRepository = Anela.Heblo.Domain.Features.FeatureFlags.IFeatureFlagOverrideRepository;
