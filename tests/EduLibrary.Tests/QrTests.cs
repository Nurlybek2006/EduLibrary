using System.Xml.Linq;
using EduLibrary.Helpers;
using Xunit;

namespace EduLibrary.Tests;

public sealed class QrTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ABC\n123")]
    public void InvalidCodesCannotReachLookupOrQrGeneration(string? code) =>
        Assert.Throws<BusinessException>(() => CopyCode.Normalize(code));

    [Fact]
    public void OverlongCodeIsRejected() => Assert.Throws<BusinessException>(() => CopyCode.Normalize(new string('X', 101)));

    [Fact]
    public void InventoryAndBarcodeWhitespaceIsNormalized() => Assert.Equal("EDU000001001", CopyCode.Normalize("  EDU000001001  "));

    [Fact]
    public void LibraryRendererProducesStandalonePassiveSvg()
    {
        var svg = CopyCode.CreateQrSvg("EDU000001001");
        var xml = XDocument.Parse(svg);
        Assert.Equal("svg", xml.Root?.Name.LocalName);
        Assert.DoesNotContain(xml.Descendants(), x => x.Name.LocalName is "script" or "foreignObject" or "image");
        Assert.DoesNotContain(xml.Descendants().Attributes(), x => x.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("student", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentCopyCodesProduceDifferentQrMatrices() => Assert.NotEqual(CopyCode.CreateQrSvg("EDU000001001"), CopyCode.CreateQrSvg("EDU000001002"));

    [Fact]
    public void UntrustedBarcodeTextCannotBecomeSvgMarkup()
    {
        var xml = XDocument.Parse(CopyCode.CreateQrSvg("<script>alert('code')</script>"));
        Assert.DoesNotContain(xml.Descendants(), x => x.Name.LocalName == "script");
    }
}
