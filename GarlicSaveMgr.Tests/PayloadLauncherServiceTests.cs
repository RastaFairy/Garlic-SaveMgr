using GarlicSaveMgr.Services;

namespace GarlicSaveMgr.Tests;

public sealed class PayloadLauncherServiceTests
{
    [Theory]
    [InlineData("v1.13", "v1.12")]
    [InlineData("1.10.0", "1.9.99")]
    [InlineData("garlic-savemgr v2.0", "v1.99.99")]
    public void CompareVersions_ReturnsPositive_WhenLeftIsNewer(string left, string right)
    {
        Assert.True(PayloadLauncherService.CompareVersions(left, right) > 0);
    }

    [Theory]
    [InlineData("v1.12", "v1.13")]
    [InlineData("1.9.99", "1.10.0")]
    [InlineData("v2.0", "v10.0")]
    public void CompareVersions_ReturnsNegative_WhenLeftIsOlder(string left, string right)
    {
        Assert.True(PayloadLauncherService.CompareVersions(left, right) < 0);
    }

    [Theory]
    [InlineData("v6.8", "6.8.0")]
    [InlineData("garlic-savemgr 1.13", "v1.13")]
    [InlineData("release-2-0", "2.0.0")]
    public void CompareVersions_ReturnsZero_WhenVersionsAreEquivalent(string left, string right)
    {
        Assert.Equal(0, PayloadLauncherService.CompareVersions(left, right));
    }

    [Fact]
    public void CompareVersions_IgnoresTextAndComparesFirstThreeNumericParts()
    {
        var result = PayloadLauncherService.CompareVersions("Garlic v1.12.3-preview", "release-1.12.2");

        Assert.True(result > 0);
    }
}
