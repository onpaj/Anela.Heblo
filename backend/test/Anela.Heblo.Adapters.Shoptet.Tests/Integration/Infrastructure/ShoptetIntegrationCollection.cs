using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;

[CollectionDefinition("ShoptetIntegration")]
public class ShoptetIntegrationCollection : ICollectionFixture<ShoptetIntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}