using Anela.Heblo.Domain.Features.Marketing;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Marketing
{
    public class MarketingFolderTypeTests
    {
        [Theory]
        [InlineData(MarketingFolderType.Images, 0)]
        [InlineData(MarketingFolderType.Texts, 1)]
        [InlineData(MarketingFolderType.Videos, 2)]
        [InlineData(MarketingFolderType.Graphics, 3)]
        [InlineData(MarketingFolderType.Other, 99)]
        public void Members_KeepPersistedIntegerValues(MarketingFolderType type, int expected)
        {
            Assert.Equal(expected, (int)type);
        }
    }
}
