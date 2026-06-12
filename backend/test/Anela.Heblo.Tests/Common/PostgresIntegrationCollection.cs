using Xunit;

namespace Anela.Heblo.Tests.Common;

[CollectionDefinition("PostgresIntegration")]
public sealed class PostgresIntegrationCollection : ICollectionFixture<PostgresSharedContainerFixture>
{
}
