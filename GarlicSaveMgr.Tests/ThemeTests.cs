using Xunit;
using System.Xml.Linq;
using GarlicSaveMgr.Infrastructure;

namespace GarlicSaveMgr.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void EmbeddedDefaultTheme_IsValidAndContainsBothModes()
    {
        var theme = ThemeLoader.LoadDefault();

        Assert.Equal("Default", theme.Id);
        Assert.Equal("Garlic Default", theme.Name);
        Assert.Equal(ThemeDefinition.CurrentSchema, theme.Schema);
        Assert.Equal("#F5F7FB", theme.GetPalette(ThemeMode.Light)["AppBackground"]);
        Assert.Equal("#171A1F", theme.GetPalette(ThemeMode.Dark)["AppBackground"]);
        Assert.Equal("Segoe UI", theme.GetAppearance(ThemeMode.Light)["Typography.FontFamily"]);
        Assert.Equal("34", theme.GetAppearance(ThemeMode.Light)["Geometry.ButtonHeight"]);
        Assert.All(ThemeValidator.RequiredKeys.Keys, key =>
        {
            Assert.True(theme.GetPalette(ThemeMode.Light).ContainsKey(key), $"Falta Light.{key}");
            Assert.True(theme.GetPalette(ThemeMode.Dark).ContainsKey(key), $"Falta Dark.{key}");
        });
    }

    [Theory]
    [InlineData("#FFFFFF", "#FFFFFF")]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("#12345678", "#12345678")]
    public void ColorValidation_NormalizesSupportedHex(string input, string expected)
    {
        Assert.True(ThemeValidator.TryNormalizeHex(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("FFFFFF")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void ColorValidation_RejectsInvalidHex(string input)
    {
        Assert.False(ThemeValidator.TryNormalizeHex(input, out _));
    }

    [Fact]
    public void PartialExternalTheme_ValidatesWithoutRequiringEveryOptionalKey()
    {
        var xml = XDocument.Parse("""
            <Theme>
              <Metadata><Name>Partial</Name><ThemeSchema>2</ThemeSchema></Metadata>
              <Light><Colors><Accent>#123456</Accent></Colors></Light>
              <Dark><Colors><Accent>#654321</Accent></Colors></Dark>
            </Theme>
            """);

        Assert.True(ThemeValidator.TryValidateDocument(xml, out var errors));
        Assert.Empty(errors);
    }
    [Theory]
    [InlineData("12", 12, 12, 12, 12)]
    [InlineData("12,6", 12, 6, 12, 6)]
    [InlineData("1,2,3,4", 1, 2, 3, 4)]
    public void ThicknessValidation_MapsWpfCompatibleShorthand(string input, double left, double top, double right, double bottom)
    {
        Assert.True(ThemeValidator.TryThickness(input, out var thickness));
        Assert.Equal(left, thickness.Left);
        Assert.Equal(top, thickness.Top);
        Assert.Equal(right, thickness.Right);
        Assert.Equal(bottom, thickness.Bottom);
    }

}
