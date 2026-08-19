using AiShot.App;

namespace AiShot.Tests;

public class DistributionChannelTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(122, true)]
    [InlineData(15700, false)]
    [InlineData(5, false)]
    public void ResultHasPackageIdentity_ClassificaRetornoNativo(int result, bool expected) =>
        Assert.Equal(expected, DistributionChannel.ResultHasPackageIdentity(result));
}
