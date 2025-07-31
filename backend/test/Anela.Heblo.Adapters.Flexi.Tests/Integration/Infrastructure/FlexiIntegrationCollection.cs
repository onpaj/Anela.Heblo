using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration;

[CollectionDefinition("FlexiIntegration")]
public class FlexiIntegrationCollection : ICollectionFixture<FlexiIntegrationTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}