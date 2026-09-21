using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Each cost provider serialises its refresh on a process-wide static RefreshLock. A test that
/// refreshes one provider must therefore not run beside another test refreshing the same provider:
/// the second caller's WaitAsync(0) returns false, the refresh is skipped, and the following read
/// hits an unhydrated cache.
///
/// One collection per provider is not enough, because MarginCostWindowAlignmentTests drives the flat
/// manufacture and overhead providers together - it can only belong to a single collection, so every
/// class touching either provider has to share this one.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CostProviderRefreshLockCollection
{
    public const string Name = "CostProviderRefreshLock";
}
