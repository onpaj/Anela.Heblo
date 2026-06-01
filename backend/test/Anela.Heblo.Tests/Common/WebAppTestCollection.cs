using Xunit;

namespace Anela.Heblo.Tests.Common;

/// <summary>
/// Collection definition for integration tests that share HebloWebApplicationFactory.
///
/// All test classes marked with [Collection("WebApp")] are forced to run sequentially
/// (DisableParallelization = true). This prevents the race condition where one factory's
/// IServiceProvider is disposed while another test class is still resolving services —
/// the root cause of the intermittent ObjectDisposedException seen in
/// ApplicationStartupTests.MediatR_Handler_Should_Be_Resolvable.
///
/// Each test class retains its own IClassFixture&lt;HebloWebApplicationFactory&gt; instance;
/// sequential execution alone is sufficient to eliminate the disposal race condition.
/// </summary>
[CollectionDefinition("WebApp", DisableParallelization = true)]
public class WebAppTestCollection
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition].
}
